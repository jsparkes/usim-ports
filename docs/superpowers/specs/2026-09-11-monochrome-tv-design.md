# Monochrome TV Port — Design Spec

## Context

`usim-cs/Display.cs` is entirely invented — fabricated `WriteVideoMemory`/`ReadVideoMemory` API with no relationship to the real hardware's control registers (mode/BOW/interrupt-enable/sync-PROM/VSYNC/HSYNC), and its monochrome bit-unpacking is **backwards**: it reads bit `31-i` for pixel `i` within a 32-pixel run; the real hardware (and the reference emulator) is LSB-first (`v & 1` tested first, then `v >>= 1`). What's rendered today doesn't match what real CADR software would produce, even before considering the missing register model. This spec replaces `Display.cs` with a faithful `Tv.cs`, matching `usim/tv.c`.

This is the first port in this project with a genuine rendering-surface concern, not just register/memory logic — `Tv`'s dimensions become config-driven (two real monitor geometries) rather than a compile-time constant, and it needs a real 60Hz interrupt tick that today's invented `Display.Update()` doesn't provide. `Display` is referenced by six files (`Mouse.cs`, `WpfBackend.cs`, `MachineControl.cs`, `ConfigManager.cs`, `Program.cs`, `WpfBackendTests.cs`) — this spec's Wiring section covers all of them except `ConfigManager.cs` (see Decisions).

Real source: `usim/tv.c` (372 lines) + `usim/tv.h`.

Color TV (`usim/colortv.c`) is a separate device with its own screen buffer, control registers, and rendering surface size — deliberately out of scope for this spec, per the brainstorming discussion that preceded it. It gets its own future spec.

## Decisions

