# Microcode Engine Phase 2: ALU Instructions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `UCode.Alu()`'s `NotImplementedException` stub with a faithful port of the real CADR ALU instruction class — `LogiOps`/`ArithOps`/`DivOps` (the 36 ALU opcodes), `QControl`/`OutControl` (Q-register and output-routing), and `WriteDest` (destination write-back, shared with Phase 7's `Byt()`).

**Architecture:** Same faithful-port philosophy as Phase 1 — one C# method per C function, real field/register names, `Ir()`-based decode. **One deliberate deviation from the design spec's stated visibility**: the spec marks `LogiOps`/`ArithOps`/`DivOps`/`QControl`/`OutControl`/`WriteDest`/`Add32`/`Sub32`/`Abs32`/`Rol32` as `private`. This plan makes them `internal` instead (still not part of `UCode`'s public API — `usim-cs`'s test files live in the same assembly as the code under test, per this codebase's established convention of no separate test project, so `internal` is sufficient to let tests call them directly and in isolation, without needing a full instruction word decoded through `Step()` for every test). `Alu()` itself, `Step()`, and the other opcode-class stubs stay exactly as Phase 1 left them (private).

**Tech Stack:** C# / .NET 8.0, no new dependencies.

**Spec:** [docs/superpowers/specs/2026-08-21-microcode-engine-design.md](../specs/2026-08-21-microcode-engine-design.md) — this plan implements the "Phase 2 — ALU Instructions" section in full, including its "Support helpers (ported from `m32.c`/`m32.h`)" subsection.

## Global Constraints

- `LogiOps`/`ArithOps`/`DivOps`/`QControl`/`OutControl`/`WriteDest`/`Add32`/`Sub32`/`Abs32`/`Rol32` are `internal`, not `private` (see Architecture above — a deliberate deviation from the spec's stated visibility, for testability; this does not change their behavior or the spec's semantics, only who can call them).
- Every numeric table in the spec (`LogiOps` codes 0-15, `ArithOps` codes 16-31, `DivOps` codes 32/33/37/41) must match exactly — these are the ported semantics of real CADR hardware; do not "simplify" or "clean up" any of them.
- `AluCarry` is zeroed at the top of `Alu()`, before the opcode switch — per the spec, `LogiOps` codes never set it (it stays 0), matching the C's behavior of only `arithOps`/`divOps` touching it.
- Tests exercise these `internal` methods directly wherever practical (Tasks 1-3), and exercise the fully-wired `Alu()` end-to-end through `Step()` in Task 4 — matching this codebase's established test-file convention (a `static class *Tests` with `RunAllTests()`, a private `Assert(bool, string)` helper, wired to a `--test-*` CLI flag in `Program.cs`).

---

## Task 1: `Add32`/`Sub32`/`Abs32`/`Rol32` support helpers

