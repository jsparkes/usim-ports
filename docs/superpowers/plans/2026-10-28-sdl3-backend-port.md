# SDL3 Backend Conversion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `usim-cs`'s WPF-only display/keyboard/mouse/audio backend with a cross-platform SDL3 backend, ported faithfully from real usim's `sdl3-video.c`/`sdl3-keyboard.c`/`sdl3-audio.c`/`sdl3-mouse.c`, using the `SDL3-CS` NuGet package.

**Architecture:** One class per real-C translation unit (`Sdl3KeyTranslator`, `Sdl3Audio`, `Sdl3Video`), composed by one `Sdl3Backend` class that plays the exact role `WpfBackend` plays today — same constructor shape, same `MachineControl` wiring point. `Mouse.cs` already contains the real `sdl3-mouse.c` port (A-memory injection, DTP_FIX, cursor-state warp detection), so no new mouse-logic class is needed; `Sdl3Backend` only feeds SDL mouse events into the existing `Mouse` API, the same way `WpfBackend` feeds WPF mouse events into it today. `Usim.csproj` drops WPF for a plain cross-platform TFM once the new backend is wired in and verified; `WpfBackend.cs`/`WpfKeyTranslator.cs` and their tests are deleted in the last task.

**Tech Stack:** .NET 8 (plain `net8.0`, no `-windows` suffix), `SDL3-CS` (github.com/edwardgushchin/SDL3-CS, NuGet id `SDL3-CS`) plus its per-RID native companion packages.

**Spec:** [docs/superpowers/specs/2026-10-28-sdl3-backend-design.md](../specs/2026-10-28-sdl3-backend-design.md)

## Global Constraints

- Full replacement, no WPF/SDL3 dual-backend or fallback mode.
- Faithful port of real C's `sdl3-video.c`/`sdl3-keyboard.c`/`sdl3-audio.c` behavior, with exactly two documented divergences: (1) the `cold_boot_scancode`/`warm_boot_scancode` typo (`(0b111111 < 10)` instead of `(0b111111 << 10)`) is fixed, not ported bit-exact; (2) the `"opengl"` render-driver hint and per-window `SDL_GL_CreateContext`/`SDL_GL_MakeCurrent` calls are omitted as vestigial relative to the real 2D-texture drawing path. Both get a code comment stating the divergence and why, the same way `ColorTv.cs:222-226` already documents its one existing divergence.
- The second color-TV window is in scope — not new surface, since `WpfBackend.cs:138-179` already has it, gated on `UsimState.ColorTvEnabled`.
- Window-placement config (`window_display`, `window_position_x`, `window_position_y`, `window_always_on_top`) is new `usim.ini`/`ConfigManager`/`MachineControl` surface, matching real C's full capability (real C defaults: `window_position_x = -1`, `window_position_y = -1`, `window_display = -1`, `window_always_on_top = false`, from `usim/tv.c:90-93`).
- CI/test coverage goes beyond today's pure-logic-only tests: the new video/audio test(s) set `SDL_VIDEODRIVER=dummy` / `SDL_AUDIODRIVER=dummy` themselves before calling init, so they run headlessly on any machine, not just where a real display/audio device exists.
- Poor performance is a named failure condition: the present/tick path must not introduce visible lag versus the current 16ms-tick WPF backend.
- `Mouse.cs`, `Keyboard.cs` (minus the new boot-chord enqueue caller), `Tv.cs`, `ColorTv.cs`, `Idle.cs`, `Iob.cs` are unchanged — only the backend driving them changes.

## Review Focus

- **No audio device available (headless CI, or a machine with no sound hardware):** real C's `sdl3_audio_init` treats a failed `SDL_OpenAudioDeviceStream` as a non-fatal warning and returns `false`/continues; a person running `usim-cs` on a headless box or a VM with no audio should see the emulator keep running silently, not crash or hang on startup.
- **Mouse motion while `Scale != 1.0`:** real C unscales SDL's window-pixel motion coordinates (`mouse_x / sdl3_video_scale`) before writing guest coordinates; a person who sets `scale = 2.0` in `usim.ini` and then moves the mouse should see the cursor land where they actually point, not at 2x the position.
- **Cold/warm boot key chord:** a person holding the real boot chord (all Control+Meta keys down together) should see the emulator actually enqueue the cold/warm boot scancode — this must use the corrected (non-zeroed) constant, not regress to the real-C typo's always-zero field.
- **Color TV disabled (`colortv = false` in `usim.ini`):** a person who disables the color TV should see no second window appear at all and no color-TV texture/renderer get created — matching `WpfBackend.cs`'s existing `if (UsimState.ColorTvEnabled)` gating.
- **Repeated beep calls with changing wavelength in quick succession:** a person running code that beeps rapidly (e.g. multiple short tones) should hear them without the emulator's own tick loop stalling — real C flushes the audio stream on a wavelength change and skips the blocking `SDL_Delay` for durations under 100ms specifically to avoid this; dropping either behavior risks the stated performance anti-goal.