- **`Display.cs` is deleted, not extended.** Its invented API has no real callers this port needs to preserve (`WriteVideoMemory`/`ReadVideoMemory`/`SetPixel`/`DrawTestPattern` are all synthetic, with zero real-C equivalents), and its color-mode rendering belongs to the separate, future color-TV spec, not this one.
- **`Tv`'s screen buffer is written eagerly, matching the real C exactly — not re-derived lazily per frame tick.** `tv_screen_write` immediately unpacks the written word's 32 bits into the corresponding 32 `FrameBuffer` pixels, using whatever foreground/background colors are current at that moment. The real C's `tv_update_screen`/`accumulate_update` dirty-rect tracking exists purely so the X11/SDL2 backends know which sub-rectangle to re-blit to the screen — a backend-specific performance optimization with no effect on pixel *values*. `WpfBackend.cs` already re-blits the *entire* frame buffer every ~16ms regardless of what changed, so porting the dirty-rect tracking would add real code for zero observable behavioral difference; not ported, matching this project's established practice of porting behavior over literal implementation detail (e.g. `FileStream` vs `mmap` in the disk port).
- **The 60Hz interrupt tick reuses the point both real entry paths already call at ~16ms intervals.** The real C's `run_at_60hz()` (a genuine, unconditional real-time timer, independent of microcode execution) calls `tv_assert_interrupt()`/`colortv_assert_interrupt()` every 1/60th second. `WpfBackend.Tick()` and `MachineControl.Run()`'s headless loop both already call `Display.Update()` on a ~16ms cadence (`DispatcherTimer` interval / `Thread.Sleep(16)`, respectively) — replacing that call with `Tv.Tick()` (which asserts the interrupt, gated on `IsInterruptEnabled()`, exactly like the real C) reaches both paths with no new timer infrastructure.
- **`the_60_cycle_clock` (a separate, IOB-owned 16-bit counter incremented by the same real timer) is out of scope.** It belongs to `usim/iob.c`, a wholly different, not-yet-ported subsystem (`BusAdaptor`'s existing `IobLo`/`IobHi` placeholder range) — noted here only because it shares the same real-time trigger this spec's `Tick()` reuses, for whoever eventually ports IOB.
- **`ConfigManager.cs`'s `GetDisplayConfig()`/`DisplayConfig` are dead code, confirmed via grep** (their only caller, `ConfigManager.Validate()`, itself has zero callers anywhere) — but `Validate()` also references `GetMemoryConfig()`/`GetMicrocodeConfig()`, unrelated to TV, in the same dead method. Deleting only the display-related third of an already-fully-dead method is worse than leaving it; this spec leaves `ConfigManager.cs` untouched and flags the whole dead `Validate()`/`GetDisplayConfig()`/`GetMemoryConfig()`/`GetMicrocodeConfig()` cluster as a separate future cleanup, not this spec's job.
- **The real `[usim]` `monitor` config value doesn't exist in C# yet** and needs adding to `Program.ApplyConfiguration` (the live config path, same one the disk port's `[disk]` section and the already-real `UsimState.ColorTvEnabled` — read from `Program.cs`'s existing `config.GetBool("Display", "colortv", false)` — both go through). `colortv` staying under the `"Display"` section while `monitor` goes under `"usim"` is not an inconsistency to fix — it faithfully matches the real C's own section split (`ucfg.c`'s `[usim]` section for `monitor`, separate from wherever `colortv_enabled` is actually read — this spec does not need to re-verify that placement, since `ColortvEnabled`'s wiring is already real and unrelated to this spec's scope).
- **Dropped, not ported: `Display.CurrentFPS`/`FrameCount`.** Invented convenience stats with no real-C counterpart and no consumer beyond a debug print line. `Tv.Tick()` no longer performs a "frame render" (screen writes already update `FrameBuffer` eagerly — see above), so there is no natural "frame" to count; simplify the status line to report dimensions only.
- **`FrameBuffer` is sized to the full possible screen-buffer address range (`0x8000` words → `0x8000*32` pixels), not to `Width*Height`.** The real C's own `tv_bitmap` is a fixed `1024*1024`-pixel array regardless of which monitor geometry is configured — deliberately oversized so that `offset*32 + bitIndex` (for any `offset` the real address range `0x3C0000`-`0x3C7FFF` permits, i.e. `0`-`0x7FFF`) never exceeds the array, even though only the first `Width*Height` pixels are ever displayed. `1024*1024` happens to equal `0x8000*32` exactly — not a coincidence, the real author sized it to exactly match the address space. Sizing `FrameBuffer` to `Width*Height*4` bytes instead would be smaller than the real buffer and risk an out-of-range write the moment a real caller (or a test) touches a screen-buffer offset beyond the visible geometry, which the real hardware/emulator allows without complaint. `WpfBackend`'s `WriteableBitmap`/`WritePixels` continue to use `Width`/`Height` for the *visible* region — the first `Width*Height*4` bytes of the (larger) `FrameBuffer` are exactly the visible screen in row-major order, so this costs nothing at the rendering end.

## Octal Literals — Independently Verified

| Octal | Decimal | Hex | Meaning |
|---|---|---|---|
| `04` | 4 | `0x4` | Mode bit 2: BOW (black-on-white) |
| `010` | 8 | `0x8` | Mode bit 3: interrupt-enable |
| `0100000` | 32768 | `0x8000` | Screen buffer size (32K words, `tv_screen_buffer`'s real allocated size) |

`(1 << N)` forms in the real C (no octal-literal risk): bit 4 = VERT FLAG / interrupt-request (`0x10`).

`tv_width`/`tv_height` (768/896 for "CPT", 768/963 for "other") are plain decimal in the real C — no conversion risk.

## Register Model

Two register groups, both already anticipated by `BusAdaptor.cs`'s existing placeholder constants (`TvScreenLo`/`Hi` = `0x3C0000`-`0x3C7FFF`; `TvControlLo`/`Hi` = `0x3DFFF0`-`0x3DFFF7`):

**Screen buffer** (`ScreenRead(offset)`/`ScreenWrite(offset, v)`, offset = word index into the packed 1-bit-per-pixel buffer):
- Read: return the stored word.
- Write: store the word, then immediately unpack its 32 bits into `FrameBuffer` — **LSB-first**: bit 0 of `v` is pixel `offset*32 + 0`, bit 1 is pixel `offset*32 + 1`, etc. (the real C's `v & 1` then `v >>= 1` loop; the *existing invented code's* `(word >> (31-bit)) & 1` is backwards and must not be preserved). Each set bit becomes `Foreground`, each clear bit becomes `Background` (both real, current colors — not fixed black/white).

**Control registers** (`ControlRead(offset)`/`ControlWrite(offset, v)`, offset 0-3):
| Offset | Read | Write |
|---|---|---|
| 0 (mode) | Current mode word, with bits 5 (VSYNC)/6 (HSYNC)/7 (sync-PROM-enable) computed read-only bits — **for this port, VSYNC/HSYNC are always 0** (no real CRT timing to simulate; matches the real C's own comment that these are read-only status bits with no consumer that would notice usim never asserting them) | Records the old BOW value, sets `_mode = v`, then **unconditionally** recomputes `Foreground`/`Background` from the new BOW value (matches the real C exactly — it recomputes on every mode write, not only on a transition; recomputing when BOW is unchanged is a same-value no-op, not a bug). **Only if the BOW value actually changed** (old ≠ new), additionally re-renders the ENTIRE screen by looping over every word (`ScreenWrite(i, ScreenRead(i))` for `i` in `0..(Width*Height/32)`) — a real, faithful full-repaint, not an optimization to skip |
| 1 (sync data) | If sync-PROM is **disabled** (vert-spacing bit 7 set — see offset 3), returns the addressed sync-RAM byte; if sync-PROM is enabled, always returns 0 | Stores into sync-RAM at the current sync pointer, if sync-PROM is not enabled |
| 2 (sync pointer) | write-only (falls to default on read) | Sets the sync-RAM address pointer (12 bits) |
| 3 (vertical spacing) | write-only (falls to default on read) | Stores the vertical-spacing value; bit 7 (`0x80`) gates whether the sync PROM is "enabled" (bit **clear** = enabled) |
| default | `SetXbusNxm()` (matches the real C's `bus_interface_set_xbus_nxm()` in this file's own default case — a different call site from `BusAdaptor`'s own generic fallback, but the same real effect) | same |

Sync-RAM (registers 1-3) has no real behavioral effect in `usim` beyond storage — the real C's own comments say so explicitly ("not relevant for usim") — but is ported faithfully anyway, matching this project's established practice for real-but-inert state.

`IsBlackOnWhite()` / `IsInterruptEnabled()`: private helpers, `(mode & 0x4) != 0` / `(mode & 0x8) != 0`.

## Tick / Interrupt

`Tick()` (called once per ~16ms from both `WpfBackend.Tick()` and `MachineControl.Run()`'s headless loop, replacing the old `Display.Update()` call): if `IsInterruptEnabled()`, set the VERT-FLAG bit (`0x10`) in the mode word and call `UCode.AssertXbusInterrupt()` (already real, from Phase 8) — matches `tv_assert_interrupt()` exactly. Does **not** re-render anything (screen writes already keep `FrameBuffer` current — see Decisions).

## Class Shape

`usim-cs/Tv.cs` (new, replaces `Display.cs`):

```csharp
public class Tv
{
    private const int MaxWords = 0x8000; // tv_screen_buffer's real allocated size (32K words)

    public uint Width { get; }
    public uint Height { get; }

    // BGRA32, sized to the FULL possible screen-buffer address range
    // (MaxWords*32 pixels), not Width*Height -- matches the real C's own
    // oversized tv_bitmap[1024*1024] (== MaxWords*32 exactly). Only the
    // first Width*Height pixels are ever displayed (see Wiring), but every
    // real screen-buffer offset must be safely writable.
    public byte[] FrameBuffer { get; } = new byte[MaxWords * 32 * 4];

    private readonly UCode _ucode;
    private readonly uint[] _screenBuffer = new uint[MaxWords];
    private readonly byte[] _syncRam = new byte[4096];
    private uint _mode;
    private uint _syncPtr;
    private uint _vertSpacing;
    private uint _foreground = 0xFFFFFFFFu; // white, BGRA32 -- matches real C's WHITE
    private uint _background = 0xFF000000u; // black, BGRA32 -- matches real C's BLACK

    public Tv(UCode ucode, uint width, uint height)
    {
        _ucode = ucode;
        Width = width;
        Height = height;
        Reset();
    }

    public void Reset() { ... } // faithful port of tv_reset -- clears mode, screen buffer, FrameBuffer, restores default fg/bg

    public uint ScreenRead(uint offset) { ... }
    public void ScreenWrite(uint offset, uint v) { ... }

    public uint ControlRead(uint offset) { ... }
    public void ControlWrite(uint offset, uint v) { ... }

    public void Tick() { ... }

    private bool IsBlackOnWhite() => (_mode & 0x4) != 0;
    private bool IsInterruptEnabled() => (_mode & 0x8) != 0;
    private bool IsSyncPromEnabled() => (_vertSpacing & 0x80) == 0;
}
```

## Wiring

1. **`BusAdaptor.cs`**: gains a settable-once `Tv? _tv` field + `WireTv(Tv tv)` method, mirroring `WireDiskController` exactly (constructed after `BusAdaptor`, since `BusAdaptor` is built inside `UCode`'s own constructor before a config-sized `Tv` can exist). `ReadXbusIo`/`WriteXbusIo`'s `TvScreenLo`/`Hi` and `TvControlLo`/`Hi` branches dispatch to `_tv.ScreenRead`/`Write`/`ControlRead`/`Write` (with the same null-guard-and-warn convention `DiskController`'s wiring already established), replacing the current generic placeholder for those two ranges only — `ColorTvScreenLo`/`Hi` and `ColorTvControlLo`/`Hi` are untouched.
2. **`Program.cs`**: `ApplyConfiguration` gains a `monitor` read under the `[usim]` section (`config.GetString("usim", "monitor", "cpt")`, mapping `"cpt"`→768×896 and `"other"`→768×963, matching `ucfg.c`'s real string values exactly) into a new `UsimState.TvWidth`/`TvHeight` pair (or an equivalent — implementer's naming choice, as long as it's clear). **An unrecognized value is a warning that falls back to `"cpt"`'s dimensions, NOT a fatal error** — this is `ucfg.c`'s own real behavior for the *string* parse (`warnx(...); return 1;`, leaving the already-defaulted `tv_monitor` untouched); the real C's `errx()` fatal path lives in `tv_init()`, which only ever sees an already-validated integer (0 or 1) by the time it runs, so it can never actually observe an unrecognized value in practice. Do not conflate the two.
3. **`MachineControl.cs`**: `Display Display { get; }` becomes `Tv Tv { get; }`; construction becomes `Tv = new Tv(UCode, UsimState.TvWidth, UsimState.TvHeight);` (after `UCode` exists), followed by `UCode.BusAdaptor.WireTv(Tv);` and `Mouse.MaxX = (int)Tv.Width; Mouse.MaxY = (int)Tv.Height;` (replacing `Mouse`'s old static-constant-based defaults). `Display.Initialize()` call sites become `Tv.Reset()`. `Display.Update()` (in the headless `Run()` loop) becomes `Tv.Tick()`. The status-print line drops the invented FPS/frame-count fields, reporting `Tv.Width`x`Tv.Height` only.
4. **`WpfBackend.cs`**: constructor parameter type `Display display` becomes `Tv tv`; every `Display.WIDTH`/`HEIGHT` (static) becomes `_tv.Width`/`Height` (instance); `_display.FrameBuffer` becomes `_tv.FrameBuffer`; the `Tick()` method's `_display.Update()` call becomes `_tv.Tick()`.
5. **`Mouse.cs`**: `MaxX`/`MaxY`'s default-value initializers (`= Display.WIDTH`/`HEIGHT`) are removed — `Mouse`'s own constructor no longer references the deleted `Display` class at all; `MachineControl` sets both explicitly post-construction (see point 3), matching how other simple cross-references are already wired in this codebase.

## Testing

New `TvTests.cs`:
- `ScreenWrite` unpacks bits LSB-first into `FrameBuffer` at the correct pixel offsets, using the current foreground/background colors — the specific bug this spec fixes; assert bit 0 of the written word lands at the FIRST pixel of the 32-pixel run, not the last.
- A BOW-bit transition triggers a full-screen repaint with the new colors (write some pixels, flip BOW, confirm `FrameBuffer` reflects the NEW color assignment for those same bits without any additional `ScreenWrite` call).
- Control register read/write round-trips for offsets 0-3, including the read-only-ness of offsets 2/3 (falls to default) and the write-only-ness where applicable.
- Sync-PROM-enabled gating: sync-RAM read/write only takes effect when vertical-spacing bit 7 is clear; confirm the always-0 behavior when it's set.
- `Tick()` asserts the real `UCode` Xbus interrupt (observed through `UCode.InterruptStatusReg`/`InterruptPendingFlag`, not a mock) only when interrupt-enable is set; confirm no assertion when it's clear.
- Default-case read/write calls `SetXbusNxm()` (via the real `BusInterface`), doesn't throw.
- The real screen-buffer size bound (`0x8000` words) — reading/writing at the boundary doesn't throw or silently wrap incorrectly. Specifically: writing at an offset beyond the *visible* screen (`>= Width*Height/32`) but still within the full `0x8000`-word range must not throw — this is the exact scenario the oversized `FrameBuffer` (see Decisions) exists to make safe, and is the one case a `FrameBuffer` naively sized to `Width*Height*4` would get wrong.

`BusAdaptorTests.cs` additions: a real-dispatch-confirmation test for the TV screen/control ranges, built the same discriminating way prior ports' fix rounds required (assert on a value only the real `Tv` path can produce, not the old placeholder).

`WpfBackendTests.cs`'s `TestDisplayByteOrder` is rewritten to test `Tv`'s real, corrected LSB-first byte order (the old test exercised the invented color-mode path, which no longer exists) — this is the test most directly validating the bug fix this spec exists to make. Its `mouse.MaxX == Display.WIDTH` assertions are updated to compare against a constructed `Tv` instance's `Width`/`Height`.

## Global Constraints

- No new NuGet dependencies.
- Every octal literal in this spec was independently verified via script — use the hex forms given.
- `mmap`/dirty-rect tracking/`the_60_cycle_clock` are not ported — see Decisions.
- The BOW-toggle full-repaint, the sync-PROM-enable gating polarity (bit clear = enabled), and the `SetUnibusMapError`-vs-`SetXbusNxm` distinction (this file's default case uses `SetXbusNxm`, matching the real C's own `bus_interface_set_xbus_nxm()` call in `tv_control_read`/`write`'s default branches) must be preserved exactly, not "cleaned up" to look more consistent.

## Out of Scope

- Color TV (`usim/colortv.c`) — its own future spec, per the brainstorming discussion.
- `the_60_cycle_clock`/IOB — a different, not-yet-ported subsystem.
- `ConfigManager.cs`'s dead `GetDisplayConfig()`/`Validate()` cluster — flagged as a separate future cleanup, spanning unrelated config areas beyond just TV.
- `tv_save_screenshot`/`tv_poll` (X11/SDL2-specific; `tv_poll` has no body relevant to a WPF-based backend, and screenshotting is a separate, optional convenience feature not blocking a faithful port of the display/register model itself).
