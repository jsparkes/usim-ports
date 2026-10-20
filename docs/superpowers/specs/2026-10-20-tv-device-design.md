# TV Device Port — Design Spec

## Context

`BusAdaptor.cs`'s TV address ranges (`TvScreenLo`/`Hi`, `TvControlLo`/`Hi`) have existed since Phase 5B as named constants, but both `ReadXbusIo`/`WriteXbusIo` fall through to the generic "un-ported XBus-I/O" logged no-op for any address in those ranges — the real device (`usim/tv.c`, 371 lines) has never been ported. This spec faithfully ports it, scoped to the main black-and-white TV only (`tv_monitor` case 0, the "CPT" monitor) — color TV (`usim/colortv.c`) stays out of scope, same deliberate boundary every prior phase has drawn around it.

**Why now:** to prove the microcode engine, bus-interface, and TV device can genuinely put pixels into the real framebuffer — headless, verified by saving an image, not by trusting that the wiring compiles.

This repo already has a `Display.cs`/`WpfBackend.cs` pair (from the earlier SDL-to-WPF conversion phase, which predates this project's faithful-port discipline) that renders a `WriteableBitmap` in a live WPF window. That pair is **not** part of this spec and is left untouched — it has its own invented video-memory model, unconnected to any real device-register address, and fixing or replacing it is a separate, future decision. This spec adds a new, independent, faithful `Tv.cs` wired directly into `BusAdaptor`, with its own screenshot-writing method — no live GUI window required.

## The Milestone (headless, screenshot-verified, no live window)

Write synthetic words through `BusAdaptor`'s real register interface at the real TV-screen address range, producing a recognizable pattern (my call: a checkerboard — trivially recognizable, and exercises every bit position and word boundary, unlike a single filled rectangle), then save it as an image and visually confirm the pattern is there. The image file itself proves the port: if the address decode, bit order, or buffer sizing were wrong, the saved image would be visibly wrong (blank, shifted, or garbled), not just "didn't throw."

**Non-goal:** getting any real CADR microcode to draw something. `promh.mcr` never touches the TV at all (re-confirmed this session, zero references in `promh.text`) — only `ucadr.mcr`'s own, still-unattempted cold-boot-toward-a-Lisp-world stretch goal could ever produce real TV output, and that's explicitly out of scope here, same boundary the boot-milestone spec drew.

**Non-goal:** color TV (`usim/colortv.c`), the TV control register's sync-RAM/sync-pointer/vertical-spacing sub-registers (real, but not needed to get a correct screen buffer rendering — ported for completeness per Design below, not because the milestone depends on them), and any change to `Display.cs`/`WpfBackend.cs`'s live-GUI path.

## Current State (verified against the real C source this session, not assumed)

- `usim/tv.c`'s actual device model is much simpler than `Display.cs`'s invented one: a flat `uint32_t tv_screen_buffer[0100000]` (32768 words = `0x8000`), with `tv_screen_write(offset, v)` storing a 32-bit word directly at `tv_screen_buffer[offset]` — no translation, `offset` is already `paddr - TvScreenLo` (word-addressed). `TvScreenHi - TvScreenLo + 1 == 0x8000` in `BusAdaptor.cs` — the existing address-range constants and the real buffer size are mutually consistent, already cross-checked this session.
- `usim/tv.c`'s `tv_save_screenshot(filename)` writes a plain-text PBM (`P1` ASCII bitmap) image directly from `tv_screen_buffer`'s raw bits: `for i in 0..width*height: offset=i/32, bitpos=i%32, bit=(buffer[offset]>>bitpos)&1`, writing `'1'`/`'0'` characters, a newline every 70 characters, header `"P1\n{width} {height}\n"`. This function has **zero dependency on `tv_mode`/BOW/color** — it reads the raw bitmap, nothing else. This is the exact, already-proven-simple reference behavior to port for this milestone.
- `tv_init()`'s `tv_monitor` case 0 ("CPT" monitor, the default — `tv_monitor` is a zero-initialized C global) sets `tv_width=768, tv_height=896`. `Display.cs`'s existing `WIDTH=768, HEIGHT=896` constants already match this case — re-verified this session (a top-of-file comment elsewhere in `tv.c` mentions "cadr: 768 x 963", which is monitor case 1, NOT the default — don't be misled by that comment into using 963; case 0's literal values are what this spec uses).
- `tv_mode` register bits (verified against the actual predicate functions, not the file's top-of-file summary comment, which is itself slightly wrong — see below): `tv_is_black_on_white()` returns `(tv_mode & 04) != 0` (octal 4 = bit 2); `tv_is_interrupt_enabled()` returns `(tv_mode & 010) != 0` (octal 10 = bit 3). **Discrepancy found and resolved:** the file's own top-of-file comment claims bit 7 of `tv_mode` is "SYNC PROM ENB", but the actual predicate `tv_is_sync_prom_enabled()` checks bit 7 of a *different* register, `tv_vert_spacing` (`(tv_vert_spacing & 0200) != 0`), not `tv_mode` at all. The code is the authority; the comment is wrong. Port the code's actual behavior.
  - **Correction (final-review pass):** the line above dropped `tv_is_sync_prom_enabled()`'s leading negation. The real C (`usim/tv.c:103-108`) is `return !((tv_vert_spacing & 0200) != 0);` — sync-prom is enabled when bit 7 is **clear**, not set. `Tv.cs`'s `SyncPromEnabled` has been fixed to `(_vertSpacing & 0x80) == 0`.
- `tv_control_write` writing the mode register (offset 0) does `tv_mode = v` with no masking — a software write can set every bit including the ones real hardware would treat as read-only status (vsync/hsync). This is the real C's own existing behavior (not a shortcut either reference or this port is introducing) — port it as-is, warts included, consistent with this project's standing rule to be faithful to the real emulator rather than "fix" it to be more hardware-accurate than its own reference implementation.
- `tv_control_write`'s BOW-flip side effect (re-reading and re-writing every word when black-on-white toggles, to trigger a full-screen redraw in the real C's SDL/X11 rendering path) only matters for `Display.cs`'s pixel-color rendering, which this spec doesn't touch — port the state-tracking (`tv_foreground`/`tv_background` aren't needed at all, actually, since nothing in this spec's scope reads them) but the redraw-loop side effect can be omitted as dead code for this port's scope (see What's Left to the Builder).
- `BusAdaptor.cs`'s `TvScreenLo`/`Hi` (`0x3C0000`-`0x3C7FFF`) and `TvControlLo`/`Hi` (`0x3DFFF0`-`0x3DFFF7`) constants already exist and are already correctly derived from the real octal ranges (`017000000`-`017077777` and `017377760`-`017377767` respectively, confirmed against `bus-adaptor.c` this session) — only used today by `DescribeXbusIo`'s logging strings; `ReadXbusIo`/`WriteXbusIo` never branch on them.