---

### Task 1: Cross-platform project file + `SDL3-CS` package

**Files:**
- Modify: `usim-cs/Usim.csproj`
- Create: `usim-cs/Sdl3Smoke.cs` (temporary-in-spirit but kept: a tiny `internal static class` with one method that calls one real `SDL3-CS` function, giving later tasks a compiled proof the package resolves — later tasks are free to delete it once `Sdl3Video.cs` exercises the package directly)
- Test: `usim-cs/Sdl3SmokeTests.cs`

**Interfaces:**
- Produces: confirms `SDL3-CS` (and its native runtime package for the current RID) resolve and run under a plain `net8.0` TFM, for every later task to build on.

- [ ] **Step 1: Write the failing test**

```csharp
// Sdl3SmokeTests.cs
Assert(Sdl3Smoke.GetVersion() > 0, "SDL3-CS reports a positive SDL version number");
```
Follow the existing `RunAllTests()`/`Assert(bool, string)` pattern used by every other `*Tests.cs` file in this project (see `WpfBackendTests.cs`).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build usim-cs` — expect a compile error (`Sdl3Smoke` doesn't exist yet).

- [ ] **Step 3: Change `Usim.csproj`:**
  - Add a `PackageReference` to `SDL3-CS` (latest stable version at implementation time — check nuget.org/packages/SDL3-CS for the current version; do not reuse the outdated `3.4.12` example from research) and to its native-runtime companion package for the current development RID (e.g. `SDL3-CS.win-x64` on Windows). Leave the `chaos-cs` `ProjectReference`, `TargetFramework`, and `UseWPF` untouched — **do not** switch the TFM or drop `UseWPF` in this task. `WpfBackend.cs`/`WpfKeyTranslator.cs` and their tests still exist and still require `net8.0-windows`/`UseWPF=true` to compile through Task 6; that cutover is Task 7's Step 3, in the same commit that deletes the WPF files. `net8.0-windows` is a superset of `net8.0`, so the `SDL3-CS` package works identically under either TFM — there is no reason to touch the TFM early, and doing so would break the build for every task before Task 7.
  - Implement `Sdl3Smoke.GetVersion(): int` in `Sdl3Smoke.cs` by calling the `SDL3-CS` binding's version query function (its exact name comes from the package's own API — inspect the installed package's namespace once restored).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet build usim-cs && dotnet run --project usim-cs -- --test-sdl3smoke` (add a `--test-sdl3smoke` case to `Program.cs`'s switch, following the exact pattern of the existing `--test-wpf` case at `Program.cs:145-148`, calling `Sdl3SmokeTests.RunAllTests()`).
Expected: PASS, version > 0.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/Usim.csproj usim-cs/Sdl3Smoke.cs usim-cs/Sdl3SmokeTests.cs usim-cs/Program.cs
git commit -m "build: add SDL3-CS package reference (TFM cutover deferred to Task 7)"
```

---

### Task 2: `Sdl3KeyTranslator`

**Files:**
- Create: `usim-cs/Sdl3KeyTranslator.cs`
- Test: `usim-cs/Sdl3KeyTranslatorTests.cs`

**Interfaces:**
- Produces: `public static class Sdl3KeyTranslator` with `public static int TranslateKey(<SDL3-CS scancode type> scancode)` returning the same Cadet-scancode values `WpfKeyTranslator.TranslateKey` returns today (0 for unmapped keys), and `public static int GetModifierState(<SDL3-CS keymod type> modifiers, bool capsLock)` returning the identical bitmask `WpfKeyTranslator.GetModifierState` returns today (bit 0 shift, bit 1 capslock, bit 2 control, bit 3 alt/meta, bit 6 windows/super).

- [ ] **Step 1: Write the failing tests**, porting every assertion in `WpfKeyTranslatorTests.cs` (`TestBootCriticalKeys`, `TestShiftDoesNotChangeTheReturnedScancode`, `TestUnmappedKeyReturnsZero`, `TestModifierKeysMapToRealScancodes`) against the SDL3-CS scancode/keymod equivalents of the same physical keys (Escape, F1, A, D1, Space, Scroll-Lock, OemPipe/backslash, OemComma, Back/Backspace, Return, LeftShift/RightShift, LeftCtrl, LeftAlt) and modifier flags (Shift, CapsLock-as-bool, Control, Alt, Windows/GUI, combined Shift+Control). Keep the same expected hex values as `WpfKeyTranslator` (e.g. Escape → `0x63`, A → `0x53`, Space → `0x5C`, Backspace → `0x13`, Return → `0x5E`) — these are real hardware scancodes, not WPF-specific, per `WpfKeyTranslator.cs`'s own header comment.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build usim-cs` — expect `Sdl3KeyTranslator` not found.

- [ ] **Step 3: Implement `Sdl3KeyTranslator`** in `Sdl3KeyTranslator.cs`, mapping each SDL3 scancode to the same hex value `WpfKeyTranslator.cs`'s `switch` already assigns to the corresponding WPF `Key` (copy the full table, translating the `case` labels from `Key.*` to the SDL3-CS scancode enum's equivalent names), and copying `GetModifierState`'s bit-assignment logic against the SDL3-CS keymod flags enum.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project usim-cs -- --test-sdl3keytranslator` (new switch case, same pattern as `--test-keytranslator` at `Program.cs:215-218`).
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/Sdl3KeyTranslator.cs usim-cs/Sdl3KeyTranslatorTests.cs usim-cs/Program.cs
git commit -m "feat: port Cadet-scancode table to SDL3 scancodes"
```

---

### Task 3: `Sdl3Audio`

**Files:**
- Create: `usim-cs/Sdl3Audio.cs`
- Test: `usim-cs/Sdl3AudioTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `public class Sdl3Audio : IDisposable` with `public bool Init()`, `public void Beep(int halfWavelengthMicros, int durationMicros)`, and `Dispose()` — the same three operations `MachineControl.cs:126`'s `Iob.OnBeep = halfWavelengthMicros => DisplayBackend?.Beep(halfWavelengthMicros, 130000);` needs, so `Sdl3Backend` (Task 6) can expose an identically-shaped `Beep(int, int)` passthrough.

