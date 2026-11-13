# End-to-End Boot Milestone — Design Spec

## Context

The CADR microcode-engine port (9 phases, closed 2026-09-06), the
`bus-interface.c` port (merged, PR #1), and the disk-subsystem port
(merged, PR #2, plus this session's real-`disk.img` regression test)
have each been validated at the component level: real `.mcr`/`.sym`
decoding, real disk-pack byte-exact reads, real register-interface
dispatch. Nobody has yet run the emulator through an actual boot and
watched the *pieces work together* for the first time. The original
microcode-engine spec explicitly named this as a later milestone
("Booting `promh.mcr`/`ucadr.mcr` to a Lisp prompt... this port's bar is
instruction-level fidelity, not an end-to-end boot") — this spec is
that later milestone, scoped down to what's actually achievable right
now.

**Why now:** the three subsystems a boot depends on (microcode engine,
bus-interface/diagnostic register, disk subsystem) are all faithfully
ported and already tested against real reference data individually.
This is the natural next integration point, and a cheap one — see
Current State below, almost nothing new needs to be built.

## The Milestone (headless, no framebuffer/display)

**Primary, committed goal:** boot the real `sys/ubin/promh.mcr` PROM
microcode against the real `usim/disk.img` T-300 pack, and detect that
it reaches the real, hardware-accurate PROM→RAM handoff point: program
counter (`UCode.Npc`) at **I-MEM address 6**, with `UCode.PromDisabled
== true`. This is the exact point `promh.text`'s own `JUMP-TO-6` reaches
after it has (a) read the real disk label, (b) loaded the `MCR1`
section (the UCADR microcode) off disk via `WRITE-I-MEM`, and (c)
written the diagnostic-mode register to disable the PROM bank — i.e.
reaching it proves the microcode engine, the disk subsystem, and the
diagnostic register are correctly wired together end to end, for real,
for the first time.

**Bonus verification, same milestone:** once there, diff the
now-populated `UCode.IMem[]` against the real `sys/ubin/ucadr.mcr` file
word-for-word. A match proves the disk-read-into-microcode path
produced bit-exact real UCADR code, not merely "something that happened
to land on address 6."

**Stretch goal, not committed:** continue stepping past the handoff and
see how far execution gets into UCADR's own cold-boot machinery
(`COLD-BOOT`, `COLD-RUN-DISK`, `DISK-RESTORE` — real labels already in
`ucadr.sym`), which would pull a Lisp "world"/band off `disk.img`'s
`LOD6` section. This depends on things nobody has checked yet — whether
that band is even compatible with this `ucadr.mcr`, GC/stack-group
support, etc. — and is explicitly NOT required for this spec to be
"done." If the stretch attempt fails or hangs, that's useful diagnostic
output for a future spec, not a failure of this one.

## Non-Goals / Anti-Goals

- No TV/framebuffer rendering, no character/glyph decoding, no literal
  "banner text" detection. Confirmed fresh against `promh.text` (754
  lines): the boot PROM never touches TV, color TV, tape, mouse,
  keyboard, or Unibus-Map DMA — only the disk-control range and the
  diagnostic register. Those placeholders stay untouched and
  out-of-scope for this spec, same as every prior phase ruled.
- Not attempting to guarantee a full Lisp REPL boots (see Stretch goal
  above) — that depends on `disk.img`'s Lisp band being compatible at
  all, which is unknown and not this spec's problem to solve.
- Not touching `--test-all`'s missing CI exit-code contract, `usim-cs-
  sdl/`, or any other previously-identified open item — unrelated scope.

## Current State (re-verified against source, not assumed from memory)

Nearly everything this milestone needs already exists:

- `UCode.cs` already has separate `Prom[]`/`IMem[]` banks, with fetch
  selected by `PromDisabled`: `P1 = PromDisabled ? IMem[Npc] :
  Prom[Npc]` (line 213-214). `WRITE-I-MEM` already writes to `IMem[]`
  only (line 558), never `Prom[]`. This is the real hardware's PROM/RAM
  split, already faithfully ported — nothing to build here.
- `UCode.LoadPromFromFile(filename)` already parses the real `.mcr`
  binary format correctly (section header + 64-bit words from four
  16-bit halves) — confirmed by reading the method, not just trusting
  its doc comment.
- `UCode.Init()` sets `Npc = 0`, the real hardware reset vector —
  `promh.mcr`'s own entry point.
- `BusAdaptor`'s diagnostic-mode-register write (`uaddr 0x3EC0A`, bit 5)
  already sets `UCode.PromDisabled` — wired since Phase 5B/bus-
  interface.
- The real disk subsystem (`DiskController`/`DiskUnit`, wired through
  `BusAdaptor`) is fully faithful and, as of this session, has
  passing regression tests against the real `usim/disk.img`.
- `MicrocodeDebugger` (`--debug-microcode`) already supports step,
  breakpoints, registers, disassembly, trace, and label lookup — but
  today's `--debug-microcode` CLI case builds a bare `new UCode()` with
  nothing wired (no PROM loaded, no disk configured) — confirmed by
  reading `Program.cs`'s case block.

**What's actually missing:** a harness that wires real `MainMemory` +
`UCode` + `DiskController` together (mirroring the pattern this
session's disk tests and `MachineControl`'s own constructor already
use), loads the real `promh.mcr` into `Prom[]`, configures disk unit 0
against the real `disk.img`, and steps with a cycle budget — plus a
`--debug-microcode` path that builds the identical wiring instead of a
bare `UCode`, so a failing automated run can be reproduced and stepped
through interactively.

## Design

### The automated test

- New test file (e.g. `BootMilestoneTests.cs`), following this
  codebase's established pattern: a `static class *Tests` with
  `RunAllTests()`, wired to its own `--test-*` CLI switch, and also
  called from `RunAllTests()`/`--test-all` — the same pattern
  `DiskControllerTests`/`SymbolTableTests`/`UCodeFetchDecodeTests`
  already use.
- Skips gracefully (prints `SKIPPED`, returns `true`, not a failure) if
  either `sys/ubin/promh.mcr` or `usim/disk.img` is absent — both are
  untracked, large reference files, same convention as every other
  real-file-dependent test in this repo.
- Harness, in this exact order — **`Init()` must run before
  `LoadPromFromFile`, not after**: `new MainMemory()` → `new
  UCode(mem)` → `ucode.Init()` → `ucode.LoadPromFromFile(promPath)` →
  `new DiskController(mem, ucode)` → `ucode.BusAdaptor.
  WireDiskController(dc)` → `dc.ConfigureUnit(0, "T-300",
  diskImagePath)`. (Caught by this spec's own builder check: `Init()`
  does `Array.Clear(Prom)`/`Array.Clear(IMem)` — calling it after
  `LoadPromFromFile` would silently zero the PROM just loaded. This
  matches `MachineControl`'s own real order: `Init()` inside
  `InitializeComponents()`, `LoadPromFromFile` later inside
  `LoadSystemFiles()`.) Operates on a **copy** of `disk.img` in a temp
  file, same reason and same pattern as this session's disk tests
  (reads shouldn't be able to mutate the shared, untracked reference
  file).
- **Path handling, and why `SKIPPED` is the expected result in a fresh
  worktree, not a bug:** use the same bare relative paths every other
  optional-file test in this codebase uses (`sys/ubin/promh.mcr`,
  `sys/ubin/promh.sym`, `sys/ubin/ucadr.mcr`, `sys/ubin/ucadr.sym`,
  `usim/disk.img`) — do NOT add a configurable/absolute-path escape
  hatch; that would break the established convention and portability
  for no real gain. These files are untracked, so a fresh git worktree
  (including the one this spec itself was written in) genuinely has
  none of them — a test run there will always hit the `SKIPPED`
  branch, which is correct, not a failure to debug. **Real validation
  of this milestone requires temporarily copying the reference files
  into whatever worktree/checkout you're running from first** (`sys/
  ubin/*` from the main checkout, `usim/disk.img` likewise), run the
  test, then delete the copies — the exact same throwaway-copy
  workflow this session already used to validate the real-`disk.img`
  DiskController tests.
- Step loop: call `ucode.Step()` repeatedly, checking after each step
  whether `ucode.Npc == 6 && ucode.PromDisabled`. Stop and fail if
  `ucode.Halted` becomes true first, or if an exception is thrown, or
  after a cycle budget is exceeded (my call: a few million steps —
  generous enough to cover a full disk-label-parse plus MCR-section
  load, bounded enough to fail fast rather than hang a CI run forever;
  the exact number is the implementer's call, tune after a first real
  run shows how many cycles it actually takes).
- On success: additionally assert `ucode.IMem[]`'s first N words (N =
  `ucadr.mcr`'s own section size, read from its header) match the real
  `ucadr.mcr` file word-for-word — the bonus verification described
  above. `LoadPromFromFile` only ever writes into `Prom[]` and would
  corrupt the already-loaded PROM if reused on `ucadr.mcr`, and there's
  no existing non-mutating `.mcr` reader — resolve this by duplicating
  the small header-plus-words parsing logic as a private, test-local
  helper (returning a plain `ulong[]`) rather than adding a new public
  method to `UCode`. Matches this codebase's existing convention of
  test files duplicating small setup logic instead of growing
  production surface area for test-only needs (see
  `DiskControllerTests.cs`'s own `MakeSparseT80Image`, explicitly
  "Duplicated from DiskUnitTests.cs deliberately -- no cross-file test
  dependency").
- On failure (budget exceeded, `Halted`, or exception): print
  diagnostics before failing the test — `Npc` (and its symbol-resolved
  name via `SymbolTable`, loaded from the real `promh.sym`/`ucadr.sym`
  if present, falling back to a bare hex address if not), 
  `PromDisabled`, `Halted`, and the last few instructions disassembled
  via the existing `Disassembler` — breadcrumbs for the next debugging
  session, not just "FAILED."

### Interactive diagnosis path

- Add a new CLI switch (my call on the name: `--debug-boot`) that builds
  the exact same wiring as the automated test's harness (real PROM
  loaded, real disk configured) and then calls `new
  MicrocodeDebugger(ucode)` instead of free-running — so a failing
  automated run can be reproduced and stepped through with the full
  existing debugger command set (step/breakpoint/registers/disasm/
  trace/label) rather than needing a separate ad-hoc tool.
  `MicrocodeDebugger`'s constructor only needs the `UCode` instance
  (confirmed by reading it), so this is a small, self-contained addition
  to `Program.cs` — no `MicrocodeDebugger.cs` changes needed.

## Constraints

- No new NuGet dependencies.
- Real-data verification is mandatory for this spec's final review, not
  optional — same standing practice as every prior phase: actually run
  the harness against the real files, don't just read the diff.
- Every octal/hex literal referenced from `promh.text` in this spec
  (I-MEM address 6, diagnostic register `uaddr 0x3EC0A` bit 5, the
  `44`/`4` octal write values) was read directly from the source during
  this spec's own research, not transcribed from an earlier phase's
  memory — consistent with this project's standing rule to re-verify
  specs against real C/microcode source, not trust prior summaries.

## What's Left to the Builder

- The exact cycle budget constant (stated as "a few million" above —
  tune based on the first real run).
- Whether `BootMilestoneTests.cs` is its own file or folded into an
  existing one (e.g. `MachineControlTests.cs`) — either is fine, pick
  whichever reads better once written.
- The exact diagnostic dump format on failure (what's listed above is
  the required content, not a required format).
- Whether the stretch goal (continuing past address 6 into UCADR's own
  `COLD-BOOT`) is attempted in the same PR/session or deferred — not
  required either way for this spec to be done.
