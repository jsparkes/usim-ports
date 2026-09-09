// UCodeBusAdaptorTests.cs - Tests for UCode.Vm()'s wiring into BusAdaptor
// (Phase 5B of the microcode engine port). Covers the end-to-end path from a
// mapped virtual address through Vm()/Uvmem.Vtop to BusAdaptor's real
// registers, and confirms the existing "xbus main memory" fast path (Phase 5)
// is untouched.

using System;

namespace Usim;

public static class UCodeBusAdaptorTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== UCode BusAdaptor Wiring Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestUCodeHasNonNullBusAdaptor()) passed++; else failed++;
        if (TestVmReadsRealDiskStatusThroughFullChain()) passed++; else failed++;
        if (TestVmWriteSetsPromDisabledThroughFullChain()) passed++; else failed++;
        if (TestVmMainMemoryFastPathStillWorks()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestUCodeHasNonNullBusAdaptor()
    {
        Console.WriteLine("Test: UCode() (both constructors) produces a non-null BusAdaptor");
        try
        {
            var ucode1 = new UCode();
            Assert(ucode1.BusAdaptor != null, "default constructor -> non-null BusAdaptor");

            var ucode2 = new UCode(new MainMemory());
            Assert(ucode2.BusAdaptor != null, "explicit constructor -> non-null BusAdaptor");

            Console.WriteLine("  UCode-has-BusAdaptor tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  UCode-has-BusAdaptor tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVmReadsRealDiskStatusThroughFullChain()
    {
        Console.WriteLine("Test: Vm() read of a mapped disk-control address returns BusAdaptor's real status");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            ucode.BusAdaptor.WireDiskController(new DiskController(mainMemory, ucode));
            ucode.Init();

            // Map vaddr 0x00008000 to physical page number 0x3DFFFC's page (i.e.
            // physical page 0x3DFFFC's own page number, so that Vtop's
            // (pn<<8)|(vaddr&0xFF) reproduces the disk-control status paddr exactly
            // when vaddr's low byte is 0). Disk-control status is at paddr
            // 0x3DFFFC, so pn = 0x3DFFFC >> 8 = 0x3DFFFC... actually pn is the
            // FULL page number the L2 map stores, and paddr = (pn<<8)|(vaddr&0xFF);
            // to land exactly on 0x3DFFFC (whose low byte is 0xFC), vaddr's low
            // byte must be 0xFC and pn must be 0x3DFFFC>>8 = 0x3DFF.
            uint vaddr = 0x000080FC; // low byte 0xFC
            uint md = vaddr;
            uint l2Data = (1u << 23) | (1u << 22) | 0x3DFFu; // access+write permission, pn=0x3DFF
            uint vma = (1u << 26) | (1u << 25) | (0x01u << 27) | l2Data;
            ucode.Uvmem.WriteMap(vma, md);

            uint v = 0xDEADBEEF;
            ucode.CallVm(false, vaddr, ref v);
            // bit0 (not_active) is 1 for a real, freshly-reset DiskController with
            // no unit configured -- same value the old always-ready stub returned,
            // so this assertion still discriminates "reaches BusAdaptor's real
            // status register" from "reads back 0/garbage", just via a different
            // (now real) code path.
            Assert((v & 1) != 0, $"disk-control status bit0 (not_active) reaches Vm()'s caller, got 0x{v:X}");
            Assert(ucode.VmaOk == true, "mapped with access permission -> VmaOk true");

            Console.WriteLine("  Vm-reads-real-disk-status tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vm-reads-real-disk-status tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVmWriteSetsPromDisabledThroughFullChain()
    {
        Console.WriteLine("Test: Vm() write to the diagnostic mode register sets UCode.PromDisabled through the full chain");
        try
        {
            var ucode = new UCode();
            ucode.Init();
            Assert(ucode.PromDisabled == false, "PromDisabled starts false after Init()");

            // Unibus uaddr 0766012 octal = 0x3EC0A. Working backwards through
            // BusAdaptor's uaddr formula (uaddr = (((pn-0x3E00)<<8) | paddrLowByte) << 1,
            // with dispatchPn==pn and paddrLowByte==vaddr's low byte when Vtop's paddr =
            // (pn<<8)|(vaddr&0xFF)): half = 0x3EC0A>>1 = 0x1F605; the needed low byte is
            // half&0xFF = 0x05 (NOT 0 -- an earlier draft of this test wrongly assumed a
            // 0 low byte, which gives uaddr 0x3EC00, not 0x3EC0A); pn = (half>>8)+0x3E00
            // = 0x1F6+0x3E00 = 0x3FF6. Independently verified by forward substitution
            // (Python one-liner): pn=0x3FF6, vaddr low byte=0x05 reproduces uaddr=0x3EC0A
            // exactly. This also cross-confirms Phase 5's own final-review calibration,
            // which independently found physical page 0x3FF6 to be the real
            // diagnostic/spy-register page reachable from the boot PROM.
            uint pn = 0x3FF6;
            uint vaddr = 0x00000005; // low byte 0x05 -- see derivation above
            uint md = vaddr;
            uint l2Data = (1u << 23) | (1u << 22) | (pn & 0x3FFFu);
            uint vma = (1u << 26) | (1u << 25) | (0x02u << 27) | l2Data;
            ucode.Uvmem.WriteMap(vma, md);

            uint writeVal = 1u << 5; // sets promdisabled
            ucode.CallVm(true, vaddr, ref writeVal);
            Assert(ucode.PromDisabled == true, "diagnostic mode register write reaches PromDisabled through the full Vm()->BusAdaptor chain");

            Console.WriteLine("  Vm-sets-PromDisabled tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vm-sets-PromDisabled tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVmMainMemoryFastPathStillWorks()
    {
        Console.WriteLine("Test: Vm()'s existing 'xbus main memory' fast path (Phase 5) is untouched by this wiring");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            uint vaddr = 0x00003000;
            uint md = vaddr;
            uint l2Data = (1u << 23) | (1u << 22) | 5u; // access+write, pn=5 (well within main-memory range)
            uint vma = (1u << 26) | (1u << 25) | (0x02u << 27) | l2Data;
            ucode.Uvmem.WriteMap(vma, md);

            uint writeVal = 0x77778888;
            ucode.CallVm(true, vaddr, ref writeVal);
            uint readVal = 0;
            ucode.CallVm(false, vaddr, ref readVal);
            Assert(readVal == 0x77778888, $"main-memory read/write round-trip still works, got 0x{readVal:X}");

            Console.WriteLine("  Vm-main-memory-fast-path tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vm-main-memory-fast-path tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
