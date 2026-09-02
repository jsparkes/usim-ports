# Microcode Engine Phase 6 — Dispatch Instructions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `UCode.Dsp()` as a faithful 1:1 port of the real C reference emulator's `dsp()` (`usim/uexec.c:740-854`), replacing the `NotImplementedException` stub Phase 1 left in place.

**Architecture:** `Dsp()` decodes a DISPATCH-class instruction's fields, handles the DMEM-write special case, rotates `MData` and builds a dispatch-table address from the rotated bits plus (optionally) two page-map bits from `Uvmem.Vtop`, reads the resulting `DMem` word, and — using exactly the same PushSpc/PopSpc/AdvanceLc/Inhibit/Popj machinery `Jmp()` already uses — updates `Npc` accordingly.

**Tech Stack:** C#, .NET 8.0. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-08-21-microcode-engine-design.md` (§ "Phase 6 — Dispatch Instructions")

## Global Constraints

- Faithful 1:1 port of `usim/uexec.c:740-854`'s `dsp()` runtime behavior. This spec section was independently re-verified line-by-line against the real C before this plan was written and found **fully correct as written** — the first section since Phase 3 with no bugs found (unlike Phases 2, 4, 5, and 5B, which each had at least one real spec bug). No corrections needed; implement the spec's code as given.
- `Dsp()` stays `private`, matching `Alu()`/`Jmp()`'s existing visibility (only reachable through `Step()` in production). Add an `internal void CallDsp() => Dsp();` test-only forwarding wrapper, matching the `CallJmp()`/`CallVm()` precedent from Phases 3 and 5.
- `Dsp()`'s push/pop/`Inhibit`/`Popj` logic is structurally identical to `Jmp()`'s (same `PushSpc(Npc)`/`PushSpc(Npc-1)`, same `PopSpc()`-with-`AdvanceLc`-on-bit-14, same `0x3FFF` masking) — already faithfully implemented and thoroughly tested in Phase 3. This plan's own tests confirm `Dsp()` wires these up correctly, but do not re-derive `PushSpc`/`PopSpc`/`AdvanceLc`'s own internal correctness from scratch (already established).
- **Test design note, learned the hard way while drafting this plan**: `Npc = target;` at the end of `Dsp()` is unconditional except for the `if (p && r) return;` early return — meaning `if (n_plus1 && n) Npc--;`'s effect is silently overwritten by the later unconditional assignment unless the call reaches that early return first. Any test of `n_plus1`'s effect must use a `p=1, r=1` scenario (triggering the early return) to actually observe it, not a fall-through scenario.

---

### Task 1: `Dsp()`

**Files:**
- Modify: `usim-cs/UCode.cs`
- Create: `usim-cs/UCodeDispatchTests.cs`
- Modify: `usim-cs/Program.cs` (register the new test suite)

**Interfaces:**
- Consumes: `Ir()`, `MData`, `Rol32`, `LcByteMode()`, `DMem`, `Uvmem.Vtop`, `MdReg`, `DispatchConstant`, `Npc`, `Inhibit`, `Popj`, `PushSpc`/`PopSpc`, `AdvanceLc` (all Phase 1/3/5).
- Produces: `Dsp()` becomes fully implemented (no longer throws) — `Step()` (Phase 1) already calls it correctly via `case 2: Dsp(); break;`, no changes needed there. `internal void CallDsp()` test-only wrapper.

- [ ] **Step 1: Write the failing tests**

Create `usim-cs/UCodeDispatchTests.cs`:

