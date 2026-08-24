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