This is a faithful port of `sdl3-audio.c`'s `sdl3_audio_init`/`sdl3_audio_internal_beep`/`sdl3_audio_fill_stream`/`sdl3_audio_quit`, using these exact real-C constants: requested sample rate `8000` (`WANT_SAMPLE_RATE`), amplitude `0.8` of `32767` (`max_amplitude = 32767.0 * 0.8`), sample buffer size `16 * 1024` `short`s, mono 16-bit signed format. The frequency formula is `1_000_000.0 / wavelength_in_us` where `wavelength_in_us = halfWavelengthMicros * 2` — note this is exactly the formula `BeepWavBuilder.BuildWav` at `usim-cs/BeepWavBuilder.cs:22` already uses (`frequency = 1_000_000.0 / (halfWavelengthMicros * 2)`); reuse that formula, not `BeepWavBuilder` itself (it builds a WAV blob for Windows-only `System.Media.SoundPlayer`, which doesn't exist cross-platform). Preserve real C's two behavioral quirks: (a) flush the audio stream only when the wavelength changes between calls (`last_wavelength_in_us` tracking), not on every call; (b) skip the blocking wait (`SDL_Delay`-equivalent) for `durationMicros < 100_000` microseconds. The ASCII-beep fallback (`sdl3_audio_use_ascii_beep`) is out of scope — real C defaults it to `false` and nothing in `usim-cs` turns it on.

- [ ] **Step 1: Write the failing tests**

