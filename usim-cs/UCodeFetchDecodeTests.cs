// UCodeFetchDecodeTests.cs - Tests for UCode's fetch/decode/pipeline foundation
// (Phase 1 of the microcode engine port). Phase 9 expands this into the full
// microcode test suite once Phases 2-7 land real instruction semantics.

using System;
using System.IO;

namespace Usim;

public static class UCodeFetchDecodeTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== UCode Fetch/Decode Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestPipelineAdvance()) passed++; else failed++;
        if (TestNpcWraparound()) passed++; else failed++;
        if (TestCommonFieldDecode()) passed++; else failed++;
        if (TestPromDecodeSanity()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestPipelineAdvance()
    {
        Console.WriteLine("Test: Pipeline Advance (IncNpc via two Step() calls)");
        try
        {
            var ucode = new UCode();
            ucode.Init();
            ucode.PromEnabledFlag = true;
            ucode.Prom[0] = 0x1111_2222_3333UL;
            ucode.Prom[1] = 0x4444_5555_6666UL;
            ucode.Npc = 0;

            // First Step(): IncNpc moves the (empty) P1 into P0, then
            // prefetches Prom[0] into P1 for next time; Npc advances to 1.
            // Step() always decodes+dispatches P0 after IncNpc (even on this
            // priming cycle, where P0 is still the initial empty value,
            // decoding as Op=0/ALU) — and since Alu/Jmp/Dsp/Byt are all
            // still NotImplementedException stubs in this phase, every
            // Step() call throws. IncNpc's pipeline update happens before
            // the dispatch, so the exception is swallowed to check it.
            try { ucode.Step(); } catch (NotImplementedException) { /* expected: dispatch stub */ }
            Assert(ucode.P0 == 0, "first Step(): P0 is still the initial (empty) P1");
            Assert(ucode.P1 == ucode.Prom[0], "first Step(): P1 prefetched Prom[0]");
            Assert(ucode.Npc == 1, "first Step(): Npc advanced to 1");

            // Second Step(): P1 (Prom[0]) becomes P0; P1 prefetches Prom[1].
            try { ucode.Step(); } catch (NotImplementedException) { /* expected: dispatch stub */ }
            Assert(ucode.P0 == ucode.Prom[0], "second Step(): P0 is now Prom[0]");
            Assert(ucode.P1 == ucode.Prom[1], "second Step(): P1 prefetched Prom[1]");
            Assert(ucode.Npc == 2, "second Step(): Npc advanced to 2");

            Console.WriteLine("  Pipeline Advance tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Pipeline Advance tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestNpcWraparound()
    {
        Console.WriteLine("Test: Npc 14-bit Wraparound");
        try
        {
            var ucode = new UCode();
            ucode.Init();
            // Deliberately IMem-backed (PromEnabledFlag stays false, its
            // Init() default), not PROM-backed: IMem is sized IMEM_SIZE
            // (0x4000), matching Npc's full 14-bit range, whereas Prom is
            // only PROM_SIZE (512) words — a real hardware PROM boot image
            // never runs code that jumps past its own small size, but this
            // test deliberately drives Npc to the top of its range purely
            // to check the register's wraparound arithmetic, so it needs
            // the correctly-sized backing store to do that safely.
            ucode.Npc = 0x3FFF;

            // Step() always dispatches after IncNpc (see TestPipelineAdvance);
            // Alu/Jmp/Dsp/Byt are still stubs in this phase, so it throws —
            // swallow it, IncNpc's Npc update already happened first.
            try { ucode.Step(); } catch (NotImplementedException) { /* expected: dispatch stub */ }
            Assert(ucode.Npc == 0, "Npc wraps from 0x3FFF to 0");

            Console.WriteLine("  Npc Wraparound tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Npc Wraparound tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestCommonFieldDecode()
    {
        Console.WriteLine("Test: Common Field Decode (Op/AAddr/MAddr)");
        try
        {
            var ucode = new UCode();
            ucode.Init();
            ucode.PromEnabledFlag = true;

            // Build one instruction word by hand:
            //   Op (bits 43-44) = 1 (JUMP)
            //   AAddr (bits 32-41) = 0x2AA
            //   MAddr (bits 26-30) = 0x15
            // msource (bit 31) = 0, so MData comes from MMem[MAddr], not MfRead.
            ulong word = ((ulong)1 << 43) | ((ulong)0x2AA << 32) | ((ulong)0x15 << 26);
            ucode.Prom[0] = word;
            ucode.MMem[0x15] = 0xABCDEF;
            ucode.AMem[0x2AA] = 0x123456;

            // Two Step() calls needed: first prefetches Prom[0] into P1
            // (decoding+dispatching on the still-empty P0, i.e. Op=0/ALU);
            // second promotes it to P0 and actually decodes our word (Op=1/
            // JUMP). Both calls dispatch through a stub (Alu()/Jmp()) that
            // throws NotImplementedException — that's fine, the decode
            // happens before the stub throws in each case, so both are
            // wrapped to check the decoded fields afterward.
            ucode.Npc = 0;
            try { ucode.Step(); } catch (NotImplementedException) { /* expected: Alu() stub */ }
            try { ucode.Step(); } catch (NotImplementedException) { /* expected: Jmp() stub */ }

            Assert(ucode.Op == 1, $"Op decoded as 1 (JUMP), got {ucode.Op}");
            Assert(ucode.AAddr == 0x2AA, $"AAddr decoded as 0x2AA, got 0x{ucode.AAddr:X}");
            Assert(ucode.MAddr == 0x15, $"MAddr decoded as 0x15, got 0x{ucode.MAddr:X}");
            Assert(ucode.AData == 0x123456, $"AData read from AMem[AAddr], got 0x{ucode.AData:X}");
            Assert(ucode.MData == 0xABCDEF, $"MData read from MMem[MAddr] (msource=0), got 0x{ucode.MData:X}");

            Console.WriteLine("  Common Field Decode tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Common Field Decode tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestPromDecodeSanity()
    {
        Console.WriteLine("Test: Real promh.mcr Decode Sanity");
        try
        {
            string path = Path.Combine("sys", "ubin", "promh.mcr");
            if (!File.Exists(path))
            {
                Console.WriteLine("  SKIPPED (sys/ubin/promh.mcr not found at expected path)\n");
                return true;
            }

            var ucode = new UCode();
            ucode.Init();
            ucode.LoadPromFromFile(path);

            int[] opCounts = new int[4];
            int sampleSize = Math.Min(200, UCode.PROM_SIZE);
            for (int i = 0; i < sampleSize; i++)
            {
                ulong word = ucode.Prom[i];
                uint op = (uint)((word >> 43) & 0x3);
                opCounts[op]++;
            }

            int nonZeroClasses = 0;
            foreach (int c in opCounts) if (c > 0) nonZeroClasses++;

            Assert(nonZeroClasses >= 2,
                $"real microcode uses at least 2 distinct opcode classes across the first {sampleSize} words " +
                $"(ALU={opCounts[0]}, JUMP={opCounts[1]}, DISPATCH={opCounts[2]}, BYTE={opCounts[3]}) " +
                "— a degenerate single-class distribution would suggest the decode's bit positions are wrong");

            Console.WriteLine($"  Opcode class distribution: ALU={opCounts[0]} JUMP={opCounts[1]} DISPATCH={opCounts[2]} BYTE={opCounts[3]}");
            Console.WriteLine("  Real promh.mcr Decode Sanity tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Real promh.mcr Decode Sanity tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
