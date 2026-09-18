# IOB (I/O Board) Design

## Context

Real-data boot testing during the color TV port's final review (`docs/superpowers/specs/2026-09-14-color-tv-design.md`) found that `usim-cs`'s IOB address range (Unibus `0764000`-`0764176` octal, `IobLo`/`IobHi` = `0x3E800`-`0x3E87E` in `BusAdaptor.cs`) is entirely un-ported — every access falls to the generic placeholder. A real boot against real `sys/ubin/promh.mcr`/`ucadr.mcr` microcode spent 479,987 of 480,000 log lines polling three IOB registers (CSR `0764112`, the microsecond clock `0764120`/`0764122`, and the Chaos-interface CSR `0764140`) and never issued a single real disk command as a result. IOB is the single biggest blocker to exercising any of this project's other faithful ports (disk, both TV devices, Unibus-Map DMA) against real, running CADR microcode.

The real reference is `usim/iob.c` (444 lines), which multiplexes several logically distinct facilities behind one Unibus register file: keyboard/mouse bridging (with a special cold/warm-boot key-combo detector), a beep pass-through, a command/status register (CSR) with interrupt gating, two clocks (microsecond and 60Hz), a GPIO stub, four Chaos-interface registers that a boot-time probe touches, a serial-interface stub, four undocumented registers, and (SDL3-only) a one-shot interval timer.