```csharp
// Sdl3AudioTests.cs
var audio = new Sdl3Audio();
Assert(audio.Init(), "Init succeeds even with SDL_AUDIODRIVER=dummy (no real device)");
// Beep must return without throwing and without blocking >= 100ms for a short duration
var sw = System.Diagnostics.Stopwatch.StartNew();
audio.Beep(halfWavelengthMicros: 500, durationMicros: 50_000); // 50ms < the 100ms no-wait threshold
sw.Stop();
Assert(sw.ElapsedMilliseconds < 100, "a beep shorter than 100ms does not block the caller for its full duration");
```
Set `Environment.SetEnvironmentVariable("SDL_AUDIODRIVER", "dummy")` at the top of `Sdl3AudioTests.RunAllTests()` before constructing `Sdl3Audio`, so this test runs headlessly on any machine.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build usim-cs` — expect `Sdl3Audio` not found.

- [ ] **Step 3: Implement `Sdl3Audio`** in `Sdl3Audio.cs` per the algorithm and constants above, using the `SDL3-CS` audio-stream binding (`SDL_OpenAudioDeviceStream`/`SDL_PutAudioStreamData`/`SDL_FlushAudioStream`/`SDL_ResumeAudioStreamDevice`/`SDL_PauseAudioStreamDevice`/`SDL_DestroyAudioStream` equivalents — exact binding names come from the installed package). `Init()` returns `true` and logs a warning (do not throw) when device-stream creation fails, matching real C's non-fatal degradation.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project usim-cs -- --test-sdl3audio` (new switch case calling `Sdl3AudioTests.RunAllTests()`).
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/Sdl3Audio.cs usim-cs/Sdl3AudioTests.cs usim-cs/Program.cs
git commit -m "feat: port sdl3-audio.c's beep generation to SDL3 audio streams"
```

---

### Task 4: Window-placement config surface

**Files:**
- Modify: `usim-cs/ConfigManager.cs` (`DisplayConfig` class, `GetDisplayConfig()`)
- Modify: `usim-cs/usim.ini` (`[Display]` section)
- Modify: `usim-cs/ConfigTests.cs` (add a case alongside the existing `TestDisplayConfig`-style test, if one exists — otherwise add one)

**Interfaces:**
- Produces: `DisplayConfig` gains `public int WindowDisplay { get; set; }`, `public int WindowPositionX { get; set; }`, `public int WindowPositionY { get; set; }`, `public bool WindowAlwaysOnTop { get; set; }`, read via `_config.GetInt("Display", "window_display", -1)`, `_config.GetInt("Display", "window_position_x", -1)`, `_config.GetInt("Display", "window_position_y", -1)`, `_config.GetBool("Display", "window_always_on_top", false)` — matching real C's defaults (`usim/tv.c:90-93`: all `-1`/`-1`/`-1`/`false`). `Sdl3Video` (Task 5) and `Sdl3Backend` (Task 6) consume these four `DisplayConfig` fields.

- [ ] **Step 1: Write the failing test** in `ConfigTests.cs`:

```csharp
var displayConfig = configManager.GetDisplayConfig();
Assert(displayConfig.WindowDisplay == -1, "window_display defaults to -1 (unset)");
Assert(displayConfig.WindowPositionX == -1, "window_position_x defaults to -1 (unset)");
Assert(displayConfig.WindowPositionY == -1, "window_position_y defaults to -1 (unset)");
Assert(displayConfig.WindowAlwaysOnTop == false, "window_always_on_top defaults to false");
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build usim-cs` — expect `DisplayConfig` has no `WindowDisplay`/etc. members.

- [ ] **Step 3: Add the four fields and `GetInt`/`GetBool` reads** exactly as specified in Interfaces above; add commented `window_display`, `window_position_x`, `window_position_y`, `window_always_on_top` keys to `usim-cs/usim.ini`'s `[Display]` section (commented-out with their defaults, following the existing style of that section's own comments at lines 44-45).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project usim-cs -- --test-config`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/ConfigManager.cs usim-cs/usim.ini usim-cs/ConfigTests.cs
git commit -m "feat: add window-placement config surface (display, position, always-on-top)"
```

---

### Task 5: `Sdl3Video`

**Files:**
- Create: `usim-cs/Sdl3Video.cs`
- Test: `usim-cs/Sdl3VideoTests.cs`

**Interfaces:**
- Consumes: `Tv.Width`/`Tv.Height`/`Tv.FrameBuffer` (`byte[]`, BGRA32, already unpacked — see `Tv.cs`), `ColorTv.Width`/`ColorTv.Height`/`ColorTv.FrameBuffer` (same shape), `UsimState.ColorTvEnabled` (`bool`), and the four `DisplayConfig` fields from Task 4.
- Produces: `public class Sdl3Video : IDisposable` with:
  - `public bool Init(uint tvWidth, uint tvHeight, uint colorTvWidth, uint colorTvHeight, bool colorTvEnabled, double scale, bool allowResize, int windowDisplay, int windowPositionX, int windowPositionY, bool windowAlwaysOnTop, string windowTitle)`
  - `public void PresentMain(byte[] frameBuffer)`
  - `public void PresentColor(byte[] frameBuffer)` (no-op if color TV wasn't enabled at `Init`)
  - `public void SetWindowTitle(string title)`
  - `public void SetScaleMode(bool useLinearFiltering)`
  - `public nint MainWindowHandle { get; }` (the real SDL window pointer/handle, for `Sdl3Backend`'s mouse-warp call)
  - `Dispose()`

This is a faithful port of `sdl3_video_init`, `sdl3_video_present_main`, `sdl3_video_present_color`, `sdl3_video_set_scalemode`, `sdl3_video_update_window_title`, and `sdl3_video_quit` — **except** `Init` does **not** set the `"opengl"` render-driver hint and does **not** call `SDL_GL_CreateContext`/`SDL_GL_MakeCurrent` anywhere (documented divergence #2 from the Global Constraints section — add a comment at the top of `Sdl3Video.cs` stating this and why: all real drawing in `sdl3-video.c` goes through the 2D `SDL_Renderer`/texture API, which manages its own context, making the explicit GL calls vestigial). Window position is set with `window_position_x + display_x`/`window_position_y + display_y` exactly as real C does (no extra centering logic — real C doesn't special-case the `-1` sentinel either, so neither does this port). `SetScaleMode` maps `true`→linear, `false`→nearest, matching `sdl3_video_set_scalemode`'s `SDL_ScaleMode` values. The color window, when enabled, is positioned at `window_position_x + display_x + tv_width * scale + 64` / `window_position_y + display_y + (tv_height * scale - colortv_height) / 2`, exactly matching `sdl3-video.c:251-252`.

- [ ] **Step 1: Write the failing tests**

```csharp
// Sdl3VideoTests.cs — set SDL_VIDEODRIVER=dummy first
var video = new Sdl3Video();
Assert(video.Init(tvWidth: 768, tvHeight: 963, colorTvWidth: 576, colorTvHeight: 454,
    colorTvEnabled: true, scale: 1.0, allowResize: false, windowDisplay: -1,
    windowPositionX: -1, windowPositionY: -1, windowAlwaysOnTop: false,
    windowTitle: "USIM - Lisp Machine Emulator"),
    "Init succeeds under SDL_VIDEODRIVER=dummy");
