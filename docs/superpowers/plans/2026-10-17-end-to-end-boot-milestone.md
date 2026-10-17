# End-to-End Boot Milestone Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Boot the real `promh.mcr` PROM against the real `disk.img` T-300 pack and detect the real, hardware-accurate PROM→RAM handoff point (`Npc == 6`, `PromDisabled == true`) — the first end-to-end exercise of the microcode engine, bus-interface/diagnostic register, and disk subsystem together, headless, with diagnostics on failure and an interactive debugger path for when it doesn't reach that point.

**Architecture:** A new `BootMilestoneTests.cs` builds a real `MainMemory`+`UCode`+`DiskController` triple (same wiring `MachineControl`'s own constructor uses), loads the real PROM, configures disk unit 0 against a throwaway copy of the real pack, and steps with a cycle budget, asserting the handoff and — as a bonus — that the microcode loaded off disk into `IMem[]` matches the real `ucadr.mcr` byte-for-byte. A new `--debug-boot` CLI switch in `Program.cs` builds the identical wiring and hands it to the existing `MicrocodeDebugger` instead of free-running, for interactive diagnosis.

**Tech Stack:** C#, .NET 8.0, no new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-17-end-to-end-boot-milestone-design.md`

## Global Constraints

- No new NuGet dependencies.
- Harness construction order is fixed: `ucode.Init()` **before** `ucode.LoadPromFromFile(...)` — `Init()` zeroes `Prom[]`/`IMem[]`/`Npc`/`PromDisabled`/`Halted`; calling it after loading the PROM would silently wipe it.
- Use bare relative paths only — `sys/ubin/promh.mcr`, `sys/ubin/promh.sym`, `sys/ubin/ucadr.mcr`, `sys/ubin/ucadr.sym`, `usim/disk.img` — no configurable/absolute-path override. These are untracked reference files; a fresh worktree genuinely lacking them and hitting the `SKIPPED` branch is correct, not a bug. Real validation requires temporarily copying them in from the main checkout first, same throwaway-copy workflow already used for the real-`disk.img` `DiskControllerTests.cs` tests this session.
- Every real-image test operates on a **copy** of `disk.img` in a temp file (`Path.GetTempFileName()` + `File.Copy(overwrite: true)`), never the original — same pattern as `DiskControllerTests.cs`'s `CopyRealDiskImage`.
- Success condition: `ucode.Npc == 6 && ucode.PromDisabled == true`, reached via a `ucode.Step()` loop bounded by a cycle budget (`MaxBootCycles = 5_000_000`, this plan's own starting value — tune after the first real run shows actual cycle counts; not a spec-mandated exact value).
- Tests that depend on an absent optional file print `SKIPPED: <path> not present` and return `true` (not a failure) — matches `SymbolTableTests.cs`/`UCodeFetchDecodeTests.cs`/`DiskControllerTests.cs`'s existing convention. The three optional files (`promh.mcr`, `ucadr.mcr`, `disk.img`) gate independently — missing one must not prevent a test that doesn't need it from running, and must not throw.
- New test file is wired into its own `--test-boot-milestone` CLI switch (`Program.cs`) and into `RunAllTests()`/`--test-all`, matching every existing `*Tests.cs` file's wiring.
- `UCode.LoadPromFromFile` only ever writes into `Prom[]`; reusing it to read `ucadr.mcr` would corrupt the already-loaded PROM. The bonus `IMem[]`-vs-`ucadr.mcr` check needs a separate, non-mutating, test-local raw-word reader — do not add a new public method to `UCode` for this.

## Review Focus

- **Cycle budget exceeded without reaching the handoff** — a reasonable person expects a clear "didn't boot, here's where it got stuck" report, not a silent `false`/assertion failure with no context. Covered by Task 1's diagnostics-on-failure requirement and its own test.
- **`ucode.Halted` becomes true before reaching `Npc == 6`** (a real PROM fatal-error path, e.g. `ERROR-DISK-ERROR`/`ERROR-BAD-LABEL`/`ERROR-PAGE-FAULT`) — must be reported with the same diagnostics as a budget timeout, not misreported as a hang. Covered by Task 1.
- **An exception thrown mid-`Step()`** (e.g. a disk seek/geometry error surfacing as a thrown `InvalidOperationException`) — must be caught and reported with diagnostics, not allowed to propagate and abort the whole `--test-all` run, which would silently skip every test queued after it. The catch-and-report mechanism is built in Task 1's harness helper; a dedicated deterministic test for this specific path is an accepted, documented gap (see Task 1's note) rather than fully pinned, since synthesizing a reliable trigger without real microcode is disproportionate effort for this milestone.
- **One of the three optional files present without the others** (e.g. `disk.img` and `promh.mcr` present but `ucadr.mcr` absent) — the bonus check must skip gracefully on its own missing file without being affected by, or affecting, the other tests' skip state. Covered by Task 2's independent `File.Exists` gate.
- **`--debug-boot` invoked with a required file missing** — since this is an interactively-invoked CLI path (not a skip-gracefully test), it must print a clear "file not found" message and exit cleanly, not throw a raw unhandled exception to the console. Covered by Task 3.

---

### Task 1: Boot harness + core handoff test

**Files:**
- Create: `usim-cs/BootMilestoneTests.cs`
- Modify: `usim-cs/Program.cs` (add `--test-boot-milestone` CLI case, `PrintUsage` line, and a call from `RunAllTests()`)

**Interfaces:**
- Consumes: `MainMemory()`, `UCode(MainMemory)`, `UCode.Init()`, `UCode.LoadPromFromFile(string)`, `DiskController(MainMemory, UCode)`, `UCode.BusAdaptor.WireDiskController(DiskController)`, `DiskController.ConfigureUnit(uint, string, string)`, `UCode.Step()`, `UCode.Npc` (`uint`), `UCode.PromDisabled` (`bool`), `UCode.Halted` (`bool`), `SymbolTable.LoadFromFile(string)`, `SymbolTable.FindByTypeValue(SymbolType, uint)`, `Disassembler.Symbols` (settable `SymbolTable?`), `Disassembler.DisassemblePC2(uint, bool)`.
- Produces (for Task 2): `private static bool TryRunUntilHandoff(UCode ucode, int maxCycles, out string diagnostics)` — runs `ucode.Step()` in a loop up to `maxCycles` times, returns `true` the moment `ucode.Npc == 6 && ucode.PromDisabled` is observed, `false` (with `diagnostics` populated) if `ucode.Halted` becomes true first, an exception is thrown, or the budget is exhausted; `diagnostics` is `""` on success. Also produces `private static string CopyRealDiskImage()` (returns a temp-file path holding a copy of `usim/disk.img`) and the three path constants (`RealPromPath`, `RealDiskImagePath`, plus `RealPromSymPath`/`RealUcadrSymPath` for optional symbol loading).

- [ ] **Step 1: Write the failing test**

In a new `usim-cs/BootMilestoneTests.cs`:

```csharp
// BootMilestoneTests.cs - End-to-end: boot the real promh.mcr PROM against
// the real disk.img T-300 pack and detect the real PROM->RAM handoff
// (Npc==6, PromDisabled) -- see docs/superpowers/specs/2026-10-17-end-to-end-boot-milestone-design.md.

using System;
using System.IO;

namespace Usim;

public static class BootMilestoneTests
{
    private const string RealPromPath = "sys/ubin/promh.mcr";
    private const string RealPromSymPath = "sys/ubin/promh.sym";
    private const string RealUcadrSymPath = "sys/ubin/ucadr.sym";
    private const string RealDiskImagePath = "usim/disk.img";
    private const int MaxBootCycles = 5_000_000;

    public static void RunAllTests()
    {
        Console.WriteLine("=== Boot Milestone Test Suite ===\n");
        int passed = 0, failed = 0;

        if (TestPromBootReachesRamHandoff()) passed++; else failed++;
        if (TestDiagnosticsOnCycleBudgetExceeded()) passed++; else failed++;
        if (TestDiagnosticsOnHalted()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestPromBootReachesRamHandoff()
    {
        Console.WriteLine("Test: booting the real promh.mcr against the real disk.img reaches the real PROM->RAM handoff (Npc==6, PromDisabled) (skipped if promh.mcr or disk.img is absent)");
        if (!File.Exists(RealPromPath) || !File.Exists(RealDiskImagePath))
        {
            Console.WriteLine($"  SKIPPED: {RealPromPath} and/or {RealDiskImagePath} not present\n");
            return true;
        }

        string diskPath = CopyRealDiskImage();
        try
        {
            var mem = new MainMemory();
            var ucode = new UCode(mem);
            ucode.Init();
            ucode.LoadPromFromFile(RealPromPath);
            var dc = new DiskController(mem, ucode);
            ucode.BusAdaptor.WireDiskController(dc);
            dc.ConfigureUnit(0, "T-300", diskPath);

            MaybeLoadSymbols();

            bool reached = TryRunUntilHandoff(ucode, MaxBootCycles, out string diagnostics);
            Assert(reached, $"PROM boot reached the real PROM->RAM handoff (Npc==6, PromDisabled) within {MaxBootCycles} cycles.\n{diagnostics}");

            Console.WriteLine("  Real-boot PROM->RAM handoff test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Real-boot PROM->RAM handoff test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(diskPath);
        }
    }

    // These two tests need no optional real files -- they deterministically
    // pin TryRunUntilHandoff's two non-success diagnostic paths so this
    // logic is actually exercised by --test-all even in an environment with
    // none of promh.mcr/ucadr.mcr/disk.img present (otherwise it would never
    // run at all in a typical dev/CI checkout).
    private static bool TestDiagnosticsOnCycleBudgetExceeded()
    {
        Console.WriteLine("Test: TryRunUntilHandoff reports a clear cycle-budget-exceeded diagnostic when the handoff is never reached (no real files needed)");
        try
        {
            var ucode = new UCode(new MainMemory());
            ucode.Init(); // Prom[]/IMem[] stay all-zero -- never produces Npc==6 && PromDisabled within a tiny budget.

            bool reached = TryRunUntilHandoff(ucode, maxCycles: 10, out string diagnostics);
            Assert(!reached, "handoff is not reached against an all-zero Prom/IMem within 10 cycles");
            Assert(diagnostics.Contains("Cycle budget"), $"diagnostics names the cycle-budget reason, got: {diagnostics}");
            Assert(diagnostics.Contains("Final Npc="), $"diagnostics includes the final Npc, got: {diagnostics}");
            Assert(diagnostics.Contains("Last instructions executed"), $"diagnostics includes recent disassembly, got: {diagnostics}");

            Console.WriteLine("  Cycle-budget-exceeded diagnostics test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Cycle-budget-exceeded diagnostics test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDiagnosticsOnHalted()
    {
        Console.WriteLine("Test: TryRunUntilHandoff reports a clear Halted diagnostic when UCode.Halted becomes true before the handoff (no real files needed)");
        try
        {
            var ucode = new UCode(new MainMemory());
            ucode.Init();
            ucode.Halted = true; // simulate a real fatal PROM error path without needing to craft real microcode for it.

            bool reached = TryRunUntilHandoff(ucode, maxCycles: 1000, out string diagnostics);
            Assert(!reached, "handoff is not reported reached when Halted is already true");
            Assert(diagnostics.Contains("Halted"), $"diagnostics names the Halted reason, got: {diagnostics}");

            Console.WriteLine("  Halted diagnostics test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Halted diagnostics test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
```

Note (accepted, documented gap — not fixed in this plan): the third `TryRunUntilHandoff` diagnostic path, an exception thrown mid-`Step()`, has no dedicated deterministic test here. Crafting a synthetic microinstruction encoding that reliably throws (rather than hitting a real error path only during an actual boot attempt) is disproportionate effort for this milestone; the try/catch is still exercised naturally whenever a real run hits a genuine error path (e.g. `ERROR-DISK-ERROR`), and the catch block's behavior (report, don't propagate) is straightforward enough that the missing dedicated test is a documented, acceptable gap, not a silently-skipped one.

