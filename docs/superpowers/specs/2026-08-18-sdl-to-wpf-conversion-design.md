# SDL2 → WPF Graphics Backend Conversion — Design

**Date:** 2026-08-18
**Status:** Approved, pending implementation plan

## Context

The USIM C# port (`usim-cs`) uses SDL2 (via SDL2-CS) for its display window,
keyboard/mouse input, and beep audio. All SDL-specific code is isolated in
`usim-cs/SDL2Backend.cs`, with three wiring points in `MachineControl.cs`
(`InitializeSDL2()`, the `SDL2Backend` property, and the polling loop inside
`Run()`). `Keyboard.cs`, `Mouse.cs`, and `Display.cs` are already
toolkit-agnostic: they only deal in plain data (an ARGB-ish `byte[]` frame
buffer, X/Y ints, an X11-keysym-style code plus a modifier bitmask, a
`MouseButtons` flags enum).

This document covers replacing the SDL2 backend with a WPF-based one.

## Decisions

1. **Full replacement, not an added backend.** SDL2Backend.cs, the SDL2-CS
   project reference, and cross-platform support for `usim-cs` are dropped.
   Going forward `usim-cs` targets `net8.0-windows` and is Windows-only. (The
   untracked `usim-cs-sdl/` directory already holds a full snapshot of the
   pre-conversion SDL2 code, so nothing needs to be preserved elsewhere.)
2. **Single-threaded, timer-driven emulation loop.** WPF's `Dispatcher` owns
   the thread. A `DispatcherTimer` ticking at ~16ms drives emulation stepping,
   `Display.Update()`, and the bitmap refresh — all on the UI thread. Keyboard
   and mouse input events also arrive on the UI thread, so no locking is
   needed around `Keyboard`/`Mouse` (their APIs are unchanged). This matches
   the current code's simplicity (`MachineControl.Run()` doesn't yet execute
   real cycles per tick).
3. **Beep via in-memory WAV + `SoundPlayer`.** No new dependency. Build a
   16-bit PCM sine wave for the requested frequency/duration up front (same
   formula as the current `AudioCallback`), wrap it in a WAV stream, play
   fire-and-forget with `System.Media.SoundPlayer`.

## Architecture

`SDL2Backend.cs` is replaced by `WpfBackend.cs`, playing the same role but
with inverted control flow (WPF drives the loop; we don't poll it ourselves):

- Builds a `Window` + `Image` control in code (no XAML, consistent with the
  rest of the codebase) hosting a `WriteableBitmap` sized
  `Display.WIDTH × HEIGHT`.
- Wraps the `Image` in a `Viewbox` with `Stretch="Uniform"` for
  aspect-ratio-preserving resize — replaces SDL's manual
  `HandleWindowEvent` aspect math.
- `Window.KeyDown/KeyUp` → a `Key → X11 keysym` translation table (same shape
  as `SDL2Backend.TranslateKeycode`, against `System.Windows.Input.Key`
  instead of `SDL_Keycode`) → `Keyboard.KeyDown/KeyUp` (unchanged API).
- `Image.MouseMove/MouseDown/MouseUp` → coordinates scaled into
  `Display.WIDTH × HEIGHT` space → `Mouse.UpdatePosition/ButtonDown/ButtonUp`
  (unchanged API).
- A `DispatcherTimer` (~16ms) ticks: step emulation, `Display.Update()`,
  `WriteableBitmap.WritePixels(...)` from `Display.FrameBuffer`.
- `MachineControl.Run()` keeps its current blocking-until-quit contract as
  seen from `Program.cs` (`_machine.Run()` still blocks until the window
  closes), but internally becomes "create backend, start timer, call
  `Application.Run(window)`" instead of a manual `while` loop.

Renames (full replacement, not an add-on):
- `SDL2Backend` → `WpfBackend`
- `MachineControl.SDL2Backend` (property) → `MachineControl.DisplayBackend`
- `MachineControl.InitializeSDL2()` → `MachineControl.InitializeDisplay()`
- ~10 call sites in `Program.cs`/`MachineControl.cs` updated accordingly.

## Data Flow & Error Handling

**Rendering.** `Display.FrameBuffer` currently writes bytes in `A,R,G,B`
order per pixel — the existing "SDL2 expects ARGB8888" comments are already a
byte-order/bit-order mismatch. WPF's `WriteableBitmap` wants `Pbgra32`
(`B,G,R,A` byte order). Rather than swap bytes every frame in the backend,
`Display.RenderBlackAndWhite`/`RenderColor` are updated to write `B,G,R,A`
directly (and their comments corrected). `WpfBackend` then does a straight
`WritePixels` each tick.

**Input.** `Keyboard`/`Mouse` public APIs are unchanged. `Mouse.MaxX/MaxY`
currently default to 1024×768, which never matched any real window size
under SDL either (SDL never called `SDL_RenderWindowToLogical`). These
defaults are corrected to `Display.WIDTH`/`Display.HEIGHT`, and `WpfBackend`
converts `Image`-space mouse coordinates into that same logical space.

**Audio.** `Beep(halfWavelengthMicros, durationMicros)` builds a PCM sine
wave WAV in memory and plays it via `SoundPlayer`, fire-and-forget.

**Window title/cursor.** `Window.Title = ...` and
`Window.Cursor = Cursors.None` are direct parallels of
`SDL_SetWindowTitle`/`SDL_ShowCursor`.

**Errors.** Same posture as today: `Initialize()` throws
`InvalidOperationException` on failure; WPF exceptions bubble up naturally
(no SDL error-string marshaling needed). `Dispose()` stops the timer and
closes the window.

**Project file.** `usim-cs/Usim.csproj`: drop the `SDL2-CS` project
reference, add `<UseWPF>true</UseWPF>`, retarget `net8.0` →
`net8.0-windows`.

## Testing

- No existing automated tests cover `SDL2Backend`, so there's no test suite
  to port. Verification is manual: launch, confirm the window opens and
  letterboxes correctly on resize, the test pattern renders, keyboard/mouse
  input reaches `Keyboard`/`Mouse` (visible via existing `TraceLog` debug
  traces), and a beep plays.
- `ConfigTests.cs`/`UCodeTests.cs` are display-independent; run them after
  the swap as a regression check.
- Manual smoke test in the real Windows environment (launch, resize, type,
  click, close) — this is a UI change that needs eyes-on verification, not
  just a green build.

## Files Touched

| File | Change |
|---|---|
| `usim-cs/SDL2Backend.cs` | deleted |
| `usim-cs/WpfBackend.cs` | new |
| `usim-cs/Display.cs` | byte order B,G,R,A in render methods; comment fixes |
| `usim-cs/Mouse.cs` | `MaxX`/`MaxY` defaults → `Display.WIDTH`/`HEIGHT` |
| `usim-cs/MachineControl.cs` | rename `SDL2Backend`→`DisplayBackend`, `InitializeSDL2`→`InitializeDisplay`; `Run()` restructured around `Application.Run`/`DispatcherTimer` |
| `usim-cs/Program.cs` | update call sites for renames; drop `--test-sdl2` handling |
| `usim-cs/SDL2VerificationTest.cs` | deleted |
| `usim-cs/Usim.csproj` | drop SDL2-CS reference, add `UseWPF`, retarget `net8.0-windows` |