Assert(video.MainWindowHandle != 0, "main window handle is non-zero after Init");

var frame = new byte[768 * 963 * 4];
video.PresentMain(frame); // must not throw
video.PresentColor(new byte[576 * 454 * 4]); // must not throw, color TV was enabled

video.Dispose(); // must not throw
```

Add a second test that calls `Init` with `colorTvEnabled: false` and then asserts `PresentColor` is a safe no-op (doesn't throw) and that no color-window handle exists (expose whatever check is simplest given the implementation — e.g. an internal flag surfaced for tests, or simply that `PresentColor` silently returns).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build usim-cs` — expect `Sdl3Video` not found.

- [ ] **Step 3: Implement `Sdl3Video`** per the Interfaces/algorithm description above, using the `SDL3-CS` window/renderer/texture bindings (`SDL_CreateWindow`, `SDL_CreateRenderer`, `SDL_CreateTexture`, `SDL_LockTexture`/`SDL_UpdateTexture`, `SDL_RenderTexture`, `SDL_RenderPresent`, `SDL_SetRenderScale`, `SDL_SetWindowPosition`, `SDL_SetWindowAlwaysOnTop`, `SDL_GetDisplays`/`SDL_GetDisplayBounds`-equivalents for `display_idx`/`display_x`/`display_y` — exact binding names come from the installed package).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project usim-cs -- --test-sdl3video` (new switch case calling `Sdl3VideoTests.RunAllTests()`, with `Environment.SetEnvironmentVariable("SDL_VIDEODRIVER", "dummy")` at the top of `RunAllTests()`).
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/Sdl3Video.cs usim-cs/Sdl3VideoTests.cs usim-cs/Program.cs
git commit -m "feat: port sdl3-video.c's window/renderer/texture/present path"
```

---

### Task 6: `Sdl3Backend`

**Files:**
- Create: `usim-cs/Sdl3Backend.cs`
- Test: `usim-cs/Sdl3BackendTests.cs`

