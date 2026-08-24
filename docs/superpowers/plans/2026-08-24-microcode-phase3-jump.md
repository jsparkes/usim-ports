# Microcode Engine Phase 3 — Jump Instructions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `UCode.Jmp()` and its `CheckJumpCondition()` helper as a faithful 1:1 port of the real C reference emulator's `jmp()`/`check_jcond()` (`usim/uexec.c:858-953`), replacing the `NotImplementedException` stub Phase 1 left in place.

**Architecture:** `Jmp()` decodes the jump-class instruction's fields (target, R/P/N flags, invert-sense), handles the two special cases (P&R micro-code-write, ILLOP halt), evaluates the jump condition via `CheckJumpCondition()`, and updates `Npc`/`Inhibit`/`Popj`/the SPC stack accordingly. `CheckJumpCondition()` is either a bit-test-and-rotate against `MData`, or one of 7 fixed internal conditions (comparisons, page-fault/interrupt checks, or unconditional true).

**Tech Stack:** C#, .NET 8.0. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-08-21-microcode-engine-design.md` (§ "Phase 3 — Jump Instructions")

## Global Constraints

- Faithful 1:1 port of `usim/uexec.c`'s `jmp()`/`check_jcond()` runtime behavior — not a reinterpretation of what the hardware "should" do. Where the C's behavior is genuinely surprising (e.g. ILLOP setting `Halted` but NOT returning early — execution continues to evaluate the jump condition and can still branch), port it exactly and document why in a comment, rather than "fixing" it.
- `CheckJumpCondition()` is `internal` (not `private`) so `UCodeJumpTests.cs` (same assembly, no `InternalsVisibleTo` needed) can exercise it directly in isolation — matching the pattern already established for Phase 2's ALU op tables (`LogiOps`/`ArithOps`/`DivOps`/etc.). `Jmp()` itself stays `private`, matching `Alu()`'s existing visibility (the per-instruction-class dispatcher is only ever exercised through `Step()`).
- The spec's `CheckJumpCondition()` snippet references a field named `InterruptPending`, but no such field exists on `UCode` — the actual, already-existing (Phase 1) field for the real C's `interrupt_pending_flag` is `InterruptPendingFlag`. Use `InterruptPendingFlag` throughout; this is a spec-text correction, not a new field to add.
- `Halted`: the real C's `machine_state.halted` is a simple global boolean the main run loop checks (`while (!machine_state.halted)`). This codebase's `MachineControl` class already has richer power-state machinery (`PowerState.Halted`, a `Halt()` method, an event) but `UCode` currently has zero reference to `MachineControl` (confirmed: they're instantiated independently in `Program.cs`), and wiring that coupling is an integration decision outside this phase's scope. Add a plain `public bool Halted { get; set; }` field directly on `UCode`, mirroring `machine_state.halted` exactly and matching the style of UCode's other simple public state flags (`InterruptPendingFlag`, `VmaOk`). Do not reach into `MachineControl` from `UCode`.
- `TraceLog` has no `Notice` severity — only `Error`/`Warning`/`Info`/`Verbose`. Map the C's `NOTICE(TRACE_USIM, ...)` (ILLOP) to `TraceLog.Instance.Info(TraceCategory.MicroCode, ...)` and `WARNING(TRACE_MICROCODE, ...)` (MISC-3) to `TraceLog.Instance.Warning(TraceCategory.MicroCode, ...)` — there is no `TraceCategory.Usim`, so `MicroCode` is the correct category for both (matching the category Phase 2's `OutControl` already uses for its own warning).
- `Init()` must reset `Halted = false`, matching every other simple state flag UCode's `Init()` already resets.

---

### Task 1: `Halted` field, `CheckJumpCondition()`, and `Jmp()`

**Files:**
- Modify: `usim-cs/UCode.cs`
- Create: `usim-cs/UCodeJumpTests.cs`
- Modify: `usim-cs/Program.cs` (register the new test suite)

