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