```csharp
// UCodeDispatchTests.cs - Tests for UCode's Dispatch instruction class
// (Phase 6 of the microcode engine port). Covers Dsp()'s DMEM-write special
// case, the mask/rotate dispatch-address construction, the L2-map-bit
// tweak, byte-mode pos override, n_plus1/enable_ish/inhibit, and the
// push/pop machinery it shares with Jmp().

using System;

namespace Usim;

public static class UCodeDispatchTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== UCode Dispatch Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestDmemWritePath()) passed++; else failed++;
        if (TestBasicDispatchNoMap()) passed++; else failed++;
        if (TestMapBits()) passed++; else failed++;
        if (TestByteModePosOverride()) passed++; else failed++;
        if (TestNPlus1EnableIshInhibitEarlyReturn()) passed++; else failed++;
        if (TestPushOnly()) passed++; else failed++;
        if (TestPopOnly()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestDmemWritePath()
    {
        Console.WriteLine("Test: Dsp() Ir(10,2)==2 writes DMem[dispAddr]=AData and returns immediately");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // dispAddr = Ir(12,11) = 0x123; Ir(10,2) = 2 (bits 10-11 = 10 binary = 2).
            ucode.P0 = ((ulong)0x123 << 12) | (2UL << 10);
            ucode.AData = unchecked((int)0xCAFEBABE);
            ucode.Npc = 0x0042; // must be untouched -- this path returns before touching Npc
            ucode.CallDsp();

            Assert(ucode.DMem[0x123] == 0xCAFEBABEu, $"DMem[dispAddr] = AData, got 0x{ucode.DMem[0x123]:X}");
            Assert(ucode.Npc == 0x0042, "DMEM-write path returns before touching Npc");
            Assert(ucode.Inhibit == false, "DMEM-write path returns before touching Inhibit");

            Console.WriteLine("  Dsp DMEM-write-path tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Dsp DMEM-write-path tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBasicDispatchNoMap()
    {
        Console.WriteLine("Test: Dsp() basic dispatch, map=0, len=0 (no MData contribution), n=p=r=0 fallthrough");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // dispAddr(raw) = Ir(12,11) = 5; len = Ir(5,3) = 0 (mask=0); map = Ir(8,2) = 0;
            // Ir(10,2) = 0 (not the DMEM-write or byte-mode case); dispConst = Ir(32,10) = 0x2AA.
            ucode.P0 = ((ulong)5 << 12) | ((ulong)0x2AA << 32);
            ucode.DMem[5] = 0x1234; // n=0,p=0,r=0 (bits 14-16 clear), target=0x1234
            ucode.Npc = 0x0099; // must be overwritten by the fallthrough
            ucode.CallDsp();

            Assert(ucode.Npc == 0x1234, $"fallthrough: Npc = target, got 0x{ucode.Npc:X}");
            Assert(ucode.Popj == false, "fallthrough sets Popj = false");
            Assert(ucode.Inhibit == false, "n=0 -> Inhibit untouched");
            Assert(ucode.DispatchConstant == 0x2AA, $"DispatchConstant = Ir(32,10), got 0x{ucode.DispatchConstant:X}");

            Console.WriteLine("  Dsp basic-dispatch tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Dsp basic-dispatch tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMapBits()
    {
        Console.WriteLine("Test: Dsp() map=1/2/3 tweak dispAddr with Uvmem's L2 bit18/bit19");
        try
        {
            // map=1 selects bit18. l2MapBits has ONLY bit18 set (bit19 clear) -- if the
            // implementation read bit19 instead by mistake, dispAddr would resolve to 0
            // (DMem[0]'s default, untouched) instead of 1, giving a different Npc.
            {
                var ucode = new UCode();
                ucode.Init();
                ucode.MdReg = 0x1000;
                ucode.Uvmem.WriteMap((1u << 25) | (1u << 18), 0x1000); // L2-only write, l1Data stays 0

                ucode.P0 = (1UL << 8); // map = Ir(8,2) = 1; dispAddr(raw)=0, len=0
                ucode.DMem[1] = 0x0055; // n=p=r=0
                ucode.CallDsp();
                Assert(ucode.Npc == 0x0055, $"map=1 uses bit18 (set) -> dispAddr=1 -> DMem[1], got Npc=0x{ucode.Npc:X}");
            }

            // map=2 selects bit19. l2MapBits has ONLY bit19 set (bit18 clear) -- same
            // discrimination logic as above, mirrored.
            {
                var ucode = new UCode();
                ucode.Init();
                ucode.MdReg = 0x1000;
                ucode.Uvmem.WriteMap((1u << 25) | (1u << 19), 0x1000);

                ucode.P0 = (2UL << 8); // map = 2
                ucode.DMem[1] = 0x0066;
                ucode.CallDsp();
                Assert(ucode.Npc == 0x0066, $"map=2 uses bit19 (set) -> dispAddr=1 -> DMem[1], got Npc=0x{ucode.Npc:X}");
            }

            // map=3 ORs both bits together. Using bit19 set / bit18 CLEAR proves the OR
            // combines them (not just "happens to work when both are set").
            {
                var ucode = new UCode();
                ucode.Init();
                ucode.MdReg = 0x1000;
                ucode.Uvmem.WriteMap((1u << 25) | (1u << 19), 0x1000);

                ucode.P0 = (3UL << 8); // map = 3
                ucode.DMem[1] = 0x0077;
                ucode.CallDsp();
                Assert(ucode.Npc == 0x0077, $"map=3 ORs bit18|bit19 -> dispAddr=1 even with only bit19 set, got Npc=0x{ucode.Npc:X}");
            }

            Console.WriteLine("  Dsp map-bits tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Dsp map-bits tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestByteModePosOverride()
    {
        Console.WriteLine("Test: Dsp() Ir(10,2)==3 uses LcByteMode()'s pos, not the raw Ir(0,5) bits");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // P0 bits 0-3 = 3, bit4 = 0 (both the raw Ir(0,5) value AND LcByteMode's
            // low-nibble input); Ir(10,2) = 3 (bits 10-11 = 11 binary = 3); raw
            // dispAddr(bits 12-22) = 7; len(bits 5-7) = 0 (mask=0, so MData's rotated
            // value never affects dispAddr -- isolates the pos-selection check to MData
            // alone). InterruptControl=0 (Init() default, not byte mode) and Lc=0
            // (Init() default) -> LcByteMode()'s "not byte mode" branch: ir4=(P0>>4)&1=0,
            // lc1=(Lc>>1)&1=0, pos=(P0&0xF)|(((0^0)==0?1:0)<<4)=3|16=19. Raw Ir(0,5)
            // would have been P0&0x1F=3 -- 19 != 3, so this genuinely discriminates
            // which pos value was actually used for the rotation.
            ucode.P0 = ((ulong)7 << 12) | (3UL << 10) | 3UL;
            ucode.MData = 1;
            ucode.DMem[7] = 0x0099; // n=p=r=0
            ucode.CallDsp();

            Assert(ucode.MData == (1 << 19), $"MData rotated by LcByteMode()'s pos=19 (not raw pos=3), got 0x{ucode.MData:X}");
            Assert(ucode.Npc == 0x0099, $"dispAddr unaffected (len=0) -> DMem[7], got Npc=0x{ucode.Npc:X}");

            Console.WriteLine("  Dsp byte-mode-pos-override tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Dsp byte-mode-pos-override tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestNPlus1EnableIshInhibitEarlyReturn()
    {
        Console.WriteLine("Test: Dsp() n_plus1 (Npc--), enable_ish (AdvanceLc), Inhibit, and p&&r's early return");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // n_plus1 = Ir(25,1) = 1; enable_ish = Ir(24,1) = 1; raw dispAddr = 9;
            // len=0, map=0. DMem[9] encodes n=p=r=1 (bits 14,15,16 all set) --
            // p&&r triggers the early return, which is REQUIRED to observe Npc--'s
            // effect (see this plan's Global Constraints note: the fallthrough's
            // unconditional "Npc = target" would otherwise silently overwrite it).
            ucode.P0 = ((ulong)9 << 12) | (1UL << 25) | (1UL << 24);
            ucode.DMem[9] = (1u << 16) | (1u << 15) | (1u << 14); // r|p|n, target=0 (unused, early return)
            ucode.Npc = 5;
            ucode.Popj = true; // must survive unchanged -- the early return skips "Popj = false"
            ucode.CallDsp();

            Assert(ucode.Npc == 4, $"n_plus1 && n -> Npc--, and the value SURVIVES because p&&r returns before the fallthrough overwrite, got {ucode.Npc}");
            Assert(ucode.Inhibit == true, "n=1 -> Inhibit = true (set before the early return)");
            Assert(ucode.Lc == 2, $"enable_ish -> AdvanceLc(0) called (Lc 0->2, not-byte-mode +2 path, no NEED-FETCH), got 0x{ucode.Lc:X}");
            Assert(ucode.Popj == true, "p&&r early return skips 'Popj = false' -- Popj stays whatever it was before the call");

            Console.WriteLine("  Dsp n_plus1/enable_ish/inhibit/early-return tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Dsp n_plus1/enable_ish/inhibit/early-return tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestPushOnly()
    {
        Console.WriteLine("Test: Dsp() p=1,r=0,n=0 -> PushSpc(Npc), then falls through to Npc=target");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            ucode.P0 = (ulong)3 << 12; // raw dispAddr=3, len=0, map=0
            ucode.DMem[3] = (1u << 15) | 0x0033; // p only (bit15), target=0x0033
            ucode.Npc = 10;
            ucode.CallDsp();

            Assert(ucode.SpcPtr == 1, $"PushSpc advanced SpcPtr from 0, got {ucode.SpcPtr}");
            Assert(ucode.Spc[1] == 10, $"PushSpc(Npc) pushed the PRE-call Npc (10), got 0x{ucode.Spc[1]:X}");
            Assert(ucode.Npc == 0x0033, $"fallthrough still sets Npc=target (p alone, no r, doesn't return early), got 0x{ucode.Npc:X}");

            Console.WriteLine("  Dsp push-only tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Dsp push-only tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestPopOnly()
    {
        Console.WriteLine("Test: Dsp() r=1,p=0,n=0 -> PopSpc(), with and without the bit-14 AdvanceLc trigger");
        try
        {
            // No bit 14 in the popped value -> no AdvanceLc call, target used as-is.
            {
                var ucode = new UCode();
                ucode.Init();
                ucode.SpcPtr = 0;
                ucode.Spc[0] = 0x0044;

                ucode.P0 = (ulong)2 << 12; // raw dispAddr=2, len=0, map=0
                ucode.DMem[2] = 1u << 16; // r only (bit16); target field unused, will be overwritten by the pop
                ucode.CallDsp();

                Assert(ucode.Npc == 0x0044, $"PopSpc() without bit14 uses the popped value directly, got 0x{ucode.Npc:X}");
                Assert(ucode.SpcPtr == 0x1F, $"PopSpc decrements SpcPtr, wrapping 0->0x1F, got 0x{ucode.SpcPtr:X}");
            }

            // Bit 14 set in the popped value -> AdvanceLc(poppedTarget) is called, and its
            // return value (not the raw popped value) becomes target. Hand-derived: with
            // Lc=0/InterruptControl=0 (Init() defaults), AdvanceLc(0x4055) computes
            // oldLc=0, Lc+=2 (Lc=2, not byte mode), bit31 of Lc clear -> ppc|=2 ->
            // returns 0x4055|2=0x4057; lastByteInWord check leaves Lc at 2 (not 0x80000002).
            // target = 0x4057 & 0x3FFF = 0x0057.
            {
                var ucode = new UCode();
                ucode.Init();
                ucode.SpcPtr = 0;
                ucode.Spc[0] = 0x4055; // bit14 set

                ucode.P0 = (ulong)2 << 12;
                ucode.DMem[2] = 1u << 16; // r only
                ucode.CallDsp();

                Assert(ucode.Npc == 0x0057, $"PopSpc() with bit14 routes through AdvanceLc, got 0x{ucode.Npc:X}");
                Assert(ucode.Lc == 2, $"AdvanceLc's own Lc update (0->2), got 0x{ucode.Lc:X}");
            }

            Console.WriteLine("  Dsp pop-only tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Dsp pop-only tests failed: {ex.Message}\n");
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

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build LispMachine.sln`
Expected: FAIL — `UCode` has no members named `CallDsp` yet, and `Dsp()` still throws unconditionally.