**Files:**
- Modify: `usim-cs/UCode.cs` (add the four helpers — these are pure/static, no instance state, so they can go anywhere in the class; add them right before the `#region ALU Operations` region Phase 1 left empty)
- Create: `usim-cs/UCodeAluTests.cs`
- Modify: `usim-cs/Program.cs` (wire a `--test-microcode-alu` CLI flag, following this codebase's established test-flag convention)

**Interfaces:**
- Produces: `internal static (uint Out, uint Carry) Add32(int a, int b, bool ci)`, `internal static (uint Out, uint Carry) Sub32(int a, int b, bool ci)`, `internal static int Abs32(int a)`, `internal static uint Rol32(uint value, int bits)` — Task 2 (`ArithOps`/`DivOps`) and later phases (`Jmp`'s `CheckJumpCondition`, `Dsp`, `Byt`) call these exact signatures.

- [ ] **Step 1: Write the failing tests**

Create `usim-cs/UCodeAluTests.cs`:

```csharp
// UCodeAluTests.cs - Tests for UCode's ALU instruction class (Phase 2 of
// the microcode engine port). Covers the Add32/Sub32/Abs32/Rol32 support
// helpers, LogiOps/ArithOps/DivOps, QControl/OutControl, and the fully
// wired Alu()/Step() pipeline.

using System;

namespace Usim;

public static class UCodeAluTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== UCode ALU Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestAdd32()) passed++; else failed++;
        if (TestSub32()) passed++; else failed++;
        if (TestAbs32()) passed++; else failed++;
        if (TestRol32()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestAdd32()
    {
        Console.WriteLine("Test: Add32");
        try
        {
            var (out1, carry1) = UCode.Add32(5, 3, false);
            Assert(out1 == 8, $"5 + 3 + 0 = 8, got {out1}");
            Assert(carry1 == 0, $"no carry expected, got {carry1}");

            var (out2, carry2) = UCode.Add32(5, 3, true);
            Assert(out2 == 9, $"5 + 3 + 1 = 9, got {out2}");
            Assert(carry2 == 0, $"no carry expected, got {carry2}");

            // unsigned overflow: 0xFFFFFFFF + 1 + 0 wraps to 0 with carry out
            var (out3, carry3) = UCode.Add32(-1, 1, false);
            Assert(out3 == 0, $"0xFFFFFFFF + 1 wraps to 0, got 0x{out3:X}");
            Assert(carry3 == 1, $"carry expected on wrap, got {carry3}");

            Console.WriteLine("  Add32 tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Add32 tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestSub32()
    {
        Console.WriteLine("Test: Sub32");
        try
        {
            var (out1, carry1) = UCode.Sub32(5, 3, true);
            Assert(out1 == 2, $"5 - 3 - 0 = 2, got {out1}");
            Assert(carry1 == 0, $"no borrow expected (result <= minuend), got {carry1}");

            var (out2, carry2) = UCode.Sub32(5, 3, false);
            Assert(out2 == 1, $"5 - 3 - 1 = 1, got {out2}");
            Assert(carry2 == 0, $"no borrow expected, got {carry2}");

            // 3 - 5 - 0 underflows (borrow)
            var (out3, carry3) = UCode.Sub32(3, 5, true);
            Assert(out3 == unchecked((uint)-2), $"3 - 5 = -2 (as uint 0xFFFFFFFE), got 0x{out3:X}");
            Assert(carry3 == 1, $"borrow expected (result > minuend, unsigned), got {carry3}");

            Console.WriteLine("  Sub32 tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Sub32 tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestAbs32()
    {
        Console.WriteLine("Test: Abs32");
        try
        {
            Assert(UCode.Abs32(5) == 5, "Abs32(5) == 5");
            Assert(UCode.Abs32(-5) == 5, "Abs32(-5) == 5");
            Assert(UCode.Abs32(0) == 0, "Abs32(0) == 0");

            Console.WriteLine("  Abs32 tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Abs32 tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestRol32()
    {
        Console.WriteLine("Test: Rol32");
        try
        {
            Assert(UCode.Rol32(0x00000001u, 0) == 0x00000001u, "Rol32 by 0 bits is identity");
            Assert(UCode.Rol32(0x00000001u, 1) == 0x00000002u, "Rol32(1, by 1) == 2");
            Assert(UCode.Rol32(0x80000000u, 1) == 0x00000001u, "Rol32 wraps the top bit to bit 0");
            Assert(UCode.Rol32(0x12345678u, 8) == 0x34567812u, "Rol32 by 8 bits (byte rotate)");

            Console.WriteLine("  Rol32 tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Rol32 tests failed: {ex.Message}\n");
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

- [ ] **Step 2: Wire a `--test-microcode-alu` CLI flag**

In `usim-cs/Program.cs`, add a case next to `--test-microcode-decode`:
```csharp
                case "--test-microcode-alu":
                    UCodeAluTests.RunAllTests();
                    Environment.Exit(0);
                    break;
```
Add a usage line next to `--test-microcode-decode`'s:
```csharp
        Console.WriteLine("  --test-microcode-alu    Run microcode ALU tests only");
```
Add it to the master `RunAllTests()`, right after the `UCodeFetchDecodeTests.RunAllTests()` call added in Phase 1's final fix wave:
```csharp

        // Run microcode ALU tests
        Console.WriteLine("Running Microcode ALU Tests...\n");
        UCodeAluTests.RunAllTests();
        Console.WriteLine();
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet build LispMachine.sln`
Expected: FAIL with `CS0117`/`CS1061`-style errors — `UCode` has no members named `Add32`/`Sub32`/`Abs32`/`Rol32` yet.

- [ ] **Step 4: Implement the four helpers**

In `usim-cs/UCode.cs`, add immediately before the empty `#region ALU Operations` block Phase 1 left:

```csharp
    /// <summary>
    /// 32-bit add with carry-in/carry-out, matching the C macro add32().
    /// </summary>
    internal static (uint Out, uint Carry) Add32(int a, int b, bool ci)
    {
        uint outv = unchecked((uint)a + (uint)b + (ci ? 1u : 0u));
        uint co = ci ? ((uint)b >= (uint)~a ? 0u : 1u) : ((uint)b > (uint)~a ? 0u : 1u);
        return (outv, co);
    }

    /// <summary>
    /// 32-bit subtract with carry-in/carry-out, matching the C macro sub32().
    /// </summary>
    internal static (uint Out, uint Carry) Sub32(int a, int b, bool ci)
    {
        uint outv = unchecked((uint)a - (uint)b - (ci ? 0u : 1u));
        uint co = outv < (uint)a ? 1u : 0u;
        return (outv, co);
    }

    /// <summary>
    /// Two's-complement absolute value, matching the C macro abs32().
    /// </summary>
    internal static int Abs32(int a) => a < 0 ? ~a + 1 : a;

    /// <summary>
    /// 32-bit rotate-left, matching the C function rol32() in m32.c.
    /// </summary>
    internal static uint Rol32(uint value, int bits)
    {
        if (bits == 0) return value;
        int mask = unchecked((int)0x80000000) >> bits;
        uint tmp = (uint)(((ulong)(value & (uint)mask)) >> (32 - bits));
        return (value << bits) | tmp;
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet build LispMachine.sln` (expect 0 errors), then `dotnet run --project usim-cs -- --test-microcode-alu`
Expected: `Passed: 4`, `Failed: 0`.

- [ ] **Step 6: Commit**

```bash
git add usim-cs/UCode.cs usim-cs/UCodeAluTests.cs usim-cs/Program.cs
git commit -m "Add Add32/Sub32/Abs32/Rol32 ALU support helpers"
```

---

## Task 2: `LogiOps`/`ArithOps`/`DivOps`

**Files:**
- Modify: `usim-cs/UCode.cs`
- Modify: `usim-cs/UCodeAluTests.cs`

**Interfaces:**
- Consumes: `Add32`/`Sub32`/`Abs32` (Task 1), `MData`/`AData`/`Q`/`AluOut`/`AluCarry` (Phase 1's register model).
- Produces: `internal void LogiOps(uint op)`, `internal void ArithOps(uint op)`, `internal void DivOps(uint op)` — Task 4's `Alu()` calls these exact signatures with the decoded 6-bit `aluop` field.

- [ ] **Step 1: Write the failing tests**

In `usim-cs/UCodeAluTests.cs`, add (inside `RunAllTests()`, after the `TestRol32` call):
```csharp
        if (TestLogiOps()) passed++; else failed++;
        if (TestArithOps()) passed++; else failed++;
        if (TestDivOps()) passed++; else failed++;
```

Add the three test methods:
```csharp
    private static bool TestLogiOps()
    {
        Console.WriteLine("Test: LogiOps");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 0: SETZ
            ucode.MData = 0x12345678; ucode.AData = 0x0F0F0F0F; ucode.AluCarry = 0;
            ucode.LogiOps(0);
            Assert(ucode.AluOut == 0, $"SETZ -> 0, got 0x{ucode.AluOut:X}");
            Assert(ucode.AluCarry == 0, "LogiOps never sets carry");

            // Code 1: AND
            ucode.MData = 0x12345678; ucode.AData = 0x0F0F0F0F;
            ucode.LogiOps(1);
            Assert(ucode.AluOut == (0x12345678u & 0x0F0F0F0Fu), $"AND, got 0x{ucode.AluOut:X}");

            // Code 6: XOR
            ucode.MData = 0x12345678; ucode.AData = 0x0F0F0F0F;
            ucode.LogiOps(6);
            Assert(ucode.AluOut == (0x12345678u ^ 0x0F0F0F0Fu), $"XOR, got 0x{ucode.AluOut:X}");

            // Code 7: IOR
            ucode.MData = 0x12345678; ucode.AData = 0x0F0F0F0F;
            ucode.LogiOps(7);
            Assert(ucode.AluOut == (0x12345678u | 0x0F0F0F0Fu), $"IOR, got 0x{ucode.AluOut:X}");

            // Code 9: EQV (boolean equality test, NOT bitwise XNOR)
            ucode.MData = 5; ucode.AData = 5;
            ucode.LogiOps(9);
            Assert(ucode.AluOut == 1, $"EQV(5,5) -> 1 (boolean true), got {ucode.AluOut}");
            ucode.MData = 5; ucode.AData = 6;
            ucode.LogiOps(9);
            Assert(ucode.AluOut == 0, $"EQV(5,6) -> 0 (boolean false), got {ucode.AluOut}");

            // Code 15: SETO
            ucode.LogiOps(15);
            Assert(ucode.AluOut == 0xFFFFFFFFu, $"SETO -> all ones, got 0x{ucode.AluOut:X}");

            Console.WriteLine("  LogiOps tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  LogiOps tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestArithOps()
    {
        Console.WriteLine("Test: ArithOps");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 22 (SUB): M - A - 1 + CIN, via Sub32 delegate. cin = Ir(2,1) — set
            // via P0 bit 2 since ArithOps reads cin from the instruction register.
            ucode.MData = 10; ucode.AData = 3;
            ucode.P0 = 1UL << 2; // cin = 1
            ucode.ArithOps(22);
            Assert(ucode.AluOut == 7, $"SUB with cin=1: 10 - 3 - 0 = 7, got {ucode.AluOut}");

            ucode.MData = 10; ucode.AData = 3;
            ucode.P0 = 0; // cin = 0
            ucode.ArithOps(22);
            Assert(ucode.AluOut == 6, $"SUB with cin=0: 10 - 3 - 1 = 6, got {ucode.AluOut}");

            // Code 25 (ADD): M + A + CIN, via Add32 delegate.
            ucode.MData = 10; ucode.AData = 3;
            ucode.P0 = 1UL << 2; // cin = 1
            ucode.ArithOps(25);
            Assert(ucode.AluOut == 14, $"ADD with cin=1: 10 + 3 + 1 = 14, got {ucode.AluOut}");

            // Code 28 ([M+1]): M + CIN, with the special all-ones-plus-carry case.
            ucode.MData = 5;
            ucode.P0 = 1UL << 2; // cin = 1
            ucode.ArithOps(28);
            Assert(ucode.AluOut == 6, $"[M+1] with cin=1: 5 + 1 = 6, got {ucode.AluOut}");
            Assert(ucode.AluCarry == 0, "no special carry case for M=5");

            ucode.MData = -1; // 0xFFFFFFFF
            ucode.P0 = 1UL << 2; // cin = 1
            ucode.ArithOps(28);
            Assert(ucode.AluOut == 0, $"[M+1] with M=0xFFFFFFFF, cin=1 wraps to 0, got 0x{ucode.AluOut:X}");
            Assert(ucode.AluCarry == 1, "special carry case: M==0xFFFFFFFF && cin");

            Console.WriteLine("  ArithOps tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ArithOps tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDivOps()
    {
        Console.WriteLine("Test: DivOps");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 32 (multiply step): Q bit 0 == 0 -> AluOut = MData, carry = sign bit.
            ucode.Q = 0; // bit 0 clear
            ucode.MData = unchecked((int)0x80000000); // sign bit set (0x80000000 is a uint literal; MData is int, needs an explicit cast)
            ucode.P0 = 0; // cin = 0 (unused on this branch)
            ucode.DivOps(32);
            Assert(ucode.AluOut == 0x80000000u, $"mult step, Q bit0=0: AluOut=MData, got 0x{ucode.AluOut:X}");
            Assert(ucode.AluCarry == 1, "mult step, Q bit0=0: carry = MData's sign bit");

            // Code 32 (multiply step): Q bit 0 == 1 -> Add32(AData, MData, cin).
            ucode.Q = 1; // bit 0 set
            ucode.AData = 5; ucode.MData = 3;
            ucode.P0 = 1UL << 2; // cin = 1
            ucode.DivOps(32);
            Assert(ucode.AluOut == 9, $"mult step, Q bit0=1: Add32(5,3,cin=1)=9, got {ucode.AluOut}");

            // Code 41 (initial divide step): unconditional Sub32(MData, Abs32(AData), !cin).
            ucode.MData = 10; ucode.AData = -3; // Abs32(-3) = 3
            ucode.P0 = 1UL << 2; // cin = 1 -> !cin = false -> Sub32(10, 3, false)
            ucode.DivOps(41);
            Assert(ucode.AluOut == 6, $"initial divide step: Sub32(10,3,ci=false)=10-3-1=6, got {ucode.AluOut}");

            Console.WriteLine("  DivOps tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  DivOps tests failed: {ex.Message}\n");
            return false;
        }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build LispMachine.sln`
Expected: FAIL — `UCode` has no members named `LogiOps`/`ArithOps`/`DivOps` yet.

- [ ] **Step 3: Implement `LogiOps`/`ArithOps`/`DivOps`**

In `usim-cs/UCode.cs`, add after the four Task 1 helpers (still before `#region ALU Operations`):

```csharp
    /// <summary>
    /// Logical ALU operations, codes 0-15 (octal 000-017).
    /// </summary>
    internal void LogiOps(uint op)
    {
        switch (op)
        {
            case 0: AluOut = 0; break;                                       // SETZ
            case 1: AluOut = (uint)(MData & AData); break;                   // AND
            case 2: AluOut = (uint)(MData & ~AData); break;                  // ANDCA
            case 3: AluOut = (uint)MData; break;                             // SETM
            case 4: AluOut = (uint)(~MData & AData); break;                  // ANDCM
            case 5: AluOut = (uint)AData; break;                             // SETA
            case 6: AluOut = (uint)(MData ^ AData); break;                   // XOR
            case 7: AluOut = (uint)(MData | AData); break;                   // IOR
            case 8: AluOut = (uint)(~AData & ~MData); break;                 // NOR
            case 9: AluOut = (AData == MData) ? 1u : 0u; break;              // EQV (boolean test)
            case 10: AluOut = (uint)~AData; break;                           // SETCA
            case 11: AluOut = (uint)(MData | ~AData); break;                // ORCA
            case 12: AluOut = (uint)~MData; break;                          // SETCM
            case 13: AluOut = (uint)(~MData | AData); break;                 // ORCM
            case 14: AluOut = (uint)(~MData | ~AData); break;                // ORCB
            case 15: AluOut = 0xFFFFFFFFu; break;                           // SETO
        }
    }

    /// <summary>
    /// Arithmetic ALU operations, codes 16-31 (octal 020-037).
    /// </summary>
    internal void ArithOps(uint op)
    {
        bool cin = Ir(2, 1) != 0;
        long lv;

        switch (op)
        {
            case 16: AluOut = cin ? 0u : uint.MaxValue; AluCarry = 0; return;
            case 17:
                lv = (long)(uint)(MData & AData) - (cin ? 0 : 1);
                break;
            case 18:
                lv = (long)(uint)(MData & ~AData) - (cin ? 0 : 1);
                break;
            case 19:
                lv = (long)(uint)MData - (cin ? 0 : 1);
                break;
            case 20:
                lv = (long)(uint)(MData | ~AData) + (cin ? 1 : 0);
                break;
            case 21:
                lv = (long)(uint)(MData | ~AData) + (uint)(MData & AData) + (cin ? 1 : 0);
                break;
            case 22:
                (AluOut, AluCarry) = Sub32(MData, AData, cin);
                return;
            case 23:
                lv = (long)(uint)(MData | ~AData) + (uint)MData + (cin ? 1 : 0);
                break;
            case 24:
                lv = (long)(uint)(MData | AData) + (cin ? 1 : 0);
                break;
            case 25:
                (AluOut, AluCarry) = Add32(MData, AData, cin);
                return;
            case 26:
                lv = (long)(uint)(MData | AData) + (uint)(MData & ~AData) + (cin ? 1 : 0);
                break;
            case 27:
                lv = (long)(uint)(MData | AData) + (uint)MData + (cin ? 1 : 0);
                break;
            case 28:
                AluOut = (uint)(MData + (cin ? 1 : 0));
                AluCarry = 0;
                if (MData == -1 && cin) AluCarry = 1;
                return;
            case 29:
                lv = (long)(uint)MData + (uint)(MData & AData) + (cin ? 1 : 0);
                break;
            case 30:
                lv = (long)(uint)MData + (uint)(MData | ~AData) + (cin ? 1 : 0);
                break;
            case 31:
                (AluOut, AluCarry) = Add32(MData, MData, cin);
                return;
            default:
                return;
        }

        AluOut = (uint)lv;
        AluCarry = (lv >> 32) != 0 ? 1u : 0u;
    }

    /// <summary>
    /// Multiply/divide-step ALU operations, codes 32, 33, 37, 41
    /// (octal 040, 041, 045, 051).
    /// </summary>
    internal void DivOps(uint op)
    {
        bool cin = Ir(2, 1) != 0;

        switch (op)
        {
            case 32: // multiply step
                if ((Q & 1) != 0)
                {
                    (AluOut, AluCarry) = Add32(AData, MData, cin);
                }
                else
                {
                    AluOut = (uint)MData;
                    AluCarry = (AluOut & 0x80000000) != 0 ? 1u : 0u;
                }
                break;
            case 33: // divide step
                if ((Q & 1) != 0)
                    (AluOut, AluCarry) = Sub32(MData, Abs32(AData), !cin);
                else
                    (AluOut, AluCarry) = Add32(MData, Abs32(AData), cin);
                break;
            case 37: // remainder correction
                if ((Q & 1) != 0)
                    AluCarry = 0;
                else
                    (AluOut, AluCarry) = Add32((int)AluOut, Abs32(AData), cin);
                break;
            case 41: // initial divide step (unconditional)
                (AluOut, AluCarry) = Sub32(MData, Abs32(AData), !cin);
                break;
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build LispMachine.sln` (expect 0 errors), then `dotnet run --project usim-cs -- --test-microcode-alu`
Expected: `Passed: 7`, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/UCode.cs usim-cs/UCodeAluTests.cs
git commit -m "Add LogiOps/ArithOps/DivOps ALU operation tables"
```

---

## Task 3: `QControl`/`OutControl`

**Files:**
- Modify: `usim-cs/UCode.cs`
- Modify: `usim-cs/UCodeAluTests.cs`

**Interfaces:**
- Consumes: `AluOut`/`AluCarry`/`Q`/`OldQ`/`MData` (Phase 1's register model and Task 2's ALU tables), `Rol32` (Task 1), `Ir()` (Phase 1).
- Produces: `internal void QControl()`, `internal void OutControl()` — Task 4's `Alu()` calls these with no arguments, in that order, after the opcode-table dispatch.

- [ ] **Step 1: Write the failing tests**

In `usim-cs/UCodeAluTests.cs`, add to `RunAllTests()`:
```csharp
        if (TestQControl()) passed++; else failed++;
        if (TestOutControl()) passed++; else failed++;
```

Add the two test methods:
```csharp
    private static bool TestQControl()
    {
        Console.WriteLine("Test: QControl");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 0: no-op (Ir(0,2) reads P0 bits 0-1)
            ucode.P0 = 0; // code 0
            ucode.Q = 0x12345678;
            ucode.QControl();
            Assert(ucode.Q == 0x12345678u, $"QControl code 0 is a no-op, got 0x{ucode.Q:X}");

            // Code 1: Q <<= 1; shift in the INVERSE of AluOut's sign bit.
            ucode.P0 = 1; // code 1
            ucode.Q = 0x00000001;
            ucode.AluOut = 0x00000000; // sign bit clear -> shift in a 1
            ucode.QControl();
            Assert(ucode.Q == 0x00000003u, $"QControl code 1 shift-left, sign clear shifts in 1, got 0x{ucode.Q:X}");

            ucode.P0 = 1;
            ucode.Q = 0x00000001;
            ucode.AluOut = 0x80000000; // sign bit set -> shift in a 0
            ucode.QControl();
            Assert(ucode.Q == 0x00000002u, $"QControl code 1 shift-left, sign set shifts in 0, got 0x{ucode.Q:X}");

            // Code 2: Q >>= 1; shift in AluOut's bit 0.
            ucode.P0 = 2; // code 2
            ucode.Q = 0x00000002;
            ucode.AluOut = 0x00000001; // bit 0 set -> shift in a 1 at bit 31
            ucode.QControl();
            Assert(ucode.Q == 0x80000001u, $"QControl code 2 shift-right, bit0 set shifts in top bit, got 0x{ucode.Q:X}");

            // Code 3: Q = AluOut.
            ucode.P0 = 3; // code 3
            ucode.AluOut = 0xDEADBEEF;
            ucode.QControl();
            Assert(ucode.Q == 0xDEADBEEFu, $"QControl code 3 loads AluOut, got 0x{ucode.Q:X}");

            Console.WriteLine("  QControl tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  QControl tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestOutControl()
    {
        Console.WriteLine("Test: OutControl");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 0 (bits 12-13 of P0): rotate MData by low 5 bits of P0.
            ucode.P0 = 0; // code 0, rotate amount 0
            ucode.MData = 0x12345678;
            ucode.OutControl();
            Assert(ucode.Out == 0x12345678u, $"OutControl code 0, rotate 0, got 0x{ucode.Out:X}");

            // Code 1: passthrough.
            ucode.P0 = 1UL << 12; // code 1
            ucode.AluOut = 0xABCDEF01;
            ucode.OutControl();
            Assert(ucode.Out == 0xABCDEF01u, $"OutControl code 1 passthrough, got 0x{ucode.Out:X}");

            // Code 2: AluOut >> 1, with AluCarry shifted into bit 31.
            ucode.P0 = 2UL << 12; // code 2
            ucode.AluOut = 0x00000002;
            ucode.AluCarry = 1;
            ucode.OutControl();
            Assert(ucode.Out == 0x80000001u, $"OutControl code 2, got 0x{ucode.Out:X}");

            // Code 3: AluOut << 1, with OldQ's sign bit shifted into bit 0.
            ucode.P0 = 3UL << 12; // code 3
            ucode.AluOut = 0x00000001;
            ucode.OldQ = 0x80000000; // sign bit set -> shift in a 1
            ucode.OutControl();
            Assert(ucode.Out == 0x00000003u, $"OutControl code 3, got 0x{ucode.Out:X}");

            Console.WriteLine("  OutControl tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  OutControl tests failed: {ex.Message}\n");
            return false;
        }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build LispMachine.sln`
Expected: FAIL — `UCode` has no members named `QControl`/`OutControl` yet.

- [ ] **Step 3: Implement `QControl`/`OutControl`**

In `usim-cs/UCode.cs`, add after `DivOps`:

```csharp
    /// <summary>
    /// Q-register shift/load control, dispatched on Ir(0,2).
    /// </summary>
    internal void QControl()
    {
        OldQ = Q;
        switch (Ir(0, 2))
        {
            case 1:
                Q <<= 1;
                if ((AluOut & 0x80000000) == 0) Q |= 1;
                break;
            case 2:
                Q >>= 1;
                if ((AluOut & 1) != 0) Q |= 0x80000000;
                break;
            case 3:
                Q = AluOut;
                break;
        }
    }

    /// <summary>
    /// ALU-output routing/shift control, dispatched on bits 12-13 of P0 (raw,
    /// not via Ir() — matches the C source's direct bit extraction).
    /// </summary>
    internal void OutControl()
    {
        switch ((P0 >> 12) & 3)
        {
            case 0:
                TraceLog.Instance.Warning(TraceCategory.MicroCode, "OutControl: out == 0!");
                Out = Rol32((uint)MData, (int)(P0 & 0x1F));
                break;
            case 1:
                Out = AluOut;
                break;
            case 2:
                Out = (AluOut >> 1) | (AluCarry != 0 ? 0x80000000u : 0);
                break;
            case 3:
                Out = (AluOut << 1) | ((OldQ & 0x80000000) != 0 ? 1u : 0);
                break;
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build LispMachine.sln` (expect 0 errors), then `dotnet run --project usim-cs -- --test-microcode-alu`
Expected: `Passed: 9`, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/UCode.cs usim-cs/UCodeAluTests.cs
git commit -m "Add QControl/OutControl ALU output routing"
```

---

## Task 4: `WriteDest` and wiring `Alu()`

**Files:**
- Modify: `usim-cs/UCode.cs`
- Modify: `usim-cs/UCodeAluTests.cs`

**Interfaces:**
- Consumes: `LogiOps`/`ArithOps`/`DivOps` (Task 2), `QControl`/`OutControl` (Task 3), `MfWrite` (does NOT exist yet — Phase 4's job; `WriteDest` calls it, so for this task, add a temporary `internal int MfWrite(uint dest, int data) { throw new NotImplementedException("MfWrite is implemented in Phase 4"); }` stub matching Phase 4's eventual signature, exactly like Phase 1 stubbed `Alu`/`Jmp`/`Dsp`/`Byt`/`MfRead` for later phases).
- Produces: `internal void WriteDest(uint dest)` — Phase 7's `Byt()` calls this exact signature too. `Alu()` itself becomes fully implemented (no longer throws) — `Step()` (Phase 1) already calls it correctly with no changes needed there.

**A note on `WriteDest`'s dependency on `MfWrite`:** the spec's `WriteDest` (see the design doc's Phase 4 section, "`WriteDest(dest)` (used by `Alu()`/`Byt()`...)") calls `MfWrite(dest, (int)Out)` for the non-A-memory-write path. Since `MfWrite` is real Phase 4 work, this task adds a stub for it — meaning any ALU instruction whose destination is NOT plain A-memory (i.e. `dest & 0x800 == 0`) will throw `NotImplementedException` when tested end-to-end via `Step()`. This task's own tests route all writes to A-memory (`dest` with bit 11 set) specifically to avoid hitting the `MfWrite` stub — this is the correct, expected boundary for this phase, not a workaround to hide from.

- [ ] **Step 1: Write the failing tests**

In `usim-cs/UCodeAluTests.cs`, add to `RunAllTests()`:
```csharp
        if (TestWriteDest()) passed++; else failed++;
        if (TestAluEndToEnd()) passed++; else failed++;
```

Add the two test methods:
```csharp
    private static bool TestWriteDest()
    {
        Console.WriteLine("Test: WriteDest");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // dest with bit 11 set (0x800) -> plain A-memory write, low 10 bits are the index.
            ucode.Out = 0xCAFEBABE;
            ucode.WriteDest(0x800 | 0x123);
            Assert(ucode.AMem[0x123] == 0xCAFEBABEu, $"A-memory write at index 0x123, got 0x{ucode.AMem[0x123]:X}");

            // dest without bit 11 -> goes through MfWrite (stubbed) AND still updates
            // the low-5-bit-addressed MMem/AMem shadow copies per the spec.
            bool threw = false;
            try
            {
                ucode.Out = 0x11111111;
                ucode.WriteDest(0x05); // dest & 037 == 5, dest & 0x800 == 0
            }
            catch (NotImplementedException)
            {
                threw = true;
            }
            Assert(threw, "non-A-memory dest routes through the still-stubbed MfWrite and throws");

            Console.WriteLine("  WriteDest tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  WriteDest tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestAluEndToEnd()
    {
        Console.WriteLine("Test: Alu() end-to-end via Step()");
        try
        {
            var ucode = new UCode();
            ucode.Init();
            ucode.PromEnabledFlag = true;

            // Build one ALU instruction word by hand:
            //   Op (bits 43-44) = 0 (ALU)
            //   AAddr (bits 32-41) = 0x010 (A-memory source address)
            //   msource (bit 31) = 0 (plain MMem read)
            //   MAddr (bits 26-30) = 0x03 (M-memory source address)
            //   dest (bits 14-25) = 0x800 | 0x020 (A-memory write, index 0x020)
            //   aluop (bits 3-8) = 1 (AND)
            //   qcontrol (bits 0-1) = 0 (no-op)
            //   outcontrol (bits 12-13) = 1 (passthrough)
            ulong dest = 0x800 | 0x020;
            ulong word = ((ulong)0 << 43) | ((ulong)0x010 << 32) | ((ulong)0x03 << 26)
                       | (dest << 14) | ((ulong)1 << 3) | ((ulong)1 << 12);
            ucode.Prom[0] = word;
            ucode.AMem[0x010] = 0xF0F0F0F0;
            ucode.MMem[0x03] = 0x0FF00FF0;

            ucode.Npc = 0;
            ucode.Step(); // prefetch into P1
            ucode.Step(); // promote to P0, decode, execute ALU, write dest

            uint expected = 0xF0F0F0F0u & 0x0FF00FF0u;
            Assert(ucode.AluOut == expected, $"AluOut = AData & MData = 0x{expected:X}, got 0x{ucode.AluOut:X}");
            Assert(ucode.Out == expected, $"Out (outcontrol=1, passthrough) = 0x{expected:X}, got 0x{ucode.Out:X}");
            Assert(ucode.AMem[0x020] == expected, $"WriteDest wrote Out to AMem[0x020], got 0x{ucode.AMem[0x020]:X}");

            Console.WriteLine("  Alu() end-to-end tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Alu() end-to-end tests failed: {ex.Message}\n");
            return false;
        }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build LispMachine.sln`
Expected: FAIL — `UCode.WriteDest` doesn't exist, and `Alu()` still throws `NotImplementedException` unconditionally.

- [ ] **Step 3: Add the temporary `MfWrite` stub, `WriteDest`, and wire `Alu()`**

In `usim-cs/UCode.cs`, add the `MfWrite` stub right next to the existing `MfRead` stub (they'll both be replaced together in Phase 4):
```csharp
    internal int MfWrite(uint dest, int data)
    {
        throw new NotImplementedException("MfWrite is implemented in Phase 4 (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md)");
    }
```

Add `WriteDest` after `OutControl`:
```csharp
    /// <summary>
    /// Route the ALU/byte-instruction result to its destination: plain
    /// A-memory (if dest bit 11 is set) or a functional register via
    /// MfWrite, plus the low-5-bit-addressed MMem/AMem shadow copies.
    /// </summary>
    internal void WriteDest(uint dest)
    {
        if ((dest & 0x800) != 0)
        {
            AMem[dest & 0x3FF] = Out;
        }
        else
        {
            MfWrite(dest, (int)Out);
            MMem[dest & 0x1F] = AMem[dest & 0x1F] = Out;
        }
    }
```

Replace the `Alu()` stub:
```csharp
    private void Alu()
    {
        throw new NotImplementedException("Alu is implemented in Phase 2 (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md)");
    }
```
with:
```csharp
    private void Alu()
    {
        uint dest = (uint)Ir(14, 12);
        uint aluop = (uint)Ir(3, 6);
        AluCarry = 0;

        if (aluop <= 15) LogiOps(aluop);
        else if (aluop >= 16 && aluop <= 31) ArithOps(aluop);
        else if (aluop == 32 || aluop == 33 || aluop == 37 || aluop == 41) DivOps(aluop);

        QControl();
        OutControl();
        WriteDest(dest);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build LispMachine.sln` (expect 0 errors), then `dotnet run --project usim-cs -- --test-microcode-alu`
Expected: `Passed: 11`, `Failed: 0`.

- [ ] **Step 5: Run the full regression suite**

Run: `dotnet run --project usim-cs -- --test-all`
Expected: all suites (`UCodeTests` stub, `UCodeFetchDecodeTests`, `UCodeAluTests`, `ConfigTests`, `WpfBackendTests`) report 0 failures. Note `UCodeFetchDecodeTests`'s `TestCommonFieldDecode` test (from Phase 1) builds a JUMP-class instruction and expects `Step()`'s second call to throw `NotImplementedException` from the still-stubbed `Jmp()` — that's unaffected by this phase's changes (`Jmp()` isn't touched), so it should still pass unchanged.

- [ ] **Step 6: Commit**

```bash
git add usim-cs/UCode.cs usim-cs/UCodeAluTests.cs
git commit -m "Add WriteDest and wire Alu() into a fully faithful ALU instruction class"
```

---

## Self-Review Notes

- **Spec coverage:** Every table in the spec's "Phase 2 — ALU Instructions" section is implemented: `LogiOps` (16 codes), `ArithOps` (16 codes), `DivOps` (4 codes), `QControl` (4 modes), `OutControl` (4 modes), the four support helpers, and `WriteDest`. `Alu()` itself now matches the spec's dispatch logic exactly (zero `AluCarry` first, dispatch by code range, then `QControl()`/`OutControl()`/`WriteDest(dest)` in that order).
- **Placeholder scan:** The only `NotImplementedException` left after this phase is `MfWrite` (and the untouched `Jmp`/`Dsp`/`Byt`/`MfRead` from Phase 1) — each explicitly named and phase-tagged, not vague. `WriteDest`'s test explicitly exercises and asserts on this exact boundary (a non-A-memory destination throws) rather than avoiding it silently.
- **Type consistency:** `Add32`/`Sub32` return `(uint Out, uint Carry)` tuples consistently used the same way in `ArithOps`/`DivOps`; `LogiOps`/`ArithOps`/`DivOps` all take `uint op` and return `void` (writing to `AluOut`/`AluCarry` as instance state, matching `Alu()`'s call sites); `WriteDest(uint dest)` and `MfWrite(uint dest, int data)` signatures match the spec's `WriteDest`/`MfWrite` calls exactly, including `Byt()`'s future dependency on this exact `WriteDest` signature (Phase 7).
- **Deliberate deviation flagged:** `internal` instead of `private` for the nine helper/table methods, recorded in Global Constraints with the reasoning (same-assembly test files, no `InternalsVisibleTo` needed, enables focused per-table testing instead of only end-to-end `Step()` tests). Does not change behavior or violate the spec's intent — the spec's own stated goal is fidelity to `uexec.c`'s semantics, not C#'s access-modifier choices.