**Interfaces:**
- Consumes: `Sdl3KeyTranslator` (Task 2), `Sdl3Audio` (Task 3), `Sdl3Video` (Task 5); `Tv`, `ColorTv`, `Iob`, `Keyboard`, `Mouse`, `Idle` (existing classes, same constructor shape `WpfBackend` already uses); `Mouse.OnWarp`, `Mouse.UpdatePosition(int, int)`, `Mouse.ButtonDown(MouseButtons)`, `Mouse.ButtonUp(MouseButtons)`; `Keyboard.KeyDown(uint, int)`, `Keyboard.KeyUp(uint, int)`; `Idle.Activity()`.
- Produces: `public class Sdl3Backend : IDisposable` with the exact same public shape `WpfBackend` has today, so Task 8's `MachineControl` cutover is close to a type-name swap:
  - `public Sdl3Backend(Tv tv, ColorTv colorTv, Iob iob, Keyboard keyboard, Mouse mouse, Idle idle, Action onTick)`
  - `public double Scale { get; set; } = 1.0;`
  - `public bool AllowResize { get; set; } = false;`
  - `public bool UseLinearFiltering { get; set; } = true;`
  - `public int WindowDisplay { get; set; } = -1;`
  - `public int WindowPositionX { get; set; } = -1;`
  - `public int WindowPositionY { get; set; } = -1;`
  - `public bool WindowAlwaysOnTop { get; set; } = false;`
  - `public bool IsRunning { get; private set; }`
  - `public void Initialize()`
  - `public void RunMessageLoop()` (pumps `SDL_PollEvent` in a loop, dispatching each event the same way `sdl3-keyboard.c`/`sdl3-mouse.c` dispatch theirs, until the main window receives a quit/close event)
  - `public void RequestExit()`
  - `public void SetWindowTitle(string title)`
  - `public void Beep(int halfWavelengthMicros, int durationMicros)` (passthrough to the owned `Sdl3Audio`)
  - `Dispose()`
  - `internal static (int X, int Y) UnscaleMotion(int rawX, int rawY, double scale)` — pure helper factoring out the scale-unscaling math below, so it's directly unit-testable without a live window/event loop (same pattern `WpfKeyTranslator`/`BeepWavBuilder` already use for their own pure-logic pieces).