- [ ] **Step 3: Implement `Dsp()` and the `CallDsp()` test wrapper**

In `usim-cs/UCode.cs`, replace the `Dsp()` stub:
```csharp
    private void Dsp()
    {
        throw new NotImplementedException("Dsp is implemented in Phase 6 (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md)");
    }
```
with:
```csharp
    /// <summary>
    /// Faithful port of dsp() (usim/uexec.c:740-854). Independently re-verified
    /// field-by-field against the real C during Phase 6 planning -- no bugs
    /// found (unlike Phases 2/4/5/5B, which each had at least one). Shares
    /// PushSpc/PopSpc/AdvanceLc/Inhibit/Popj machinery with Jmp() (Phase 3).
    /// </summary>
    private void Dsp()
    {
        uint dispAddr = (uint)Ir(12, 11);
        if (Ir(10, 2) == 2) { DMem[dispAddr] = (uint)AData; return; }

        int pos = (int)Ir(0, 5);
        if (Ir(10, 2) == 3) pos = LcByteMode();

        MData = (int)Rol32((uint)MData, pos);

        int len = (int)Ir(5, 3);
        int leftMaskIndex = (len - 1) & 0x1F;
        int mask = len == 0 ? 0 : unchecked((int)(~0u >> (31 - leftMaskIndex)));
        dispAddr |= (uint)MData & (uint)mask;

        uint map = (uint)Ir(8, 2);
        if (map != 0)
        {
            Uvmem.Vtop(MdReg, out _, out uint l2MapBits, out _, out _, out _);
            uint bit19 = (l2MapBits >> 19) & 1, bit18 = (l2MapBits >> 18) & 1;
            dispAddr |= map switch { 1 => bit18, 2 => bit19, 3 => bit18 | bit19, _ => 0 };
        }

        dispAddr &= 0x7FF;
        uint dispWord = DMem[dispAddr];
        DispatchConstant = (uint)Ir(32, 10);

        uint target = dispWord & 0x3FFF;
        bool n = ((dispWord >> 14) & 1) != 0, p = ((dispWord >> 15) & 1) != 0, r = ((dispWord >> 16) & 1) != 0;

        if (Ir(25, 1) != 0 && n) Npc--;
        if (Ir(24, 1) != 0) AdvanceLc(0);
        if (n) Inhibit = true;
        if (p && r) return;

        if (p) { if (!n) PushSpc(Npc); else PushSpc(Npc - 1); }
        if (r)
        {
            target = PopSpc();
            if ((target >> 14 & 1) != 0) target = AdvanceLc(target);
            target &= 0x3FFF;
        }
        Npc = target;
        Popj = false;
    }

    /// <summary>
    /// Test-only forwarding wrapper: Dsp() stays private (matching Alu()/Jmp()'s
    /// existing visibility), but UCodeDispatchTests needs to exercise its many
    /// branch combinations directly. Matches the CallJmp()/CallVm() precedent
    /// from Phases 3 and 5.
    /// </summary>
    internal void CallDsp() => Dsp();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build LispMachine.sln` (expect 0 errors), then `dotnet run --project usim-cs -- --test-microcode-dispatch`
