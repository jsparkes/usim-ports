# Color TV Design

## Context

The monochrome TV port (`docs/superpowers/specs/2026-09-11-monochrome-tv-design.md`, merged in [PR #3](https://github.com/jsparkes/usim-ports/pull/3)) replaced the invented `Display.cs` with a faithful port of `usim/tv.c`. This spec covers the second, independent CADR video device: color TV (`usim/colortv.c`, 209 lines, read in full).

Color TV is a genuinely separate piece of hardware from monochrome TV, not a mode of it: 576×454 pixels, 4 bits per pixel, with a 64-entry RGB color map (only 16 of the 64 slots are ever selected by a 4-bit pixel value, but all 64 are writable — `usim/colortv.c:24-26`). It occupies its own physical address ranges (`ColorTvScreenLo/Hi` = `0x3D0000`-`0x3D7FFF`, `ColorTvControlLo/Hi` = `0x3DFFE8`-`0x3DFFEF`), both of which already exist as constants in `usim-cs/BusAdaptor.cs` (added incidentally by an earlier phase) but are currently unrouted — they fall through to the generic un-ported-device placeholder. `UsimState.ColorTvEnabled` already exists too (settable via `--colortv` and `[Display] colortv` config), but nothing consumes it yet.

Real hardware probes for color TV's presence by writing 1 to its screen-buffer base and reading it back (`usim/colortv.c:14-15`); if the echo isn't 1, color TV is treated as absent. Real `bus-adaptor.c` gates both the screen and control address ranges on a global `colortv_enabled` flag (`usim/bus-adaptor.c:79,86`), so when disabled, the probe reads back 0 (unmapped) and real microcode correctly detects absence — this is the same mechanism this port needs, not a separate probe implementation.

Two behavioral differences from monochrome TV that shape this design:

1. **Rendering is lazy, not eager.** `colortv_screen_write` (`usim/colortv.c:74-79`) does no pixel unpacking at all — it just stores the raw word. All pixel-to-RGB unpacking happens once per frame, in the real renderer's present function (`usim/sdl3-video.c:520-599`, `sdl3_video_present_color`), which reads `colortv_screen_buffer` and `colortv_color_map` together at render time. Monochrome TV's `ScreenWrite` unpacks eagerly on every write; color TV's must not.
2. **A busy-loop hazard exists in real Lisp.** `WRITE-COLOR-MAP` (`sys/window/color.lisp:123-146`), when called with `SYNCHRONIZE=T`, spins reading the control register's VSYNC status bit until it clears (`sys/window/color.lisp:139-140`). Monochrome TV's design already established the precedent that VSYNC/HSYNC/sync-PROM-enable status bits are always reported as their "ready" value (never toggled) rather than genuinely simulating hardware timing — the same precedent applies here and prevents this exact loop from ever hanging.

## Decisions

- **New `ColorTv.cs` class**, structurally mirroring `Tv.cs`'s shape (constructor, `Width`/`Height`, `FrameBuffer`, `Reset()`, `ScreenRead`/`ScreenWrite`, `ControlRead`/`ControlWrite`, `Tick()`) but with its own register semantics — the two devices share no code, matching the real C's two independent files.
- **`FrameBuffer` is sized to the exact visible geometry** (`576*454*4` bytes), unlike monochrome's deliberately oversized buffer. Color TV's addressable screen-buffer range (576×454/8 = 32,688 words) is already just under the 0x8000-word buffer size, so there's no analogous "writable-but-off-screen" gap to guard against, and the real C keeps no oversized bitmap for color TV either (`sdl3_video_present_color` iterates exactly `colortv_width * colortv_height` pixels, nothing more).
- **Rendering happens once per `Tick()`, not per `ScreenWrite`.** `ScreenWrite` only stores the raw word (matching `colortv_screen_write` exactly — no unpacking). `Tick()` performs a full unpack of `_screenBuffer` + `_colorMap` into `FrameBuffer`, mirroring the real renderer's per-frame present function. This means a color-map change is reflected on the very next tick with no separate repaint-tracking logic — simpler than monochrome's BOW-transition repaint, since a full-frame unpack every tick is already idempotent and always current.
- **VSYNC/HSYNC (mode-read bits 5/6) toggle once per `Tick()` call; sync-PROM-enabled (bit 7) reflects real state.** This tick-based (non-cycle-accurate) architecture cannot simulate genuine video-timing rates, so toggling both bits once per `Tick()` call (~16ms) is the finest faithful granularity achievable — but real toggling, not a fixed value, is required: `WRITE-COLOR-MAP` (`sys/window/color.lisp:139-140`), when `SYNCHRONIZE=T`, loops *while the VSYNC bit is clear* and only exits once it reads set (a `GO A`/`RETURN` loop, not the reverse), and its `%XBUS-WRITE-SYNC` microcode (`sys/ucadr/uc-cadr.lisp:140-161`) runs unconditionally on every call and waits for a full HSYNC clear-then-set transition before writing. An always-0 VSYNC/HSYNC design (the original approach here) makes both of these hang forever, confirmed against the real Lisp/microcode source — not the safe choice it was originally believed to be. Toggling both bits every `Tick()` bounds either wait to at most two ticks (~32ms). Bit 7 (sync-PROM-enabled) is different in kind: it is not a hardware timing signal at all, so it simply reflects the real, already-tracked `_syncPromEnabled` field (set on offset-3 writes) rather than a fixed or toggling value.
- **`Tick()` is a no-op when `UsimState.ColorTvEnabled` is false** — no unpack, no interrupt assertion. This single early-return captures both of real C's separate enable checks (`usim.c:130`'s `if (colortv_enabled) colortv_assert_interrupt()`, and `sdl3-video.c`'s own `if (colortv_enabled)` guards around all color-window rendering) in one place, even though the real C spreads them across two files.
- **Color-map write's hardware-undefined channel encoding (`channel == 3`) is a warning + no-op, not a crash.** The real C's `colortv_control_write` case 4 calls `errx(1, ...)` for this case (`usim/colortv.c:179-180`) — a genuine emulator-halting error. The header/schematic comment (`usim/bus-adaptor.c`'s color-map-write doc, mirrored in this port) says this 2-bit field's value `3` ("11") is simply "not defined" by the hardware, implying real microcode never emits it. Consistent with the monochrome port's identical judgment call on `tv_init()`'s practically-unreachable fatal path, this port treats it as a warning rather than a process-ending exception; final review will re-verify against real boot activity whether this path is ever actually reached (if it is, that's itself a significant finding, not a silent miss).
- **`ColorTv.Reset()` is a deliberate, literal no-op.** The real `colortv_bus_reset()` (`usim/colortv.c:205-208`) has a genuinely empty body — color TV state is never cleared by any reset, unlike monochrome TV's `tv_reset()`. `MachineControl` still calls `ColorTv.Reset()` at the same call sites it calls `Tv.Reset()`, for wiring symmetry; the method itself does nothing, with a comment explaining why.
- **`ColorTv` is always constructed**, mirroring `Tv`'s unconditional construction — cheap, and matches the real C's globals always existing in memory regardless of `colortv_enabled`. The enable gate lives entirely in `BusAdaptor`'s dispatch (routes to `ColorTv` only when `UsimState.ColorTvEnabled`; otherwise falls through to the generic placeholder, preserving the real hardware-probe-reads-0-when-absent behavior) and in `WpfBackend`'s decision whether to create the second window — not in whether the C# object exists.
- **A second, independent WPF window**, created in `WpfBackend.Initialize()` only when `UsimState.ColorTvEnabled` is true, matching real C's separate `color_window`/`color_renderer`/`color_texture` (`usim/sdl3-video.c`). Fixed 576×454 (no `AllowResize`/`Scale` interaction needed beyond what the main window already has — same `Viewbox` pattern), its own title, and no keyboard/mouse handlers of its own — real hardware has one shared keyboard/mouse for the whole machine, already handled by the main window. Both windows share the existing single `DispatcherTimer`; `Tick()` blits both bitmaps from the one tick.

## Octal Literals — Independently Verified

Computed via Python, not by hand:

| Literal | Decimal | Hex | Meaning |
|---|---|---|---|
| `010` | 8 | `0x8` | interrupt-enable (mode bit 3) — same bit position as monochrome TV |
| `0200` | 128 | `0x80` | sync-PROM-enable status bit (mode-read bit 7) *and* the vert-spacing write's sync-PROM-enable gate bit (bit 7) — color TV reuses this one literal for both, unlike monochrome which derives sync-PROM-enable from vert-spacing on every read; color TV caches it into its own `_syncPromEnabled` bool at write time instead (see Register Model) |
| `037` | 31 | `0x1F` | mode-write mask (bits 0-4) |
| `0177` | 127 | `0x7F` | vert-spacing mask (bits 0-6) |
| `0100000` | 32768 | `0x8000` | screen-buffer word count (32K words) — same size as monochrome TV's buffer |
| `040` | 32 | `0x20` | VSYNC status bit (mode-read bit 5) |
| `0100` | 64 | `0x40` | HSYNC status bit (mode-read bit 6) |
| `(1<<4)` | 16 | `0x10` | VERT-FLAG / interrupt-request bit (mode bit 4) — already a shift form, no octal risk, same bit position as monochrome |

Hex literals already in the real C or in this port's addressing (`0x3F` location mask, `0xFF` color-value byte mask, `0x0FFF` sync-pointer mask, `0x3D0000`-range address constants already verified by the phase that added them to `BusAdaptor.cs`) carry no octal-transcription risk and are not re-tabulated.

## Register Model

**Screen buffer** (`_screenBuffer[0x8000]`, `uint[]`):
- `ScreenRead(offset)` / `ScreenWrite(offset, v)` store and retrieve the raw word — no pixel unpacking, no `FrameBuffer` interaction. This is the one place color TV's register model differs structurally from monochrome's: `Tv.ScreenWrite` unpacks immediately; `ColorTv.ScreenWrite` does not.

**Color map** (`_colorMap[64]`, `uint[]`, ARGB with alpha always `0xFF`):
- Not directly readable via any control-register offset (offset 4 is write-only, matching the real C's comment `// case 4: color map is write-only`).
- Write decodes a 32-bit value: `location = v & 0x3F` (6 bits), `channel = (v >> 6) & 0x3` (2 bits: 0=R, 1=G, 2=B), `value = 255 - ((v >> 8) & 0xFF)` (8 bits, inverted — 0 is max intensity, 255 is min, matching the real C's comment exactly). The write reads the current 32-bit map entry, replaces only the byte for the written channel (R → bits 16-23, G → bits 8-15, B → bits 0-7), forces bit 24-31 (alpha) to `0xFF`, and stores it back. `channel == 3` logs a warning and performs no write (see Decisions).

**Control registers** (offsets 0-4 via `ControlRead`/`ControlWrite`):
- Offset 0 (mode): write masks the input to `v & 0x1F` and stores it. Read returns the stored mode OR'd with three status bits at positions 5 (VSYNC), 6 (HSYNC), 7 (sync-PROM-enabled) — bits 5/6 reflect `_vsync`/`_hsync`, toggled once per `Tick()` call; bit 7 reflects the real, already-tracked `_syncPromEnabled` field — see Decisions for why genuine toggling (not a fixed 0) is required.
- Offset 1 (sync data): read/write behavior is identical in shape to monochrome TV's offset 1 — gated on `_syncPromEnabled` (see below), reads 0 / no-op-writes when the sync PROM is enabled, otherwise reads/writes `_syncRam[_syncPtr]`.
- Offset 2 (sync pointer, write-only): `_syncPtr = v & 0x0FFF`, identical shape to monochrome.
- Offset 3 (vert spacing, write-only): `_syncPromEnabled = (v & 0x80) == 0` (cached directly at write time — unlike monochrome, which derives this on every read from a single stored `_vertSpacing` field, color TV's real C stores a separate `sync_prom_enb` bool alongside `colortv_vert_spacing`, so this port keeps both fields too, for faithfulness, even though `_vertSpacing` itself has no other reader in the real C beyond this same masked store). `_vertSpacing = v & 0x7F`.
- Offset 4 (color map, write-only): see Color map above. Reading offset 4, and any offset ≥ 5, falls to the default case: logs a warning and calls `UCode.BusInterface.SetXbusNxm()` — identical convention to monochrome TV's default case.

## Tick / Interrupt

`Tick()`:
```
if (!UsimState.ColorTvEnabled) return;

// unpack _screenBuffer + _colorMap into FrameBuffer, LSB-nibble-first,
// 8 pixels per word, row-major (576/8 = 72 words per row exactly, no
// cross-row word-boundary spillover)

if (IsInterruptEnabled())  // mode bit 3
{
    _mode |= 1 << 4;  // VERT-FLAG
    _ucode.AssertXbusInterrupt();
}
```
Called from the same ~16ms tick sites as `Tv.Tick()` (`WpfBackend.Tick()` and `MachineControl.Run()`'s headless loop), unconditionally — the enable check lives inside `Tick()` itself, not at the call site, so both tick sites stay simple and uniform between the two devices.

## Class Shape

```csharp
// ColorTv.cs - Faithful port of usim/colortv.c (color TV display). A
// genuinely separate device from Tv (monochrome), not a mode of it. See
// docs/superpowers/specs/2026-09-14-color-tv-design.md.

using System;

namespace Usim;

public class ColorTv
{
    private const int MaxWords = 0x8000; // colortv_screen_buffer's real allocated size (32K words)

    public uint Width { get; } = 576;
    public uint Height { get; } = 454;

    // Sized to the exact visible geometry -- unlike Tv's deliberately
    // oversized FrameBuffer, color TV's addressable screen-buffer range
    // (576*454/8 = 32,688 words) is already just under MaxWords, so there
    // is no writable-but-off-screen gap to guard against here.
    public byte[] FrameBuffer { get; } = new byte[576 * 454 * 4];

    private readonly UCode _ucode;
    private readonly uint[] _screenBuffer = new uint[MaxWords];
    private readonly uint[] _colorMap = new uint[64];
    private readonly byte[] _syncRam = new byte[4096];
    private uint _mode;
    private uint _syncPtr;
    private uint _vertSpacing;
    private bool _syncPromEnabled;
    private bool _hsync;
    private bool _vsync;

    public ColorTv(UCode ucode)
    {
        _ucode = ucode;
    }

    /// <summary>
    /// Faithful port of colortv_bus_reset (usim/colortv.c:205-208), which
    /// has a genuinely empty body -- color TV state is never cleared by
    /// any reset in the real C, unlike Tv.Reset(). Kept as an explicit
    /// method (rather than omitted) purely for wiring symmetry with Tv.
    /// </summary>
    public void Reset()
    {
    }

    /// <summary>Faithful port of colortv_screen_read (usim/colortv.c:68-72).</summary>
    public uint ScreenRead(uint offset)
    {
        uint v = _screenBuffer[offset];
        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
            $"colortv: screen read: offset:{offset} v:0x{_screenBuffer[offset]:X}");
        return v;
    }

    /// <summary>
    /// Faithful port of colortv_screen_write (usim/colortv.c:74-79). Unlike
    /// Tv.ScreenWrite, this does NOT unpack into FrameBuffer -- real color
    /// TV rendering happens once per frame in Tick(), not per write.
    /// </summary>
    public void ScreenWrite(uint offset, uint v)
    {
        _screenBuffer[offset] = v;
        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
            $"colortv: screen write: offset:{offset} v:0x{v:X}");
    }

    /// <summary>Faithful port of colortv_control_read (usim/colortv.c:82-113).</summary>
    public uint ControlRead(uint offset)
    {
        switch (offset)
        {
            case 0:
                {
                    // Bit 7 (sync-PROM-enabled) reflects the real, deterministic
                    // _syncPromEnabled field (set on offset-3 writes) -- it is
                    // NOT a hardware timing signal, unlike bits 5/6. Bits 5/6
                    // (VSYNC/HSYNC) reflect _vsync/_hsync, which Tick() toggles
                    // once per call -- see Tick()'s comment for why this
                    // (not literally always 0) is required for correctness.
                    uint result = _mode
                        | (_syncPromEnabled ? 0x80u : 0u)
                        | (_hsync ? 0x40u : 0u)
                        | (_vsync ? 0x20u : 0u);
                    TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                        $"colortv: read mode: 0x{result:X}");
                    return result;
                }

            case 1:
                {
                    uint v = _syncPromEnabled ? 0u : _syncRam[_syncPtr];
                    if (!_syncPromEnabled)
                    {
                        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                            $"colortv: read sync_ram[0x{_syncPtr:X}] = 0x{v:X}");
                    }
                    TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                        $"colortv: read sync data: 0x{v:X}");
                    return v;
                }

            default:
                TraceLog.Instance.Warning(TraceCategory.Display,
                    $"colortv: read invalid offset:{offset}");
                _ucode.BusInterface.SetXbusNxm();
                return 0;
        }
    }

    /// <summary>Faithful port of colortv_control_write (usim/colortv.c:115-197).</summary>
    public void ControlWrite(uint offset, uint v)
    {
        switch (offset)
        {
            case 0:
                TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                    $"colortv: write mode: 0x{v:X}");
                _mode = v & 0x1F;
                break;

            case 1:
                TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                    $"colortv: write sync data: 0x{v:X}");
                if (!_syncPromEnabled)
                {
                    _syncRam[_syncPtr] = (byte)(v & 0xFF);
                    TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                        $"colortv: write sync_ram[0x{_syncPtr:X}] = 0x{_syncRam[_syncPtr]:X}");
                }
                break;

            case 2:
                TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                    $"colortv: write sync pointer: 0x{v:X}");
                _syncPtr = v & 0x0FFF;
                break;

            case 3:
                TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                    $"colortv: write vert spacing: 0x{v:X}");
                _syncPromEnabled = (v & 0x80) == 0;
                _vertSpacing = v & 0x7F;
                break;

            case 4:
                {
                    TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                        $"colortv: write color map: 0x{v:X}");
                    uint colorChannelValue = 255 - ((v >> 8) & 0xFF);
                    uint colorChannel = (v >> 6) & 0x3;
                    uint location = v & 0x3F;
                    uint currentValue = _colorMap[location];
                    uint newValue;
                    switch (colorChannel)
                    {
                        case 0:
                            newValue = (currentValue & 0x0000FFFF) | (colorChannelValue << 16);
                            break;
                        case 1:
                            newValue = (currentValue & 0x00FF00FF) | (colorChannelValue << 8);
                            break;
                        case 2:
                            newValue = (currentValue & 0x00FFFF00) | colorChannelValue;
                            break;
                        default:
                            TraceLog.Instance.Warning(TraceCategory.Display,
                                $"colortv: write invalid color channel:{colorChannel} (hardware-undefined 2-bit encoding 3)");
                            return;
                    }
                    _colorMap[location] = 0xFF000000 | newValue;
                    TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                        $"colortv: write loc:{location} channel:{colorChannel} value:{colorChannelValue} final:0x{_colorMap[location]:X8}");
                }
                break;

            default:
                TraceLog.Instance.Warning(TraceCategory.Display,
                    $"colortv: write invalid offset:{offset}");
                _ucode.BusInterface.SetXbusNxm();
                break;
        }
    }

    private bool IsInterruptEnabled() => (_mode & 0x8) != 0;

    /// <summary>
    /// Faithful port of colortv_assert_interrupt (usim/colortv.c:49-58),
    /// combined with the two real enable-gates that wrap rendering/
    /// interrupt assertion in usim.c/sdl3-video.c into one early-return.
    /// </summary>
    public void Tick()
    {
        if (!UsimState.ColorTvEnabled)
        {
            return;
        }

        // Real hardware toggles VSYNC/HSYNC continuously at video timing
        // rates; this emulator only updates color-TV state once per Tick()
        // (~16ms), so toggling both here is the finest faithful
        // approximation achievable -- but it is NOT optional. Real Lisp
        // (sys/window/color.lisp's WRITE-COLOR-MAP) both explicitly waits
        // for a VSYNC transition (when SYNCHRONIZE=T) and, unconditionally
        // on every call, relies on microcode (%XBUS-WRITE-SYNC) that waits
        // for a full HSYNC clear-then-set transition before writing the
        // color map. Never toggling these bits makes both waits hang
        // forever -- this was a real, confirmed bug in the original design.
        _vsync = !_vsync;
        _hsync = !_hsync;

        UnpackFrameBuffer();

        if (IsInterruptEnabled())
        {
            _mode |= 1u << 4;
            _ucode.AssertXbusInterrupt();
        }
    }

    /// <summary>
    /// Faithful port of sdl3_video_present_color's unpacking loop
    /// (usim/sdl3-video.c:544-577), minus the hsync/vsync toggling and
    /// per-scanline timing (see the design's Out of Scope). LSB-nibble-
    /// first: bit 0-3 of a word is the first pixel in its run of 8.
    /// </summary>
    private void UnpackFrameBuffer()
    {
        uint wordIndex = 0;
        uint bitsInWord = 0;
        uint word = _screenBuffer[0];

        for (uint y = 0; y < Height; y++)
        {
            for (uint x = 0; x < Width; x++)
            {
                uint pixel = word & 0xF;
                uint color = _colorMap[pixel];
                uint byteIndex = (y * Width + x) * 4;
                FrameBuffer[byteIndex + 0] = (byte)(color & 0xFF);         // B
                FrameBuffer[byteIndex + 1] = (byte)((color >> 8) & 0xFF); // G
                FrameBuffer[byteIndex + 2] = (byte)((color >> 16) & 0xFF); // R
                FrameBuffer[byteIndex + 3] = (byte)((color >> 24) & 0xFF); // A

                bitsInWord += 4;
                if (bitsInWord == 32)
                {
                    bitsInWord = 0;
                    wordIndex++;
                    word = _screenBuffer[wordIndex];
                }
                else
                {
                    word >>= 4;
                }
            }
        }
    }
}
```

## Wiring

1. **`BusAdaptor.cs`**: gains settable-once `ColorTv? _colorTv` + `WireColorTv(ColorTv colorTv)`, mirroring `_tv`/`WireTv`. `ReadXbusIo`/`WriteXbusIo` gain new branches for `ColorTvScreenLo/Hi` and `ColorTvControlLo/Hi`, placed alongside the existing `TvScreenLo/Hi`/`TvControlLo/Hi` branches, each guarded by `UsimState.ColorTvEnabled &&` in addition to the address-range check and the existing null-guard-and-warn convention — when disabled, falls through to the generic placeholder exactly as today (preserving the real hardware-probe-reads-0-when-absent behavior). `DescribeXbusIo`'s existing `"color TV screen"`/`"color TV control"` lines become unreachable when enabled (real dispatch now handles it) but stay reachable and correct when disabled — no change needed to `DescribeXbusIo` itself.
2. **`MachineControl.cs`**: constructor builds `ColorTv = new ColorTv(UCode);` right after `Tv` construction, then `UCode.BusAdaptor.WireColorTv(ColorTv);`. New `public ColorTv ColorTv { get; private set; }` property. Both `Reset()` and `InitializeComponents()` call `ColorTv.Reset()` alongside the existing `Tv.Reset()` call (a no-op, per Decisions, but kept for wiring symmetry). The headless `Run()` loop's tick site calls `ColorTv.Tick()` alongside the existing `Tv.Tick()` call, unconditionally (the enable check is inside `Tick()` itself).
3. **`WpfBackend.cs`**: constructor gains a `ColorTv colorTv` parameter (alongside the existing `Tv tv`). `Initialize()` gains, after the existing main-window setup: if `UsimState.ColorTvEnabled`, construct a second `WriteableBitmap` (576×454, `Pbgra32`), a second `Image`/`Viewbox`, and a second `Window` (title `"USIM - Color TV"`, no `KeyDown`/`KeyUp`/`MouseMove`/`MouseDown`/`MouseUp` handlers, no `Cursor = Cursors.None` requirement since it's display-only) — shown alongside the main window, not nested inside it. The existing single `DispatcherTimer`'s `Tick()` method calls `_colorTv.Tick()` and, if the color window was created, blits its bitmap the same way `UpdateBitmap()` does for the main one. `Dispose()` closes both windows if both exist.
4. **`MachineControl.InitializeDisplay`**: passes `ColorTv` as the new `WpfBackend` constructor argument.

## Testing

`ColorTvTests.cs` (new), mirroring `TvTests.cs`'s structure:
- `ScreenWrite`/`ScreenRead` round-trips the raw word and does **not** touch `FrameBuffer` (asserts `FrameBuffer` is still all-zero after a `ScreenWrite` with `UsimState.ColorTvEnabled` left false, or more precisely: calls `ScreenWrite` without calling `Tick()`, and asserts `FrameBuffer` is unchanged from its constructed-zero state) — the key behavioral difference from monochrome TV's eager unpacking.
- `Tick()`'s LSB-nibble-first unpacking: write a color-map entry, write a screen word with a known nibble pattern, call `Tick()` with `UsimState.ColorTvEnabled = true`, assert the correct pixel bytes land in `FrameBuffer` at the correct offset (and that a later nibble in the same word lands at the correct *later* pixel, proving nibble order, not just single-pixel correctness).
- Mode-write masking (`ControlWrite(0, 0xFF)` then `ControlRead(0) == 0x1F`, valid when the test never calls `Tick()` or writes offset 3, so bits 5-7 all still read their default-`false` state of 0).
- `Tick()` toggles VSYNC/HSYNC (mode-read bits 5/6) once per call: read the mode register before any `Tick()` (both bits clear), call `Tick()` once (both bits set), call `Tick()` again (both clear again) — this is required behavior, not an always-0 status (see Decisions: an always-0 design was the original, incorrect approach and would hang real Lisp/microcode sync-waits).
- Mode-read's bit 7 (sync-PROM-enabled) reflects the real `_syncPromEnabled` state: write offset 3 with bit 7 clear (sync PROM enabled) and confirm mode-read bit 7 is set; write offset 3 with bit 7 set (sync PROM disabled) and confirm mode-read bit 7 is clear.
- Sync-PROM gating: writing offset 3 with bit 0x80 set/clear correctly gates offset 1 reads/writes, same shape as monochrome's test.
- Color-map write: three sequential writes (one per channel) to the same `location`, asserting the final stored value has all three channel bytes correctly set and independently preserved (not clobbered by the later writes), plus alpha forced to `0xFF`.
- Channel-3 write: asserts no exception is thrown, and that the target `_colorMap` entry is left completely untouched by the invalid write (verified by writing a known value to that location via a legitimate channel first, then issuing the channel-3 write, then confirming the location's stored value is unchanged). No assertion on log output — no test in this codebase captures `TraceLog` output, and this port doesn't need to be the first.
- `Tick()`'s interrupt gating: same shape as monochrome's test, using a real `UCode` instance's `InterruptPendingFlag`.
- `Tick()` with `UsimState.ColorTvEnabled = false`: asserts `FrameBuffer` is untouched by a `Tick()` call (proving the early return actually skips the unpack) and that no interrupt fires even with the interrupt-enable bit set.

`BusAdaptorTests.cs` gains a color-TV dispatch test with two cases: `UsimState.ColorTvEnabled = false` (with a real `ColorTv` wired) confirms the range still returns the generic-placeholder's value (0), not anything from `ColorTv`; `UsimState.ColorTvEnabled = true` confirms a real round-trip through `ColorTv`, mirroring the existing `TestTvRangeDispatchesToRealTv` pattern. (Save/restore `UsimState.ColorTvEnabled` around this test, matching the established `UsimState.TvWidth`/`TvHeight`/`DiskUnits` save-restore convention, since it's process-wide static state.)

`WpfBackendTests.cs`: no new test for actual second-window creation (not practically unit-testable in this harness, per Decisions) — real-data/manual verification covers this instead, during final review.

## Global Constraints

- No new NuGet dependencies.
- Every octal literal in this spec was independently verified via Python — see the table above.
- `colortv_hsync`/`colortv_vsync`'s literal per-scanline toggling, the real renderer's `nanosleep`-paced scanline timing, and the dirty-rect-adjacent `accumulate_update`-style bookkeeping some backends use are not ported — out of scope, no observable effect once the status bits are fixed at 0 (see Decisions).
- `colortv_init()`'s only real behavior is a log notice (`usim/colortv.c:199-203`) — not ported as a separate method; `MachineControl` already logs on `ColorTv` construction via the existing `TraceCategory.Display` conventions if a log is wanted, but no new logging is required by this spec.
- Color TV's window is display-only — no keyboard/mouse routing through it, matching the real single-shared-input-device hardware.

## Out of Scope

- Any change to `Tv.cs`/monochrome TV's own behavior — this spec is purely additive.
- `tv_save_screenshot`-equivalent for color TV (the real C has none).
- Any UI for toggling `UsimState.ColorTvEnabled` at runtime (it's a startup-time config/CLI flag today, same as before this spec, and this spec doesn't change that).
