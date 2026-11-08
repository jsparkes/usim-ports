# SDL3 Backend Design

## Intent and why

`usim-cs`'s only GUI backend today is `WpfBackend.cs`, built on WPF
(`net8.0-windows`, `UseWPF=true`). That ties the whole emulator to
Windows. This project replaces it with a single cross-platform backend
built on SDL3, so `usim-cs` runs its display, keyboard, mouse, and beep
audio on Windows, Linux, and macOS.

The new backend is a **faithful 1:1 port of real usim's own C
implementation** — `usim/sdl3-video.c` (608 lines), `usim/sdl3-keyboard.c`
(600 lines), `usim/sdl3-audio.c` (222 lines), and `usim/sdl3-mouse.c`
(118 lines) — not an idiomatic from-scratch rewrite of `WpfBackend.cs`'s
existing logic. This matches how every other device in this project has
been ported, and it means the new backend inherits real usim's own
hsync/vsync signaling, dual-window layout, and scancode-queue behavior
rather than reinventing them. The existing `usim-cs-sdl/` fork in this
repo is explicitly **not** used as a base or reference for this port.

## Goals

- Replace `WpfBackend.cs`/`WpfKeyTranslator.cs` with an SDL3-based
  backend that reaches full functional parity with what `WpfBackend.cs`
  does today, built by porting the real C `sdl3-*.c` files function by
  function.
- Preserve the second "color TV" window. `WpfBackend.cs` already opens
  this window (gated by `UsimState.ColorTvEnabled`, lines 138-179) and
  real usim's own `sdl3-video.c` drives a second window the same way
  (`sdl3_video_present_main` / `sdl3_video_present_color`). This is not
  new scope — it's part of the faithful port.
- Make the build cross-platform: Windows, Linux, and macOS, from one
  project, one TFM.
- Add the window-placement config surface real C's `sdl3_video_init`
  reads (`window_display`, `window_position_x`/`_y`,
  `window_always_on_top`) that doesn't exist anywhere in `usim-cs` today
  — new `usim.ini`/`ConfigManager`/`UsimState` keys, not just a backend
  change. `usim.ini`'s `[Display]` section today only has `scale`;
  `WpfBackend.cs` only exposes `Scale`/`AllowResize`/
  `UseLinearFiltering`.
- Use the `SDL3-CS` package (github.com/edwardgushchin/SDL3-CS, NuGet id
  `SDL3-CS`, versioned to track native SDL 3.4.x, with platform-native
  companion packages per RID) as the binding layer.
- Expand automated test coverage beyond today's pure-logic-only tests
  (`WpfBackendTests.cs`: key translation, beep WAV generation,
  frame-buffer byte order). CI also exercises the real SDL3
  init → window → renderer → texture → present path headlessly, using
  `SDL_VIDEODRIVER=dummy`.
- No performance regression versus the WPF backend's 16ms-tick cadence.
  This is the one named anti-goal (below) — treat it as a real
  acceptance bar, not an afterthought.

## Non-goals

- Keeping WPF as a coexisting or fallback backend. This is a full
  replacement: once the SDL3 backend reaches parity, `WpfBackend.cs`,
  `WpfKeyTranslator.cs`, and their test files are deleted, and
  `Usim.csproj` drops `UseWPF`/`net8.0-windows` for a plain cross-platform
  TFM.
- Using or adapting `usim-cs-sdl/`. It is not read for this port.
- Changing any emulated-hardware behavior. `Tv.cs`, `ColorTv.cs`,
  `Keyboard.cs`, `Mouse.cs`, `Idle.cs` are unchanged — only the backend
  that drives/presents them changes.
- Re-adding `ColorTv`'s per-scanline hsync/vsync toggling into the
  rendering path. `ColorTv.cs` already documents (line 222-226) that its
  C# port of `sdl3_video_present_color`'s unpacking loop deliberately
  omits per-scanline hsync/vsync timing — that divergence already exists
  and is out of scope to revisit here. The new backend treats `ColorTv`
  as a frame-buffer source exactly as `WpfBackend.cs` does today.

## Anti-goals (would make this a failure even if it "works")

- **Poor performance.** Explicitly named by the project owner as a
  failure condition even if the port is otherwise functionally correct.
  The texture-streaming/present path must not introduce visible lag or
  stutter versus the current WPF backend.