Expected: `Passed: 10`, `Failed: 0` (as of the final-review fix wave, which added two coverage tests and strengthened a third; originally `Passed: 7`).

- [ ] **Step 5: Wire the new test suite into `Program.cs`**

Add next to the existing test-suite cases:
```csharp
                case "--test-microcode-dispatch":
                    UCodeDispatchTests.RunAllTests();
                    Environment.Exit(0);
                    break;
```
Add to the usage help text:
```csharp
        Console.WriteLine("  --test-microcode-dispatch Run microcode dispatch tests only");
```
Add to the master `RunAllTests()` method:
```csharp
        UCodeDispatchTests.RunAllTests();
```

- [ ] **Step 6: Run the full regression suite**

Run: `dotnet run --project usim-cs -- --test-all`
Expected: all suites report 0 failures. `Byt()`/`MfRead` code 9 (already implemented)/`Dsp()`'s own tests are the only Phase 6-relevant changes; every earlier phase's suite should be unaffected (in practice, the Phase 1 fetch/decode suite WAS affected once `Dsp()` went from stub to real, and was fixed in commit 82893d5).

- [ ] **Step 7: Commit**

```bash
git add usim-cs/UCode.cs usim-cs/UCodeDispatchTests.cs usim-cs/Program.cs
git commit -m "Add faithful Dsp() dispatch instruction class"
```