Behavior to port faithfully from `sdl3-keyboard.c` and `sdl3-mouse.c`:
- **Key events:** translate each `SDL_EVENT_KEY_DOWN`/`SDL_EVENT_KEY_UP` via `Sdl3KeyTranslator.TranslateKey`/`GetModifierState` (Task 2) and call `Keyboard.KeyDown`/`KeyUp`, calling `Idle.Activity()` first — the same sequence `WpfBackend.HandleKeyEvent` (`WpfBackend.cs:237-260`) already follows.
- **Boot chord:** port `sdl3_keyboard_are_all_ctrl_and_meta_down_for_boot` and the cold/warm-boot scancode enqueue it gates (`sdl3-keyboard.c:359-388`), using the **corrected** constants (Global Constraints divergence #1): `cold_boot_scancode = (0b11111 << 19) | (0b111 << 16) | (0b111111 << 10) | 046` and `warm_boot_scancode = (0b11111 << 19) | (0b111 << 16) | (0b111111 << 10) | 062` (note the fixed `<<`, not `<`). Add a code comment at the declaration noting real C has `(0b111111 < 10)` here — a typo that always evaluates to 0 — and that this port intentionally uses the shift the surrounding pattern clearly intends.
- **Mouse motion:** on `SDL_EVENT_MOUSE_MOTION`, call `Idle.Activity()`, then call `var (x, y) = UnscaleMotion((int)event.motion.x, (int)event.motion.y, Scale); Mouse.UpdatePosition(x, y);` — the unscaling step is required to match real C's `mouse_x / sdl3_video_scale` unscaling (`sdl3-mouse.c:62-63`); omitting it means clicks land in the wrong place whenever `Scale != 1.0` (see Review Focus). `UnscaleMotion(rawX, rawY, scale)` returns `((int)(rawX / scale), (int)(rawY / scale))`.
- **Mouse buttons:** on `SDL_EVENT_MOUSE_BUTTON_DOWN`/`UP`, call `Idle.Activity()`, map the SDL button index to `MouseButtons` (1→Left, 2→Middle, 3→Right, matching `sdl3-mouse.c:101-105`/`112-116`; discard any other button, matching `WpfBackend.OnMouseButton`'s `_ => MouseButtons.None` discard), then call `Mouse.ButtonDown`/`ButtonUp`.
- **Mouse warp:** `Mouse.OnWarp` is wired to a method that calls the `SDL3-CS` binding for `SDL_WarpMouseInWindow`, passing `Sdl3Video.MainWindowHandle` and the given `(x, y)` — matching `sdl3_mouse_warp_if_needed`'s `SDL_WarpMouseInWindow(main_window, mouse_warp_x, mouse_warp_y)` (`sdl3-mouse.c:48`). Unlike `WpfBackend.WarpCursor`, which moves the real OS cursor via `SetCursorPos` (a Windows-only P/Invoke this port cannot keep), this uses SDL3's own cross-platform warp call, which is the actual real-C behavior anyway.
- **Tick:** `Initialize()` starts a loop/timer that calls (in this order, matching `WpfBackend.Tick`, `WpfBackend.cs:208-217`) `_onTick()`, `Tv.Tick()`, `ColorTv.Tick()`, `Iob.Tick()`, `Keyboard.Tick()`, then `Sdl3Video.PresentMain(Tv.FrameBuffer)` and (if color TV enabled) `Sdl3Video.PresentColor(ColorTv.FrameBuffer)`, at a ~16ms cadence.

- [ ] **Step 1: Write the failing tests**

```csharp
// Sdl3BackendTests.cs — set SDL_VIDEODRIVER=dummy and SDL_AUDIODRIVER=dummy first
// Test 1: boot-chord constants are the corrected (non-zeroed) values
Assert(Sdl3Backend.ColdBootScancode == ((0b11111 << 19) | (0b111 << 16) | (0b111111 << 10) | 0x26 /* 046 octal */),
    "cold boot scancode uses the corrected shift, not real C's zeroed typo");
Assert(Sdl3Backend.WarmBootScancode == ((0b11111 << 19) | (0b111 << 16) | (0b111111 << 10) | 0x32 /* 062 octal */),
    "warm boot scancode uses the corrected shift, not real C's zeroed typo");

// Test 2: construction and shape parity with WpfBackend
var tv = new Tv(ucode, 768, 963);
var colorTv = new ColorTv(ucode);
var backend = new Sdl3Backend(tv, colorTv, iob, keyboard, mouse, idle, onTick: () => { });
Assert(backend.Scale == 1.0 && backend.AllowResize == false && backend.UseLinearFiltering == true,
    "defaults match WpfBackend's defaults");

// Test 3: mouse motion unscaling (Review Focus: Scale != 1.0)
var (x, y) = Sdl3Backend.UnscaleMotion(rawX: 100, rawY: 200, scale: 2.0);
Assert(x == 50 && y == 100, "motion coordinates at scale=2.0 are halved before reaching Mouse.UpdatePosition");
var (x1, y1) = Sdl3Backend.UnscaleMotion(rawX: 100, rawY: 200, scale: 1.0);
Assert(x1 == 100 && y1 == 200, "motion coordinates at scale=1.0 (the default) pass through unchanged");
```
(Expose `ColdBootScancode`/`WarmBootScancode` as `internal const uint` or `public static readonly uint` so the test can check the corrected values directly without needing a live keyboard-chord simulation.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build usim-cs` — expect `Sdl3Backend` not found.

- [ ] **Step 3: Implement `Sdl3Backend`** per the Interfaces and behavior description above.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project usim-cs -- --test-sdl3backend` (new switch case calling `Sdl3BackendTests.RunAllTests()`).
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/Sdl3Backend.cs usim-cs/Sdl3BackendTests.cs usim-cs/Program.cs
git commit -m "feat: add Sdl3Backend composing video/audio/keyboard/mouse event plumbing"
```

---

### Task 7: `MachineControl` cutover and WPF removal

**Files:**
- Modify: `usim-cs/MachineControl.cs`
- Modify: `usim-cs/Program.cs`
- Modify: `usim-cs/Usim.csproj`
- Delete: `usim-cs/WpfBackend.cs`, `usim-cs/WpfKeyTranslator.cs`, `usim-cs/WpfBackendTests.cs`, `usim-cs/WpfKeyTranslatorTests.cs`

**Interfaces:**
- Consumes: `Sdl3Backend` (Task 6), the four `DisplayConfig` window-placement fields (Task 4).
- Produces: `MachineControl.DisplayBackend` is now `Sdl3Backend?`; `InitializeDisplay`'s signature grows to accept the window-placement values: `public void InitializeDisplay(bool allowResize = false, double scale = 1.0, int windowDisplay = -1, int windowPositionX = -1, int windowPositionY = -1, bool windowAlwaysOnTop = false)`.

- [ ] **Step 1: Write the failing test** — update whichever existing `MachineControlTests.cs` test constructs/initializes a display backend (if any references `WpfBackend` by type) to reference `Sdl3Backend` instead, and add:

```csharp
Assert(machine.DisplayBackend == null || machine.DisplayBackend is Sdl3Backend,
    "DisplayBackend, once initialized, is the Sdl3Backend type");
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build usim-cs` — expect a compile error since `MachineControl.cs:62`/`:118` still reference `WpfBackend`.

- [ ] **Step 3: Make the changes:**
  - `MachineControl.cs:62`: `public Sdl3Backend? DisplayBackend { get; private set; }`.
  - `MachineControl.cs:110-128`: widen `InitializeDisplay`'s signature as specified above; construct `new Sdl3Backend(Tv, ColorTv, Iob, Keyboard, Mouse, Idle, onTick: RunMicrocodeBatch) { AllowResize = allowResize, Scale = scale, UseLinearFiltering = true, WindowDisplay = windowDisplay, WindowPositionX = windowPositionX, WindowPositionY = windowPositionY, WindowAlwaysOnTop = windowAlwaysOnTop }`.
  - `Program.cs:539`: change `_machine.InitializeDisplay(allowResize: false, scale: 1.0);` to also pass the four window-placement values read from `ConfigManager.GetDisplayConfig()` (Task 4's `WindowDisplay`/`WindowPositionX`/`WindowPositionY`/`WindowAlwaysOnTop`).
  - Remove the `--test-wpf` (`Program.cs:145-148`) and `--test-keytranslator` (`Program.cs:215-218`) switch cases and their `RunAllTests()` calls (`Program.cs:681`, `:731`); delete the four WPF files listed above.
  - Remove the WPF-specific `using` directives and any leftover `System.Windows.*`/`System.Media` references anywhere else in `usim-cs` (grep for `System.Windows` and `System.Media` after deleting the four files to confirm none remain).
  - **In `Usim.csproj`, now perform the TFM cutover held back from Task 1:** remove `<TargetFramework>net8.0-windows</TargetFramework>` and `<UseWPF>true</UseWPF>`, add `<TargetFramework>net8.0</TargetFramework>`. This is safe only now, in the same commit, because the WPF files that required them are deleted in this same step.

- [ ] **Step 4: Run tests to verify everything passes**

Run: `dotnet build usim-cs && dotnet run --project usim-cs -- --test-all` (with `SDL_VIDEODRIVER=dummy`/`SDL_AUDIODRIVER=dummy` set in the shell, so the new SDL-backed tests run headlessly alongside everything else).
Expected: full suite green, no `WpfBackend`/`WpfKeyTranslator` references remain anywhere in `usim-cs`.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/MachineControl.cs usim-cs/Program.cs usim-cs/Usim.csproj
git rm usim-cs/WpfBackend.cs usim-cs/WpfKeyTranslator.cs usim-cs/WpfBackendTests.cs usim-cs/WpfKeyTranslatorTests.cs
git commit -m "feat: cut MachineControl over to Sdl3Backend, remove WPF backend"
```

---

### Task 8: Manual cross-platform verification

**Files:** none (manual verification task — no new source).

**Interfaces:** none — this task consumes the finished `Sdl3Backend` end to end.

- [ ] **Step 1:** On Windows, run `usim-cs` with a real display and confirm: the main window opens and shows the boot screen; the color-TV window opens when `colortv = true`; keyboard input reaches the emulator (confirm a cold boot via the real boot chord); mouse motion/clicks register correctly at `scale = 1.0` and again at a non-1.0 scale (e.g. `1.5`); a beep is audible.
- [ ] **Step 2:** Repeat Step 1 on at least one non-Windows platform (Linux or macOS), on real hardware or a VM with a real display/audio device attached — this is the first real confirmation the cross-platform goal is met, since every automated test up to this point runs under the `dummy` drivers.
- [ ] **Step 3:** While running on Windows, compare perceived frame smoothness/input latency side by side against the pre-cutover `WpfBackend` (check out the commit before Task 7 in a second worktree, or compare against the user's own memory of the WPF backend's feel) — confirm no regression, per the project's stated performance anti-goal. Record the outcome (pass/fail and any concrns) back to the project owner before considering this plan done.
- [ ] **Step 4:** Report results to the project owner. If any step fails, open a follow-up task rather than silently patching around it — this step exists specifically to catch what the headless/dummy-driver automated tests structurally cannot.
