// DisassemblerTests.cs - Tests for the real disassembler (Phase 8 of the
// microcode engine port). Replaces the placeholder-only prior behavior.

using System;

namespace Usim;

public static class DisassemblerTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== Disassembler Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestAluInstructionNamesLogicOp()) passed++; else failed++;
        if (TestJumpInstructionDecodesTargetAndFlags()) passed++; else failed++;
        if (TestDispatchInstructionDecodesFieldsAndDefMicsLookup()) passed++; else failed++;
        if (TestByteInstructionNamesMrSrBits()) passed++; else failed++;
        if (TestDefMicsLookupKnownAndUnknownValues()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestAluInstructionNamesLogicOp()
    {
        Console.WriteLine("Test: ALU instruction (Op=0) with aluop=6 disassembles as XOR (LogiOps real name)");
        try
        {
            // Op(Ir 43,2)=0, aluop(Ir 3,6)=6 (XOR, per UCode.LogiOps case 6).
            ulong instruction = (6UL << 3);
            string result = Disassembler.DisassembleInst2(instruction, false);

            Assert(result.Contains("ALU"), $"result mentions ALU class: {result}");
            Assert(result.Contains("XOR"), $"result names the real op XOR, not an invented AluOp enum name: {result}");

            Console.WriteLine("  ALU logic-op naming test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ALU logic-op naming test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestJumpInstructionDecodesTargetAndFlags()
    {
        Console.WriteLine("Test: JUMP instruction (Op=1) decodes target and p/r/n flags");
        try
        {
            // Op(Ir 43,2)=1, target(Ir 12,14)=0x123, p(Ir 8,1)=1, r(Ir 9,1)=0, n(Ir 7,1)=0.
            ulong instruction = (1UL << 43) | (0x123UL << 12) | (1UL << 8);
            string result = Disassembler.DisassembleInst2(instruction, false);

            Assert(result.Contains("JUMP"), $"result mentions JUMP class: {result}");
            Assert(result.Contains("123"), $"result includes the target address 0x123: {result}");
            // "flags=P" (not a bare "P" or "PUSH" substring check, which
            // "JUMP" itself would satisfy trivially) -- confirms only the
            // p-flag is set, not r/n/invertSense.
            Assert(result.Contains("flags=P "), $"result reflects exactly the p-flag being set, not r/n/invertSense: {result}");

            Console.WriteLine("  JUMP field-decode test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  JUMP field-decode test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDispatchInstructionDecodesFieldsAndDefMicsLookup()
    {
        Console.WriteLine("Test: DISPATCH instruction (Op=2) decodes dispAddr/map and looks up disp_const in DefMics");
        try
        {
            // Op(Ir 43,2)=2, dispAddr(Ir 12,11)=7, disp_const(Ir 32,10)=162
            // (0x92 << 32 -- 162 decimal is (CAR . M-CAR) in the real
            // defmics[] table, per Step 1's spot-check).
            ulong instruction = (2UL << 43) | (7UL << 12) | (162UL << 32);
            string result = Disassembler.DisassembleInst2(instruction, false);

            Assert(result.Contains("DISPATCH"), $"result mentions DISPATCH class: {result}");
            Assert(result.Contains("(CAR . M-CAR)"), $"result looks up disp_const=162 in DefMics and finds the real name: {result}");

            Console.WriteLine("  DISPATCH field-decode and DefMics-lookup test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  DISPATCH field-decode and DefMics-lookup test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestByteInstructionNamesMrSrBits()
    {
        Console.WriteLine("Test: BYTE instruction (Op=3) names mrSrBits=3 as DPB");
        try
        {
            // Op(Ir 43,2)=3, mrSrBits(Ir 12,2)=3 (DPB).
            ulong instruction = (3UL << 43) | (3UL << 12);
            string result = Disassembler.DisassembleInst2(instruction, false);

            Assert(result.Contains("BYTE"), $"result mentions BYTE class: {result}");
            Assert(result.Contains("DPB"), $"result names mrSrBits=3 as DPB: {result}");

            Console.WriteLine("  BYTE mrSrBits-naming test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  BYTE mrSrBits-naming test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDefMicsLookupKnownAndUnknownValues()
    {
        Console.WriteLine("Test: DefMics.Lookup returns the real name for a known value and null for an unmapped one");
        try
        {
            Assert(DefMics.Lookup(162) == "(CAR . M-CAR)", $"DefMics.Lookup(162) is (CAR . M-CAR), got {DefMics.Lookup(162)}");
            Assert(DefMics.Lookup(163) == "(CDR . M-CDR)", $"DefMics.Lookup(163) is (CDR . M-CDR), got {DefMics.Lookup(163)}");
            Assert(DefMics.Lookup(999999) == null, "an unmapped value returns null, not a fabricated name");

            Console.WriteLine("  DefMics lookup test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  DefMics lookup test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