- Silently diverging from real C's documented behavior (dual-window
  layout, scancode queue/thread model, boot-chord detection) without
  flagging the divergence as deliberate, the way `ColorTv.cs` already
  does for its one documented omission. Undocumented drift breaks this
  project's faithful-port discipline.

## Constraints and facts established by reading the real code

These aren't decisions — they're what's actually there, confirmed by
reading the source (not assumed from this document's own prose, per this
project's standing rule to verify against the real C/C# before building
on it):

- **Single wiring point.** `MachineControl.cs:62` and `:118` are the only
  place `WpfBackend` is constructed: `DisplayBackend = new
  WpfBackend(Tv, ColorTv, Iob, Keyboard, Mouse, Idle, onTick:
  RunMicrocodeBatch)`. The new backend's constructor should match this
  shape so this call site is a near drop-in replacement.
- **Keyboard mapping is already backend-agnostic.** `WpfKeyTranslator.cs`
  explicitly documents (header comment) that its scancode table and
  `GetModifierState` logic were carried over from an earlier
  `SDL2Backend`, independently derived from
  `usim/sdl3-keyboard-cadet-scancodes.defs` and
  `usim/sdl3-keyboard-default-mapping.defs`. The Cadet-scancode values
  and modifier bitmask are real, already-verified facts about the
  hardware, not WPF-specific — reuse them against SDL3's own key/scancode
  enum rather than re-deriving from the `.defs` files from scratch,
  unless SDL3's enum requires it.
- **Real C's keyboard backend runs a dedicated thread.**
  `sdl3-keyboard.c`'s `sdl3_keyboard_thread_run` dequeues scancodes
  (`sdl3_keyboard_dequeue`) off a queue that `sdl3_keyboard_key_down`/
  `sdl3_keyboard_key_up` enqueue into, and has specific cold-boot/
  warm-boot chord detection (`sdl3_keyboard_are_all_ctrl_and_meta_down_
  for_boot`). This is real, intentional behavior to preserve — not an
  implementation detail to simplify away. Today's WPF backend runs
  everything off one `DispatcherTimer` tick with no separate thread; see
  "What's left to the builder" for the judgment call this creates.
