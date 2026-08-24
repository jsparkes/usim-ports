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
        if (TestLogiOps()) passed++; else failed++;
        if (TestArithOps()) passed++; else failed++;
        if (TestDivOps()) passed++; else failed++;
        if (TestQControl()) passed++; else failed++;
        if (TestOutControl()) passed++; else failed++;

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
            Assert(carry1 == 0, $"carry=0: signed comparison 3 > ~5(=-6) is true, got {carry1}");

            var (out2, carry2) = UCode.Add32(5, 3, true);
            Assert(out2 == 9, $"5 + 3 + 1 = 9, got {out2}");
            Assert(carry2 == 0, $"carry=0: signed comparison 3 >= ~5(=-6) is true, got {carry2}");

            // unsigned overflow: 0xFFFFFFFF + 1 + 0 wraps to 0
            var (out3, carry3) = UCode.Add32(-1, 1, false);
            Assert(out3 == 0, $"0xFFFFFFFF + 1 wraps to 0, got 0x{out3:X}");
            Assert(carry3 == 0, $"carry=0: signed comparison 1 > ~(-1)(=0) is true, got {carry3}");

            // a genuine carry=1 case: b <= ~a under signed comparison (negative b, small a)
            var (out4, carry4) = UCode.Add32(10, -20, false);
            Assert(out4 == unchecked((uint)-10), $"10 + (-20) = -10 (as uint 0xFFFFFFF6), got 0x{out4:X}");
            Assert(carry4 == 1, $"carry=1: signed comparison -20 > ~10(=-11) is FALSE, got {carry4}");

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
            Assert(carry1 == 1, $"carry=1 per m32.h macro (inverted from naive intuition), got {carry1}");

            var (out2, carry2) = UCode.Sub32(5, 3, false);
            Assert(out2 == 1, $"5 - 3 - 1 = 1, got {out2}");
            Assert(carry2 == 1, $"carry=1 per m32.h macro (inverted from naive intuition), got {carry2}");

            // 3 - 5 - 0 underflows (carry=0 per inverted m32.h logic)
            var (out3, carry3) = UCode.Sub32(3, 5, true);
            Assert(out3 == unchecked((uint)-2), $"3 - 5 = -2 (as uint 0xFFFFFFFE), got 0x{out3:X}");
            Assert(carry3 == 0, $"carry=0 per m32.h macro (inverted from naive intuition), got {carry3}");

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

            // Code 17: (M AND A) - 1 + CIN, lv-based — negative operand exercises
            // the sign-extension path (regression coverage for a zero- vs
            // sign-extension bug found in review).
            ucode.MData = -1; ucode.AData = -1; // M&A = -1 (all bits set)
            ucode.P0 = 1UL << 2; // cin = 1
            ucode.ArithOps(17);
            Assert(ucode.AluOut == unchecked((uint)-1), $"code17 M=A=-1,cin=1: (-1)-0=-1, got 0x{ucode.AluOut:X}");
            Assert(ucode.AluCarry == 1, $"code17 M=A=-1,cin=1: lv=-1 sign-extended, lv>>32 != 0, got {ucode.AluCarry}");

            // Code 19: M - 1 + CIN, lv-based.
            ucode.MData = -1;
            ucode.P0 = 0; // cin = 0
            ucode.ArithOps(19);
            Assert(ucode.AluOut == unchecked((uint)-2), $"code19 M=-1,cin=0: (-1)-1=-2, got 0x{ucode.AluOut:X}");
            Assert(ucode.AluCarry == 1, $"code19 M=-1,cin=0: lv=-2 sign-extended, lv>>32 != 0, got {ucode.AluCarry}");

            // Code 24: (M OR A) + CIN, lv-based, non-negative case gives carry=0
            // (confirms the fix doesn't spuriously set carry for ordinary values).
            ucode.MData = 5; ucode.AData = 3;
            ucode.P0 = 0; // cin = 0
            ucode.ArithOps(24);
            Assert(ucode.AluOut == 7, $"code24 M=5,A=3,cin=0: (5|3)+0=7, got {ucode.AluOut}");
            Assert(ucode.AluCarry == 0, $"code24 M=5,A=3,cin=0: lv=7, no sign extension needed, got {ucode.AluCarry}");

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

            // Code 37 (remainder correction), Q bit0 == 0 branch: replicates the C
            // macro's self-aliasing quirk (add32's out/a alias to alu_out at this
            // call site — the carry line re-reads the just-written alu_out).
            ucode.Q = 0; // bit 0 clear -> take the add32-aliasing branch
            ucode.AluOut = 5; // pre-call alu_out, used as the 'a' operand for the sum
            ucode.AData = 3; // Abs32(3) = 3
            ucode.P0 = 0; // cin = 0
            ucode.DivOps(37);
            Assert(ucode.AluOut == 8, $"code37: newOut = 5+3+0 = 8, got {ucode.AluOut}");
            // carry re-reads the NEW AluOut (8), not the pre-call value (5):
            // signed comparison bArg(3) > ~8(=-9) is true -> carry=0
            Assert(ucode.AluCarry == 0, $"code37: carry recomputed from post-write AluOut=8, got {ucode.AluCarry}");

            // Code 37, Q bit0 == 1 branch: unconditional carry=0, AluOut untouched.
            ucode.Q = 1;
            ucode.AluOut = 42;
            ucode.DivOps(37);
            Assert(ucode.AluOut == 42, $"code37 Q bit0=1: AluOut untouched, got {ucode.AluOut}");
            Assert(ucode.AluCarry == 0, "code37 Q bit0=1: carry always 0");

            Console.WriteLine("  DivOps tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  DivOps tests failed: {ex.Message}\n");
            return false;
        }
    }

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

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