Two existing classes turned out to also be entirely invented, not faithful ports, once this design started tracing the real keyboard/mouse data path: `usim-cs/IOBus.cs` (a generic `IIODevice` device-registry framework, never wired into `BusAdaptor` — the same situation `Display.cs` was in before the monochrome TV port) and, more subtly, `usim-cs/Keyboard.cs`/`Mouse.cs` (whose internal buffer/modifier-bit-packing and mouse-XY-scaling schemes bear no relation to the real hardware protocol). The real, modern protocol lives entirely in `iob.c`'s `WITH_SDL3`-guarded functions — `iob_set_keyboard_ready(scancode)` and `iob_set_mouse_ready(x, rawx, y, rawy, head, middle, tail)` (`usim/iob.c:87-133`), called directly from `usim/sdl3-keyboard.c`/`sdl3-mouse.c`'s input handlers. The older, non-SDL3 path (`kbd.c`/`mouse.c`'s queue/A-memory-symbol-injection logic, `cadet.c`/`knight.c`'s keyboard-type variants) is confirmed dead code on the SDL3 build (`iob_init()`'s `#ifdef WITH_SDL3 ... #else kbd_init(); mouse_init(); #endif` never calls them when `WITH_SDL3` is defined) — this port targets the SDL3 model exclusively, matching every other "WPF replaces the modern SDL3/X11 backend" decision already made in this project.

## Decisions

- **Scope, decided across two rounds of scoping with the user as real complexity surfaced:** this port fixes `IOBus.cs` (replaced outright, mirroring the `Display.cs` precedent), and `Keyboard.cs`/`Mouse.cs`'s **wire protocol** (the real CSR ready-bit/clear-on-read/interrupt semantics, and the real mouse X/Y/button-toggle bit encoding) — the load-bearing part for input to reach real Lisp software at all. It does **not** replicate `sdl3-keyboard.c`'s full Cadet-scancode mapping table (driven by external `.defs` files) or its background thread/queue/exponential-backoff delivery mechanism — `WpfKeyTranslator.cs` keeps its existing, simpler key-to-scancode mapping, and key/mouse events are delivered synchronously and directly from WPF's own event dispatch, no queue. Real Chaosnet networking (`uch11.c`) stays a separate, future port; this port gives the four Chaos registers real boot activity touches (`ch_csr`/`ch_myaddr`/`ch_rbf`/`ch_rbc`/`ch_xmt`) a minimal, safe idle-state stub.
- **No queue/backoff means a possible overwrite, not a hang or a drop.** The real C's SDL3 thread retries with exponential backoff (up to ~256ms) if the keyboard-ready bit is still set, to avoid clobbering an unread scancode; queuing overflow is only handled by the (unported) `.defs`-table-driven thread wrapper. Without that wrapper, calling `SetKeyboardReady`/`SetMouseReady` directly and unconditionally (matching `iob_set_keyboard_ready`/`iob_set_mouse_ready`'s own bodies, which have no internal gating at all — the gating lives entirely in the thread wrapper this port doesn't replicate) means a second keypress arriving before microcode reads the first one overwrites it rather than queuing. This is judged safe in practice: WPF key events arrive at human typing speed, many orders of magnitude slower than microcode's read loop, and losing an occasional keystroke under real contention is a graceful degradation, not a correctness bug — no queue exists to overflow silently either.
- **Mouse raw encoder bits (`rawX`/`rawY`) are always 0.** The real hardware reports raw quadrature-encoder ticks alongside the resolved X/Y position; WPF delivers discrete `MouseMove` events with an absolute position, not encoder ticks, so there is no realistic value to synthesize. The bit *layout* (`0x7777` mask, shift amounts 12/14) is preserved exactly so any code that does inspect those bits sees deterministic zeros rather than garbage, not because zero is a faithful hardware value.
- **The interval timer (`0764124` write, SDL3-only in the real C via `setitimer`/`SIGALRM`) is tick-counted, not signal-based.** Matching the same coarse-tick approximation already established for TV/color-TV interrupt timing (see both TV specs' Decisions sections): a countdown decremented once per `Iob.Tick()`, firing `UCode.AssertUnibusInterrupt(0274)` at zero. The real C's `16 * v` microsecond unit is converted to an equivalent tick count using the same ~16ms-per-tick assumption WpfBackend/the headless loop already use elsewhere.
- **The 60Hz clock (`the_60_cycle_clock`, `0764124` read) finally gets a home.** Both TV specs explicitly flagged this as "a separate IOB-owned counter, out of scope for TV/color TV" — this port is that home: incremented once per `Iob.Tick()`, read back as a free-running 16-bit counter (matching the real C's `uint16_t the_60_cycle_clock`).
- **A found-and-preserved real-C oddity: the cold/warm-boot scancode constants have a likely `<`-vs-`<<` typo in the real source, but it's harmless.** `usim/sdl3-keyboard.c:20-21` defines `cold_boot_scancode`/`warm_boot_scancode` using `(0b111111 < 10)` — a *comparison*, not the evidently-intended `(0b111111 << 10)` shift (this term evaluates to `0`, since `63 < 10` is false, rather than to `0b111111` shifted into bits 10-15). Independently verified via direct computation: the "as literally written" values are `cold_boot_scancode = 0xFF0026`, `warm_boot_scancode = 0xFF0032`; the "evidently intended" values would be `0xFFFC26`/`0xFFFC32`. This makes no behavioral difference because both constants are purely self-referential sentinels — `iob_set_keyboard_ready` only ever compares an incoming scancode against these same two constants, and no other code depends on their specific bit pattern, so the choice of value is arbitrary as long as it can't collide with a real Cadet-table key scancode (which it can't, at this bit width, either way). This port replicates the real C's literal, as-written values (`0xFF0026`/`0xFF0032`) rather than the "corrected" ones, matching this project's standing discipline of porting the real source faithfully rather than second-guessing it, since here that faithfulness costs nothing.
- **Register width is genuinely 16 bits, not 32.** `iob_unibus_read`/`iob_unibus_write` take `uint16_t *pv`/`uint16_t v` — unlike every XBus-I/O device ported so far (disk, TV, color TV), which use 32-bit registers. `Iob`'s own read/write methods use `ushort`, and `BusAdaptor`'s dispatch masks to `0xFFFF` at the boundary rather than silently carrying a 32-bit value through a 16-bit device.
- **Cold/warm boot triggers `MachineControl.PowerOn()`** (matching real `machine_control_boot()`'s own `machine_control_reset()` + `promdisabled = false`, which map directly to `MachineControl`'s existing reset path and `UCode.PromDisabled` reset inside `UCode.Init()`). `Iob` receives this as a constructor-injected `Action` callback (matching the existing `WpfBackend(..., onTick: RunMicrocodeBatch)` convention already used elsewhere in `MachineControl`), not a settable-once `Wire*` method — the boot trigger is a one-shot callback, not a persistent object dependency. After triggering the callback, `Iob` still delivers the boot scancode as a normal keyboard-ready event (matching the real C, which lets the PROM's own microcode inspect the low 6 bits afterward to decide cold vs. warm — `Iob` does not make that distinction itself).
- **Chaos-interface registers get a minimal, safe idle stub, not real networking.** The four registers real boot activity actually touches: `ch_csr` (`0764140`) reads back a constant "idle, no packet pending, no error" value; `ch_myaddr` (`0764142`) reads back a placeholder Chaos address; `ch_rbf`/`ch_rbc` (`0764144`/`0764146`) read back 0 (no packet ever arrives); `ch_xmt` (`0764152`) is a no-op read (matching the real C's own read-triggers-transmission oddity, but with no actual transmission happening). Writes to any of these are accepted and ignored. This is enough for the real boot-time probe to see a consistent, non-hanging response without pretending to implement Chaosnet.
- **GPIO, serial, and undocumented registers are warn-and-return-0 stubs**, matching the real C's own `warnx(...)` calls for these exact ranges (`usim/iob.c:250-254,285-299`) — not ported behavior, just non-fatal visibility.

## Octal Literals — Independently Verified

Computed via Python, not by hand:

| Literal | Decimal | Hex | Meaning |
|---|---|---|---|
| `017` (CSR write mask) | 15 | `0xF` | CSR bits 0-3 are the only real writable bits (`iob_csr = (iob_csr & ~017) \| (v & 017)`) |
| `0260` (keyboard interrupt vector) | 176 | `0xB0` | passed to `AssertUnibusInterrupt` on keyboard-ready |
| `0264` (mouse interrupt vector) | 180 | `0xB4` | passed to `AssertUnibusInterrupt` on mouse-ready |
| `0274` (interval-timer interrupt vector) | 188 | `0xBC` | passed to `AssertUnibusInterrupt` when the tick-counted interval timer expires |
| `cold_boot_scancode` (as literally written, `<` not `<<`) | 16711718 | `0xFF0026` | see Decisions — the real C's own likely typo, preserved faithfully since it's harmless |
| `warm_boot_scancode` (as literally written) | 16711730 | `0xFF0032` | same |
| `0x7777` (mouse position/encoder mask) | — | `0x7777` binary `0111011101110111` | every 4th bit reserved for raw-encoder state, preserved as a bit-layout constant even though this port always writes 0 into those bit positions |
| register addresses (`0764100`-`0764176` octal) | — | `0x3E840`-`0x3E87E` | already independently verified as the existing `IobLo`/`IobHi` `BusAdaptor.cs` constants from an earlier phase; individual register offsets re-derived in the Register Model section below |

## Register Model

All registers are 16-bit words at Unibus addresses `0764100`-`0764176` octal (`0x3E840`-`0x3E87E`), computed as offsets from `IobLo` (`0x3E800`) in 2-byte (1-word) steps — i.e. register `N` is at `IobLo + 2*N` for the low half of the visible range.

**Keyboard** (`0764100`/`0764102` = offsets `0x20`/`0x22` from `IobLo`):
- Read offset `0x20` (kbd low): returns `_scancode & 0xFFFF`, then clears the keyboard-ready bit (CSR bit 5).
- Read offset `0x22` (kbd high): returns `(_scancode >> 16) & 0xFFFF`. Does **not** clear the ready bit again (already cleared by the low-word read, matching the real C's comment `// CSR cleared above when reading 0100`).
- `SetKeyboardReady(uint scancode)`: if `scancode == 0xFF0026` or `== 0xFF0032` (cold/warm boot), invoke the boot-trigger callback first. Then: `_scancode = scancode`; set CSR bit 5; if CSR bit 2 (keyboard-interrupt-enable) is set, call `UCode.AssertUnibusInterrupt(0xB0)`.

**Mouse** (`0764104`/`0764106` = offsets `0x24`/`0x26`):
- Read offset `0x24` (mouse Y): returns `_mouseY`, then clears the mouse-ready bit (CSR bit 4).
- Read offset `0x26` (mouse X): returns `_mouseX`. Does **not** clear the ready bit (matching the real asymmetry — Y-read is the "consume" side).
- `SetMouseReady(int x, int y, bool tailPressed, bool middlePressed, bool headPressed)`: `rawX`/`rawY` are always 0 (see Decisions). `_mouseX = (0 << 12) | (0 << 14) | (x & 0x7777)`. `_mouseY = ((tailPressed ? 1 : 0) << 12) | ((middlePressed ? 1 : 0) << 13) | ((headPressed ? 1 : 0) << 14) | (y & 0x7777)`. Set CSR bit 4; if CSR bit 1 (mouse-interrupt-enable) is set, call `UCode.AssertUnibusInterrupt(0xB4)`.

**Beep** (`0764110` = offset `0x28`): the real C's read-triggered path calls `sdl3_audio_beep(-1)`, a sentinel for "the audio subsystem's own fixed default tone" — its own comment (`usim/iob.c:209-211`) gives that tone as `#o1350` (744, independently verified: `744 = 0o1350`) microseconds half-wavelength (≈672Hz) for 0.13 seconds (130000 microseconds), which is exactly `WpfBackend.Beep(int halfWavelengthMicros, int durationMicros)`'s two parameters. So: read triggers `OnBeep?.Invoke(744)`; write triggers `OnBeep?.Invoke((int)v)` (the real C's write path passes `v` directly as the half-wavelength, matching `sdl3_audio_beep(v)`). Both cases use the same fixed 130000-microsecond duration — `Iob` exposes this as an `Action<int>? OnBeep` callback (mirroring the boot-trigger callback shape) that `MachineControl`'s wiring subscribes to as `halfWavelengthMicros => DisplayBackend?.Beep(halfWavelengthMicros, 130000)`, rather than `Iob` holding a direct reference to `WpfBackend`.

**CSR** (`0764112` = offset `0x2A`): read returns the stored CSR value directly (no derived bits, unlike TV's mode register — every real CSR bit here is either set by `SetKeyboardReady`/`SetMouseReady`/`SetKeyboardReady`-clearing or written directly). Write: `_csr = (_csr & ~0xF) | (v & 0xF)` (only the low 4 bits are writable).

**Microsecond clock** (`0764120`/`0764122` = offsets `0x30`/`0x32`, read-only): backed by a `Stopwatch` started on first read (matching `get_us_clock`'s own "first call returns 0 and records the epoch" behavior). Read offset `0x30`: `(uint)(ElapsedMicroseconds & 0xFFFF)`. Read offset `0x32`: `(uint)((ElapsedMicroseconds >> 16) & 0xFFFF)`.

**60Hz clock** (`0764124` = offset `0x34`): read returns the free-running `_the60CycleClock` counter (incremented once per `Tick()`, wrapping at 16 bits naturally via `ushort` overflow). Write sets the tick-counted interval timer: `_intervalTimerTicksRemaining = (uint)Math.Ceiling(16.0 * v / TickMicroseconds)`, where `TickMicroseconds` is the same ~16000µs-per-tick constant already implicit in `WpfBackend`'s `DispatcherTimer`/the headless loop's `Thread.Sleep(16)`.

**GPIO** (`0764126` = offset `0x36`, read-only in the real C): returns 0, logs a warning — matching `usim/iob.c:251-254`'s own `warnx("iob: read from gpio")`.

**Chaos interface** (`0764140`-`0764152` = offsets `0x40`-`0x4A`, even offsets only — `0x40`=`ch_csr`, `0x42`=`ch_myaddr`, `0x44`=`ch_rbf`, `0x46`=`ch_rbc`, `0x4A`=`ch_xmt`; `0x48`/`0x4C`/`0x4E` = `ch_nop`/obsolete, warn-and-0):
- Read `ch_csr`: returns a constant idle value (`0` — no transmit-done, no receive-done, no CRC error, no lost-packet count; matches "nothing has ever happened" rather than any specific real hardware reset state).
- Read `ch_myaddr`: returns a placeholder Chaos address (`0177040` octal, matching the real C's own hardcoded `LOCAL-CADR` fallback default in `uch11.c:42` when no config-supplied Chaos name resolves — see Octal Literals).
- Read `ch_rbf`/`ch_rbc`: return `0` (no packet ever arrives).
- Read `ch_xmt`: returns `ch_myaddr`'s same placeholder value (matching the real C's own read-side echo), performs no actual transmission.
- Writes to any of these: accepted, ignored (no state change) — matching that this port implements no real Chaosnet protocol.

**Serial interface** (`0764160`-`0764166` = offsets `0x60`-`0x66`): read or write, warn-and-return-0/no-op — matching `usim/iob.c:286-291,375-380`'s own `warnx(...)` stubs.

**Undocumented** (`0764170`-`0764176` = offsets `0x68`-`0x6E`): read warn-and-return-0, matching `usim/iob.c:294-299`. (The real C has no write case for this range at all — any write here falls to `iob_unibus_write`'s `default: errx(...)`, a genuine fatal error in the real C. This port's default-case convention, established by every prior device, is a non-fatal warning instead — consistent with the monochrome TV port's identical judgment call on `tv_init()`'s practically-unreachable fatal path.)

## Tick / Interrupt

`Iob.Tick()`, called from the same ~16ms cadence as every other device (`WpfBackend.Tick()` and the headless `Run()` loop):
```
_the60CycleClock++; // free-running, wraps naturally as a ushort

if (_intervalTimerTicksRemaining > 0)
{
    _intervalTimerTicksRemaining--;
    if (_intervalTimerTicksRemaining == 0)
    {
        _ucode.AssertUnibusInterrupt(0xBC);
    }
}
```
No enable gate on this device's `Tick()` — unlike TV/color TV, IOB has no `UsimState.*Enabled` flag (it's not an optional peripheral; the real hardware is always present).

## Class Shape

```csharp
// Iob.cs - Faithful port of usim/iob.c's WITH_SDL3 code path (the CADR I/O
// board). Real Chaosnet networking (usim/uch11.c) is a separate, future
// port; the four Chaos registers real boot activity touches get a minimal,
// safe idle stub here. See docs/superpowers/specs/2026-09-18-iob-design.md.

using System;
using System.Diagnostics;

namespace Usim;

public class Iob
{
    // usim/sdl3-keyboard.c:20-21, preserved as literally written (a likely
    // '<' vs '<<' typo in the real C -- see the design's Decisions for why
    // this is harmless: both constants are purely self-referential sentinels).
    private const uint ColdBootScancode = 0xFF0026;
    private const uint WarmBootScancode = 0xFF0032;

    private const uint ChaosMyAddrPlaceholder = 0x7C20; // 0177040 octal, uch11.c's LOCAL-CADR fallback

    private readonly UCode _ucode;
    private readonly Action _onBootTrigger;

    public Action<int>? OnBeep { get; set; }

    private uint _csr;
    private uint _scancode;
    private uint _mouseX;
    private uint _mouseY;
    private ushort _the60CycleClock;
    private uint _intervalTimerTicksRemaining;
    private readonly Stopwatch _usecStopwatch = new();
    private bool _usecStarted;

    public Iob(UCode ucode, Action onBootTrigger)
    {
        _ucode = ucode;
        _onBootTrigger = onBootTrigger;
    }

    private bool IsKeyboardInterruptEnabled() => (_csr & 0x4) != 0;
    private bool IsMouseInterruptEnabled() => (_csr & 0x2) != 0;

    /// <summary>
    /// Faithful port of iob_set_keyboard_ready (usim/iob.c:88-107, WITH_SDL3
    /// branch). Called directly from Keyboard's key-down path -- no queue,
    /// no retry/backoff (see Decisions).
    /// </summary>
    public void SetKeyboardReady(uint scancode)
    {
        if (scancode == ColdBootScancode || scancode == WarmBootScancode)
        {
            _onBootTrigger();
        }

        _scancode = scancode;
        _csr |= 1u << 5;

        if (IsKeyboardInterruptEnabled())
        {
            _ucode.AssertUnibusInterrupt(0xB0);
        }
    }

    /// <summary>
    /// Faithful port of iob_set_mouse_ready (usim/iob.c:112-132, WITH_SDL3
    /// branch). rawX/rawY are always 0 -- see Decisions.
    /// </summary>
    public void SetMouseReady(int x, int y, bool tailPressed, bool middlePressed, bool headPressed)
    {
        _mouseX = (uint)(x & 0x7777);
        _mouseY = ((tailPressed ? 1u : 0u) << 12)
            | ((middlePressed ? 1u : 0u) << 13)
            | ((headPressed ? 1u : 0u) << 14)
            | (uint)(y & 0x7777);
        _csr |= 1u << 4;

        if (IsMouseInterruptEnabled())
        {
            _ucode.AssertUnibusInterrupt(0xB4);
        }
    }

    /// <summary>Faithful port of iob_unibus_read (usim/iob.c:138-303, WITH_SDL3 branch + shared registers).</summary>
    public ushort Read(uint offset)
    {
        switch (offset)
        {
            case 0x20: // kbd low
                {
                    ushort v = (ushort)(_scancode & 0xFFFF);
                    _csr &= ~(1u << 5);
                    return v;
                }

            case 0x22: // kbd high
                return (ushort)((_scancode >> 16) & 0xFFFF);

            case 0x24: // mouse y
                {
                    ushort v = (ushort)_mouseY;
                    _csr &= ~(1u << 4);
                    return v;
                }

            case 0x26: // mouse x
                return (ushort)_mouseX;

            case 0x28: // beep
                OnBeep?.Invoke(744); // usim/iob.c:209-211's own fixed default: 744us half-wavelength (~672Hz)
                return 0;

            case 0x2A: // csr
                return (ushort)_csr;

            case 0x30: // usec clock low
                return (ushort)(ElapsedMicroseconds() & 0xFFFF);

            case 0x32: // usec clock high
                return (ushort)((ElapsedMicroseconds() >> 16) & 0xFFFF);

            case 0x34: // 60Hz clock
                return _the60CycleClock;

            case 0x36: // gpio
                TraceLog.Instance.Warning(TraceCategory.IOBus, "iob: read from gpio");
                return 0;

            case 0x40: // ch_csr
                return 0;

            case 0x42: // ch_myaddr
                return (ushort)ChaosMyAddrPlaceholder;

            case 0x44: // ch_rbf
            case 0x46: // ch_rbc
                return 0;

            case 0x48: // ch_nop (obsolete)
            case 0x4C:
            case 0x4E:
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: chaos read obsolete unibus address: 0x{offset:X}");
                return 0;

            case 0x4A: // ch_xmt
                return (ushort)ChaosMyAddrPlaceholder;

            case 0x60: // serial
            case 0x62:
            case 0x64:
            case 0x66:
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: read from serial interface: 0x{offset:X}");
                return 0;

            case 0x68: // undocumented
            case 0x6A:
            case 0x6C:
            case 0x6E:
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: read from unknown device: 0x{offset:X}");
                return 0;

            default:
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: read from unknown offset: 0x{offset:X}");
                return 0;
        }
    }

    /// <summary>Faithful port of iob_unibus_write (usim/iob.c:307-384).</summary>
    public void Write(uint offset, ushort v)
    {
        switch (offset)
        {
            case 0x20: // kbd low
            case 0x22: // kbd high
            case 0x24: // mouse y
            case 0x26: // mouse x
                break; // real C: DEBUG trace only, no state change

            case 0x28: // beep
                OnBeep?.Invoke(v);
                break;

            case 0x2A: // csr
                _csr = (_csr & ~0xFu) | (v & 0xFu);
                break;

            case 0x30: // usec clock
            case 0x32:
                break; // real C: DEBUG trace only, no state change (read-only in practice)

            case 0x34: // interval timer set
                {
                    const double tickMicroseconds = 16000.0; // ~16ms per Tick(), matching WpfBackend/the headless loop
                    _intervalTimerTicksRemaining = (uint)Math.Ceiling(16.0 * v / tickMicroseconds);
                }
                break;

            case 0x40: // ch_csr
            case 0x42: // ch_myaddr write (buffer write in the real C)
            case 0x44: // ch_rbf
            case 0x46: // ch_rbc
            case 0x48: // ch_nop
            case 0x4A: // ch_xmt
                break; // accepted, ignored -- see Decisions

            case 0x60: // serial
            case 0x62:
            case 0x64:
            case 0x66:
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: uart write: 0x{offset:X} v=0x{v:X}");
                break;

            default:
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: write to unknown offset: 0x{offset:X} v=0x{v:X}");
                break;
        }
    }

    /// <summary>
    /// Faithful port of run_at_60hz's the_60_cycle_clock++ (usim/usim.c) plus
    /// the tick-counted interval-timer approximation (see Decisions).
    /// </summary>
    public void Tick()
    {
        _the60CycleClock++;

        if (_intervalTimerTicksRemaining > 0)
        {
            _intervalTimerTicksRemaining--;
            if (_intervalTimerTicksRemaining == 0)
            {
                _ucode.AssertUnibusInterrupt(0xBC);
            }
        }
    }

    private long ElapsedMicroseconds()
    {
        if (!_usecStarted)
        {
            _usecStopwatch.Start();
            _usecStarted = true;
            return 0;
        }

        return _usecStopwatch.Elapsed.Ticks / 10; // Stopwatch ticks are 100ns
    }
}
```

## Wiring

1. **`BusAdaptor.cs`**: gains settable-once `Iob? _iob` + `WireIob(Iob iob)`, mirroring `_tv`/`WireTv`. `ReadUnibus`/`WriteUnibus` gain a branch for `IobLo`/`IobHi`, computing `offset = uaddr - IobLo`, calling `_iob.Read(offset)`/`_iob.Write(offset, (ushort)v)` with the existing null-guard-and-warn convention. Reads mask the returned `ushort` back up through `ReadUnibus`'s `uint` return type (implicit widening, no data loss); writes truncate the incoming `uint v` to `(ushort)v` at the call site, matching the real 16-bit register width (see Decisions).
2. **`MachineControl.cs`**: constructs `Iob = new Iob(UCode, onBootTrigger: () => PowerOn());` after `ColorTv`, wires it via `UCode.BusAdaptor.WireIob(Iob)`. Subscribes `Iob.OnBeep = halfWavelengthMicros => DisplayBackend?.Beep(halfWavelengthMicros, 130000);` (matching `WpfBackend.Beep(int halfWavelengthMicros, int durationMicros)`'s real signature and the fixed 130000-microsecond duration derived in the Register Model section above). Both `Reset()`/`InitializeComponents()` and the headless `Run()` loop's tick site gain `Iob.Tick()` alongside `Tv.Tick()`/`ColorTv.Tick()`.
3. **`Keyboard.cs`**: sheds `AddKey`/`GetKey`/`HasKeys`/`BufferCount`/the `CadrKeyCode` modifier-bit-packing enum entirely. `KeyDown(uint keysym, int modifiers)`/`KeyUp` keep translating WPF input via the existing `WpfKeyTranslator`, but now combine the result into the real scancode encoding (`v = ((!keydown) << 8) | code`) and call `_iob.SetKeyboardReady(v)` — `Keyboard` gains a constructor-injected `Iob` reference (or a settable-once `WireIob`, matching the session's established pattern for a dependency that must exist before construction completes).
4. **`Mouse.cs`**: `MoveTo`/`SetButton` (renamed/reshaped as needed during planning to match toggle semantics rather than press/release flags for `_tail`/`_middle`/`_head`) call `_iob.SetMouseReady(x, y, tailToggled, middleToggled, headToggled)` on every position or button change. `ReadXRegister`/`ReadYRegister`/`ReadButtonsRegister`/`MouseButtonBits` and the invented 11-bit linear XY scaling are removed — `Iob` is now the sole owner of the real register encoding.
5. **`usim-cs/IOBus.cs`**: deleted entirely (the invented `IIODevice`/device-registry framework, never wired into `BusAdaptor`, has no faithful counterpart and no remaining callers once `Iob.cs` exists).
6. **`TraceLog.cs`**: no change needed — `TraceCategory.IOBus = 128` already exists (confirmed by direct inspection), just unused until this port's `Iob.cs` starts calling `TraceLog.Instance.Warning(TraceCategory.IOBus, ...)`.

## Testing

`IobTests.cs` (new): CSR read/write masking (low 4 bits only); keyboard-ready set via `SetKeyboardReady`, read-clears-on-low-word-only, interrupt gating on CSR bit 2; mouse-ready set via `SetMouseReady`, read-clears-on-Y-only, interrupt gating on CSR bit 1; the real `0x7777`-masked bit layout for a representative X/Y value with `rawX`/`rawY` always 0; cold and warm boot scancode detection invoking the boot-trigger callback exactly once each, with a non-boot scancode never invoking it; the usec clock returning 0 on its first read and a monotonically increasing value on subsequent reads; the 60Hz clock incrementing exactly once per `Tick()` call and wrapping correctly past `ushort.MaxValue`; the interval timer firing `AssertUnibusInterrupt(0xBC)` at exactly the computed tick count, not one tick early or late; every GPIO/serial/undocumented/Chaos-stub register returning its documented value without throwing, on both read and write.

`Keyboard`/`Mouse` (whichever test files already exist, or new ones if not — checked during planning) get updated to verify the new scancode/toggle encoding calls the wired `Iob` correctly, using a real `Iob` instance rather than a mock, matching this project's established convention.

`BusAdaptorTests.cs` gains a real-dispatch-confirmation test for the `IobLo`/`IobHi` range, mirroring `TestTvRangeDispatchesToRealTv`'s shape.

## Global Constraints

- No new NuGet dependencies.
- Every octal/hex literal in this spec was independently verified via Python — see the table above.
- Register width is 16 bits (`ushort`), not 32 — a genuine difference from every prior XBus-I/O device port.
- No `UsimState.*Enabled` gate on this device — IOB is not optional hardware, unlike TV/color TV.
- The Cadet-scancode mapping table, the SDL3 keyboard thread's queue/backoff mechanism, and real Chaosnet networking (`uch11.c`) are explicitly out of scope for this port (see Decisions and Out of Scope).

## Out of Scope

- `usim/uch11.c` (and its `-chaosd`/`-local`/`-udp` variants) — real Chaosnet packet transmission/reception. A separate, future port; this port's Chaos-register stub is deliberately minimal.
- `usim/kbd.c`/`mouse.c`/`cadet.c`/`knight.c` — confirmed dead code on the `WITH_SDL3` build path this project targets.
- `sdl3-keyboard.c`'s Cadet-scancode table (`.defs`-file-driven) and its background thread/queue/exponential-backoff delivery — see Decisions for why the wire-protocol fix doesn't require this.
- Any UI for configuring Chaos networking, GPIO, or serial ports — none of these are implemented beyond the stub level, so there's nothing to configure yet.
