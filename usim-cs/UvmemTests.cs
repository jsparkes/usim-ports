// UvmemTests.cs - Tests for the two-level virtual memory page-table class
// (Phase 5 of the microcode engine port). Covers Vtop's address resolution
// and WriteMap's L1/L2 table updates, including the same-call L1-then-L2
// write-visibility behavior the real C's sequential statement order gives.

using System;

namespace Usim;

public static class UvmemTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== Uvmem Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestVtopBeforeAnyMapping()) passed++; else failed++;
        if (TestWriteMapL1Only()) passed++; else failed++;
        if (TestWriteMapL2Only()) passed++; else failed++;
        if (TestWriteMapBothInOneCall()) passed++; else failed++;
        if (TestVtopUsesUpdatedMapping()) passed++; else failed++;
        if (TestVtopUpperVaddrBitsIgnored()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestVtopBeforeAnyMapping()
    {
        Console.WriteLine("Test: Vtop before any WriteMap call (all-zero maps)");
        try
        {
            var uvmem = new Uvmem();

            uint paddr = uvmem.Vtop(0x00123456, out uint l1, out uint l2, out uint pn, out bool wp, out bool ap);
            Assert(l1 == 0, $"l1Data is 0 (unmapped L1 entry), got {l1}");
            Assert(l2 == 0, $"l2Data is 0 (unmapped L2 entry), got {l2}");
            Assert(pn == 0, $"physicalPageNumber is 0, got {pn}");
            Assert(wp == false, "writePermission is false before any mapping");
            Assert(ap == false, "accessPermission is false before any mapping");
            Assert(paddr == ((0u << 8) | (0x00123456u & 0xFF)), $"paddr = (pn<<8)|(vaddr&0xFF), got 0x{paddr:X}");

            Console.WriteLine("  Vtop-before-mapping tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vtop-before-mapping tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestWriteMapL1Only()
    {
        Console.WriteLine("Test: WriteMap with only the L1 enable bit set");
        try
        {
            var uvmem = new Uvmem();

            // md must resolve to the SAME l1Index Vtop(vaddr) will use, AND its bits
            // 8-12 must match vaddr's for the (unused, here) L2 index too -- using
            // md=vaddr directly guarantees both, and matches how real microcode always
            // uses the same address for both mapping and the access that follows.
            uint vaddr = 0x00246000;
            uint md = vaddr;
            uint vma = (1u << 26) | (0x15u << 27); // enable L1 only; L1 data = 0x15 (5 bits)

            uvmem.WriteMap(vma, md);

            _ = uvmem.Vtop(vaddr, out uint l1, out uint l2, out uint pn, out bool wp, out bool ap);
            Assert(l1 == 0x15, $"L1 entry written, got 0x{l1:X}");
            Assert(l2 == 0, "L2 entry untouched (L2 enable bit was not set)");
            Assert(pn == 0, "physicalPageNumber still 0 (derived from untouched L2 entry)");

            Console.WriteLine("  WriteMap L1-only tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  WriteMap L1-only tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestWriteMapL2Only()
    {
        Console.WriteLine("Test: WriteMap with only the L2 enable bit set (requires a pre-existing L1 entry)");
        try
        {
            var uvmem = new Uvmem();

            uint vaddr = 0x00246000;
            uint md = vaddr; // md=vaddr keeps both L1 and L2 indices consistent with Vtop(vaddr)

            // First, set up L1 (a separate call, matching real usage -- the C's own comment
            // says "the first level map... must have been written previously").
            uvmem.WriteMap((1u << 26) | (0x07u << 27), md);

            // Now write L2 only. l2Data = vma & 0x00FFFFFF; write+access permission bits are
            // bits 22/23 of that same value.
            uint l2DataToWrite = (1u << 23) | (1u << 22) | 0x1234u; // access=1, write=1, pn=0x1234
            uint vma2 = (1u << 25) | l2DataToWrite; // enable L2 only
            uvmem.WriteMap(vma2, md);

            _ = uvmem.Vtop(vaddr, out uint l1, out uint l2, out uint pn, out bool wp, out bool ap);
            Assert(l1 == 0x07, $"L1 entry unchanged by the L2-only call, got 0x{l1:X}");
            Assert(l2 == l2DataToWrite, $"L2 entry written, got 0x{l2:X}");
            Assert(pn == 0x1234, $"physicalPageNumber extracted from L2 data, got 0x{pn:X}");
            Assert(wp == true, "writePermission true (bit 22 set)");
            Assert(ap == true, "accessPermission true (bit 23 set)");

            Console.WriteLine("  WriteMap L2-only tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  WriteMap L2-only tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestWriteMapBothInOneCall()
    {
        Console.WriteLine("Test: WriteMap with BOTH enable bits set in one call -- L2 sees the just-written L1 entry");
        try
        {
            var uvmem = new Uvmem();

            uint vaddr = 0x00050000;
            uint md = vaddr; // md=vaddr keeps both L1 and L2 indices consistent with Vtop(vaddr)

            // L1 data = 0x03 (5 bits, goes in vma bits 27-31); L2 data = a page number with
            // both permission bits set (goes in vma bits 0-23, masked to 24 bits).
            uint l1DataToWrite = 0x03u;
            uint l2DataToWrite = (1u << 23) | (1u << 22) | 0x0055u;
            uint vma = (1u << 26) | (1u << 25) | (l1DataToWrite << 27) | l2DataToWrite;

            uvmem.WriteMap(vma, md);

            _ = uvmem.Vtop(vaddr, out uint l1, out uint l2, out uint pn, out bool wp, out bool ap);
            Assert(l1 == l1DataToWrite, $"L1 entry from the combined call, got 0x{l1:X}");
            // This is the key assertion: if L2's write used a STALE (pre-call) L1 index
            // instead of the just-written one, l2 would be read from the wrong slot and
            // very likely still be 0 (a fresh Uvmem's L2 map starts all-zero) instead of
            // l2DataToWrite.
            Assert(l2 == l2DataToWrite, $"L2 entry sees the JUST-WRITTEN L1 data for its own index, got 0x{l2:X}");
            Assert(pn == 0x0055, $"physicalPageNumber, got 0x{pn:X}");

            Console.WriteLine("  WriteMap both-in-one-call tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  WriteMap both-in-one-call tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVtopUsesUpdatedMapping()
    {
        Console.WriteLine("Test: Vtop's returned physical address matches (pn<<8)|(vaddr&0xFF) after a real mapping");
        try
        {
            var uvmem = new Uvmem();

            uint vaddr = 0x00099999; // low byte 0x99
            uint md = vaddr; // md=vaddr keeps both L1 and L2 indices consistent with Vtop(vaddr)
            uint l2DataToWrite = (1u << 23) | (1u << 22) | 0x2000u; // pn = 0x2000
            uint vma = (1u << 26) | (1u << 25) | (0x01u << 27) | l2DataToWrite;

            uvmem.WriteMap(vma, md);

            uint paddr = uvmem.Vtop(vaddr, out _, out _, out uint pn, out bool wp, out bool ap);
            Assert(pn == 0x2000, $"physicalPageNumber, got 0x{pn:X}");
            uint expectedPaddr = (0x2000u << 8) | (vaddr & 0xFF);
            Assert(paddr == expectedPaddr, $"paddr = (pn<<8)|(vaddr&0xFF) = 0x{expectedPaddr:X}, got 0x{paddr:X}");
            Assert(wp && ap, "both permission bits set");

            Console.WriteLine("  Vtop-uses-updated-mapping tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vtop-uses-updated-mapping tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVtopUpperVaddrBitsIgnored()
    {
        Console.WriteLine("Test: Vtop masks vaddr to 24 bits before use (upper 8 bits ignored)");
        try
        {
            var uvmem = new Uvmem();

            uint lowVaddr = 0x00123456;
            uint highVaddr = 0xFF123456; // same low 24 bits, garbage in the upper 8

            uint paddrLow = uvmem.Vtop(lowVaddr, out uint l1a, out uint l2a, out uint pnA, out _, out _);
            uint paddrHigh = uvmem.Vtop(highVaddr, out uint l1b, out uint l2b, out uint pnB, out _, out _);

            Assert(paddrLow == paddrHigh, $"upper 8 bits of vaddr are ignored, got 0x{paddrLow:X} vs 0x{paddrHigh:X}");
            Assert(l1a == l1b && l2a == l2b && pnA == pnB, "identical resolution regardless of vaddr's upper 8 bits");

            Console.WriteLine("  Vtop-upper-bits-ignored tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vtop-upper-bits-ignored tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