**Interfaces:**
- Consumes: `Ir()`, `MData`/`AData` (Phase 1), `Rol32` (Phase 2's `Add32`/`Sub32`/`Abs32`/`Rol32` group), `PushSpc`/`PopSpc`/`AdvanceLc`/`Npc`/`Inhibit`/`Popj`/`IMem`/`Iwr` (Phase 1), `VmaOk` (Phase 1, defaults `true` until Phase 5), `InterruptControl`/`InterruptPendingFlag` (Phase 1).
- Produces: `internal bool CheckJumpCondition()`, `public bool Halted { get; set; }`. `Jmp()` itself becomes fully implemented (no longer throws) — `Step()` (Phase 1) already calls it correctly via the `case 1: Jmp(); break;` dispatch, no changes needed there.

- [ ] **Step 1: Write the failing tests**

Create `usim-cs/UCodeJumpTests.cs`:

```csharp
// UCodeJumpTests.cs - Tests for UCode's Jump instruction class (Phase 3 of
// the microcode engine port). Covers CheckJumpCondition()'s bit-test and
// fixed-condition modes, and Jmp()'s field decode, SPC push/pop, ILLOP/
// MISC-3 handling, and the P&R micro-code-write special case.

using System;

namespace Usim;

public static class UCodeJumpTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== UCode Jump Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestCheckJumpConditionBitTest()) passed++; else failed++;
        if (TestCheckJumpConditionFixedCodes()) passed++; else failed++;
        if (TestJmpUnconditional()) passed++; else failed++;
        if (TestJmpConditionalNotTaken()) passed++; else failed++;
        if (TestJmpPushPop()) passed++; else failed++;
        if (TestJmpInvertSense()) passed++; else failed++;
        if (TestJmpMicrocodeWrite()) passed++; else failed++;
        if (TestJmpIllop()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestCheckJumpConditionBitTest()
    {
        Console.WriteLine("Test: CheckJumpCondition bit-test mode (Ir(5,1)==0)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // rot = Ir(0,5) = 1 (bits 0-4), Ir(5,1) = 0 (bit 5 clear -> bit-test mode).
            ucode.P0 = 1; // rot=1, bit5=0
            ucode.MData = 0x00000001; // rotate left by 1 -> 0x00000002, bit0 clear
            bool cond = ucode.CheckJumpCondition();
            Assert(ucode.MData == 0x00000002, $"MData mutated by Rol32 (side effect), got 0x{ucode.MData:X}");
            Assert(cond == false, $"bit0 of rotated MData is 0 -> false, got {cond}");

            // rot = 0 -> no rotation; bit0 already set -> true.
            ucode.P0 = 0; // rot=0, bit5=0
            ucode.MData = 0x00000001;
            cond = ucode.CheckJumpCondition();
            Assert(ucode.MData == 0x00000001, "rot=0 is a no-op rotation");
            Assert(cond == true, $"bit0 of MData(rot=0) is 1 -> true, got {cond}");

            Console.WriteLine("  CheckJumpCondition bit-test tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  CheckJumpCondition bit-test tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestCheckJumpConditionFixedCodes()
    {
        Console.WriteLine("Test: CheckJumpCondition fixed condition codes (Ir(5,1)!=0)");
        try
        {
            var ucode = new UCode();
            ucode.Init();
            ucode.P0 = 1UL << 5; // bit5 = 1 -> fixed-condition mode; Ir(0,4) set per case below

            // Code 1: MData < AData (signed).
            ucode.P0 = (1UL << 5) | 1;
            ucode.MData = -5; ucode.AData = 3;
            Assert(ucode.CheckJumpCondition() == true, "code1: -5 < 3");
            ucode.MData = 5; ucode.AData = 3;
            Assert(ucode.CheckJumpCondition() == false, "code1: 5 < 3 is false");

            // Code 2: MData <= AData.
            ucode.P0 = (1UL << 5) | 2;
            ucode.MData = 3; ucode.AData = 3;
            Assert(ucode.CheckJumpCondition() == true, "code2: 3 <= 3");

            // Code 3: MData == AData.
            ucode.P0 = (1UL << 5) | 3;
            ucode.MData = 7; ucode.AData = 7;
            Assert(ucode.CheckJumpCondition() == true, "code3: 7 == 7");
            ucode.MData = 7; ucode.AData = 8;
            Assert(ucode.CheckJumpCondition() == false, "code3: 7 == 8 is false");

            // Code 4: !VmaOk.
            ucode.P0 = (1UL << 5) | 4;
            ucode.VmaOk = true;
            Assert(ucode.CheckJumpCondition() == false, "code4: VmaOk=true -> false");
            ucode.VmaOk = false;
            Assert(ucode.CheckJumpCondition() == true, "code4: VmaOk=false -> true");
            ucode.VmaOk = true; // restore default for later tests

            // Code 5: !VmaOk || (bit27 set && InterruptPendingFlag).
            ucode.P0 = (1UL << 5) | 5;
            ucode.VmaOk = true; ucode.InterruptControl = 0; ucode.InterruptPendingFlag = true;
            Assert(ucode.CheckJumpCondition() == false, "code5: bit27 clear -> InterruptPendingFlag ignored");
            ucode.InterruptControl = 1u << 27; ucode.InterruptPendingFlag = true;
            Assert(ucode.CheckJumpCondition() == true, "code5: bit27 set & pending -> true");
            ucode.InterruptControl = 1u << 27; ucode.InterruptPendingFlag = false;
            Assert(ucode.CheckJumpCondition() == false, "code5: bit27 set & not pending -> false");
            ucode.InterruptControl = 0;

            // Code 6: !VmaOk || (bit27 set && InterruptPendingFlag) || (bit26 set).
            ucode.P0 = (1UL << 5) | 6;
            ucode.VmaOk = true; ucode.InterruptControl = 1u << 26; ucode.InterruptPendingFlag = false;
            Assert(ucode.CheckJumpCondition() == true, "code6: bit26 set alone -> true");
            ucode.InterruptControl = 0;
            Assert(ucode.CheckJumpCondition() == false, "code6: nothing set -> false");

            // Code 7: unconditional true.
            ucode.P0 = (1UL << 5) | 7;
            Assert(ucode.CheckJumpCondition() == true, "code7: always true");

            // Code 0: matches the C's fall-through-to-err() fatal path.
            ucode.P0 = (1UL << 5) | 0;
            bool threw = false;
            try { ucode.CheckJumpCondition(); }
            catch (InvalidOperationException) { threw = true; }
            Assert(threw, "code0 throws InvalidOperationException, matching C's err()");

            Console.WriteLine("  CheckJumpCondition fixed-code tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  CheckJumpCondition fixed-code tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestJmpUnconditional()
    {
        Console.WriteLine("Test: Jmp unconditional (code 7), no P/R/N");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // target = Ir(12,14) = 0x1234; r=p=n=invertSense=0; condition code 7 (always true).
            ucode.P0 = ((ulong)0x1234 << 12) | (1UL << 5) | 7;
            ucode.Npc = 0x0100;
            ucode.CallJmp();

            Assert(ucode.Npc == 0x1234, $"cond=true, no P/R -> Npc=target, got 0x{ucode.Npc:X}");
            Assert(ucode.Popj == false, "Popj forced false when cond is true");
            Assert(ucode.Inhibit == false, "n=0 -> Inhibit untouched");

            Console.WriteLine("  Jmp unconditional tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Jmp unconditional tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestJmpConditionalNotTaken()
    {
        Console.WriteLine("Test: Jmp condition false leaves Npc/Popj untouched");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // condition code 3 (MData==AData), made false; target must NOT be taken.
            ucode.P0 = ((ulong)0x2000 << 12) | (1UL << 5) | 3;
            ucode.MData = 1; ucode.AData = 2;
            ucode.Npc = 0x0055;
            ucode.Popj = true; // pre-set, must survive since cond is false
            ucode.CallJmp();

            Assert(ucode.Npc == 0x0055, $"cond=false -> Npc untouched, got 0x{ucode.Npc:X}");
            Assert(ucode.Popj == true, "cond=false -> Popj untouched");

            Console.WriteLine("  Jmp conditional-not-taken tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Jmp conditional-not-taken tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestJmpPushPop()
    {
        Console.WriteLine("Test: Jmp P (push SPC) and R (pop SPC) flags");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // P=1 (bit8), N=0 (bit7), cond true (code7) -> pushSpc(Npc).
            ucode.P0 = ((ulong)0x3000 << 12) | (1UL << 8) | (1UL << 5) | 7;
            ucode.Npc = 0x0042;
            ucode.CallJmp();
            Assert(ucode.Npc == 0x3000, "P alone still takes the jump target");

            // Now R=1 (bit9), P=0, cond true -> target = popSpc() (the 0x0042 just pushed),
            // masked with 037777 = 0x3FFF. Bit 14 of that popped value is 0, so no AdvanceLc.
            ucode = new UCode();
            ucode.Init();
            ucode.P0 = ((ulong)0x3000 << 12) | (1UL << 8) | (1UL << 5) | 7; // push 0x99 via P
            ucode.Npc = 0x0099;
            ucode.CallJmp();
            ucode.P0 = ((ulong)0x1111 << 12) | (1UL << 9) | (1UL << 5) | 7; // R=1, target ignored (popped instead)
            ucode.CallJmp();
            Assert(ucode.Npc == 0x0099, $"R=1 -> Npc = popped SPC value, got 0x{ucode.Npc:X}");

            Console.WriteLine("  Jmp push/pop tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Jmp push/pop tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestJmpInvertSense()
    {
        Console.WriteLine("Test: Jmp invertSense flips the condition");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // condition code 3 (MData==AData) is FALSE, but invertSense (bit6) flips it to true.
            // target must stay within Ir(12,14)'s 14-bit field (max 0x3FFF) or it gets masked
            // away by Ir() itself before Jmp() ever sees it.
            ucode.P0 = ((ulong)0x2400 << 12) | (1UL << 6) | (1UL << 5) | 3;
            ucode.MData = 1; ucode.AData = 2;
            ucode.Npc = 0x0000;
            ucode.CallJmp();

            Assert(ucode.Npc == 0x2400, $"invertSense flips false cond to true -> jump taken, got 0x{ucode.Npc:X}");

            Console.WriteLine("  Jmp invertSense tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Jmp invertSense tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestJmpMicrocodeWrite()
    {
        Console.WriteLine("Test: Jmp P&R micro-code-write special case (bypasses condition entirely)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // P=1 (bit8) & R=1 (bit9) -> IMem[target] = Iwr; return immediately (no condition
            // evaluated, so an otherwise-fatal code0 condition must NOT throw here).
            ucode.P0 = ((ulong)0x0777 << 12) | (1UL << 9) | (1UL << 8) | (1UL << 5) | 0;
            ucode.Iwr = 0xDEADBEEFCAFEUL;
            ucode.Npc = 0x1234; // must be untouched, since this path returns before touching Npc
            ucode.CallJmp();

            Assert(ucode.IMem[0x0777] == 0xDEADBEEFCAFEUL, $"P&R writes Iwr to IMem[target], got 0x{ucode.IMem[0x0777]:X}");
            Assert(ucode.Npc == 0x1234, "P&R path returns before touching Npc");

            Console.WriteLine("  Jmp micro-code-write tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Jmp micro-code-write tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestJmpIllop()
    {
        Console.WriteLine("Test: Jmp ILLOP sets Halted but continues executing (does not return early)");
        try
        {
            var ucode = new UCode();
            ucode.Init();
            Assert(ucode.Halted == false, "Halted starts false after Init()");

            // Ir(10,2) == 1 -> ILLOP. Also code 7 (always true) so the jump target is STILL
            // taken afterward, proving execution continued past the ILLOP check. target must
            // stay within Ir(12,14)'s 14-bit field (max 0x3FFF) or it gets masked away by
            // Ir() itself before Jmp() ever sees it.
            ucode.P0 = ((ulong)0x2500 << 12) | (1UL << 10) | (1UL << 5) | 7;
            ucode.Npc = 0x0000;
            ucode.CallJmp();

            Assert(ucode.Halted == true, "ILLOP sets Halted = true");
            Assert(ucode.Npc == 0x2500, "ILLOP does not return early -> jump condition still evaluated and taken");

            Console.WriteLine("  Jmp ILLOP tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Jmp ILLOP tests failed: {ex.Message}\n");
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

Note: the tests call `ucode.CallJmp()`, a small `internal` test-only forwarding wrapper — add it in Step 3 alongside `Jmp()` itself, since `Jmp()` stays `private` (matching `Alu()`'s existing visibility) but the tests need a way to invoke it directly without going through the full `Step()` pipeline (which would also run `IncNpc()`, OA-merge, and common-field decode using `Step()`'s own bit positions for `Op`/`AAddr`/`MAddr`/etc. — unnecessary noise for a `Jmp()`-focused test). This mirrors no existing Phase 2 pattern exactly (Phase 2's `Alu()` was only tested via full `Step()`), but Phase 2's `Alu()` test only needed one end-to-end scenario; `Jmp()` has many more branch combinations worth covering directly, so a thin forwarding wrapper is the right-sized solution — not `internal void Jmp()` itself, which would blur the established private/internal convention (dispatchers stay private; testable logic units are internal).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build LispMachine.sln`
Expected: FAIL — `UCode` has no members named `CheckJumpCondition`/`Halted`/`CallJmp` yet, and `Jmp()` still throws unconditionally.

- [ ] **Step 3: Implement `Halted`, `CheckJumpCondition()`, `Jmp()`, and the `CallJmp()` test wrapper**

In `usim-cs/UCode.cs`, add a `Halted` property near the other simple public state flags (e.g. next to `VmaOk`):

```csharp
    /// <summary>
    /// Set by Jmp()'s ILLOP handling (matches the C's machine_state.halted).
    /// This class has no reference to MachineControl's richer power-state
    /// machinery — wiring this flag to an actual run-loop stop condition is
    /// an integration concern outside this phase's scope.
    /// </summary>
    public bool Halted { get; set; }
```

In `Init()`, add `Halted = false;` alongside the other flag resets.

Replace the `Jmp()` stub:
```csharp
    private void Jmp()
    {
        throw new NotImplementedException("Jmp is implemented in Phase 3 (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md)");
    }
```
with:
```csharp
    /// <summary>
    /// Fixed jump-condition codes (Ir(0,4) when Ir(5,1) != 0), or a rotate-
    /// and-test-bit-0 mode when Ir(5,1) == 0. Faithful port of check_jcond()
    /// (usim/uexec.c:858-892) — note the bit-test mode mutates MData as a
    /// side effect (it really does rotate MData in place in the real C too).
    /// </summary>
    internal bool CheckJumpCondition()
    {
        if (Ir(5, 1) == 0)
        {
            int rot = (int)Ir(0, 5);
            MData = (int)Rol32((uint)MData, rot);
            return (MData & 1) != 0;
        }
        return Ir(0, 4) switch
        {
            1 => MData < AData,
            2 => MData <= AData,
            3 => MData == AData,
            4 => !VmaOk,
            5 => !VmaOk || (((InterruptControl & (1 << 27)) != 0) && InterruptPendingFlag),
            6 => !VmaOk || (((InterruptControl & (1 << 27)) != 0) && InterruptPendingFlag) || (InterruptControl & (1 << 26)) != 0,
            7 => true,
            _ => throw new InvalidOperationException($"unknown jump condition {Ir(0, 4)}"), // includes code 0, matching the C's fall-through-to-err()
        };
    }

    /// <summary>
    /// Faithful port of jmp() (usim/uexec.c:894-953). Note ILLOP sets
    /// Halted but does NOT return early — the real C continues on to
    /// evaluate the jump condition and can still branch afterward. Port
    /// this exactly; it is surprising but real reference behavior, not a
    /// bug to "fix".
    /// </summary>
    private void Jmp()
    {
        uint target = (uint)Ir(12, 14);
        bool r = Ir(9, 1) != 0, p = Ir(8, 1) != 0, n = Ir(7, 1) != 0;
        bool invertSense = Ir(6, 1) != 0;

        if (Ir(10, 2) == 1)
        {
            TraceLog.Instance.Info(TraceCategory.MicroCode, "usim: illop, asserting halted");
            Halted = true;
        }
        if (Ir(10, 2) == 3)
        {
            TraceLog.Instance.Warning(TraceCategory.MicroCode, "jump w/misc-3!");
        }

        if (p && r)
        {
            IMem[target] = Iwr;
            return;
        }

        bool cond = CheckJumpCondition();
        if (invertSense) cond = !cond;

        if (p && cond)
        {
            if (!n) PushSpc(Npc); else PushSpc(Npc - 1);
        }
        if (r && cond)
        {
            target = PopSpc();
            if ((target >> 14 & 1) != 0) target = AdvanceLc(target);
            target &= 0x3FFF;
        }
        if (cond)
        {
            if (n) Inhibit = true;
            Npc = target;
            Popj = false;
        }
    }

    /// <summary>
    /// Test-only forwarding wrapper: Jmp() itself stays private (matching
    /// Alu()'s existing visibility — the per-instruction-class dispatcher
    /// is only ever meant to be reached through Step()), but UCodeJumpTests
    /// needs to exercise its many branch combinations directly without
    /// going through Step()'s full fetch/decode pipeline.
    /// </summary>
    internal void CallJmp() => Jmp();
```

Add both new methods (and `CallJmp`) inside the existing `#region ALU Operations` block if convenient, or immediately after it — match whatever placement keeps `Jmp()`/`CheckJumpCondition()` next to the other per-instruction-class methods (`Alu()`, and the still-stubbed `Dsp()`/`Byt()`) rather than inside the ALU-specific region, since jump instructions are a distinct instruction class from ALU instructions.

In `usim-cs/Program.cs`, register the new suite next to the existing `--test-microcode-*` cases:
```csharp
                case "--test-microcode-jump":
                    UCodeJumpTests.RunAllTests();
                    break;
```
(placed next to the `--test-microcode-alu` case), add it to the usage help text (next to the `--test-microcode-alu` help line):
```csharp
        Console.WriteLine("  --test-microcode-jump   Run microcode jump tests only");
```
and add it to the master `RunAllTests()` method (next to the `UCodeAluTests.RunAllTests();` call):
```csharp
        UCodeJumpTests.RunAllTests();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build LispMachine.sln` (expect 0 errors), then `dotnet run --project usim-cs -- --test-microcode-jump`
Expected: `Passed: 8`, `Failed: 0`.

- [ ] **Step 5: Run the full regression suite**

Run: `dotnet run --project usim-cs -- --test-all`
Expected: all suites (`UCodeTests`, `UCodeFetchDecodeTests`, `UCodeAluTests`, `UCodeJumpTests`, `ConfigTests`, `WpfBackendTests`) report 0 failures.

- [ ] **Step 6: Commit**

```bash
git add usim-cs/UCode.cs usim-cs/UCodeJumpTests.cs usim-cs/Program.cs
git commit -m "Add faithful Jmp()/CheckJumpCondition() jump instruction class"
```

---

## Self-Review Notes

- **Spec coverage:** Both methods the spec's "Phase 3 — Jump Instructions" section calls for (`Jmp()`, `CheckJumpCondition()`) are implemented, plus the `Halted` field the spec explicitly flagged as needing a decision during Phase 3 (resolved: a plain field on `UCode`, not a `MachineControl` dependency).
- **Spec corrections applied:** the spec snippet's `InterruptPending` reference corrected to the actual existing field `InterruptPendingFlag` (verified by reading `usim-cs/UCode.cs` and cross-checking the real C's `interrupt_pending_flag`). Both corrected against a fresh, independent read of `usim/uexec.c:858-953` (`jmp()`/`check_jcond()`) — no other discrepancies found between the spec's proposed code and the real C for this phase (unlike Phase 2, which had three real bugs in its spec/brief text).
- **Placeholder scan:** no TBD/TODO markers; every test method has real, hand-derivable assertions rather than smoke-test-only checks.
- **Type consistency:** `CheckJumpCondition()` returns `bool` (matching how its call site uses it purely as a condition, even though the real C's function returns `int`); `Halted` is `bool` matching `machine_state.halted`; `Jmp()`'s `target` is `uint` matching the existing `Npc`/`PushSpc`/`PopSpc`/`AdvanceLc` signatures from Phase 1.
- **Deliberate deviation flagged:** `CallJmp()` is a new, minimal test-only wrapper — the first time this project has needed one, since `Jmp()`'s branch combinations are numerous enough to warrant direct testing (unlike `Alu()`, which Phase 2 tested adequately via one end-to-end `Step()` scenario). Does not change `Jmp()`'s own visibility or behavior.
