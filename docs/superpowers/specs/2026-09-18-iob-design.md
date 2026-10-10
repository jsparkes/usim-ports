# IOB (I/O Board) Design

## Context

Real-data boot testing during the color TV port's final review (`docs/superpowers/specs/2026-09-14-color-tv-design.md`) found that `usim-cs`'s IOB address range (Unibus `0764000`-`0764176` octal, `IobLo`/`IobHi` = `0x3E800`-`0x3E87E` in `BusAdaptor.cs`) was entirely un-ported — every access fell to the generic placeholder. A real boot against real `sys/ubin/promh.mcr`/`ucadr.mcr` microcode spent 479,987 of 480,000 log lines polling three IOB registers (CSR `0764112`, the microsecond clock `0764120`/`0764122`, and the Chaos-interface CSR `0764140`) and never issued a single real disk command as a result. IOB was the single biggest blocker to exercising any of this project's other faithful ports (disk, both TV devices, Unibus-Map DMA) against real, running CADR microcode.

**Result of this port, on the same real data:** a real boot (promh PROM + disk image) now hands off from the PROM to ucadr, issues ~1115 disk reads (plus ~100 writes and one read-compare), writes ~121k TV screen words, and reaches a working Lisp Listener ("LM-3 System, Experimental System 303.0, Microcode 323"), with zero IOB warnings. Typed keys echo and parse in Lisp, the mouse cursor tracks the host pointer exactly, and a right click opens the System Menu. (The Chaos CSR reads back 0; Lisp accepts that — its network time lookup times out and it falls back to asking for the date.)

The real reference is `usim/iob.c` (444 lines), which multiplexes several logically distinct facilities behind one Unibus register file: keyboard/mouse bridging (with a special cold/warm-boot key-combo detector), a beep pass-through, a command/status register (CSR) with interrupt gating, two clocks (microsecond and 60Hz), a GPIO stub, four Chaos-interface registers that a boot-time probe touches, a serial-interface stub, four undocumented registers, and (SDL3-only) a one-shot interval timer.

