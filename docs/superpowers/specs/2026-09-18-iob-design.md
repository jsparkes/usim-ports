# IOB (I/O Board) Design

## Context

Real-data boot testing during the color TV port's final review (`docs/superpowers/specs/2026-09-14-color-tv-design.md`) found that `usim-cs`'s IOB address range (Unibus `0764000`-`0764176` octal, `IobLo`/`IobHi` = `0x3E800`-`0x3E87E` in `BusAdaptor.cs`) is entirely un-ported — every access falls to the generic placeholder. A real boot against real `sys/ubin/promh.mcr`/`ucadr.mcr` microcode spent 479,987 of 480,000 log lines polling three IOB registers (CSR `0764112`, the microsecond clock `0764120`/`0764122`, and the Chaos-interface CSR `0764140`) and never issued a single real disk command as a result. IOB is the single biggest blocker to exercising any of this project's other faithful ports (disk, both TV devices, Unibus-Map DMA) against real, running CADR microcode.

The real reference is `usim/iob.c` (444 lines), which multiplexes several logically distinct facilities behind one Unibus register file: keyboard/mouse bridging (with a special cold/warm-boot key-combo detector), a beep pass-through, a command/status register (CSR) with interrupt gating, two clocks (microsecond and 60Hz), a GPIO stub, four Chaos-interface registers that a boot-time probe touches, a serial-interface stub, four undocumented registers, and (SDL3-only) a one-shot interval timer.

