// UCodeFetchDecodeTests.cs - Tests for UCode's fetch/decode/pipeline foundation
// (Phase 1 of the microcode engine port). Phase 9 expands this into the full
// microcode test suite once Phases 2-7 land real instruction semantics.

using System;
using System.IO;

namespace Usim;

public static class UCodeFetchDecodeTests
{
    /// <summary>
    /// Tri-state test outcome, distinguishing an explicit skip (e.g. a
    /// required data file not being present) from a real pass or failure,
    /// so a skip never silently counts as a pass in the summary.
    /// </summary>
    private enum TestOutcome { Passed, Failed, Skipped }

    public static void RunAllTests()
    {
        Console.WriteLine("=== UCode Fetch/Decode Test Suite ===\n");

        int passed = 0;
        int failed = 0;
        int skipped = 0;

        if (TestPipelineAdvance()) passed++; else failed++;
        if (TestNpcWraparound()) passed++; else failed++;
        if (TestCommonFieldDecode()) passed++; else failed++;

        switch (TestPromDecodeSanity())
        {
            case TestOutcome.Passed: passed++; break;
            case TestOutcome.Failed: failed++; break;
            case TestOutcome.Skipped: skipped++; break;
        }

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Skipped: {skipped}");
        Console.WriteLine($"Total:  {passed + failed + skipped}");
    }

    private static bool TestPipelineAdvance()
    {
        Console.WriteLine("Test: Pipeline Advance (IncNpc via two Step() calls)");
        try
        {
            var ucode = new UCode();
            ucode.Init();
            ucode.PromEnabledFlag = true;
            // Op field (bits 43-44) is forced to 3 (Byte instructions) in both
            // words — Byt() is the one instruction class still stubbed as of
            // Phase 6 (Alu()/Jmp()/Dsp() are all real now), so Step()'s
            // dispatch reliably throws NotImplementedException regardless of
            // the rest of these otherwise-arbitrary bit patterns. Phase 7
            // (Byte instructions) will need to revisit this test again once
            // Byt() stops throwing, the same way Phase 6 just did.
            ucode.Prom[0] = 0x1911_2222_3333UL;
            // Also has popj (bit 42) set: harmless here since Prom[1] is only ever
            // prefetched into P1 in this test and never itself promoted into P0/
            // dispatched -- flag as a latent trap only if a third Step() call is
            // ever added to this test.
            ucode.Prom[1] = 0x5c44_5555_6666UL;
            ucode.Npc = 0;

            // First Step(): IncNpc moves the (empty) P1 into P0, then
            // prefetches Prom[0] into P1 for next time; Npc advances to 1.
            // Step() always decodes+dispatches P0 after IncNpc (even on this
            // priming cycle, where P0 is still the initial empty value,
            // decoding as Op=0/ALU). Alu() is fully implemented (Phase 2) and
            // never throws, so this first call's dispatch completes silently.
            // IncNpc's pipeline update happens before the dispatch either way.
            try { ucode.Step(); } catch (NotImplementedException) { /* expected: dispatch stub */ }
            Assert(ucode.P0 == 0, "first Step(): P0 is still the initial (empty) P1");
            Assert(ucode.P1 == ucode.Prom[0], "first Step(): P1 prefetched Prom[0]");
            Assert(ucode.Npc == 1, "first Step(): Npc advanced to 1");

            // Second Step(): P1 (Prom[0]) becomes P0; P1 prefetches Prom[1].
            // P0 is now Prom[0], which decodes as Op=3 (Byte instructions) --
            // still stubbed today, so this currently throws
            // NotImplementedException from Byt() itself. What actually keeps
            // this assertion valid isn't the throw: it's that Byt() (stubbed
            // or, later, real) never touches Npc or Popj. Decoding this
            // exact Prom[0] value's other fields shows a real Byt() would
            // route to a WriteDest on an A-memory address, which doesn't
            // touch Npc/Popj either -- so this test is expected to keep
            // passing unchanged once Phase 7 lands, with the catch simply
            // going dead rather than needing another revisit.
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
            // Deliberately IMem-backed: explicitly set PromDisabled so
            // IncNpc() fetches from IMem rather than Prom. IMem is sized
            // IMEM_SIZE (0x4000), matching Npc's full 14-bit range, whereas
            // Prom is only PROM_SIZE (512) words — a real hardware PROM boot
            // image never runs code that jumps past its own small size, but
            // this test deliberately drives Npc to the top of its range
            // purely to check the register's wraparound arithmetic, so it
            // needs the correctly-sized backing store to do that safely.
            // (Since the final review's fix wave, PromDisabled — not
            // PromEnabledFlag — controls which store IncNpc() fetches from,
            // and it defaults to false/PROM-mapped to match real hardware.)
            ucode.PromDisabled = true;
            ucode.Npc = 0x3FFF;

            // Step() always dispatches after IncNpc (see TestPipelineAdvance).
            // As there, this drives an all-zero P0 through the now-real
            // Alu() -> LogiOps(0) (SETZ) -> WriteDest, which still hits the
            // stubbed MfWrite (Phase 4) and throws — swallow it, IncNpc's
            // Npc update already happened first.
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
            // JUMP). Alu() and Jmp() are now fully implemented (Phases 2
            // and 3): the first call's all-zero P0 still throws
            // NotImplementedException (SETZ's WriteDest reaches the
            // still-stubbed MfWrite, Phase 4's job), same as
            // TestPipelineAdvance/TestNpcWraparound above. The second
            // call's word decodes as a real jump (with all of Jmp()'s own
            // Ir() fields reading as 0, per this word's bit layout) that
            // does NOT throw — it's a real branch taken via the fully-
            // implemented Jmp(), so this try/catch is harmless but no
            // longer expected to catch anything; both calls are still
            // wrapped uniformly so the decoded fields can be checked
            // afterward regardless of which one throws.
            ucode.Npc = 0;
            try { ucode.Step(); } catch (NotImplementedException) { /* expected: Alu()'s WriteDest reaches stubbed MfWrite */ }
            try { ucode.Step(); } catch (NotImplementedException) { /* not expected to throw now that Jmp() is implemented; kept defensively */ }

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

    private static TestOutcome TestPromDecodeSanity()
    {
        Console.WriteLine("Test: Real promh.mcr Decode Sanity");
        try
        {
            string path = Path.Combine("sys", "ubin", "promh.mcr");
            if (!File.Exists(path))
            {
                Console.WriteLine("  SKIPPED (sys/ubin/promh.mcr not found at expected path)\n");
                return TestOutcome.Skipped;
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

            // Exact counts verified by hand-decoding the first 200 words of
            // the real sys/ubin/promh.mcr during final review: this turns
            // the sanity check into a real regression guard on the decode's
            // bit positions rather than a loose "at least 2 classes" check.
            Assert(opCounts[0] == 55, $"ALU class count is exactly 55, got {opCounts[0]}");
            Assert(opCounts[1] == 103, $"JUMP class count is exactly 103, got {opCounts[1]}");
            Assert(opCounts[2] == 1, $"DISPATCH class count is exactly 1, got {opCounts[2]}");
            Assert(opCounts[3] == 41, $"BYTE class count is exactly 41, got {opCounts[3]}");

            Console.WriteLine($"  Opcode class distribution: ALU={opCounts[0]} JUMP={opCounts[1]} DISPATCH={opCounts[2]} BYTE={opCounts[3]}");
            Console.WriteLine("  Real promh.mcr Decode Sanity tests passed\n");
            return TestOutcome.Passed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Real promh.mcr Decode Sanity tests failed: {ex.Message}\n");
            return TestOutcome.Failed;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