- [ ] **Step 2: Run test to verify it fails to compile/run (methods don't exist yet)**

Run: `dotnet build usim-cs`
Expected: FAIL — `TryRunUntilHandoff`, `CopyRealDiskImage`, and `MaybeLoadSymbols` are undefined.

- [ ] **Step 3: Implement the harness helpers in `BootMilestoneTests.cs`**

```csharp
private static string CopyRealDiskImage()
{
    string path = Path.GetTempFileName();
    File.Copy(RealDiskImagePath, path, overwrite: true);
    return path;
}

private static void MaybeLoadSymbols()
{
    var symbols = new SymbolTable();
    bool any = false;
    if (File.Exists(RealPromSymPath)) { symbols.LoadFromFile(RealPromSymPath); any = true; }
    if (File.Exists(RealUcadrSymPath)) { symbols.LoadFromFile(RealUcadrSymPath); any = true; }
    Disassembler.Symbols = any ? symbols : null;
}

private static bool TryRunUntilHandoff(UCode ucode, int maxCycles, out string diagnostics)
{
    var recentPcs = new uint[5];
    var recentPromDisabled = new bool[5];
    int ring = 0;

    for (int i = 0; i < maxCycles; i++)
    {
        recentPcs[ring % 5] = ucode.Npc;
        recentPromDisabled[ring % 5] = ucode.PromDisabled;
        ring++;

        try
        {
            ucode.Step();
        }
        catch (Exception ex)
        {
            diagnostics = BuildDiagnostics(ucode, recentPcs, recentPromDisabled, ring, $"Exception during Step(): {ex.Message}");
            return false;
        }

        if (ucode.Npc == 6 && ucode.PromDisabled)
        {
            diagnostics = "";
            return true;
        }

        if (ucode.Halted)
        {
            diagnostics = BuildDiagnostics(ucode, recentPcs, recentPromDisabled, ring, "UCode.Halted became true");
            return false;
        }
    }

    diagnostics = BuildDiagnostics(ucode, recentPcs, recentPromDisabled, ring, $"Cycle budget ({maxCycles}) exhausted");
    return false;
}

private static string BuildDiagnostics(UCode ucode, uint[] recentPcs, bool[] recentPromDisabled, int ring, string reason)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"Reason: {reason}");
    sb.AppendLine($"Final Npc=0x{ucode.Npc:X4} PromDisabled={ucode.PromDisabled} Halted={ucode.Halted}");

    string? label = ucode.PromDisabled
        ? Disassembler.Symbols?.FindByTypeValue(SymbolType.IMem, ucode.Npc)
        : null; // Prom-side addresses aren't resolved -- promh.sym's own entries are also typed I-MEM, indistinguishable from IMem[]'s, so only resolve when PromDisabled (i.e. genuinely executing from IMem).
    if (label != null) sb.AppendLine($"  (resolved label: {label})");

    sb.AppendLine("Last instructions executed (oldest first):");
    int count = Math.Min(ring, 5);
    for (int k = count - 1; k >= 0; k--)
    {
        int idx = (ring - 1 - k) % 5;
        sb.AppendLine($"  {Disassembler.DisassemblePC2(recentPcs[idx], recentPromDisabled[idx])}");
    }
    return sb.ToString();
}
```

One line on the approach: the ring buffer stores the PC/`PromDisabled` pair *before* each `Step()` call (the instruction about to execute), so on failure it reflects the last few instructions actually run, oldest first, each disassembled with the correct PROM/RAM flag it executed under.

- [ ] **Step 4: Run test to verify it passes or fails meaningfully**

Run: `dotnet run --project usim-cs -- --test-boot-milestone`
Expected (no real files in this worktree): `TestDiagnosticsOnCycleBudgetExceeded` and `TestDiagnosticsOnHalted` both pass (they need no real files); `TestPromBootReachesRamHandoff` prints `SKIPPED: sys/ubin/promh.mcr and/or usim/disk.img not present`. 3/3 passed overall.
Then, after temporarily copying `sys/ubin/promh.mcr`, `sys/ubin/promh.sym`, `sys/ubin/ucadr.sym`, and `usim/disk.img` in from the main checkout: re-run the same command. Expected: `TestPromBootReachesRamHandoff` either passes (prints "Real-boot PROM->RAM handoff test passed"), or fails with a printed diagnostics block showing `Final Npc=...`, `PromDisabled=...`, `Halted=...`, and the last 5 disassembled instructions — in either case, inspect the real output before moving on; a failure here is itself useful information for the stretch goal, not a blocker to fix before committing (see spec's Stretch Goal section — reaching the handoff is the committed bar, not guaranteed success on the first real run). Delete the temporarily-copied files afterward.

- [ ] **Step 5: Wire `--test-boot-milestone` and `--test-all` in `Program.cs`**

Add immediately after the existing `case "--test-disk-controller":` block (around line 238):

```csharp
                case "--test-boot-milestone":
                    BootMilestoneTests.RunAllTests();
                    Environment.Exit(0);
                    break;
```

Add a `PrintUsage` line next to the other `--test-*` lines:

```csharp
        Console.WriteLine("  --test-boot-milestone   Run end-to-end boot milestone tests only");
```

Add a call inside `RunAllTests()`, after the existing `DiskControllerTests.RunAllTests();` call:

```csharp
        Console.WriteLine("Running Boot Milestone Tests...\n");
        BootMilestoneTests.RunAllTests();
        Console.WriteLine();
```

- [ ] **Step 6: Run the full suite**

Run: `dotnet build usim-cs && dotnet run --project usim-cs -- --test-all`
Expected: builds clean; every suite passes (the boot-milestone test prints `SKIPPED` in a worktree without the real files, which counts as passed).

- [ ] **Step 7: Commit**

```bash
git add usim-cs/BootMilestoneTests.cs usim-cs/Program.cs
git commit -m "Add end-to-end boot milestone test: PROM->RAM handoff via real promh.mcr+disk.img"
```

---

### Task 2: Bonus verification — loaded `IMem[]` matches real `ucadr.mcr`

**Files:**
- Modify: `usim-cs/BootMilestoneTests.cs`

**Interfaces:**
- Consumes: Task 1's `TryRunUntilHandoff`, `CopyRealDiskImage`, `MaybeLoadSymbols`, path constants; `UCode.IMem` (`ulong[]`, `IMEM_SIZE = 16*1024`); raw `.mcr` file format (section header: 3×`uint32` PDP-endian `code`/`start`/`size`, then `size` 64-bit words each from four little-endian `uint16` halves assembled `(w1<<48)|(w2<<32)|(w3<<16)|w4` — same format `UCode.LoadPromFromFile` already parses, documented in its own doc comment).
- Produces: nothing consumed by a later task.

- [ ] **Step 1: Write the failing test**

Add to `BootMilestoneTests.cs`:

```csharp
    private const string RealUcadrPath = "sys/ubin/ucadr.mcr";

    private static bool TestLoadedUCadrMatchesRealFile()
    {
        Console.WriteLine("Test: the microcode loaded off the real disk.img into IMem[] during boot matches the real ucadr.mcr file word-for-word (skipped if promh.mcr, ucadr.mcr, or disk.img is absent)");
        if (!File.Exists(RealPromPath) || !File.Exists(RealUcadrPath) || !File.Exists(RealDiskImagePath))
        {
            Console.WriteLine($"  SKIPPED: {RealPromPath}, {RealUcadrPath}, and/or {RealDiskImagePath} not present\n");
            return true;
        }

        string diskPath = CopyRealDiskImage();
        try
        {
            var mem = new MainMemory();
            var ucode = new UCode(mem);
            ucode.Init();
            ucode.LoadPromFromFile(RealPromPath);
            var dc = new DiskController(mem, ucode);
            ucode.BusAdaptor.WireDiskController(dc);
            dc.ConfigureUnit(0, "T-300", diskPath);
            MaybeLoadSymbols();

            bool reached = TryRunUntilHandoff(ucode, MaxBootCycles, out string diagnostics);
            Assert(reached, $"PROM boot reached the handoff before comparing IMem[] (prerequisite for this test).\n{diagnostics}");

            ulong[] realUcadr = ReadRawMcrWords(RealUcadrPath);
            for (int i = 0; i < realUcadr.Length; i++)
                Assert(ucode.IMem[i] == realUcadr[i], $"IMem[{i}] matches real ucadr.mcr, got 0x{ucode.IMem[i]:X16}, expected 0x{realUcadr[i]:X16}");

            Console.WriteLine($"  Loaded-UCADR-matches-real-file test passed ({realUcadr.Length} words verified)\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Loaded-UCADR-matches-real-file test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(diskPath);
        }
    }
```

- [ ] **Step 2: Run test to verify it fails to compile (`ReadRawMcrWords` undefined, not yet wired into `RunAllTests()`)**

Run: `dotnet build usim-cs`
Expected: FAIL — `ReadRawMcrWords` is undefined.

- [ ] **Step 3: Implement `private static ulong[] ReadRawMcrWords(string path)` in `BootMilestoneTests.cs`**

A test-local, non-mutating reader duplicating the same section-header-plus-words parsing `UCode.LoadPromFromFile` uses (3×PDP-endian `uint32` header, then `size` words from four little-endian `uint16` halves), returning a plain `ulong[]` of length `size` instead of writing into any `UCode` array — deliberately duplicated here rather than added as a new `UCode` public method (see this plan's Global Constraints).

- [ ] **Step 4: Wire the new test into `RunAllTests()`**

```csharp
        if (TestPromBootReachesRamHandoff()) passed++; else failed++;
        if (TestLoadedUCadrMatchesRealFile()) passed++; else failed++;
```

- [ ] **Step 5: Run test to verify it passes (with real files temporarily copied in) or skips (without them)**

Run: `dotnet run --project usim-cs -- --test-boot-milestone`
Expected: 4/4 passed — the two always-run diagnostic tests from Task 1, plus `TestPromBootReachesRamHandoff` and this new test, the latter two either both via `SKIPPED` (no real files) or both exercising real data (if the handoff was reached). If the real handoff wasn't reached, this test's own `Assert(reached, ...)` will fail with the same diagnostics block `TestPromBootReachesRamHandoff` printed, which is expected and informative, not a bug in this task.

- [ ] **Step 6: Run the full suite**

Run: `dotnet build usim-cs && dotnet run --project usim-cs -- --test-all`
Expected: builds clean, every suite passes.

- [ ] **Step 7: Commit**

```bash
git add usim-cs/BootMilestoneTests.cs
git commit -m "Add bonus check: real disk.img-loaded IMem matches real ucadr.mcr"
```

---

### Task 3: `--debug-boot` interactive diagnosis path

**Files:**
- Modify: `usim-cs/Program.cs`

**Interfaces:**
- Consumes: the same construction sequence as Task 1 (`MainMemory`, `UCode`, `DiskController`, `BusAdaptor.WireDiskController`, `ConfigureUnit`), `MicrocodeDebugger(UCode)`, `MicrocodeDebugger.StartDebugSession()`.
- Produces: nothing consumed by a later task.

- [ ] **Step 1: Add the `--debug-boot` case**

Add immediately after the existing `case "--debug-microcode": case "--debug-ucode":` block (around line 245) in `Program.cs`:

```csharp
                case "--debug-boot":
                {
                    const string promPath = "sys/ubin/promh.mcr";
                    const string diskImagePath = "usim/disk.img";
                    if (!File.Exists(promPath) || !File.Exists(diskImagePath))
                    {
                        Console.Error.WriteLine($"Error: --debug-boot requires {promPath} and {diskImagePath}");
                        Environment.Exit(1);
                    }

                    var bootMem = new MainMemory();
                    var bootUcode = new UCode(bootMem);
                    bootUcode.Init();
                    bootUcode.LoadPromFromFile(promPath);
                    var bootDc = new DiskController(bootMem, bootUcode);
                    bootUcode.BusAdaptor.WireDiskController(bootDc);
                    bootDc.ConfigureUnit(0, "T-300", diskImagePath);

                    var bootDebugger = new MicrocodeDebugger(bootUcode);
                    bootDebugger.StartDebugSession();
                    Environment.Exit(0);
                    break;
                }
```

Note: `--debug-boot` runs directly against the real `usim/disk.img` (not a copy) since it's an interactive, read/write diagnostic session the user drives deliberately — unlike the automated tests, which must never risk mutating the checked-in reference file unattended.

Add a `PrintUsage` line next to `--debug-microcode`:

```csharp
        Console.WriteLine("  --debug-boot            Interactive debugger pre-wired with real promh.mcr+disk.img");
```

- [ ] **Step 2: Manual verification (no automated test — this is an interactive CLI path)**

Run: `dotnet run --project usim-cs -- --debug-boot` with no `sys/ubin/promh.mcr`/`usim/disk.img` present.
Expected: prints `Error: --debug-boot requires sys/ubin/promh.mcr and usim/disk.img` and exits with code 1, no unhandled exception.

Then, with the real files temporarily copied in: re-run the same command.
Expected: drops into the existing `MicrocodeDebugger` prompt with the real PROM loaded and disk configured — confirm with the debugger's own `pc`/`regs` commands that `Npc` starts at 0 and `step`/`continue` advance it.

- [ ] **Step 3: Run the full suite to confirm no regression**

Run: `dotnet build usim-cs && dotnet run --project usim-cs -- --test-all`
Expected: builds clean, every suite still passes.

- [ ] **Step 4: Commit**

```bash
git add usim-cs/Program.cs
git commit -m "Add --debug-boot: interactive debugger pre-wired with real promh.mcr+disk.img"
```