---

## Self-Review Notes

- **Spec coverage:** every construct the spec's "Phase 6" section calls for (`Dsp()`, including the DMEM-write special case, the mask/rotate/map-bit dispatch-address construction, and the shared push/pop/`Inhibit`/`Popj` machinery) is implemented.
- **No spec corrections needed** — independently re-verified the entire spec section field-by-field against `usim/uexec.c:740-854` before writing this plan and found it fully correct, the first clean pass since Phase 3.
- **Placeholder scan:** no TBD/TODO markers; every test method has real, hand-derivable assertions rather than smoke-test-only checks. One test (`TestByteModePosOverride`) was specifically redesigned during planning after an initial draft's chosen values accidentally made the raw-`Ir(0,5)`-vs-`LcByteMode()` distinction non-discriminating (both formulas coincidentally gave the same pos for the first set of values tried) — the final values (`pos=19` vs raw `pos=3`) were verified to genuinely differ.
- **Type consistency:** `Dsp()`'s signature (`private void Dsp()`) is unchanged from the Phase 1 stub, so `Step()`'s call site needs no changes. `CallDsp()` matches the `CallJmp()`/`CallVm()` pattern exactly.
- **Deliberate deviation flagged:** none beyond the already-established `CallDsp()` test-wrapper precedent — this phase needed no new architectural decisions, unlike Phases 5/5B.

