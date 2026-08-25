# Microcode Engine Phase 4 — Special M-Registers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `UCode.MfRead()` and `UCode.MfWrite()` as a faithful 1:1 port of the real C reference emulator's `mfread()`/`mfwrite()` (`usim/uexec.c:233-459`), replacing the `NotImplementedException` stubs Phase 1/2 left in place.

**Architecture:** Both methods switch on a 5-bit sub-address (`addr & 0x1F` for reads, `dest >> 5` for writes) to access ~20 special "M-registers" — PDL/SPC stack pointers and data, VMA/MD virtual-memory registers, the OA-register instruction modifiers, LC, and interrupt control. Two register codes (`MEMORY-MAP-DATA` on the read side; the four VMA/MD-map-write-triggering writes on the write side) call into virtual memory machinery that doesn't exist until Phase 5, and one write case (`INTERRUPT-CONTROL` bit 28) calls a bus-interface reset routine from a wholly separate, not-yet-ported C subsystem (`bus-interface.c`) — all get clearly-scoped placeholder behavior rather than blocking this phase on unrelated future work.

**Tech Stack:** C#, .NET 8.0. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-08-21-microcode-engine-design.md` (§ "Phase 4 — Special M-Registers")

## Global Constraints

- Faithful 1:1 port of `usim/uexec.c:233-459`'s `mfread()`/`mfwrite()` runtime behavior — not a reinterpretation of what the code comments *say* it should do. Where a C comment describes intent that the actual code doesn't implement (see the `INTERRUPT-CONTROL` bus-reset item below), port the code, not the comment — this project has already found and deliberately ported one such case (`m32.h`'s doc-comment-vs-macro-code mismatch in Phase 2's `Add32`/`Sub32`).
- **Spec correction (found during Phase 4 planning, verified against `usim/uexec.c:243,280` directly):** the spec's `MfRead` table gives codes 1 and 12 a mask of `0x1FFFFF` (21 bits). The real C literal is `01777777` (octal), which is `0x7FFFF` (19 bits) — hand-verified twice, and cross-checked by converting `0x7FFFF` back to octal (524287 → `1777777`, matching the literal exactly). Use `0x7FFFF`, not `0x1FFFFF`. This is the third octal-to-hex mistranslation caught in this spec (after Phase 1's LC/OA-REG masks); always re-derive from the literal octal digits directly rather than trust a restated hex value, in this plan or any other document.
- `MfRead()` and `MfWrite()` both become `internal` (not `private`) so `UCodeMRegisterTests.cs` (same assembly, no `InternalsVisibleTo` needed) can exercise them directly — matching the established pattern for every other multi-branch table method in this project (`LogiOps`/`ArithOps`/`DivOps`/`QControl`/`OutControl`/`CheckJumpCondition`). This *reverses* Phase 2's own fix-wave decision to make `MfWrite` `private` (Phase 2 Minor finding M-1) — that ruling was correct when `MfWrite` was an empty stub with no caller-visible logic to test; now that this phase gives it 18 real branches, the same testability reasoning that applies to every other table method applies here too. Note this reversal explicitly in the implementation so it doesn't read as an accidental inconsistency with the ledger's own prior ruling.
- `MfRead`'s `addr & 037` (octal) = `addr & 0x1F` — unchanged from the existing signature. `MfWrite`'s `dest >> 5` — unchanged from the existing signature.
- **Deferred to Phase 5** (virtual memory): `MfRead` code 9 (`MEMORY-MAP-DATA`, needs `Uvmem.Vtop`) stays a scoped `NotImplementedException` *inside* the switch (all other `MfRead` cases get real implementations now — this is the first "mostly real, one case still stubbed" method in this project, unlike `Dsp()`/`Byt()`/pre-Phase-3 `Jmp()`, which were entirely stubbed). `MfWrite`'s `VmWrite` and `Uvmem.WriteMap` calls (codes 18/19/26/27) get plain **no-op** private placeholder methods on `UCode` itself (`VmWrite(uint,uint)`, `WriteMap(uint,uint)`) — NOT throwing stubs, matching `VmRead`'s existing Phase 1 no-op precedent (`v = 0;`), specifically so that tests exercising `MfWrite`'s other, unrelated cases aren't forced into `try/catch` wrappers the way `WriteDest`'s tests needed for the (correctly throwing) `MfWrite` stub in Phase 2. Do not create a `Uvmem` class in this phase — that's Phase 5's own "new file" — `WriteMap` is a temporary private method on `UCode` that Phase 5's consumer-rework will replace with a real `Uvmem.WriteMap(...)` call.
- **Deferred, different subsystem** (not part of this 9-phase microcode plan at all): `MfWrite`'s `INTERRUPT-CONTROL` case (code 2) real C calls `bus_interface_bus_reset()` when bit 28 is set — a function in `usim/bus-interface.c`, an entirely separate, not-yet-ported emulator subsystem (Unibus/Xbus register-level state), unrelated to `UCode`'s microcode execution. Do not add a throwing stub for this (that would crash the emulator on ordinary microcode that legitimately writes bit 28) — log it at `Info` level (mirroring the C's own `INFO(TRACE_USIM, "usim: ic.bus reset\n");`) and treat the actual reset as a no-op, with a comment flagging the deferred integration point.
- **Comment-vs-code mismatch, port the code:** the real C's `INTERRUPT-CONTROL` case comment claims to "detect a 1-0 transition" for the bus-reset trigger, but the actual code (`if (interrupt_control & (1 << 28)) { ...; bus_interface_bus_reset(); }`) does no such thing — it fires every time a write sets bit 28, regardless of the register's previous value. Port the literal check (bit 28 set on this write), not the comment's described transition-detection behavior.
- `trace_pdlptr_pop`/`trace_pdlidx_read`/`trace_pdlptr_read`/`trace_pdlptr_write`/`trace_pdlptr_push`/`trace_pdlidx_write` (`usim/dump.c:393-`) are a pure debug-history ring-buffer feature (used for post-mortem dumps), not part of emulation behavior — drop them, consistent with this project's established practice of not porting the C's DEBUG-tier instrumentation.

---

### Task 1: `MfRead()`

**Files:**
- Modify: `usim-cs/UCode.cs`
- Create: `usim-cs/UCodeMRegisterTests.cs`
- Modify: `usim-cs/Program.cs` (register the new test suite)

**Interfaces:**
- Consumes: `DispatchConstant`/`SpcPtr`/`Spc`/`PdlPointer`/`PdlIndex`/`Pdl`/`Opc`/`Q`/`VmaReg`/`MdReg`/`InterruptControl`/`Lc` (all Phase 1 fields).
- Produces: `internal int MfRead(uint addr)`. `Step()` (Phase 1) already calls it correctly via `MData = msource == 0 ? (int)MMem[MAddr] : MfRead(MAddr);` — no changes needed there.

- [ ] **Step 1: Write the failing tests**

Create `usim-cs/UCodeMRegisterTests.cs`:

```csharp
// UCodeMRegisterTests.cs - Tests for UCode's special M-register access
// (Phase 4 of the microcode engine port). Covers MfRead()'s 15 implemented
// register codes plus its Phase-5-deferred MEMORY-MAP-DATA stub and fatal
// default, and (Task 2) MfWrite()'s register codes plus its Phase-5/
// bus-interface-deferred placeholders.

using System;

namespace Usim;

public static class UCodeMRegisterTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== UCode M-Register Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestMfReadSimpleRegisters()) passed++; else failed++;
        if (TestMfReadSpcPeekAndPop()) passed++; else failed++;
        if (TestMfReadPdlPopAndPeek()) passed++; else failed++;
        if (TestMfReadLcByteModeGate()) passed++; else failed++;
        if (TestMfReadPlaceholdersAndDeferred()) passed++; else failed++;
        if (TestMfReadDefaultThrows()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestMfReadSimpleRegisters()
    {
        Console.WriteLine("Test: MfRead simple pass-through registers (codes 0,2,3,6,7,8,10)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            ucode.DispatchConstant = 0x12345;
            Assert(ucode.MfRead(0) == 0x12345, $"code0: DispatchConstant, got 0x{ucode.MfRead(0):X}");

            ucode.PdlPointer = 0x3FF;
            Assert(ucode.MfRead(2) == 0x3FF, $"code2: PdlPointer & 0x3FF, got 0x{ucode.MfRead(2):X}");
            ucode.PdlPointer = 0x7FF; // exercise the mask
            Assert(ucode.MfRead(2) == 0x3FF, $"code2: PdlPointer masked to 0x3FF, got 0x{ucode.MfRead(2):X}");

            ucode.PdlIndex = 0x2AA;
            Assert(ucode.MfRead(3) == 0x2AA, $"code3: PdlIndex & 0x3FF, got 0x{ucode.MfRead(3):X}");

            ucode.Opc = 0xABCD;
            Assert(ucode.MfRead(6) == 0xABCD, $"code6: Opc, got 0x{ucode.MfRead(6):X}");

            ucode.Q = 0xDEADBEEF;
            Assert(ucode.MfRead(7) == unchecked((int)0xDEADBEEF), $"code7: Q, got 0x{ucode.MfRead(7):X}");

            ucode.VmaReg = 0x0FF00FF0;
            Assert(ucode.MfRead(8) == 0x0FF00FF0, $"code8 (010 octal): VmaReg, got 0x{ucode.MfRead(8):X}");

            ucode.MdReg = 0x55555555;
            Assert(ucode.MfRead(10) == 0x55555555, $"code10 (012 octal): MdReg, got 0x{ucode.MfRead(10):X}");

            Console.WriteLine("  MfRead simple-register tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfRead simple-register tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfReadSpcPeekAndPop()
    {
        Console.WriteLine("Test: MfRead SPC peek (code 1) and pop (code 12)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 1: (SpcPtr<<24) | (Spc[SpcPtr] & 0x7FFFF) -- NOT 0x1FFFFF, see the
            // Global Constraints correction. Use a value with bits above 19 set to
            // prove the mask is really 0x7FFFF (19 bits), not the spec's original
            // (wrong) 0x1FFFFF (21 bits): 0x1F0000 has bit 20 set (part of the 21-bit
            // mask but NOT the 19-bit one), so a correct 19-bit mask must clear it.
            ucode.SpcPtr = 5;
            ucode.Spc[5] = 0x1FFFFF; // if masked with 0x7FFFF -> 0x7FFFF; if (wrongly) with 0x1FFFFF -> 0x1FFFFF
            int expected1 = (int)((5u << 24) | (0x1FFFFFu & 0x7FFFFu));
            Assert(ucode.MfRead(1) == expected1, $"code1: (SpcPtr<<24)|(Spc[SpcPtr]&0x7FFFF), got 0x{ucode.MfRead(1):X}, expected 0x{expected1:X}");
            Assert(ucode.SpcPtr == 5, "code1 (peek) does not decrement SpcPtr");

            // Code 12 (014 octal): same read+mask as code 1, but decrements SpcPtr afterward.
            ucode.SpcPtr = 5;
            ucode.Spc[5] = 0x1FFFFF;
            int expected12 = (int)((5u << 24) | (0x1FFFFFu & 0x7FFFFu));
            int res12 = ucode.MfRead(12);
            Assert(res12 == expected12, $"code12: same value as code1 before decrement, got 0x{res12:X}");
            Assert(ucode.SpcPtr == 4, $"code12 (pop) decrements SpcPtr by 1 (mod 0x20), got {ucode.SpcPtr}");

            // Code 12 wraparound: SpcPtr=0 decrements to 0x1F (5-bit wraparound).
            ucode.SpcPtr = 0;
            ucode.Spc[0] = 0;
            ucode.MfRead(12);
            Assert(ucode.SpcPtr == 0x1F, $"code12 decrement wraps 0 -> 0x1F, got 0x{ucode.SpcPtr:X}");

            Console.WriteLine("  MfRead SPC peek/pop tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfRead SPC peek/pop tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfReadPdlPopAndPeek()
    {
        Console.WriteLine("Test: MfRead PDL pop (code 20) and peek (code 21) and code 5");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 5: Pdl[PdlIndex], no mutation.
            ucode.PdlIndex = 0x10;
            ucode.Pdl[0x10] = 0x77777777;
            Assert(ucode.MfRead(5) == unchecked((int)0x77777777), $"code5: Pdl[PdlIndex], got 0x{ucode.MfRead(5):X}");
            Assert(ucode.PdlIndex == 0x10, "code5 does not mutate PdlIndex");

            // Code 20 (024 octal): Pdl[PdlPointer], THEN decrement PdlPointer (mod 0x400).
            ucode.PdlPointer = 0x20;
            ucode.Pdl[0x20] = 0x88888888;
            int res20 = ucode.MfRead(20);
            Assert(res20 == unchecked((int)0x88888888), $"code20: Pdl[PdlPointer] before decrement, got 0x{res20:X}");
            Assert(ucode.PdlPointer == 0x1F, $"code20 decrements PdlPointer by 1, got 0x{ucode.PdlPointer:X}");

            // Code 20 wraparound: PdlPointer=0 decrements to 0x3FF (10-bit wraparound).
            ucode.PdlPointer = 0;
            ucode.Pdl[0] = 0;
            ucode.MfRead(20);
            Assert(ucode.PdlPointer == 0x3FF, $"code20 decrement wraps 0 -> 0x3FF, got 0x{ucode.PdlPointer:X}");

            // Code 21 (025 octal): Pdl[PdlPointer], no mutation.
            ucode.PdlPointer = 0x30;
            ucode.Pdl[0x30] = 0x99999999;
            Assert(ucode.MfRead(21) == unchecked((int)0x99999999), $"code21: Pdl[PdlPointer], got 0x{ucode.MfRead(21):X}");
            Assert(ucode.PdlPointer == 0x30, "code21 does not mutate PdlPointer");

            Console.WriteLine("  MfRead PDL pop/peek tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfRead PDL pop/peek tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfReadLcByteModeGate()
    {
        Console.WriteLine("Test: MfRead code 11 (LC, byte-mode-gated bit-0 clear)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 11 (013 octal): byte mode (InterruptControl bit 29 set) -> Lc verbatim.
            ucode.InterruptControl = 1u << 29;
            ucode.Lc = 0x0ABCDEF1; // odd, bit0 set
            Assert(ucode.MfRead(11) == 0x0ABCDEF1, $"code11 byte mode: Lc verbatim, got 0x{ucode.MfRead(11):X}");

            // Not byte mode -> Lc with bit0 cleared.
            ucode.InterruptControl = 0;
            ucode.Lc = 0x0ABCDEF1;
            Assert(ucode.MfRead(11) == 0x0ABCDEF0, $"code11 not byte mode: Lc & ~1, got 0x{ucode.MfRead(11):X}");

            Console.WriteLine("  MfRead LC byte-mode-gate tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfRead LC byte-mode-gate tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfReadPlaceholdersAndDeferred()
    {
        Console.WriteLine("Test: MfRead placeholder codes 13,22 and Phase-5-deferred code 9");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            Assert(ucode.MfRead(13) == 0, "code13 (015 octal): placeholder, matches C's '???' returning 0");
            Assert(ucode.MfRead(22) == 0, "code22 (026 octal): placeholder, matches C's '???' returning 0");

            bool threw = false;
            try { ucode.MfRead(9); }
            catch (NotImplementedException) { threw = true; }
            Assert(threw, "code9 (011 octal, MEMORY-MAP-DATA) is deferred to Phase 5 and throws NotImplementedException");

            Console.WriteLine("  MfRead placeholder/deferred tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfRead placeholder/deferred tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfReadDefaultThrows()
    {
        Console.WriteLine("Test: MfRead default case throws, matching C's fatal err()");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            bool threw = false;
            try { ucode.MfRead(4); } // octal 4 is not a valid code
            catch (InvalidOperationException) { threw = true; }
            Assert(threw, "code4 is not a valid MfRead register; must throw");

            Console.WriteLine("  MfRead default-throws tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfRead default-throws tests failed: {ex.Message}\n");
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
Expected: FAIL — `UCode.MfRead` is still `private` and still throws unconditionally.

- [ ] **Step 3: Implement `MfRead()`**

In `usim-cs/UCode.cs`, replace the `MfRead` stub:
```csharp
    private int MfRead(uint addr)
    {
        throw new NotImplementedException("MfRead is implemented in Phase 4 (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md)");
    }
```
with:
```csharp
    /// <summary>
    /// Faithful port of mfread() (usim/uexec.c:233-304). Code 9 (MEMORY-MAP-DATA)
    /// needs Phase 5's Uvmem.Vtop and stays deferred; every other code is fully
    /// implemented. Codes 1/12's mask is 0x7FFFF (19 bits, from the C's literal
    /// octal 01777777) -- NOT 0x1FFFFF (21 bits) as an earlier draft of the spec
    /// mistranslated; re-derived and hand-verified against the literal digits.
    /// </summary>
    internal int MfRead(uint addr)
    {
        switch (addr & 0x1F)
        {
            case 0: return (int)DispatchConstant;
            case 1: return (int)((SpcPtr << 24) | (Spc[SpcPtr] & 0x7FFFF));
            case 2: return (int)(PdlPointer & 0x3FF);
            case 3: return (int)(PdlIndex & 0x3FF);
            case 5: return (int)Pdl[PdlIndex];
            case 6: return (int)Opc;
            case 7: return (int)Q;
            case 8: return (int)VmaReg;
            case 9:
                throw new NotImplementedException("MfRead code 9 (MEMORY-MAP-DATA) is implemented in Phase 5 (needs Uvmem.Vtop)");
            case 10: return (int)MdReg;
            case 11: return (int)((InterruptControl & (1 << 29)) != 0 ? Lc : Lc & ~1u);
            case 12:
            {
                int res = (int)((SpcPtr << 24) | (Spc[SpcPtr] & 0x7FFFF));
                SpcPtr = (SpcPtr - 1) & 0x1F;
                return res;
            }
            case 13: return 0; // placeholder, matches the real C's own "???" comment
            case 20:
            {
                int res = (int)Pdl[PdlPointer];
                PdlPointer = (PdlPointer - 1) & 0x3FF;
                return res;
            }
            case 21: return (int)Pdl[PdlPointer];
            case 22: return 0; // placeholder, matches the real C's own "???" comment
            default:
                // .NET has no built-in octal format specifier (unlike the C's %o) --
                // hex is used here purely for a readable diagnostic message; this has
                // no bearing on emulation behavior, only on the exception's text.
                throw new InvalidOperationException($"unknown MF register (0x{addr:X}) read"); // matches the C's fatal err()
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build LispMachine.sln` (expect 0 errors), then `dotnet run --project usim-cs -- --test-microcode-mregisters`
Expected: `Passed: 6`, `Failed: 0`.

- [ ] **Step 5: Wire the new test suite into `Program.cs`**

Add next to the existing `--test-microcode-*` cases:
```csharp
                case "--test-microcode-mregisters":
                    UCodeMRegisterTests.RunAllTests();
                    break;
```
Add to the usage help text (next to the `--test-microcode-jump` line):
```csharp
        Console.WriteLine("  --test-microcode-mregisters Run microcode M-register tests only");
```
Add to the master `RunAllTests()` method (next to the `UCodeJumpTests.RunAllTests();` call):
```csharp
        UCodeMRegisterTests.RunAllTests();
```

- [ ] **Step 6: Run the full regression suite**

Run: `dotnet run --project usim-cs -- --test-all`
Expected: all suites (`UCodeTests`, `UCodeFetchDecodeTests`, `UCodeAluTests`, `UCodeJumpTests`, `UCodeMRegisterTests`, `ConfigTests`, `WpfBackendTests`) report 0 failures.

- [ ] **Step 7: Commit**

```bash
git add usim-cs/UCode.cs usim-cs/UCodeMRegisterTests.cs usim-cs/Program.cs
git commit -m "Add faithful MfRead() special M-register read access"
```

---

### Task 2: `MfWrite()`

**Files:**
- Modify: `usim-cs/UCode.cs`
- Modify: `usim-cs/UCodeMRegisterTests.cs`

**Interfaces:**
- Consumes: `Lc`/`InterruptControl`/`Pdl`/`PdlPointer`/`PdlIndex`/`PushSpc`/`OaRegLow`/`Oal`/`OaRegHigh`/`Oah`/`VmaReg`/`MdReg`/`NewMd`/`NewMdDelay` (Phase 1), `VmRead` (Phase 1, existing no-op stub).
- Produces: `internal void MfWrite(uint dest, int data)` (visibility reversed from Phase 2's `private` — see Global Constraints), plus two new private Phase-5 placeholder methods: `private void VmWrite(uint vaddr, uint data)` and `private void WriteMap(uint vma, uint data)`. `WriteDest` (Phase 2/4) already calls `MfWrite(dest, (int)Out)` correctly — no changes needed there.

- [ ] **Step 1: Write the failing tests**

Append to `usim-cs/UCodeMRegisterTests.cs`'s `RunAllTests()`:
```csharp
        if (TestMfWriteLc()) passed++; else failed++;
        if (TestMfWriteInterruptControl()) passed++; else failed++;
        if (TestMfWritePdlAndSpcRegisters()) passed++; else failed++;
        if (TestMfWriteOaRegisters()) passed++; else failed++;
        if (TestMfWriteVmaAndMdRegisters()) passed++; else failed++;
        if (TestMfWriteNoOpAndDefault()) passed++; else failed++;
```

Add the six test methods:
```csharp
    private static bool TestMfWriteLc()
    {
        Console.WriteLine("Test: MfWrite code 1 (LC)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Not byte mode (InterruptControl bit29 clear): low bit cleared, bit31 (NEED-FETCH) set.
            ucode.InterruptControl = 0;
            ucode.Lc = 0xFFFFFFFF; // pre-existing garbage in the untouched high bits, to prove the mask
            ucode.MfWrite(1 << 5, unchecked((int)0x07FFFFFF)); // data with bit26 set, above the 26-bit mask
            uint expected = (0xFFFFFFFFu & ~0x03FFFFFFu) | (0x07FFFFFFu & 0x03FFFFFFu);
            expected &= ~1u;          // not byte mode -> low bit cleared
            expected |= (1u << 31);   // NEED-FETCH always set
            Assert(ucode.Lc == expected, $"code1 not byte mode, got 0x{ucode.Lc:X}, expected 0x{expected:X}");

            // Byte mode (bit29 set): low bit is NOT forced clear.
            ucode.InterruptControl = 1u << 29;
            ucode.Lc = 0;
            ucode.MfWrite(1 << 5, 0x00000003); // odd value, bit0 set
            Assert((ucode.Lc & 1) == 1, "code1 byte mode: low bit is NOT cleared");
            Assert((ucode.Lc & (1u << 31)) != 0, "code1 byte mode: NEED-FETCH still set unconditionally");

            Console.WriteLine("  MfWrite LC tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfWrite LC tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfWriteInterruptControl()
    {
        Console.WriteLine("Test: MfWrite code 2 (INTERRUPT-CONTROL)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            ucode.Lc = 0;
            ucode.MfWrite(2 << 5, unchecked((int)(0xFu << 26))); // set all 4 preserved-flag bits
            Assert(ucode.InterruptControl == (0xFu << 26), $"code2: InterruptControl set verbatim, got 0x{ucode.InterruptControl:X}");
            Assert(ucode.Lc == (0xFu << 26), $"code2: Lc bits 26-29 mirror InterruptControl, got 0x{ucode.Lc:X}");

            // Bit 28 (bus reset) does not throw -- it's a deferred, different-subsystem no-op.
            ucode.MfWrite(2 << 5, unchecked((int)(1u << 28)));
            Assert(true, "code2 bit28 (bus reset) does not throw");

            Console.WriteLine("  MfWrite INTERRUPT-CONTROL tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfWrite INTERRUPT-CONTROL tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfWritePdlAndSpcRegisters()
    {
        Console.WriteLine("Test: MfWrite PDL/SPC registers (codes 8,9,10,11,12,13)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 8 (010 octal): Pdl[PdlPointer] = data (no pointer mutation).
            ucode.PdlPointer = 0x15;
            ucode.MfWrite(8 << 5, unchecked((int)0xAAAAAAAA));
            Assert(ucode.Pdl[0x15] == 0xAAAAAAAA, $"code8: Pdl[PdlPointer] written, got 0x{ucode.Pdl[0x15]:X}");
            Assert(ucode.PdlPointer == 0x15, "code8 does not mutate PdlPointer");

            // Code 9 (011 octal): PdlPointer++ (mod 0x400) THEN write.
            ucode.PdlPointer = 0x15;
            ucode.MfWrite(9 << 5, unchecked((int)0xBBBBBBBB));
            Assert(ucode.PdlPointer == 0x16, $"code9: PdlPointer incremented first, got 0x{ucode.PdlPointer:X}");
            Assert(ucode.Pdl[0x16] == 0xBBBBBBBB, $"code9: written at the NEW pointer, got 0x{ucode.Pdl[0x16]:X}");

            // Code 9 wraparound: PdlPointer=0x3FF increments to 0 (10-bit wraparound).
            ucode.PdlPointer = 0x3FF;
            ucode.MfWrite(9 << 5, 1);
            Assert(ucode.PdlPointer == 0, $"code9 increment wraps 0x3FF -> 0, got 0x{ucode.PdlPointer:X}");

            // Code 10 (012 octal): Pdl[PdlIndex] = data.
            ucode.PdlIndex = 0x20;
            ucode.MfWrite(10 << 5, unchecked((int)0xCCCCCCCC));
            Assert(ucode.Pdl[0x20] == 0xCCCCCCCC, $"code10: Pdl[PdlIndex] written, got 0x{ucode.Pdl[0x20]:X}");

            // Code 11 (013 octal): PdlIndex = data & 0x3FF.
            ucode.MfWrite(11 << 5, 0x7FF);
            Assert(ucode.PdlIndex == 0x3FF, $"code11: PdlIndex masked to 0x3FF, got 0x{ucode.PdlIndex:X}");

            // Code 12 (014 octal): PdlPointer = data & 0x3FF.
            ucode.MfWrite(12 << 5, 0x7FF);
            Assert(ucode.PdlPointer == 0x3FF, $"code12: PdlPointer masked to 0x3FF, got 0x{ucode.PdlPointer:X}");

            // Code 13 (015 octal): PushSpc(data).
            ucode.SpcPtr = 0;
            ucode.MfWrite(13 << 5, unchecked((int)0x12345678));
            Assert(ucode.SpcPtr == 1, $"code13: PushSpc advanced SpcPtr, got {ucode.SpcPtr}");
            Assert(ucode.Spc[1] == 0x12345678, $"code13: pushed value, got 0x{ucode.Spc[1]:X}");

            Console.WriteLine("  MfWrite PDL/SPC tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfWrite PDL/SPC tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfWriteOaRegisters()
    {
        Console.WriteLine("Test: MfWrite OA-REG-LO/HI (codes 14,15)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 14 (016 octal): OaRegLow = data & 0x03FFFFFF (26 bits); Oal = true.
            ucode.MfWrite(14 << 5, unchecked((int)0xFFFFFFFF));
            Assert(ucode.OaRegLow == 0x03FFFFFF, $"code14: 26-bit mask, got 0x{ucode.OaRegLow:X}");
            Assert(ucode.Oal == true, "code14 sets Oal");

            // Code 15 (017 octal): OaRegHigh = data & 0x7FFFFF (23 bits); Oah = true.
            ucode.MfWrite(15 << 5, unchecked((int)0xFFFFFFFF));
            Assert(ucode.OaRegHigh == 0x7FFFFF, $"code15: 23-bit mask, got 0x{ucode.OaRegHigh:X}");
            Assert(ucode.Oah == true, "code15 sets Oah");

            Console.WriteLine("  MfWrite OA-register tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfWrite OA-register tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfWriteVmaAndMdRegisters()
    {
        Console.WriteLine("Test: MfWrite VMA/MD registers (codes 16,17,18,19,24,25,26,27)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 16 (020 octal): VmaReg = data.
            ucode.MfWrite(16 << 5, unchecked((int)0x11111111));
            Assert(ucode.VmaReg == 0x11111111, $"code16: VmaReg set, got 0x{ucode.VmaReg:X}");

            // Code 17 (021 octal): VmaReg = data; VmRead(VmaReg, out NewMd); NewMdDelay = 2.
            ucode.NewMdDelay = 0;
            ucode.MfWrite(17 << 5, unchecked((int)0x22222222));
            Assert(ucode.VmaReg == 0x22222222, $"code17: VmaReg set, got 0x{ucode.VmaReg:X}");
            Assert(ucode.NewMdDelay == 2, $"code17: NewMdDelay set to 2, got {ucode.NewMdDelay}");

            // Code 18 (022 octal): VmaReg = data; VmWrite(VmaReg, MdReg) -- no-op placeholder, must not throw.
            ucode.MfWrite(18 << 5, unchecked((int)0x33333333));
            Assert(ucode.VmaReg == 0x33333333, $"code18: VmaReg set, got 0x{ucode.VmaReg:X}");

            // Code 19 (023 octal): VmaReg = data; Uvmem.WriteMap placeholder -- no-op, must not throw.
            ucode.MfWrite(19 << 5, unchecked((int)0x44444444));
            Assert(ucode.VmaReg == 0x44444444, $"code19: VmaReg set, got 0x{ucode.VmaReg:X}");

            // Code 24 (030 octal): MdReg = data.
            ucode.MfWrite(24 << 5, unchecked((int)0x55555555));
            Assert(ucode.MdReg == 0x55555555, $"code24: MdReg set, got 0x{ucode.MdReg:X}");

            // Code 25 (031 octal): MdReg = data; VmRead(VmaReg, out NewMd); NewMdDelay = 2.
            // Note: reads from VmaReg, not the just-written MdReg -- matches the real C exactly.
            // VmaReg is left at whatever code 19 set it to just above (0x44444444); irrelevant
            // to this assertion since VmRead is currently a no-op regardless of its argument.
            ucode.NewMdDelay = 0;
            ucode.MfWrite(25 << 5, unchecked((int)0x66666666));
            Assert(ucode.MdReg == 0x66666666, $"code25: MdReg set, got 0x{ucode.MdReg:X}");
            Assert(ucode.NewMdDelay == 2, $"code25: NewMdDelay set to 2, got {ucode.NewMdDelay}");

            // Code 26 (032 octal): MdReg = data; VmWrite(VmaReg, MdReg) -- no-op, must not throw.
            ucode.MfWrite(26 << 5, unchecked((int)0x77777777));
            Assert(ucode.MdReg == 0x77777777, $"code26: MdReg set, got 0x{ucode.MdReg:X}");

            // Code 27 (033 octal): MdReg = data; Uvmem.WriteMap placeholder -- no-op, must not throw.
            // Note: MdReg == 0x88888888u (bare uint literal), not a cast int -- comparing a
            // uint field against a negative int constant expression doesn't compile in C#
            // (no implicit conversion for a negative value into uint), unlike the method
            // argument above, which legitimately needs the int cast since MfWrite's data
            // parameter is int.
            ucode.MfWrite(27 << 5, unchecked((int)0x88888888));
            Assert(ucode.MdReg == 0x88888888u, $"code27: MdReg set, got 0x{ucode.MdReg:X}");

            Console.WriteLine("  MfWrite VMA/MD tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfWrite VMA/MD tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfWriteNoOpAndDefault()
    {
        Console.WriteLine("Test: MfWrite code 0 (no-op) and default (non-fatal warning)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 0: no-op, must not throw or mutate anything observable.
            ucode.MfWrite(0 << 5, unchecked((int)0xFFFFFFFF));
            Assert(true, "code0: no-op does not throw");

            // Default (e.g. dest>>5 == 3, unassigned): matches C's non-fatal warn(), does not throw.
            ucode.MfWrite(3 << 5, 0);
            Assert(true, "default case does not throw (non-fatal warning, matching C's warn())");

            Console.WriteLine("  MfWrite no-op/default tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfWrite no-op/default tests failed: {ex.Message}\n");
            return false;
        }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build LispMachine.sln`
Expected: FAIL — `MfWrite` still throws unconditionally, `VmWrite`/`WriteMap` don't exist.

- [ ] **Step 3: Implement `MfWrite()` and its two Phase-5 placeholder methods**

In `usim-cs/UCode.cs`, add the two new placeholder methods near the existing `VmRead` placeholder:
```csharp
    /// <summary>
    /// Placeholder for the real virtual-memory write path (Phase 5's Vm()-
    /// backed implementation). A no-op until then, matching VmRead's own
    /// Phase 1 no-op precedent -- deliberately NOT throwing, so MfWrite's
    /// other, unrelated register codes remain testable without needing
    /// try/catch wrappers.
    /// </summary>
    private void VmWrite(uint vaddr, uint data)
    {
    }

    /// <summary>
    /// Placeholder for Uvmem.WriteMap (Phase 5's "new file" Uvmem.cs does
    /// not exist yet). A no-op until then, for the same reason as VmWrite.
    /// </summary>
    private void WriteMap(uint vma, uint data)
    {
    }
```

Replace the `MfWrite` stub:
```csharp
    private void MfWrite(uint dest, int data)
    {
        throw new NotImplementedException("MfWrite is implemented in Phase 4 (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md)");
    }
```
with:
```csharp
    /// <summary>
    /// Faithful port of mfwrite() (usim/uexec.c:306-459). Codes 18/19/26/27
    /// call the Phase-5-deferred VmWrite/WriteMap placeholders (no-ops until
    /// then). Code 2's bit-28 bus-reset is a no-op + Info log -- the real
    /// bus_interface_bus_reset() lives in a wholly separate, not-yet-ported
    /// subsystem (usim/bus-interface.c). Note: the real C's comment on this
    /// case claims to detect a "1-0 transition", but the actual code just
    /// checks whether bit 28 is set on THIS write -- ported the code, not
    /// the comment, per this project's established practice.
    /// </summary>
    internal void MfWrite(uint dest, int data)
    {
        uint udata = (uint)data;
        switch (dest >> 5)
        {
            case 0:
                return;
            case 1:
                Lc = (Lc & ~0x03FFFFFFu) | (udata & 0x03FFFFFFu);
                if ((InterruptControl & (1 << 29)) == 0)
                {
                    Lc &= ~1u;
                }
                Lc |= (1u << 31);
                return;
            case 2:
                InterruptControl = udata;
                if ((InterruptControl & (1 << 28)) != 0)
                {
                    TraceLog.Instance.Info(TraceCategory.MicroCode, "usim: ic.bus reset");
                }
                Lc = (Lc & ~(0xFu << 26)) | (InterruptControl & (0xFu << 26));
                return;
            case 8:
                Pdl[PdlPointer] = udata;
                return;
            case 9:
                PdlPointer = (PdlPointer + 1) & 0x3FF;
                Pdl[PdlPointer] = udata;
                return;
            case 10:
                Pdl[PdlIndex] = udata;
                return;
            case 11:
                PdlIndex = udata & 0x3FF;
                return;
            case 12:
                PdlPointer = udata & 0x3FF;
                return;
            case 13:
                PushSpc(udata);
                return;
            case 14:
                OaRegLow = udata & 0x03FFFFFF;
                Oal = true;
                return;
            case 15:
                OaRegHigh = udata & 0x7FFFFF;
                Oah = true;
                return;
            case 16:
                VmaReg = udata;
                return;
            case 17:
                VmaReg = udata;
                VmRead(VmaReg, out uint newMd17);
                NewMd = newMd17;
                NewMdDelay = 2;
                return;
            case 18:
                VmaReg = udata;
                VmWrite(VmaReg, MdReg);
                return;
            case 19:
                VmaReg = udata;
                WriteMap(VmaReg, MdReg);
                return;
            case 24:
                MdReg = udata;
                return;
            case 25:
                MdReg = udata;
                VmRead(VmaReg, out uint newMd25);
                NewMd = newMd25;
                NewMdDelay = 2;
                return;
            case 26:
                MdReg = udata;
                VmWrite(VmaReg, MdReg);
                return;
            case 27:
                MdReg = udata;
                WriteMap(VmaReg, MdReg);
                return;
            default:
                // Hex, not octal, for the same reason noted in MfRead's default case --
                // .NET has no built-in octal format specifier; this is diagnostic text only.
                TraceLog.Instance.Warning(TraceCategory.MicroCode, $"unknown MF register (0x{dest:X}) write (0x{data:X})");
                return;
        }
    }
```

Note: `MdReg` is `uint` (Phase 1), so `VmWrite(VmaReg, MdReg)`/`WriteMap(VmaReg, MdReg)` pass it directly with no cast needed.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build LispMachine.sln` (expect 0 errors), then `dotnet run --project usim-cs -- --test-microcode-mregisters`
Expected: `Passed: 12`, `Failed: 0`.

- [ ] **Step 5: Run the full regression suite**

Run: `dotnet run --project usim-cs -- --test-all`
Expected: all suites report 0 failures. Note: `Byt()`/`Dsp()`/`MfRead`'s code-9 path remain deferred stubs (Phase 5/6/7's job) — unaffected by this task.

- [ ] **Step 6: Commit**

```bash
git add usim-cs/UCode.cs usim-cs/UCodeMRegisterTests.cs
git commit -m "Add faithful MfWrite() special M-register write access"
```

---

## Self-Review Notes

- **Spec coverage:** every `MfRead`/`MfWrite` register code the spec's "Phase 4" section lists is implemented (15 of 16 `MfRead` codes real, 1 correctly deferred to Phase 5; 18 of 18 `MfWrite` codes real, 4 of those calling correctly-scoped Phase-5 placeholders, 1 calling a correctly-scoped different-subsystem placeholder).
- **Spec correction applied:** `MfRead` codes 1/12's mask corrected from the spec's `0x1FFFFF` to the real C-literal-verified `0x7FFFF` — independently re-derived twice (direct octal-to-decimal arithmetic, and a reverse decimal-to-octal conversion cross-check) against a fresh read of `usim/uexec.c:243,280`, not trusted from the spec text. No other discrepancies found in either table against the real C.
- **Placeholder scan:** the only `NotImplementedException` this phase leaves behind is `MfRead` code 9 (explicitly phase-tagged); `VmWrite`/`WriteMap` are deliberate no-ops (not exceptions), explicitly commented as to why, matching `VmRead`'s existing Phase 1 precedent — not vague TODOs.
- **Type consistency:** `MfRead`/`MfWrite` signatures (`int MfRead(uint)`, `void MfWrite(uint, int)`) are unchanged from Phase 1/2's existing stubs, so `Step()`/`WriteDest`'s call sites need no changes. `VmWrite`/`WriteMap`'s `uint vaddr/vma, uint data` signatures match how they're called from `MfWrite` (`VmaReg`/`MdReg` are both `uint`).
- **Deliberate deviation flagged:** `MfWrite`'s visibility reverses from Phase 2's `private` back to `internal` — a considered re-reversal, not an inconsistency, since Phase 2's `private` ruling was explicitly conditioned on `MfWrite` having no real logic yet ("nothing outside UCode calls it and no test calls it directly").
