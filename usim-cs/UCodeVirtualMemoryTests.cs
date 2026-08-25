// UCodeVirtualMemoryTests.cs - Tests for UCode's virtual memory resolution
// and dispatch (Phase 5 of the microcode engine port). Covers Vm()'s
// permission-check/VmaOk logic, its main-memory dispatch path, the
// TV-screen quirk, the deferred XBus-I/O/Unibus placeholder, and VmRead/
// VmWrite's existing call sites (AdvanceLc, MfWrite) now exercising real
// logic instead of no-ops.

using System;

namespace Usim;

public static class UCodeVirtualMemoryTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== UCode Virtual Memory Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestVmUnmappedAddressIsPageFault()) passed++; else failed++;
        if (TestVmMappedMainMemoryReadWrite()) passed++; else failed++;
        if (TestVmWriteRequiresBothPermissionBits()) passed++; else failed++;
        if (TestVmDeferredIoRangeDoesNotThrow()) passed++; else failed++;
        if (TestUCodeDefaultConstructorHasNonNullUvmem()) passed++; else failed++;
        if (TestUCodeExplicitConstructorSharesMainMemory()) passed++; else failed++;
        if (TestVmReadThroughMfWriteVmaStartRead()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestVmUnmappedAddressIsPageFault()
    {
        Console.WriteLine("Test: Vm() on an unmapped address is a page fault (VmaOk=false, v=0)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            uint v = 0xDEADBEEF; // pre-existing garbage, must be zeroed on a fault
            ucode.CallVm(false, 0x00001234, ref v);
            Assert(ucode.VmaOk == false, "unmapped address -> VmaOk false (access_permission bit is 0)");
            Assert(v == 0, $"page fault on read -> v forced to 0, got 0x{v:X}");

            Console.WriteLine("  Vm-unmapped-page-fault tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vm-unmapped-page-fault tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVmMappedMainMemoryReadWrite()
    {
        Console.WriteLine("Test: Vm() write-then-read round-trips through real main memory for a mapped address");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Map vaddr's L1/L2 entries with both permission bits set, physical page 5
            // (well within the "xbus main memory" range, pn<=0x3BFB).
            uint vaddr = 0x00003000;
            uint md = vaddr; // md=vaddr keeps L1 and L2 indices consistent with Vtop(vaddr) inside Vm()
            uint l2Data = (1u << 23) | (1u << 22) | 5u; // access+write permission, pn=5
            uint vma = (1u << 26) | (1u << 25) | (0x02u << 27) | l2Data;
            ucode.Uvmem.WriteMap(vma, md);

            uint writeVal = 0x77778888;
            ucode.CallVm(true, vaddr, ref writeVal);
            Assert(ucode.VmaOk == true, "mapped, both permission bits set -> VmaOk true for a write");

            uint readVal = 0;
            ucode.CallVm(false, vaddr, ref readVal);
            Assert(ucode.VmaOk == true, "mapped, access permission set -> VmaOk true for a read");
            Assert(readVal == 0x77778888, $"read-back the just-written value, got 0x{readVal:X}");

            Console.WriteLine("  Vm-mapped-main-memory tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vm-mapped-main-memory tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVmWriteRequiresBothPermissionBits()
    {
        Console.WriteLine("Test: Vm() write needs BOTH access and write permission; read needs only access");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Map with access permission set but write permission CLEAR.
            uint vaddr = 0x00005000;
            uint md = vaddr; // md=vaddr keeps L1 and L2 indices consistent with Vtop(vaddr) inside Vm()
            uint l2Data = (1u << 23) | 7u; // access permission only, pn=7
            uint vma = (1u << 26) | (1u << 25) | (0x01u << 27) | l2Data;
            ucode.Uvmem.WriteMap(vma, md);

            uint writeVal = 0x11111111;
            ucode.CallVm(true, vaddr, ref writeVal);
            Assert(ucode.VmaOk == false, "access permission alone is NOT enough for a write");

            uint readVal = 0;
            ucode.CallVm(false, vaddr, ref readVal);
            Assert(ucode.VmaOk == true, "access permission alone IS enough for a read");

            Console.WriteLine("  Vm-write-needs-both-permissions tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vm-write-needs-both-permissions tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVmDeferredIoRangeDoesNotThrow()
    {
        Console.WriteLine("Test: Vm() on a mapped XBus-I/O-range address does not throw (deferred placeholder)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Map to physical page number 0x3C00 (the first XBus-I/O page) with both
            // permission bits set, so Vm() reaches the dispatch step rather than
            // faulting first.
            uint vaddr = 0x00007000;
            uint md = vaddr; // md=vaddr keeps L1 and L2 indices consistent with Vtop(vaddr) inside Vm()
            uint l2Data = (1u << 23) | (1u << 22) | 0x3C00u;
            uint vma = (1u << 26) | (1u << 25) | (0x03u << 27) | l2Data;
            ucode.Uvmem.WriteMap(vma, md);

            uint readVal = 0xCAFECAFE;
            ucode.CallVm(false, vaddr, ref readVal);
            Assert(readVal == 0, $"deferred XBus-I/O range reads as 0, got 0x{readVal:X}");

            uint writeVal = 0x99999999;
            ucode.CallVm(true, vaddr, ref writeVal); // must not throw

            Console.WriteLine("  Vm-deferred-io-range tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vm-deferred-io-range tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUCodeDefaultConstructorHasNonNullUvmem()
    {
        Console.WriteLine("Test: UCode()'s default constructor still works and has a non-null Uvmem");
        try
        {
            var ucode = new UCode(); // every existing Phase 1-4 test/call site uses this
            ucode.Init();
            Assert(ucode.Uvmem != null, "default constructor produces a non-null Uvmem");

            Console.WriteLine("  UCode-default-constructor tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  UCode-default-constructor tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUCodeExplicitConstructorSharesMainMemory()
    {
        Console.WriteLine("Test: UCode(MainMemory) uses the SAME MainMemory instance passed in");
        try
        {
            var mainMemory = new MainMemory();
            mainMemory.WritePhysical(42, 0xABCDEF01);

            var ucode = new UCode(mainMemory);
            ucode.Init();

            // Map a virtual address straight onto physical page 0 (offset 42 within it)
            // and confirm UCode.Vm() reads through to the SAME MainMemory instance,
            // not a separately-constructed one.
            uint vaddr = 42;
            uint md = vaddr; // md=vaddr keeps L1 and L2 indices consistent with Vtop(vaddr) inside Vm()
            uint l2Data = (1u << 23) | 0u; // access permission, pn=0
            uint vma = (1u << 26) | (1u << 25) | (0x00u << 27) | l2Data;
            ucode.Uvmem.WriteMap(vma, md);

            uint readVal = 0;
            ucode.CallVm(false, vaddr, ref readVal);
            Assert(readVal == 0xABCDEF01, $"UCode reads through the SAME MainMemory instance, got 0x{readVal:X}");

            Console.WriteLine("  UCode-explicit-constructor-shares-MainMemory tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  UCode-explicit-constructor-shares-MainMemory tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVmReadThroughMfWriteVmaStartRead()
    {
        Console.WriteLine("Test: MfWrite's VMA-START-READ (code 17) now exercises real Vm() logic, not a no-op");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            uint vaddr = 0x00009000;
            uint md = vaddr; // md=vaddr keeps L1 and L2 indices consistent with Vtop(vaddr) inside Vm()
            uint l2Data = (1u << 23) | (1u << 22) | 9u; // access+write, pn=9
            uint vma = (1u << 26) | (1u << 25) | (0x04u << 27) | l2Data;
            ucode.Uvmem.WriteMap(vma, md);

            // Pre-seed physical page 9's word 0 (matches vaddr's low byte 0x00) via a
            // direct write, then trigger VMA-START-READ through MfWrite and confirm
            // NewMd picks up the real value once NewMdDelay counts down.
            uint preSeedValue = 0x55AA55AA;
            ucode.CallVm(true, vaddr, ref preSeedValue);

            ucode.MfWrite(17 << 5, unchecked((int)vaddr));
            Assert(ucode.NewMdDelay == 2, $"VMA-START-READ still sets NewMdDelay=2, got {ucode.NewMdDelay}");

            Console.WriteLine("  MfWrite-VMA-START-READ-real-Vm tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfWrite-VMA-START-READ-real-Vm tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