## Design

### `usim-cs/Tv.cs` (new file)

A new class, `Tv`, faithfully porting:
- `uint[] ScreenBuffer` — `new uint[0x8000]` (32768 words), matching the real `tv_screen_buffer`.
- `uint Width`/`Height` — `768`/`896` (monitor case 0's values — constants, not configurable; `tv_monitor`'s other cases are out of scope).
- `uint Mode` — the `tv_mode` register, initialized to `0`.
- `ScreenRead(uint offset, out uint value)` / `ScreenWrite(uint offset, uint value)` — direct `ScreenBuffer[offset]` get/set. **No bounds check beyond what the array itself enforces** — real hardware's address decode in `BusAdaptor` already guarantees `offset` is in `[0, 0x8000)` before calling this (`TvScreenHi - TvScreenLo + 1 == 0x8000` exactly), so an out-of-range `offset` here would mean `BusAdaptor`'s own range check is broken, not a TV-level concern — a raw `IndexOutOfRangeException` in that case is an acceptable, honest failure, not something to silently clamp.
- `ControlRead(uint offset, out uint value)` / `ControlWrite(uint offset, uint value)` — port `tv_control_read`/`tv_control_write`'s real switch statement: offset 0 is the mode register (read returns `Mode`; write sets `Mode = value` with no masking, matching the real C's own unmasked behavior); offset 1 is the sync-RAM register (port the real `tv_sync_ram[4096]`/`tv_sync_ptr`/`tv_vert_spacing`-gated logic faithfully — it's real, if minor); offsets 2/3 (sync pointer, vertical spacing) are write-only per the real C, matching that asymmetry. Any other offset: a real behavior to port too — the real C calls `bus_interface_set_xbus_nxm()` and logs a warning for an invalid offset. Check whether `BusInterface.cs` (already ported, Phase 4) has an equivalent NXM-setting method; if so call it, matching real fidelity; if genuinely no such hook exists yet, log via `TraceLog` and treat as a non-fatal no-op, same convention `BusAdaptor.cs`'s other still-partial paths already use — this is a minor completeness detail, not load-bearing for the milestone.
- `Reset()` — `Mode = 0`, clear `ScreenBuffer`.
- `SaveScreenshotPbm(string filename)` — faithful port of `tv_save_screenshot`'s exact bit-extraction algorithm (described in Current State above) to the same PBM (`P1` ASCII) format. Keep it exactly bit-for-bit faithful — this is the milestone's actual proof mechanism, not a place to "improve" the format.

**Deliberately not ported** (see Current State): `tv_foreground`/`tv_background`, the BOW-flip full-screen-redraw side effect, `tv_assert_interrupt`/vblank interrupt delivery, `tv_update_screen`'s dirty-rect accumulation (`accumulate_update`/`u_minh` etc.) — all exist only to drive the real C's SDL/X11 pixel-color rendering path, which this spec has no equivalent of (and `Display.cs`/`WpfBackend.cs`, which do, are out of scope). If a future spec wires `Tv.cs` into `Display.cs` for live rendering, these become relevant then, not now.

### `BusAdaptor.cs` wiring

Add a `Tv` field (constructed internally, like `_diskController` is wired post-construction — but `Tv` has no external dependencies the way `DiskController` needs `UCode`, so it can likely just be constructed directly in `BusAdaptor`'s own constructor, no `WireTv` method needed — confirm this is simpler than the disk-controller precedent and do the simpler thing if so). In `ReadXbusIo`/`WriteXbusIo`, add branches for `TvScreenLo..Hi` (→ `Tv.ScreenRead`/`ScreenWrite`) and `TvControlLo..Hi` (→ `Tv.ControlRead`/`ControlWrite`), ahead of the generic fallback — same structural pattern the disk-control branch already uses.

### Synthetic test + screenshot

A new test file, `TvTests.cs` (same established convention: `static class`, `RunAllTests()`, wired to `--test-tv` and into `--test-all`). At minimum:
- A register-interface test: write a few words through `BusAdaptor.Write`/`Read` at `TvScreenLo`+offset, confirm they read back correctly (same real-register-interface discipline the disk tests established — never reach into `Tv`'s internals directly from a test that's supposed to prove the `BusAdaptor` wiring).
- A control-register test: write the mode register through `BusAdaptor`, confirm `Tv.Mode` (or a read-back through the same interface) reflects it.
- A screenshot test: write a checkerboard pattern (my call — alternating bits, recognizable and exercises every bit/word boundary) through the real register interface across the whole screen buffer, call `SaveScreenshotPbm` to a temp file, and assert the file's content matches the expected checkerboard bit-for-bit by independently computing the expected PBM text (not by re-deriving it from the same code path that wrote it) — same "cross-check against an independent computation" discipline the disk-image tests used.

This test needs no optional/untracked real files (unlike the disk and boot-milestone tests) — it's fully self-contained and should just run normally in `--test-all`, no skip-gracefully logic needed.

## Constraints

- No new NuGet dependencies. No image-format library needed — PBM is plain ASCII text, trivially written with `StreamWriter`/`StringBuilder`.
- `Tv.cs` must not reference `Display.cs`, `WpfBackend.cs`, or any WPF type — keep this fully headless and testable without a GUI toolkit loaded.
- Match the real C's buffer size (`32768` words) and dimensions (`768x896`) exactly — these are hardware constants, not configurable in this port's scope.
- Every octal/hex literal and bit position referenced in this spec was read directly from `usim/tv.c`/`usim/bus-adaptor.c` during this spec's own research, not transcribed from a summary — including the one real discrepancy found (the `tv_mode` bit-7 comment vs. the actual `tv_vert_spacing`-based check), resolved in favor of the code.

## What's Left to the Builder

- Whether `BusAdaptor` constructs `Tv` directly in its own constructor or via a `WireTv`-style method — the spec's judgment is "probably just construct it directly since there's no constructor-ordering problem like `DiskController` has," but confirm against the actual `BusAdaptor` constructor before committing to either.
- The exact synthetic checkerboard pattern's bit values (any recognizable, verifiable pattern satisfies the milestone — the spec's choice is "alternating bits" as a simple default, not a hard requirement).
- Whether the TV control register's invalid-offset NXM-setting hook exists yet in `BusInterface.cs`'s public surface — if not, fall back to a logged no-op, consistent with every other still-partial `BusAdaptor` path.
- Exact PBM output file path/naming for the test (a temp file is sufficient; this spec doesn't require it be saved anywhere permanent — if the user wants a copy to look at after the test runs, that's a one-line `SendUserFile`-style addition at the call site, not part of this spec's test).