Two existing classes turned out to also be entirely invented, not faithful ports, once this design started tracing the real keyboard/mouse data path: `usim-cs/IOBus.cs` (a generic `IIODevice` device-registry framework, never wired into `BusAdaptor` — the same situation `Display.cs` was in before the monochrome TV port) and, more subtly, `usim-cs/Keyboard.cs`/`Mouse.cs` (whose internal buffer/modifier-bit-packing and mouse-XY-scaling schemes bear no relation to the real hardware protocol). The real, modern protocol lives entirely in `iob.c`'s `WITH_SDL3`-guarded functions — `iob_set_keyboard_ready(scancode)` and `iob_set_mouse_ready(x, rawx, y, rawy, head, middle, tail)` (`usim/iob.c:87-133`), called directly from `usim/sdl3-keyboard.c`/`sdl3-mouse.c`'s input handlers. The older, non-SDL3 path (`kbd.c`/`mouse.c`'s queue/A-memory-symbol-injection logic, `cadet.c`/`knight.c`'s keyboard-type variants) is confirmed dead code on the SDL3 build (`iob_init()`'s `#ifdef WITH_SDL3 ... #else kbd_init(); mouse_init(); #endif` never calls them when `WITH_SDL3` is defined) — this port targets the SDL3 model exclusively, matching every other "WPF replaces the modern SDL3/X11 backend" decision already made in this project.

## Decisions

- **Scope, decided across three rounds of scoping with the user as real complexity surfaced:** this port fixes `IOBus.cs` (replaced outright, mirroring the `Display.cs` precedent), and `Keyboard.cs`/`Mouse.cs`'s **wire protocol** (the real CSR ready-bit/clear-on-read/interrupt semantics, and the real mouse X/Y/button-toggle bit encoding) — the load-bearing part for input to reach real Lisp software at all. It does **not** replicate `sdl3-keyboard.c`'s external `.defs`-file-loading architecture or its background thread/queue/exponential-backoff delivery mechanism — key/mouse events are delivered synchronously and directly from WPF's own event dispatch, no queue, no runtime file loading. Real Chaosnet networking (`uch11.c`) stays a separate, future port; this port gives the four Chaos registers real boot activity touches (`ch_csr`/`ch_myaddr`/`ch_rbf`/`ch_rbc`/`ch_xmt`) a minimal, safe idle-state stub.
- **`WpfKeyTranslator.cs`'s current output cannot be reused as-is.** It produces X11-keysym-style codes with shift baked into the returned value (e.g. unshifted `a` = `0x61`, shifted `A` = `0x41`) — a fundamentally different model from the real Cadet scancode table, which assigns each *physical key* one fixed 7-bit scancode regardless of shift state (shift is communicated separately, as a modifier bit elsewhere in the real protocol). Reusing X11-keysym values directly as the low bits of a delivered scancode would not correspond to any real Cadet-table entry for most keys, so real Lisp software would receive meaningless codes for ordinary typing — silently defeating the purpose of fixing the wire protocol at all. The real per-key scancode values are, however, already sitting in this repo as data: `usim/sdl3-keyboard-cadet-scancodes.defs` (physical key name → 7-bit octal scancode) and `usim/sdl3-keyboard-default-mapping.defs` (SDL/physical key → Cadet key name). This port replaces `WpfKeyTranslator`'s key-to-code step with a small, hardcoded C# table built by combining those two real files' data (covering ordinary US-layout typing keys: letters, digits, punctuation, function keys F1-F7, arrows, Home/End/PageUp/PageDown, modifiers) — independently verified via a Python script rather than transcribed by hand (see the Octal Literals table). This is a data substitution, not an architectural one: no `.defs`-file runtime loading, no per-name string lookup machinery, just a `Dictionary<Key, int>` (or switch) with the real values baked in, replacing `TranslateKey`'s existing switch body.
- **The keyboard-triggered cold/warm-reboot hotkey is dropped, not replicated.** The real trigger is a *separate* mechanism from ordinary scancode delivery: `sdl3-keyboard.c` detects all four Meta/Control keys plus Rubout or Return held simultaneously (a genuine multi-key combo detector, independent of the Cadet-scancode table) and feeds a sentinel value into `iob_set_keyboard_ready` when it fires. Without replicating that combo detector, nothing in this port would ever produce the sentinel, so keeping the sentinel-comparison logic in `SetKeyboardReady` would be dead code. Since `MachineControl.PowerOn()` is already reachable through this project's existing CLI/API paths, the keyboard hotkey is a hardware convenience feature, not a functional gap — this port omits both the combo detector and the sentinel-comparison logic in `SetKeyboardReady` entirely, rather than leaving unreachable code behind.
- **No queue/backoff means a possible overwrite, not a hang or a drop.** The real C's SDL3 thread retries with exponential backoff (up to ~256ms) if the keyboard-ready bit is still set, to avoid clobbering an unread scancode; queuing overflow is only handled by the (unported) `.defs`-table-driven thread wrapper. Without that wrapper, calling `SetKeyboardReady`/`SetMouseReady` directly and unconditionally (matching `iob_set_keyboard_ready`/`iob_set_mouse_ready`'s own bodies, which have no internal gating at all — the gating lives entirely in the thread wrapper this port doesn't replicate) means a second keypress arriving before microcode reads the first one overwrites it rather than queuing. This is judged safe in practice: WPF key events arrive at human typing speed, many orders of magnitude slower than microcode's read loop, and losing an occasional keystroke under real contention is a graceful degradation, not a correctness bug — no queue exists to overflow silently either.
- **Mouse raw encoder bits (`rawX`/`rawY`) are always 0.** The real hardware reports raw quadrature-encoder ticks alongside the resolved X/Y position; WPF delivers discrete `MouseMove` events with an absolute position, not encoder ticks, so there is no realistic value to synthesize. The bit *layout* (`0x7777` mask, shift amounts 12/14) is preserved exactly so any code that does inspect those bits sees deterministic zeros rather than garbage, not because zero is a faithful hardware value.
- **The interval timer (`0764124` write, SDL3-only in the real C via `setitimer`/`SIGALRM`) is tick-counted, not signal-based.** Matching the same coarse-tick approximation already established for TV/color-TV interrupt timing (see both TV specs' Decisions sections): a countdown decremented once per `Iob.Tick()`, firing `UCode.AssertUnibusInterrupt(0274)` at zero. The real C's `16 * v` microsecond unit is converted to an equivalent tick count using the same ~16ms-per-tick assumption WpfBackend/the headless loop already use elsewhere.
- **The 60Hz clock (`the_60_cycle_clock`, `0764124` read) finally gets a home.** Both TV specs explicitly flagged this as "a separate IOB-owned counter, out of scope for TV/color TV" — this port is that home: incremented once per `Iob.Tick()`, read back as a free-running 16-bit counter (matching the real C's `uint16_t the_60_cycle_clock`).
- **A found-but-not-relevant real-C oddity: the cold/warm-boot scancode constants have a likely `<`-vs-`<<` typo in the real source.** `usim/sdl3-keyboard.c:20-21` defines `cold_boot_scancode`/`warm_boot_scancode` using `(0b111111 < 10)` — a *comparison*, not the evidently-intended `(0b111111 << 10)` shift. Noted here only because it was independently verified during design (the "as literally written" values are `0xFF0026`/`0xFF0032`; the "evidently intended" ones would be `0xFFFC26`/`0xFFFC32`) — it has no bearing on this port at all, since the entire cold/warm-boot-hotkey mechanism these constants belong to is out of scope (see the Scope decision above).
- **Register width is genuinely 16 bits, not 32.** `iob_unibus_read`/`iob_unibus_write` take `uint16_t *pv`/`uint16_t v` — unlike every XBus-I/O device ported so far (disk, TV, color TV), which use 32-bit registers. `Iob`'s own read/write methods use `ushort`, and `BusAdaptor`'s dispatch masks to `0xFFFF` at the boundary rather than silently carrying a 32-bit value through a 16-bit device.
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
| `0x7777` (mouse position/encoder mask) | — | `0x7777` binary `0111011101110111` | every 4th bit reserved for raw-encoder state, preserved as a bit-layout constant even though this port always writes 0 into those bit positions |

**The real per-key Cadet scancode table** (hardcoded WPF `Key` → real scancode, per the "`WpfKeyTranslator.cs`'s current output cannot be reused as-is" Decision above), computed by combining `usim/sdl3-keyboard-cadet-scancodes.defs` (physical key → octal scancode) with `usim/sdl3-keyboard-default-mapping.defs` (SDL/physical key → Cadet key name) via a Python script, not by hand — every value below is independently derived, not transcribed from either `.defs` file's own notation:

| WPF `Key` | Hex | Octal | WPF `Key` | Hex | Octal |
|---|---|---|---|---|---|
| `Escape` | `0x63` | `0143` | `Home` | `0x77` | `0167` |
| `F1` | `0x61` | `0141` | `End` | `0x6E` | `0156` |
| `F2` | `0x22` | `042` | `PageUp` | `0x37` | `067` |
| `F3` | `0x26` | `046` | `PageDown` | `0x27` | `047` |
| `F4` | `0x20` | `040` | `Up` | `0x46` | `0106` |
| `F5` | `0x4E` | `0116` | `Down` | `0x7E` | `0176` |
| `F6` | `0x48` | `0110` | `Left` | `0x4F` | `0117` |
| `F7` | `0x28` | `050` | `Right` | `0x0F` | `017` |
| `Grave`/OemTilde | `0x3F` | `077` | `LeftShift`/`RightShift` | `0x14` | `024` |
| `D1` | `0x51` | `0121` | `LeftCtrl`/`RightCtrl` | `0x10` | `020` |
| `D2` | `0x31` | `061` | `LeftWin`/`RWin` (super) | `0x05` | `05` |
| `D3` | `0x71` | `0161` | `LeftAlt`/`RightAlt` (meta) | `0x25` | `045` |
| `D4` | `0x09` | `011` | `Space` | `0x5C` | `0134` |
| `D5` | `0x49` | `0111` | `A` | `0x53` | `0123` |
| `D6` | `0x29` | `051` | `B` | `0x4C` | `0114` |
| `D7` | `0x69` | `0151` | `C` | `0x74` | `0164` |
| `D8` | `0x19` | `031` | `D` | `0x73` | `0163` |
| `D9` | `0x39` | `071` | `E` | `0x72` | `0162` |
| `D0` | `0x79` | `0171` | `F` | `0x0B` | `013` |
| `OemMinus` | `0x59` | `0131` | `G` | `0x4B` | `0113` |
| `OemPlus` (`=`) | `0x56` | `0126` | `H` | `0x2B` | `053` |
| `Back` (Backspace → rubout) | `0x13` | `023` | `I` | `0x1A` | `032` |
| `Tab` | `0x12` | `022` | `J` | `0x6B` | `0153` |
| `Q` | `0x52` | `0122` | `K` | `0x1B` | `033` |
| `W` | `0x32` | `062` | `L` | `0x3B` | `073` |
| `Return` | `0x5E` | `0136` | `M` | `0x6C` | `0154` |
| `CapsLock` | `0x55` | `0125` | `N` | `0x2C` | `054` |
| `OemOpenBrackets`/`OemCloseBrackets` (brace_left/right) | `0x76`/`0x66` | `0166`/`0146` | `O` | `0x3A` | `072` |
| `OemBackslash`/`OemPipe` | `0x1F` | `037` | `P` | `0x7A` | `0172` |
| `OemSemicolon` | `0x7B` | `0173` | `R` | `0x0A` | `012` |
| `OemQuotes` | `0x5B` | `0133` | `S` | `0x33` | `063` |
| `OemComma` | `0x1C` | `034` | `T` | `0x4A` | `0112` |
| `OemPeriod` | `0x3C` | `074` | `U` | `0x6A` | `0152` |
| `OemQuestion` (`/`) | `0x7C` | `0174` | `V` | `0x0C` | `014` |
| | | | `X` | `0x34` | `064` |
| | | | `Y` | `0x2A` | `052` |
| | | | `Z` | `0x54` | `0124` |

Any WPF `Key` not in this table (function keys F8-F12, numpad, Insert/Delete/PrintScreen/etc.) has no real default Cadet mapping either (confirmed by the `.defs` file's own "no default mapping" comment block) and returns 0 (`XK_VoidSymbol`-equivalent), matching the real system's own behavior for those keys.
| register addresses (`0764100`-`0764176` octal) | — | `0x3E840`-`0x3E87E` | already independently verified as the existing `IobLo`/`IobHi` `BusAdaptor.cs` constants from an earlier phase; individual register offsets re-derived in the Register Model section below |

## Register Model

All registers are 16-bit words at Unibus addresses `0764100`-`0764176` octal (`0x3E840`-`0x3E87E`), computed as offsets from `IobLo` (`0x3E800`) in 2-byte (1-word) steps — i.e. register `N` is at `IobLo + 2*N` for the low half of the visible range.

**Keyboard** (`0764100`/`0764102` = offsets `0x20`/`0x22` from `IobLo`):
- Read offset `0x20` (kbd low): returns `_scancode & 0xFFFF`, then clears the keyboard-ready bit (CSR bit 5).
- Read offset `0x22` (kbd high): returns `(_scancode >> 16) & 0xFFFF`. Does **not** clear the ready bit again (already cleared by the low-word read, matching the real C's comment `// CSR cleared above when reading 0100`).
- `SetKeyboardReady(uint scancode)`: `_scancode = scancode`; set CSR bit 5; if CSR bit 2 (keyboard-interrupt-enable) is set, call `UCode.AssertUnibusInterrupt(0xB0)`. (No cold/warm-boot sentinel check — that mechanism is dropped, see Decisions.)

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
    private const uint ChaosMyAddrPlaceholder = 0x7C20; // 0177040 octal, uch11.c's LOCAL-CADR fallback

    private readonly UCode _ucode;

    public Action<int>? OnBeep { get; set; }

    private uint _csr;
    private uint _scancode;
    private uint _mouseX;
    private uint _mouseY;
    private ushort _the60CycleClock;
    private uint _intervalTimerTicksRemaining;
    private readonly Stopwatch _usecStopwatch = new();
    private bool _usecStarted;

    public Iob(UCode ucode)
    {
        _ucode = ucode;
    }

    private bool IsKeyboardInterruptEnabled() => (_csr & 0x4) != 0;
    private bool IsMouseInterruptEnabled() => (_csr & 0x2) != 0;

    /// <summary>
    /// Faithful port of iob_set_keyboard_ready (usim/iob.c:88-107, WITH_SDL3
    /// branch), minus the cold/warm-boot sentinel check (that hotkey
    /// mechanism is dropped -- see Decisions). Called directly from
    /// Keyboard's key-down path -- no queue, no retry/backoff (see Decisions).
    /// </summary>
    public void SetKeyboardReady(uint scancode)
    {
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
2. **`MachineControl.cs`**: constructs `Iob = new Iob(UCode);` after `ColorTv`, wires it via `UCode.BusAdaptor.WireIob(Iob)`. Subscribes `Iob.OnBeep = halfWavelengthMicros => DisplayBackend?.Beep(halfWavelengthMicros, 130000);` (matching `WpfBackend.Beep(int halfWavelengthMicros, int durationMicros)`'s real signature and the fixed 130000-microsecond duration derived in the Register Model section above). Both `Reset()`/`InitializeComponents()` and the headless `Run()` loop's tick site gain `Iob.Tick()` alongside `Tv.Tick()`/`ColorTv.Tick()`.
3. **`Keyboard.cs`**: sheds `AddKey`/`GetKey`/`HasKeys`/`BufferCount`/the `CadrKeyCode` modifier-bit-packing enum entirely. `WpfKeyTranslator.TranslateKey`'s switch body is replaced with the real hardcoded Cadet-scancode table (see Octal Literals). `KeyDown(uint keysym, int modifiers)`/`KeyUp` combine the translated code into the real scancode encoding (`v = ((!keydown) << 8) | code`) and call `_iob.SetKeyboardReady(v)` — `Keyboard` gains a constructor-injected `Iob` reference (or a settable-once `WireIob`, matching the session's established pattern for a dependency that must exist before construction completes).
4. **`Mouse.cs`**: `MoveTo`/`SetButton` (renamed/reshaped as needed during planning to match toggle semantics rather than press/release flags for `_tail`/`_middle`/`_head`) call `_iob.SetMouseReady(x, y, tailToggled, middleToggled, headToggled)` on every position or button change. `ReadXRegister`/`ReadYRegister`/`ReadButtonsRegister`/`MouseButtonBits` and the invented 11-bit linear XY scaling are removed — `Iob` is now the sole owner of the real register encoding.
5. **`usim-cs/IOBus.cs`**: deleted entirely (the invented `IIODevice`/device-registry framework, never wired into `BusAdaptor`, has no faithful counterpart and no remaining callers once `Iob.cs` exists).
6. **`TraceLog.cs`**: no change needed — `TraceCategory.IOBus = 128` already exists (confirmed by direct inspection), just unused until this port's `Iob.cs` starts calling `TraceLog.Instance.Warning(TraceCategory.IOBus, ...)`.

## Testing

`IobTests.cs` (new): CSR read/write masking (low 4 bits only); keyboard-ready set via `SetKeyboardReady`, read-clears-on-low-word-only, interrupt gating on CSR bit 2; mouse-ready set via `SetMouseReady`, read-clears-on-Y-only, interrupt gating on CSR bit 1; the real `0x7777`-masked bit layout for a representative X/Y value with `rawX`/`rawY` always 0; the usec clock returning 0 on its first read and a monotonically increasing value on subsequent reads; the 60Hz clock incrementing exactly once per `Tick()` call and wrapping correctly past `ushort.MaxValue`; the interval timer firing `AssertUnibusInterrupt(0xBC)` at exactly the computed tick count, not one tick early or late; every GPIO/serial/undocumented/Chaos-stub register returning its documented value without throwing, on both read and write. A handful of `WpfKeyTranslator`'s hardcoded scancode-table entries get spot-checked against the Octal Literals table's values (not exhaustively re-verifying all ~55 entries in a test — the table itself was independently computed, not hand-transcribed).

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