- **SDL3-CS is a real, actively maintained package** — confirmed via web
  search, not assumed. NuGet id `SDL3-CS`, source at
  github.com/edwardgushchin/SDL3-CS, with per-RID native companion
  packages. Do not confuse it with the similarly-named `ppy.SDL3-CS`
  (a different maintainer's bindings).
- **Cold/warm-boot scancode constants have a typo in real C.**
  `sdl3-keyboard.c:20-21`:
  ```c
  const uint32_t cold_boot_scancode = (0b11111 << 19) | (0b111 << 16) | (0b111111 < 10) | 046;
  const uint32_t warm_boot_scancode = (0b11111 << 19) | (0b111 << 16) | (0b111111 < 10) | 062;
  ```
  `(0b111111 < 10)` is a comparison (always `0`), not the `<<` shift the
  surrounding pattern clearly intends — this silently zeroes part of
  both boot-chord values in real C. Confirmed by reading the file
  directly, not just taken on a reviewer's word, per this project's
  standing verify-against-source discipline. **Resolved: fix it** (use
  the intended `<<` shift) and document the divergence in code, the
  same way `ColorTv.cs` already documents its one deliberate omission —
  do not port the bug bit-exact.
- **Real C's OpenGL hint/context calls are dropped.** `sdl3_video_init`
  sets `SDL_HINT_RENDER_DRIVER` to `"opengl"` and calls
  `SDL_GL_CreateContext`/`SDL_GL_MakeCurrent` per window before each
  present (`sdl3-video.c:170,190,226,457,527`), even though all actual
  drawing goes through the 2D `SDL_Renderer`/texture API
  (`SDL_LockTexture`, `SDL_RenderTexture`, `SDL_RenderPresent`), which
  manages its own context. SDL's `dummy` video driver — needed for the
  headless-CI goal — doesn't back a real GL context, so porting these
  calls literally would likely break headless CI. **Resolved: omit the
  GL hint/context calls** as vestigial relative to the real drawing
  path, and document the omission in code as a deliberate divergence.
- **Window-placement config is in scope, not skipped.** See the Goals
  section above — `window_display`/`window_position_x`/`_y`/
  `window_always_on_top` get their own `usim.ini`/`ConfigManager`/
  `UsimState` surface as part of this port, not just backend-internal
  defaults.
- **Current project file:** `Usim.csproj` has
  `<TargetFramework>net8.0-windows</TargetFramework>`,
  `<UseWPF>true</UseWPF>`, and a `ProjectReference` to `chaos-cs`. Both
  WPF-specific settings go away; the `chaos-cs` reference is unrelated
  and stays.

## Decisions made in this conversation

- Full replacement, no dual-backend/fallback mode (owner's call).
- `SDL3-CS` specifically, not a different binding package (owner's
  call).
- Faithful port of the real C `sdl3-*.c` files, not an idiomatic rewrite
  of `WpfBackend.cs` (owner's call).
- Second color-TV window is in scope (owner's call — and turns out to
  already exist in `WpfBackend.cs`, so this is continuity, not new
  surface area).
- CI test coverage expands to exercise the real SDL3 init/window/texture
  path headlessly via `SDL_VIDEODRIVER=dummy`, beyond today's
  pure-logic-only tests (owner's call).
- `usim-cs-sdl/` is not read or reused for this port (owner's call).
- Target platforms: Windows, Linux, macOS (owner's call).
- Poor performance is the named anti-goal/failure condition (owner's
  call).
- The cold/warm-boot scancode typo in real C gets fixed, not ported
  bit-exact, with the divergence documented in code (owner's call,
  after a builder-check review surfaced the discrepancy).
- Real C's OpenGL hint/context calls are dropped as vestigial, with the
  omission documented in code (owner's call, same review).
- Window-placement config (`window_display`, `window_position_x`/`_y`,
  `window_always_on_top`) is added as new `usim.ini`/`ConfigManager`/
  `UsimState` surface, matching real C's full capability rather than
  staying at today's WPF-only parity (owner's call, same review).

## What's left to the builder

- **API mapping.** Walk `sdl3-video.c`, `sdl3-keyboard.c`,
  `sdl3-audio.c`, `sdl3-mouse.c` function-by-function against the actual
  `SDL3-CS` binding signatures (window/renderer/texture creation,
  `SDL_LockTexture`/`SDL_UpdateTexture`, `SDL_RenderTexture`,
  `SDL_SetRenderScale`, the audio stream API, event polling) while
  implementing. Don't assume a 1:1 name match going in.
- **Class structure.** The real C splits this across 4 translation units
  with module-level static state. A natural C# mapping is 4 classes
  (e.g. `Sdl3Video`, `Sdl3Keyboard`, `Sdl3Audio`, `Sdl3Mouse`) composed
  by one `Sdl3Backend` that plays the role `WpfBackend` plays today at
  the `MachineControl` call site — but the builder may choose a different
  split if the port reveals a better seam. Either way, keep the same
  constructor shape `MachineControl.cs` already calls.
- **Threading model for keyboard input.** Decide whether to port
  `sdl3_keyboard_thread_run`'s dedicated thread literally (a real
  `Thread`/`Task` dequeuing a concurrent queue) or fold it into the
  existing single-tick model safely. Before deciding, work out *why* the
  C code uses a separate thread (decoupling SDL's event pump from
  scancode delivery timing) — if the C# port diverges from the literal
  thread model, document why in code, the same way `ColorTv.cs` already
  documents its one deliberate omission.
- **RID/packaging details.** Exact RID list, self-contained vs.
  framework-dependent publish settings, and which `SDL3-CS.{RID}` native
  packages to reference for Windows/Linux/macOS.
- **Headless CI mechanics.** Where `SDL_VIDEODRIVER=dummy` gets set (test
  runner env, CI config, or both), and exactly which init/present code
  paths get exercised headlessly vs. which stay pure-logic-only (as
  today's key-translation/beep tests already are).
- **Performance verification.** "Poor performance is a failure" needs a
  concrete check — e.g., logging/asserting frame-present timing against
  the 16ms tick budget, or a manual side-by-side comparison against the
  WPF backend before it's deleted. Exact mechanism is the builder's call.
- **Parity bar for deletion.** `WpfBackend.cs`/`WpfKeyTranslator.cs` and
  their tests are deleted once the SDL3 backend is verified at parity.
  What "verified" means in practice (e.g., automated headless tests
  green on all three platforms plus a manual run-through on at least one
  real display) is the builder's call to propose and the owner's to
  confirm before deletion.
