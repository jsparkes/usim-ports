// MainMemoryTests.cs - Tests for MainMemory's populated-page-count gate
// (Phase 8B of the microcode engine port).

using System;

namespace Usim;

public static class MainMemoryTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== MainMemory Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestDefaultNpagesMatchesRealDefault()) passed++; else failed++;
        if (TestReadWriteWithinPopulatedRangeWork()) passed++; else failed++;
        if (TestReadWriteAtOrBeyondNpagesFail()) passed++; else failed++;
        if (TestCustomNpagesGatesCorrectly()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestDefaultNpagesMatchesRealDefault()
    {
        Console.WriteLine("Test: MainMemory()'s default npages is 8192 (usim/ucfg.c's memory.size=2048 KW default x4), and PHYSICAL_PAGES stays the 16384 max-allocation ceiling");
        try
        {
            var mem = new MainMemory();

            // A word at the very last populated page (8191) must work;
            // a word at page 8192 (the first UNpopulated page) must not --
            // this indirectly confirms the default npages, since there's
            // no direct public getter for it (matching MainMemory's
            // existing style: PHYSICAL_PAGES/PHYSICAL_MEM_SIZE are public
            // consts, but the runtime npages limit isn't separately
            // exposed beyond ReadPhysical/WritePhysical's own behavior).
            uint lastPopulatedWord = (8191u << 8) | 0xFF; // last word of page 8191
            uint firstUnpopulatedWord = (8192u << 8); // first word of page 8192

            mem.WritePhysical(lastPopulatedWord, 0x12345678);
            Assert(mem.ReadPhysical(lastPopulatedWord) == 0x12345678, "page 8191 (last populated) is writable/readable");

            mem.WritePhysical(firstUnpopulatedWord, 0xDEADBEEF);
            Assert(mem.ReadPhysical(firstUnpopulatedWord) == 0xFFFFFFFF, "page 8192 (first unpopulated) silently ignores the write and reads back 0xFFFFFFFF (matching the real C's out-of-range return value)");

            Console.WriteLine("  default-npages test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  default-npages test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestReadWriteWithinPopulatedRangeWork()
    {
        Console.WriteLine("Test: ReadPhysical/WritePhysical work normally for any address within the populated range");
        try
        {
            var mem = new MainMemory();
            mem.WritePhysical(0, 1);
            mem.WritePhysical(1000, 2);
            Assert(mem.ReadPhysical(0) == 1, "word 0 round-trips");
            Assert(mem.ReadPhysical(1000) == 2, "word 1000 round-trips");

            Console.WriteLine("  within-populated-range test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  within-populated-range test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestReadWriteAtOrBeyondNpagesFail()
    {
        Console.WriteLine("Test: ReadPhysical/WritePhysical at or beyond npages silently no-op/return 0, but never throw or corrupt other pages");
        try
        {
            var mem = new MainMemory(npages: 4); // tiny, easy to reason about

            // Page 3 (word range [768,1023]) is the last populated page.
            mem.WritePhysical(1023, 0xAAAA);
            Assert(mem.ReadPhysical(1023) == 0xAAAA, "last word of last populated page (npages=4) works");

            // Page 4 (word 1024) is the first unpopulated page.
            mem.WritePhysical(1024, 0xBBBB);
            Assert(mem.ReadPhysical(1024) == 0xFFFFFFFF, "first word of first unpopulated page silently no-ops the write and reads back 0xFFFFFFFF");

            // Far beyond npages (but still within the 16384-page hard
            // ceiling) must behave identically -- no exception, no
            // corruption of page 3's already-written value.
            mem.WritePhysical(100000, 0xCCCC);
            Assert(mem.ReadPhysical(100000) == 0xFFFFFFFF, "far-beyond-npages address also silently no-ops the write and reads back 0xFFFFFFFF");
            Assert(mem.ReadPhysical(1023) == 0xAAAA, "writing far beyond npages didn't corrupt the populated range");

            Console.WriteLine("  at-or-beyond-npages test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  at-or-beyond-npages test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestCustomNpagesGatesCorrectly()
    {
        Console.WriteLine("Test: a custom npages value gates at exactly that boundary, not the 16384 hard ceiling");
        try
        {
            var mem = new MainMemory(npages: 1); // only page 0 populated

            mem.WritePhysical(255, 111); // last word of page 0
            Assert(mem.ReadPhysical(255) == 111, "page 0's last word works");

            mem.WritePhysical(256, 222); // first word of page 1 -- unpopulated
            Assert(mem.ReadPhysical(256) == 0xFFFFFFFF, "page 1's first word (unpopulated) silently no-ops the write and reads back 0xFFFFFFFF");

            Console.WriteLine("  custom-npages test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  custom-npages test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