Two existing classes turned out to also be entirely invented, not faithful ports, once this design started tracing the real keyboard/mouse data path: `usim-cs/IOBus.cs` (a generic `IIODevice` device-registry framework, never wired into `BusAdaptor` — the same situation `Display.cs` was in before the monochrome TV port) and, more subtly, `usim-cs/Keyboard.cs`/`Mouse.cs` (whose internal buffer/modifier-bit-packing and mouse-XY-scaling schemes bore no relation to the real hardware protocol). The real, modern protocol lives in `iob.c`'s `WITH_SDL3`-guarded functions — `iob_set_keyboard_ready(scancode)` and `iob_set_mouse_ready(x, rawx, y, rawy, head, middle, tail)` (`usim/iob.c:87-133`), called from `usim/sdl3-keyboard.c`/`sdl3-mouse.c`'s input handlers. The older, non-SDL3 path (`kbd.c`/`mouse.c`'s queue/A-memory-symbol-injection logic, `cadet.c`/`knight.c`'s keyboard-type variants) is dead code on the SDL3 build (`iob_init()`'s `#ifdef WITH_SDL3 ... #else kbd_init(); mouse_init(); #endif` never calls them when `WITH_SDL3` is defined) — this port targets the SDL3 model exclusively, matching every other "WPF replaces the modern SDL3/X11 backend" decision already made in this project.

## Decisions

- **Scope.** This port replaces `IOBus.cs` outright (mirroring the `Display.cs` precedent), and rebuilds `Keyboard.cs`/`Mouse.cs` around the **real SDL3 wire protocol**: the CSR ready-bit/clear-on-read/interrupt semantics, the NKB-headed keyboard scancode, level-based mouse buttons, and microcode A-memory position injection for the mouse. It does **not** replicate `sdl3-keyboard.c`'s external `.defs`-file-loading architecture or its background thread/queue/exponential-backoff delivery mechanism — key/mouse events are delivered synchronously and directly from WPF's own event dispatch, with no queue and no runtime file loading. Real Chaosnet networking (`uch11.c`) stays a separate, future port; the Chaos registers real boot activity touches get a minimal, safe idle stub.
- **`WpfKeyTranslator.cs`'s old output could not be reused.** It produced X11-keysym-style codes with shift baked into the returned value (e.g. unshifted `a` = `0x61`, shifted `A` = `0x41`) — a fundamentally different model from the real Cadet scancode table, which assigns each *physical key* one fixed 7-bit scancode regardless of shift state (shift is its own key, with its own scancode). The real per-key values already sit in this repo as data: `usim/sdl3-keyboard-cadet-scancodes.defs` (physical key name → 7-bit octal scancode) and `usim/sdl3-keyboard-default-mapping.defs` (SDL/physical key → Cadet key name). `TranslateKey`'s body is replaced with a small, hardcoded C# table built by combining those two files (covering ordinary US-layout typing keys: letters, digits, punctuation, function keys F1-F7, arrows, Home/End/PageUp/PageDown, modifiers) — independently verified via a Python script rather than transcribed by hand (see Octal Literals). This is a data substitution, not an architectural one: no `.defs` runtime loading, no string lookup machinery.
- **Keyboard scancodes carry the NKB header.** The real `sdl3_keyboard_new_scancode()` builds every scancode as `(0b11111 << 19) | (0b001 << 16)` = `0xF90000` ORed with `(cadet_scancode & 0x7F)`, with bit 8 (`0x100`) set for key-up. NKB<23:19> is all ones (reserved) and NKB<18:16> = `001` ("new keyboard"); Lisp's `KBD-CONVERT-TO-SOFTWARE-CHAR` selects its converter from bits 18:16 and **drops the key entirely if the header is missing**. `Keyboard` therefore delivers `0xF90000 | (code & 0x7F)` for key-down and `0xF90000 | 0x100 | (code & 0x7F)` for key-up, so the keyboard high register reads `0x00F9` and the low register reads the code (bit 8 = up). A scancode of 0 (an unmapped key) is never delivered. (The first cut of this design omitted the header; typing echoed nothing on a real boot until it was added.)
- **Mouse position travels by microcode A-memory injection, not through the IOB registers.** This is exactly what the real `sdl3-mouse.c` does: it writes fixnum x/y straight into the microcode's `A-MOUSE-X`/`A-MOUSE-Y` A-memory cells (as `DTP_FIX`: `(x & ((1 << 24) - 1)) | (5 << 25)`) and sets `A-MOUSE-WAKEUP` to the value held in `A-V-TRUE`. It then calls `iob_set_mouse_ready(0, 0, 0, 0, head, middle, tail)` with x = y = 0, because the microcode's `TRACK-MOUSE` treats the IOB X/Y registers as *relative* quadrature counters; passing an absolute host position there flings the Lisp cursor to the bottom-right corner. `Mouse.WireMicrocode(UCode)` gives `Mouse` access to `UCode.AMem`; the four variables are resolved **lazily by name** from the currently loaded microcode symbols (`Disassembler.Symbols`) via `SymbolTable.TryFindByName(SymbolType.AMem, name, out value)` — the lookup half of `usym.c`'s `sym_find()`, added to `SymbolTable` for this port. Resolution is cached per `SymbolTable` instance, masked to the 10-bit A-memory index like `UCode.ReadAMem`, and never throws: if the symbols are missing (no microcode symbols loaded, or a symbol file without these names) injection silently becomes a no-op with at most one logged warning, and the IOB mouse-ready report still happens.
- **Mouse buttons are level-based: bit = 1 while held.** `SetMouseReady(0, 0, tail, middle, head)` is driven from the held state of the WPF buttons on every position or button update (Left → tail → Y bit 12, Middle → middle → bit 13, Right → head → bit 14). This matches Lisp's own definition of the register ("a 1 for each button currently held down", read un-inverted from `LDB #o1403` of the Y register in `mouse.lisp`), and was confirmed on a real boot: a right click opens the System Menu. It also makes a redundant press/release, or a press inside the window and release outside it, harmless — there is no hardware toggle state to desynchronize. (The legacy `mouse.c` path XOR-toggles; that path is dead code on SDL3. `sdl3-mouse.c` itself appears to write the opposite polarity — 0 on down, 1 on up — which looks like an upstream bug; this port keeps 1 = held on the strength of the real-boot evidence.)
- **The keyboard-triggered cold/warm-reboot hotkey is dropped, not replicated.** The real trigger is a *separate* mechanism from ordinary scancode delivery: `sdl3-keyboard.c` detects all four Meta/Control keys plus Rubout or Return held simultaneously and feeds a sentinel value into `iob_set_keyboard_ready` when it fires. Without that combo detector nothing would ever produce the sentinel, so the sentinel comparison in `SetKeyboardReady` (and `iob_prepare_for_warm_boot`) are omitted entirely rather than left as unreachable code. `MachineControl.PowerOn()` is already reachable through this project's CLI/API paths, so the hotkey is a hardware convenience, not a functional gap. (Incidentally, `sdl3-keyboard.c:20-21` defines `cold_boot_scancode`/`warm_boot_scancode` using `(0b111111 < 10)` — a comparison, not the evidently intended `<<` shift. Noted only for the record; irrelevant here.)
- **Key queue (added after the initial port).** `Keyboard` now holds `sdl3-keyboard.c`'s 100-entry queue and replaces its delivery thread with a poll (`Keyboard.Tick()` plus a pump on every enqueue): the head scancode is delivered only while CSR bit 5 is clear, and is dropped with a warning after the thread's 2+4+...+256 = 510 ms back-off. A full queue drops new keys with a warning. This removes the overwrite/stuck-modifier hazard the initial port documented.
- **Mouse raw encoder bits (`rawX`/`rawY`) are always 0.** The bit *layout* (`0x7777` mask, button shift amounts 12/13/14) is preserved exactly so code that inspects those bits sees deterministic zeros rather than garbage. In practice `Mouse` always passes x = y = 0 (see the injection decision above), so the X register reads 0 and the Y register carries only the button bits.
- **The interval timer (`0764124` write, SDL3-only in the real C via `setitimer`/`SIGALRM`) is tick-counted, not signal-based.** A countdown decremented once per `Iob.Tick()`, firing `UCode.AssertUnibusInterrupt(0274)` at zero. The real C's `16 * v` microsecond unit is converted to an equivalent tick count using the same ~16ms-per-tick assumption `WpfBackend` and the headless loop already use elsewhere (a written value of 0 yields 0 ticks, i.e. never fires, which matches `setitimer` with a zero value disarming the timer in practice).
- **The 60Hz clock (`the_60_cycle_clock`, `0764124` read) finally gets a home.** Both TV specs flagged this as "a separate IOB-owned counter, out of scope for TV/color TV" — this port is that home: incremented once per `Iob.Tick()`, read back as a free-running 16-bit counter (matching the real C's `uint16_t the_60_cycle_clock`).
- **The microsecond clock's high word is a latch.** Reading the low word (`0764120`) samples the clock and latches the full 32-bit value; reading the high word (`0764122`) returns the high half of *that latched value*, not a fresh sample — so a low-then-high read pair is self-consistent even if the low word wraps in between, matching `iob.c`. A high read before any low read returns 0 and does not start the clock.
- **Register width is genuinely 16 bits, not 32.** `iob_unibus_read`/`iob_unibus_write` take `uint16_t *pv`/`uint16_t v` — unlike every XBus-I/O device ported so far (disk, TV, color TV), which use 32-bit registers. `Iob`'s read/write use `ushort`, and `BusAdaptor`'s dispatch truncates to 16 bits at the boundary rather than silently carrying a 32-bit value through a 16-bit device.
- **Register offsets are `octal_address - 0764000`.** `IobLo` is `0764000` octal (= `0x3E800`), so e.g. the keyboard low register at `0764100` is offset `0x40`, not `0x20`. See the Register Model for the full table.
- **Chaos-interface registers get a minimal, safe idle stub, not real networking.** `ch_csr` (`0764140`) reads back a constant "idle, no packet pending, no error" value (0); `ch_myaddr` (`0764142`) reads back a placeholder Chaos address; `ch_rbf`/`ch_rbc` (`0764144`/`0764146`) read back 0 (no packet ever arrives); `ch_xmt` (`0764152`) echoes the placeholder address (matching the real C's read-side echo) and transmits nothing. Writes are accepted and ignored. This gives the real boot-time probe a consistent, non-hanging response without pretending to implement Chaosnet.
- **GPIO, serial, and undocumented registers are warn-and-return-0 stubs**, matching the real C's own `warnx(...)` calls for these exact ranges (`usim/iob.c:250-254,285-299`) — non-fatal visibility, not ported behavior.
- **`Keyboard.Initialize()` exists as a minimal reset.** It only clears the `KeysPressed` counter; `MachineControl`'s reset paths still call it. `Mouse` has no hardware toggle state at all (levels come from its `Buttons` flags), so `Mouse.Initialize()` has nothing of that kind to clear.

## Octal Literals — Independently Verified

Computed via Python, not by hand:

| Literal | Decimal | Hex | Meaning |
|---|---|---|---|
| `0764000` (IOB base) | 256000 | `0x3E800` | `IobLo`; every register offset is `octal_addr - 0764000` |
| `017` (CSR write mask) | 15 | `0xF` | CSR bits 0-3 are the only real writable bits (`iob_csr = (iob_csr & ~017) \| (v & 017)`) |
| `0260` (keyboard interrupt vector) | 176 | `0xB0` | passed to `AssertUnibusInterrupt` on keyboard-ready |
| `0264` (mouse interrupt vector) | 180 | `0xB4` | passed to `AssertUnibusInterrupt` on mouse-ready |
| `0274` (interval-timer interrupt vector) | 188 | `0xBC` | passed to `AssertUnibusInterrupt` when the tick-counted interval timer expires |
| `0x7777` (mouse position/encoder mask) | — | `0x7777` binary `0111011101110111` | every 4th bit reserved for raw-encoder state, preserved as a bit-layout constant |
| `(0b11111 << 19) \| (0b001 << 16)` (NKB header) | 16318464 | `0xF90000` | ORed into every keyboard scancode; high register reads `0x00F9` |
| `0x7F` / `0x100` (scancode fields) | — | `0x7F` / `0x100` | low 7 bits = Cadet scancode; bit 8 set = key-up |
| `0177040` (`uch11_myaddr`, `LOCAL-CADR`) | 65056 | `0xFE20` | the real default Chaos address |
| `01350` (default beep half-wavelength) | 744 | `0x2E8` | microseconds (~672Hz), from `iob.c`'s own comment |

CSR bit masks (unrelated to register offsets): bit 5 `0x20` = keyboard-ready, bit 4 `0x10` = mouse-ready, bit 2 `0x4` = keyboard-interrupt-enable, bit 1 `0x2` = mouse-interrupt-enable.

**The real per-key Cadet scancode table** (hardcoded WPF `Key` → real 7-bit scancode, replacing `WpfKeyTranslator.TranslateKey`'s body), computed by combining `usim/sdl3-keyboard-cadet-scancodes.defs` (physical key → octal scancode) with `usim/sdl3-keyboard-default-mapping.defs` (SDL/physical key → Cadet key name) via a Python script, not by hand — every value below is independently derived, not transcribed from either `.defs` file's own notation. These are the values that go in the low 7 bits under the `0xF90000` NKB header:

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

Any WPF `Key` not in this table (function keys F8-F12, numpad, Insert/Delete/PrintScreen/etc.) has no real default Cadet mapping either (confirmed by the `.defs` file's own "no default mapping" comment block) and returns 0 (`XK_VoidSymbol`-equivalent), matching the real system's own behavior for those keys; `Keyboard` ignores a 0 scancode.

## Register Model

All registers are 16-bit words in the Unibus range `0764000`-`0764176` octal (`0x3E800`-`0x3E87E`). `Iob` receives `offset = uaddr - IobLo`, where `IobLo` = `0764000` octal = `0x3E800`; the offset of any register is therefore its **octal address minus `0764000`** (not a `2*N` index from the start of the visible range). Registers are 2 bytes apart.

| Register | Octal address | Offset |
|---|---|---|
| keyboard low | `0764100` | `0x40` |
| keyboard high | `0764102` | `0x42` |
| mouse Y | `0764104` | `0x44` |
| mouse X | `0764106` | `0x46` |
| beep | `0764110` | `0x48` |
| CSR | `0764112` | `0x4A` |
| usec clock low / high | `0764120` / `0764122` | `0x50` / `0x52` |
| 60Hz clock (read) / interval timer (write) | `0764124` | `0x54` |
| GPIO | `0764126` | `0x56` |
| `ch_csr`, `ch_myaddr`, `ch_rbf`, `ch_rbc` | `0764140`-`0764146` | `0x60`, `0x62`, `0x64`, `0x66` |
| `ch_nop` (obsolete), `ch_xmt` | `0764150`, `0764152` | `0x68`, `0x6A` |
| obsolete Chaos | `0764154`, `0764156` | `0x6C`, `0x6E` |
| serial | `0764160`-`0764166` | `0x70`-`0x76` |
| undocumented | `0764170`-`0764176` | `0x78`-`0x7E` |

**Keyboard** (offsets `0x40`/`0x42`):
- Read `0x40` (kbd low): returns `_scancode & 0xFFFF`, then clears the keyboard-ready bit (CSR bit 5).
- Read `0x42` (kbd high): returns `(_scancode >> 16) & 0xFFFF` (`0x00F9` for any key delivered through `Keyboard`). Does **not** clear the ready bit again (already cleared by the low-word read, matching the real C's comment `// CSR cleared above when reading 0100`).
- `SetKeyboardReady(uint scancode)`: `_scancode = scancode`; set CSR bit 5; if CSR bit 2 (keyboard-interrupt-enable) is set, call `UCode.AssertUnibusInterrupt(0xB0)`. (No cold/warm-boot sentinel check — see Decisions.)

**Mouse** (offsets `0x44`/`0x46`):
- Read `0x44` (mouse Y): returns `_mouseY`, then clears the mouse-ready bit (CSR bit 4).
- Read `0x46` (mouse X): returns `_mouseX`. Does **not** clear the ready bit (matching the real asymmetry — the Y read is the "consume" side).
- `SetMouseReady(int x, int y, bool tailPressed, bool middlePressed, bool headPressed)`: `_mouseX = x & 0x7777`; `_mouseY = (tail << 12) | (middle << 13) | (head << 14) | (y & 0x7777)`; set CSR bit 4; if CSR bit 1 (mouse-interrupt-enable) is set, call `UCode.AssertUnibusInterrupt(0xB4)`. The button arguments are held-state levels. `Mouse` always passes `x = y = 0`; position is delivered via A-memory (see Decisions).

**Beep** (offset `0x48`): the real C's read-triggered path calls `sdl3_audio_beep(-1)`, a sentinel for "the audio subsystem's own fixed default tone" — its own comment (`usim/iob.c:209-211`) gives that tone as `#o1350` (744) microseconds half-wavelength (≈672Hz) for 0.13 seconds (130000 microseconds), which is exactly `WpfBackend.Beep(int halfWavelengthMicros, int durationMicros)`'s two parameters. So: a read triggers `OnBeep?.Invoke(744)`; a write triggers `OnBeep?.Invoke((int)v)` (the real C's write path passes `v` directly as the half-wavelength). Both use the same fixed 130000-microsecond duration — `Iob` exposes an `Action<int>? OnBeep` callback that `MachineControl.InitializeDisplay` subscribes as `halfWavelengthMicros => DisplayBackend?.Beep(halfWavelengthMicros, 130000)`, rather than `Iob` holding a direct reference to `WpfBackend`.

**CSR** (offset `0x4A`): read returns the stored CSR value directly (no derived bits — every real CSR bit is either set by `SetKeyboardReady`/`SetMouseReady`, cleared by the corresponding read, or written directly). Write: `_csr = (_csr & ~0xF) | (v & 0xF)` (only the low 4 bits are writable).

**Microsecond clock** (offsets `0x50`/`0x52`, read-only): backed by a `Stopwatch` started on the first low read (matching `get_us_clock`'s "first call returns 0 and records the epoch" behavior). Read `0x50` samples the elapsed microseconds, latches the 32-bit value, and returns its low 16 bits. Read `0x52` returns the high 16 bits of the *latched* value (see Decisions). Writes are accepted and ignored (the real C only traces them).

**60Hz clock / interval timer** (offset `0x54`): read returns the free-running `_the60CycleClock` counter (incremented once per `Tick()`, wrapping at 16 bits via `ushort` overflow). Write sets the tick-counted interval timer: `_intervalTimerTicksRemaining = (uint)Math.Ceiling(16.0 * v / 16000.0)`, where 16000µs is the ~16ms tick period already implicit in `WpfBackend`'s `DispatcherTimer`/the headless loop's `Thread.Sleep(16)`.

**GPIO** (offset `0x56`, read-only in the real C): returns 0, logs a warning — matching `usim/iob.c:251-254`'s `warnx("iob: read from gpio")`.

**Chaos interface** (offsets `0x60`-`0x6E`):
- Read `ch_csr` (`0x60`): returns 0 — no transmit-done, no receive-done, no CRC error, no lost-packet count; "nothing has ever happened" rather than any specific real hardware reset state.
- Read `ch_myaddr` (`0x62`): returns the constant `ChaosMyAddrPlaceholder` = `0xFE20` — the real C's hardcoded `LOCAL-CADR` default `uch11_myaddr = 0177040` (`uch11.c:42`). (Fixed: previously returned `0x7C20`/`076040` octal with a mismatched `0177040` comment.)
- Read `ch_rbf`/`ch_rbc` (`0x64`/`0x66`): return 0 (no packet ever arrives).
- Read `ch_xmt` (`0x6A`): returns the same placeholder address (the real C's read-side echo); performs no transmission.
- Read `ch_nop` (`0x68`) and the obsolete registers `0x6C`/`0x6E`: warn and return 0.
- Writes to `0x60`-`0x6A`: accepted, ignored (no state change).

**Serial interface** (`0x70`-`0x76`): read or write, warn-and-return-0/no-op — matching `usim/iob.c:286-291,375-380`'s `warnx(...)` stubs.

**Undocumented** (`0x78`-`0x7E`): read warn-and-return-0, matching `usim/iob.c:294-299`. (The real C has no write case for this range — any write falls to `iob_unibus_write`'s `default: errx(...)`, a genuine fatal error. This port's default-case convention, established by every prior device, is a non-fatal warning instead — consistent with the monochrome TV port's identical judgment call.) Any other offset likewise warns and returns 0 / ignores the write.

## Tick / Interrupt

`Iob.Tick()`, called from the same ~16ms cadence as every other device (`WpfBackend.Tick()` in GUI mode and the headless `Run()` loop):
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
No enable gate on this device's `Tick()` — unlike TV/color TV, IOB has no `UsimState.*Enabled` flag (it's not an optional peripheral; the real hardware is always present). Note that `UCode.AssertUnibusInterrupt` is a no-op unless the microcode's interrupt status register has the Unibus-interrupt-enable bit (`0x400`) set, so every interrupt assertion described here is additionally gated by that bit.

## Class Shape

The committed `usim-cs/Iob.cs`, reproduced verbatim (it is the source of truth; if the two ever disagree, the code wins):

```csharp
// Iob.cs - Faithful port of usim/iob.c's WITH_SDL3 code path (the CADR I/O
// board). Real Chaosnet networking (usim/uch11.c) is a separate, future
// port; the four Chaos registers real boot activity touches get a minimal,
// safe idle stub here. The keyboard-triggered cold/warm-reboot hotkey is
// dropped entirely -- see docs/superpowers/specs/2026-09-18-iob-design.md.

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
    private uint _usecLatched;

    public Iob(UCode ucode)
    {
        _ucode = ucode;
    }

    private bool IsKeyboardInterruptEnabled() => (_csr & 0x4) != 0;
    private bool IsMouseInterruptEnabled() => (_csr & 0x2) != 0;

    /// <summary>
    /// Faithful port of iob_bus_reset (usim/iob.c:428-431), which has a
    /// genuinely empty body -- IOB state is never cleared by any reset in
    /// the real C. Kept as an explicit method purely for wiring symmetry
    /// with Tv/ColorTv.
    /// </summary>
    public void Reset()
    {
    }

    /// <summary>
    /// Faithful port of iob_set_keyboard_ready (usim/iob.c:88-107, WITH_SDL3
    /// branch), minus the cold/warm-boot sentinel check (that hotkey
    /// mechanism is dropped -- see the design's Decisions). Called directly
    /// from Keyboard's key-down path -- no queue, no retry/backoff.
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
    /// branch). rawX/rawY are always 0 -- WPF delivers discrete positions,
    /// not raw quadrature-encoder ticks, so there is no realistic value to
    /// synthesize (see the design's Decisions).
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

    /// <summary>Faithful port of iob_unibus_read (usim/iob.c:138-303, WITH_SDL3 branch + shared registers). Offsets are relative to IobLo (0764000 octal = 0x3E800).</summary>
    public ushort Read(uint offset)
    {
        switch (offset)
        {
            case 0x40: // kbd low (0764100)
                {
                    ushort v = (ushort)(_scancode & 0xFFFF);
                    _csr &= ~(1u << 5);
                    return v;
                }

            case 0x42: // kbd high (0764102)
                return (ushort)((_scancode >> 16) & 0xFFFF);

            case 0x44: // mouse y (0764104)
                {
                    ushort v = (ushort)_mouseY;
                    _csr &= ~(1u << 4);
                    return v;
                }

            case 0x46: // mouse x (0764106)
                return (ushort)_mouseX;

            case 0x48: // beep (0764110)
                OnBeep?.Invoke(744); // usim/iob.c:209-211's own fixed default: 744us half-wavelength (~672Hz)
                return 0;

            case 0x4A: // csr (0764112)
                return (ushort)_csr;

            case 0x50: // usec clock low (0764120): samples and latches the clock
                _usecLatched = (uint)ElapsedMicroseconds();
                return (ushort)(_usecLatched & 0xFFFF);

            case 0x52: // usec clock high (0764122): value latched by the last low read
                return (ushort)((_usecLatched >> 16) & 0xFFFF);

            case 0x54: // 60Hz clock (0764124)
                return _the60CycleClock;

            case 0x56: // gpio (0764126)
                TraceLog.Instance.Warning(TraceCategory.IOBus, "iob: read from gpio");
                return 0;

            case 0x60: // ch_csr (0764140)
                return 0;

            case 0x62: // ch_myaddr (0764142)
                return (ushort)ChaosMyAddrPlaceholder;

            case 0x64: // ch_rbf (0764144)
            case 0x66: // ch_rbc (0764146)
                return 0;

            case 0x68: // ch_nop (0764150, obsolete)
            case 0x6C: // obsolete chaos (0764154)
            case 0x6E: // obsolete chaos (0764156)
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: chaos read obsolete unibus address: 0x{offset:X}");
                return 0;

            case 0x6A: // ch_xmt (0764152)
                return (ushort)ChaosMyAddrPlaceholder;

            case 0x70: // serial (0764160)
            case 0x72: // serial (0764162)
            case 0x74: // serial (0764164)
            case 0x76: // serial (0764166)
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: read from serial interface: 0x{offset:X}");
                return 0;

            case 0x78: // undocumented (0764170)
            case 0x7A: // undocumented (0764172)
            case 0x7C: // undocumented (0764174)
            case 0x7E: // undocumented (0764176)
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: read from unknown device: 0x{offset:X}");
                return 0;

            default:
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: read from unknown offset: 0x{offset:X}");
                return 0;
        }
    }

    /// <summary>Faithful port of iob_unibus_write (usim/iob.c:307-384). Offsets are relative to IobLo (0764000 octal = 0x3E800).</summary>
    public void Write(uint offset, ushort v)
    {
        switch (offset)
        {
            case 0x40: // kbd low (0764100)
            case 0x42: // kbd high (0764102)
            case 0x44: // mouse y (0764104)
            case 0x46: // mouse x (0764106)
                break; // real C: DEBUG trace only, no state change

            case 0x48: // beep (0764110)
                OnBeep?.Invoke(v);
                break;

            case 0x4A: // csr (0764112)
                _csr = (_csr & ~0xFu) | (v & 0xFu);
                break;

            case 0x50: // usec clock low (0764120)
            case 0x52: // usec clock high (0764122)
                break; // real C: DEBUG trace only, no state change (read-only in practice)

            case 0x54: // interval timer set (0764124)
                {
                    const double tickMicroseconds = 16000.0; // ~16ms per Tick(), matching WpfBackend/the headless loop
                    _intervalTimerTicksRemaining = (uint)Math.Ceiling(16.0 * v / tickMicroseconds);
                }
                break;

            case 0x60: // ch_csr (0764140)
            case 0x62: // ch_myaddr write (0764142; buffer write in the real C)
            case 0x64: // ch_rbf (0764144)
            case 0x66: // ch_rbc (0764146)
            case 0x68: // ch_nop (0764150)
            case 0x6A: // ch_xmt (0764152)
                break; // accepted, ignored -- real Chaosnet networking is out of scope

            case 0x70: // serial (0764160)
            case 0x72: // serial (0764162)
            case 0x74: // serial (0764164)
            case 0x76: // serial (0764166)
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: uart write: 0x{offset:X} v=0x{v:X}");
                break;

            default:
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: write to unknown offset: 0x{offset:X} v=0x{v:X}");
                break;
        }
    }

    /// <summary>
    /// Faithful port of run_at_60hz's the_60_cycle_clock++ (usim/usim.c) plus
    /// the tick-counted interval-timer approximation (see the design).
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

1. **`BusAdaptor.cs`**: gains a settable `Iob? _iob` + `WireIob(Iob iob)`, mirroring `_tv`/`WireTv`. `ReadUnibus`/`WriteUnibus` gain a branch for `IobLo`/`IobHi` (`0x3E800`-`0x3E87E`), computing `offset = uaddr - IobLo`, calling `_iob.Read(offset)`/`_iob.Write(offset, (ushort)v)` with the existing null-guard-and-warn convention. Reads widen the returned `ushort` to `uint` implicitly; writes truncate the incoming `uint v` to `(ushort)v` at the call site, matching the real 16-bit register width.
2. **`MachineControl.cs`**: constructs `Iob = new Iob(UCode);` after `ColorTv`, then wires it: `UCode.BusAdaptor.WireIob(Iob)`, `Keyboard.WireIob(Iob)`, `Mouse.WireIob(Iob)`, and `Mouse.WireMicrocode(UCode)` (A-memory access for mouse position injection). `InitializeDisplay` passes `Iob` into the `WpfBackend` constructor and, after `DisplayBackend.Initialize()`, sets `Iob.OnBeep = halfWavelengthMicros => DisplayBackend?.Beep(halfWavelengthMicros, 130000);`. Both `Reset()`/`InitializeComponents()` call `Iob.Reset()` alongside the other devices' resets, and the headless `Run()` loop calls `Iob.Tick()` next to `Tv.Tick()`/`ColorTv.Tick()`.
3. **`WpfBackend.cs`**: the constructor takes an `Iob` (`WpfBackend(Tv, ColorTv, Iob, Keyboard, Mouse, onTick)`; null is rejected with `ArgumentNullException`), and `Tick()` calls `_iob.Tick()` after `_colorTv.Tick()`. Without this the 60Hz clock and interval timer would be dead in GUI mode — the headless loop is not the GUI tick site.
4. **`Keyboard.cs`**: sheds `AddKey`/`GetKey`/`HasKeys`/`BufferCount`/the `CadrKeyCode` modifier-bit-packing enum entirely. A settable `WireIob(Iob)` supplies the device. `KeyDown(uint keysym, int modifiers)` builds `0xF90000 | (keysym & 0x7F)` and `KeyUp` builds `0xF90000 | 0x100 | (keysym & 0x7F)`, each calling `_iob.SetKeyboardReady(v)`; a zero keysym is ignored. `Initialize()` is a minimal reset (clears `KeysPressed`).
5. **`WpfKeyTranslator.cs`**: `TranslateKey`'s switch body is replaced with the real hardcoded Cadet-scancode table (see Octal Literals); signature unchanged (the `shift` parameter is kept, unused).
6. **`Mouse.cs`**: keeps `X`/`Y`/`Buttons`/`MaxX`/`MaxY`/events (WPF-facing state used for clamping and the status line). Every `MoveTo`/`SetButton` calls an internal `ReportToIob()`, which first injects position into A-memory (resolving `A-MOUSE-X`/`A-MOUSE-Y`/`A-MOUSE-WAKEUP`/`A-V-TRUE` lazily via `SymbolTable.TryFindByName`, see Decisions) and then calls `_iob.SetMouseReady(0, 0, Left, Middle, Right)` with the current held levels. `ReadXRegister`/`ReadYRegister`/`ReadButtonsRegister`/`MouseButtonBits` and the invented 11-bit linear XY scaling are removed — `Iob` is the sole owner of the real register encoding.
7. **`SymbolTable.cs`**: gains `public bool TryFindByName(SymbolType type, string name, out uint value)`, backed by a `(type, name) → value` dictionary populated alongside the existing value → name one (last definition wins, so a later-loaded `ucadr.sym` overrides `promh.sym`). It is case-sensitive and type-keyed, unlike the real `sym_find()`; the real names are uppercase A-MEM so there is no practical difference.
8. **`usim-cs/IOBus.cs`**: deleted entirely (the invented `IIODevice`/device-registry framework, never wired into `BusAdaptor`, has no faithful counterpart and no remaining callers once `Iob.cs` exists).
9. **`TraceLog.cs`**: no change needed — `TraceCategory.IOBus = 128` already existed, just unused until `Iob.cs` started calling `TraceLog.Instance.Warning(TraceCategory.IOBus, ...)`.

## Testing

`IobTests.cs` (new): CSR read/write masking (low 4 bits only); keyboard-ready set via `SetKeyboardReady`, read-clears-on-low-word-only, interrupt gating on CSR bit 2; mouse-ready set via `SetMouseReady`, read-clears-on-Y-only, interrupt gating on CSR bit 1; the real `0x7777`-masked bit layout for representative X/Y values with tail/middle/head packed into Y at bits 12/13/14 (including asymmetric-button cases); the usec clock returning 0 on its first low read, a high read before any low read returning 0 without starting the clock, and a monotonically increasing value afterwards; the 60Hz clock incrementing exactly once per `Tick()` and wrapping past `ushort.MaxValue`; the interval timer firing `AssertUnibusInterrupt(0xBC)` at exactly the computed tick count, not one early or late; the beep callback on read (744) and write (the written value); every GPIO/serial/undocumented/Chaos-stub register returning its documented value without throwing, on both read and write. The test helper `MakeIob` calls `ucode.SetInterruptStatusReg(0x400)` to set the Unibus-interrupt-enable bit — `AssertUnibusInterrupt` is a no-op without it, which would silently hide every interrupt assertion; with the gate open the "no interrupt" assertions rely solely on `Iob`'s own CSR-bit gating.

`KeyboardTests.cs` (new): `KeyDown`/`KeyUp` deliver to a real wired `Iob` with the right encoding — key-down low word = code with bit 8 clear, key-up with bit 8 set, and the **high word reads `0x00F9`** (the NKB header; this assertion would have caught the original omission); the low 7-bit mask (`0x1D3` → `0x53` down / `0x153` up); a zero keysym never sets the ready bit.

`MouseTests.cs` (new): `MoveTo` sets the mouse-ready bit; the IOB X register and Y position bits are 0 after a move (x = y = 0); Left/Middle/Right set exactly the tail/middle/head Y bits while held and clear on release, redundant presses/releases being harmless (level-based); the WPF-facing `Buttons` flags still track held state; position injection writes `DTP_FIX` x/y into `A-MOUSE-X`/`A-MOUSE-Y` and `A-V-TRUE`'s value into `A-MOUSE-WAKEUP` (symbols supplied via a temporary symbol file and `Disassembler.Symbols`), with the IOB X/Y registers still 0; and injection is a silent no-op, with the IOB mouse-ready report still happening, when no symbols are loaded or one of the four names is missing. 

`WpfKeyTranslatorTests.cs` (new): a handful of table entries spot-checked against the Octal Literals values (Backspace → `0x13`, Return → `0x5E`, shift-independence of letters, modifiers, unmapped keys → 0) rather than re-verifying all ~77 entries — the table itself was computed by script and reviewed against the `.defs` files. `WpfBackendTests.TestKeyTranslation` asserts the real Cadet scancodes instead of the old X11 keysym values.

`BusAdaptorTests.cs` gains a real-dispatch-confirmation test for the `IobLo`/`IobHi` range, mirroring `TestTvRangeDispatchesToRealTv`'s shape (offsets as in the Register Model, e.g. CSR at `0764112`/offset `0x4A`).

## Global Constraints

- No new NuGet dependencies.
- Every octal/hex literal in this spec was independently verified via Python — see the tables above.
- Register width is 16 bits (`ushort`), not 32 — a genuine difference from every prior XBus-I/O device port.
- Register offsets are `octal_address - 0764000` (`IobLo` = `0x3E800`).
- No `UsimState.*Enabled` gate on this device — IOB is not optional hardware, unlike TV/color TV.
- The Cadet-scancode values are hardcoded in C# (the `.defs`-file runtime-loading architecture is not replicated); the SDL3 keyboard thread's queue/backoff mechanism and real Chaosnet networking (`uch11.c`) are out of scope (see Decisions and Out of Scope).

## Out of Scope

- `usim/uch11.c` (and its `-chaosd`/`-local`/`-udp` variants) — real Chaosnet packet transmission/reception. A separate, future port; this port's Chaos-register stub is deliberately minimal.
- `usim/kbd.c`/`mouse.c`/`cadet.c`/`knight.c` — dead code on the `WITH_SDL3` build path this project targets.
- `sdl3-keyboard.c`'s `.defs`-file-driven runtime scancode loading (its queue/backoff delivery was added later; see Decisions).
- `iob_prepare_for_warm_boot` and the SDL3 "all keys up" code. The cold/warm-reboot hotkey is dropped, so a warm boot is not distinguishable from a cold one to the microcode (not reachable without the hotkey), and Lisp tracks modifiers from their own down/up codes.
- (Done later) `sdl3_mouse_warp_if_needed`: `Mouse` fires `OnWarp(x, y)` on a change of `A-MOUSE-CURSOR-STATE` to 3, checked on mouse events only as in the C; `WpfBackend` moves the real cursor with `SetCursorPos`, only while the main window is active.
- Any UI for configuring Chaos networking, GPIO, or serial ports — none are implemented beyond the stub level, so there is nothing to configure yet.

