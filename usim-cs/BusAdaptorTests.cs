// BusAdaptorTests.cs - Tests for the deliberately-minimal bus-adaptor device
// routing (Phase 5B of the microcode engine port). Covers the two real
// registers (disk-controller status, diagnostic-interface mode register) and
// confirms every placeholder path is non-fatal (never throws).

using System;

namespace Usim;

public static class BusAdaptorTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== BusAdaptor Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestDiskControlStatusRead()) passed++; else failed++;
        if (TestDiskControlOtherOffsetsAndWrites()) passed++; else failed++;
        if (TestDiagnosticModeRegisterWrite()) passed++; else failed++;
        if (TestDiagnosticOtherRegistersNoThrow()) passed++; else failed++;
        if (TestPlaceholderPathsDoNotThrow()) passed++; else failed++;
        if (TestBusInterfaceRangeDispatchesForReal()) passed++; else failed++;
        if (TestDiskControlDispatchesToRealController()) passed++; else failed++;
        if (TestUnibusMapUnconfiguredPageSetsMapError()) passed++; else failed++;
        if (TestUnibusMapWriteWithoutPermitSetsMapError()) passed++; else failed++;
        if (TestUnibusMapDmaWriteToMainMemory()) passed++; else failed++;
        if (TestUnibusMapDmaReadFromMainMemory()) passed++; else failed++;
        if (TestUnibusMapMdRegisterBackdoor()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestDiskControlStatusRead()
    {
        Console.WriteLine("Test: disk-controller status register (offset 0) reflects real state, not a fake always-ready stub");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var diskController = new DiskController(mainMemory, ucode);
            ucode.BusAdaptor.WireDiskController(diskController);

            // With no unit configured, a real DiskController correctly reports
            // not-active (bit0=1) AND offline (bit9=1) -- unlike the old stub,
            // which faked "ready, online" unconditionally. This matches real
            // hardware with no disk attached (an intended, documented behavior
            // change -- see this plan's Global Constraints).
            uint status = ucode.BusAdaptor.Read(0x3DFFFC);
            Assert((status & 1) != 0, $"bit0 (not_active) set with no disk configured, got 0x{status:X}");
            Assert((status & (1u << 9)) != 0, $"bit9 (!online) set with no disk configured, got 0x{status:X}");

            Console.WriteLine("  Disk-control status tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Disk-control status tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDiskControlOtherOffsetsAndWrites()
    {
        Console.WriteLine("Test: disk-controller other offsets read 0; writes are a no-op (not a working disk)");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var diskController = new DiskController(mainMemory, ucode);
            ucode.BusAdaptor.WireDiskController(diskController);
            var busAdaptor = ucode.BusAdaptor;
            bool promDisabled = false;

            // Offsets 1 (memory address), 2 (disk address), 3 (ECC) -- no real disk
            // state, all read 0.
            Assert(busAdaptor.Read(0x3DFFFD) == 0, "offset 1 (memory address) reads 0");
            Assert(busAdaptor.Read(0x3DFFFE) == 0, "offset 2 (disk address) reads 0");
            Assert(busAdaptor.Read(0x3DFFFF) == 0, "offset 3 (ECC) reads 0");

            // Writes to any disk-control offset (command/CLP/DA) must not throw.
            busAdaptor.Write(0x3DFFFC, 0xE, ref promDisabled); // command register, "reset" value
            busAdaptor.Write(0x3DFFFD, 0, ref promDisabled);
            busAdaptor.Write(0x3DFFFE, 0x1234, ref promDisabled);
            Assert(promDisabled == false, "disk-control writes never touch promDisabled");

            Console.WriteLine("  Disk-control other-offsets tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Disk-control other-offsets tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDiagnosticModeRegisterWrite()
    {
        Console.WriteLine("Test: diagnostic-interface mode register (Unibus 0766012) sets PromDisabled");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = new BusAdaptor(ucode.BusInterface, mainMemory, ucode);
            bool promDisabled = false;

            // Unibus uaddr 0766012 octal = 0x3EC0A. Bit 5 set -> promDisabled = true.
            busAdaptor.Write(UaddrToPaddr(0x3EC0A), 1 << 5, ref promDisabled);
            Assert(promDisabled == true, "bit5 set -> PromDisabled becomes true");

            busAdaptor.Write(UaddrToPaddr(0x3EC0A), 0, ref promDisabled);
            Assert(promDisabled == false, "bit5 clear -> PromDisabled becomes false");

            busAdaptor.Write(UaddrToPaddr(0x3EC0A), 0xFFFFFFFF, ref promDisabled);
            Assert(promDisabled == true, "bit5 set among other bits -> still true");

            Console.WriteLine("  Diagnostic mode-register tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Diagnostic mode-register tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDiagnosticOtherRegistersNoThrow()
    {
        Console.WriteLine("Test: other diagnostic-interface registers are a no-op, not the real C's fatal errx()");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = new BusAdaptor(ucode.BusInterface, mainMemory, ucode);
            bool promDisabled = false;

            // DEBUG-IR (0766000-0766004), clock control (0766006, real C errx()s if v!=1),
            // OPC control (0766010, real C errx()s unconditionally) -- all must be safe no-ops here.
            busAdaptor.Write(UaddrToPaddr(0x3EC00), 0x1234, ref promDisabled);
            busAdaptor.Write(UaddrToPaddr(0x3EC06), 0x99, ref promDisabled); // clock control (0766006 octal), NOT 1 -- would errx() in the real C
            busAdaptor.Write(UaddrToPaddr(0x3EC08), 0x42, ref promDisabled); // OPC control (0766010 octal) -- always errx()s in the real C
            Assert(promDisabled == false, "none of these touch promDisabled");

            Assert(busAdaptor.Read(UaddrToPaddr(0x3EC00)) == 0, "reading a non-mode spy register is a safe default (0)");

            Console.WriteLine("  Diagnostic other-registers tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Diagnostic other-registers tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestPlaceholderPathsDoNotThrow()
    {
        Console.WriteLine("Test: every un-implemented device path is non-fatal (TV, color TV, Unibus Map, IOB, tape, unmapped)");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = new BusAdaptor(ucode.BusInterface, mainMemory, ucode);
            bool promDisabled = false;

            // Main TV screen (XBus I/O, 0x3C0000-0x3C7FFF).
            Assert(busAdaptor.Read(0x3C0000) == 0, "TV screen read is a safe default (0)");
            busAdaptor.Write(0x3C0000, 0x1234, ref promDisabled);

            // Tape controller (0x3F550-0x3F55A uaddr).
            Assert(busAdaptor.Read(UaddrToPaddr(0x3F550)) == 0, "tape-controller read is a safe default (0)");
            busAdaptor.Write(UaddrToPaddr(0x3F550), 0x1234, ref promDisabled);

            // Genuinely unmapped XBus I/O address (within the XBus-I/O page-number
            // range but outside every named device's absolute-paddr window).
            Assert(busAdaptor.Read(0x3C8000) == 0, "unmapped XBus-I/O read is a safe default (0)");
            busAdaptor.Write(0x3C8000, 0x1234, ref promDisabled);

            Assert(promDisabled == false, "none of these placeholder paths touch promDisabled");

            Console.WriteLine("  Placeholder-paths tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Placeholder-paths tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBusInterfaceRangeDispatchesForReal()
    {
        Console.WriteLine("Test: 0766040-range Unibus addresses dispatch to BusInterface, not the generic fallback");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = new BusAdaptor(ucode.BusInterface, mainMemory, ucode);
            bool promDisabled = false;

            // 0766040 (interrupt status register, real write mask 0x3C01):
            // an all-1s write is only masked to 0x3C01 if this genuinely
            // reaches BusInterface -- the old generic fallback (still
            // reachable for real unmapped addresses) never touches UCode
            // state at all, so InterruptStatusReg would stay at its default
            // 0 if dispatch weren't wired. Unlike a plain read-back-of-0
            // check (which both paths satisfy identically), this assertion
            // actually discriminates the two.
            busAdaptor.Write(UaddrToPaddr(0x3EC20), 0xFFFFFFFF, ref promDisabled); // 0766040 octal
            Assert(ucode.InterruptStatusReg == 0x3C01,
                $"0766040 write reaches BusInterface and masks to 0x3C01, got 0x{ucode.InterruptStatusReg:X}");

            Console.WriteLine("  Bus-interface real-dispatch test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Bus-interface real-dispatch test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDiskControlDispatchesToRealController()
    {
        Console.WriteLine("Test: disk-control range dispatches to the real DiskController, not a stub");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var diskController = new DiskController(mainMemory, ucode);
            ucode.BusAdaptor.WireDiskController(diskController);
            bool promDisabled = false;

            // Writing 0xE (reset) then reading offset 0 must return exactly 0
            // even though EncodeStatus() would normally set bit0 -- the old
            // stub had no concept of a reset condition and could never
            // produce this.
            ucode.BusAdaptor.Write(0x3DFFFC, 0xE, ref promDisabled);
            uint statusDuringReset = ucode.BusAdaptor.Read(0x3DFFFC);
            Assert(statusDuringReset == 0, $"reset condition forces status to 0, got 0x{statusDuringReset:X}");
            Assert(!promDisabled, "disk-control writes never touch promDisabled");

            Console.WriteLine("  Disk-control real-dispatch test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Disk-control real-dispatch test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUnibusMapUnconfiguredPageSetsMapError()
    {
        Console.WriteLine("Test: an unconfigured Unibus-Map page (map_valid=0) sets Unibus Map Error, not NXM");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = ucode.BusAdaptor;
            bool promDisabled = false;

            uint status = busAdaptor.Read(UaddrToPaddr(0xC000)); // page 0, never configured
            Assert(status == 0, $"unconfigured page read returns 0, got 0x{status:X}");
            Assert(ucode.BusInterface.IsUnibusMapError(), "unconfigured page read sets Unibus Map Error");
            Assert(!ucode.BusInterface.IsUnibusNxm(), "unconfigured page read does NOT set Unibus NXM (a different error class)");

            ucode.BusInterface.ResetBusErrorStatus();
            busAdaptor.Write(UaddrToPaddr(0xC000), 0x1234, ref promDisabled);
            Assert(ucode.BusInterface.IsUnibusMapError(), "unconfigured page write sets Unibus Map Error");

            Console.WriteLine("  Unconfigured-page test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Unconfigured-page test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUnibusMapWriteWithoutPermitSetsMapError()
    {
        Console.WriteLine("Test: a write to a MAP_VALID-but-not-WRITE_PERMIT page sets Unibus Map Error, doesn't transfer");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = ucode.BusAdaptor;
            bool promDisabled = false;

            // Configure page 1: MAP_VALID set, WRITE_PERMIT clear, xbus page 0 (main memory).
            busAdaptor.Write(UaddrToPaddr(0x3EC62), 0x8000, ref promDisabled); // register 1 = 0x3EC60 + 2

            mainMemory.WritePhysical(0, 0xDEADBEEF); // sentinel -- must survive untouched

            busAdaptor.Write(UaddrToPaddr(0xC400), 0x1111, ref promDisabled); // page 1 low half
            Assert(ucode.BusInterface.IsUnibusMapError(), "write without permit sets Unibus Map Error");
            Assert(mainMemory.ReadPhysical(0) == 0xDEADBEEF, "no transfer happened -- sentinel untouched");

            Console.WriteLine("  Write-without-permit test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Write-without-permit test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUnibusMapDmaWriteToMainMemory()
    {
        Console.WriteLine("Test: a real Unibus-Map DMA write assembles a 32-bit word into MainMemory");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = ucode.BusAdaptor;
            bool promDisabled = false;

            // Configure page 2: MAP_VALID + WRITE_PERMIT, xbus page 5 (well within main memory).
            busAdaptor.Write(UaddrToPaddr(0x3EC64), 0xC005, ref promDisabled); // register 2 = 0x3EC60 + 4

            // Page 2's Unibus range starts at 0xC000 + 2*0x400 = 0xC800.
            // uaddr bits 9-2 select the word within the page; use uaddr offset 0
            // -> paddr = (5 << 8) | 0 = 0x500.
            uint pageBase = 0xC800;
            busAdaptor.Write(UaddrToPaddr(pageBase), 0x2222, ref promDisabled);      // low half
            busAdaptor.Write(UaddrToPaddr(pageBase + 2), 0x3333, ref promDisabled);  // high half -- completes transfer

            uint expected = (0x3333u << 16) | 0x2222u;
            Assert(mainMemory.ReadPhysical(0x500) == expected,
                $"assembled 32-bit word landed at paddr 0x500, got 0x{mainMemory.ReadPhysical(0x500):X}, expected 0x{expected:X}");

            Console.WriteLine("  DMA-write-to-main-memory test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  DMA-write-to-main-memory test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUnibusMapDmaReadFromMainMemory()
    {
        Console.WriteLine("Test: a real Unibus-Map DMA read splits a 32-bit MainMemory word across two half-cycles");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = ucode.BusAdaptor;
            bool promDisabled = false;

            busAdaptor.Write(UaddrToPaddr(0x3EC64), 0xC005, ref promDisabled); // register 2, xbus page 5

            mainMemory.WritePhysical(0x500, 0x77776666);

            uint pageBase = 0xC800;
            uint lo = busAdaptor.Read(UaddrToPaddr(pageBase));       // low half -- real transfer happens here
            uint hi = busAdaptor.Read(UaddrToPaddr(pageBase + 2));   // high half -- from the buffer, no new transfer

            Assert(lo == 0x6666, $"low half correct, got 0x{lo:X}");
            Assert(hi == 0x7777, $"high half correct (from buffer), got 0x{hi:X}");

            Console.WriteLine("  DMA-read-from-main-memory test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  DMA-read-from-main-memory test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUnibusMapMdRegisterBackdoor()
    {
        Console.WriteLine("Test: an Xbus page number >= 0x3E00 through the Unibus map reads/writes UCode.MdReg directly, not memory");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = ucode.BusAdaptor;
            bool promDisabled = false;

            // Configure page 3: MAP_VALID + WRITE_PERMIT, xbus page 0x3E00 (the MD-register threshold).
            busAdaptor.Write(UaddrToPaddr(0x3EC66), 0xFE00, ref promDisabled); // register 3 = 0x3EC60 + 6; 0x8000|0x4000|0x3E00

            uint pageBase = 0xCC00; // page 3 = 0xC000 + 3*0x400
            mainMemory.WritePhysical(0x500, 0xDEADBEEF); // sentinel -- must stay untouched

            busAdaptor.Write(UaddrToPaddr(pageBase), 0xAAAA, ref promDisabled);      // low half -> MdReg low
            busAdaptor.Write(UaddrToPaddr(pageBase + 2), 0xBBBB, ref promDisabled);  // high half -> MdReg high

            Assert(ucode.MdReg == ((0xBBBBu << 16) | 0xAAAAu),
                $"MdReg set directly, got 0x{ucode.MdReg:X}");
            Assert(mainMemory.ReadPhysical(0x500) == 0xDEADBEEF,
                "main memory untouched by the MD-register backdoor path");

            uint loRead = busAdaptor.Read(UaddrToPaddr(pageBase));
            uint hiRead = busAdaptor.Read(UaddrToPaddr(pageBase + 2));
            Assert(loRead == 0xAAAA && hiRead == 0xBBBB, "MdReg reads back correctly through the same backdoor");

            Console.WriteLine("  MD-register-backdoor test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MD-register-backdoor test failed: {ex.Message}\n");
            return false;
        }
    }

    /// <summary>
    /// Inverse of BusAdaptor's own uaddr formula, for test setup: given a Unibus
    /// uaddr, produce a paddr whose dispatchPn resolves into the Unibus range and
    /// which BusAdaptor.Read/Write will convert back to exactly that uaddr.
    /// uaddr = (((dispatchPn - 0x3E00) &lt;&lt; 8) | (paddr &amp; 0xFF)) &lt;&lt; 1, so working
    /// backwards: halfWordIndex = uaddr &gt;&gt; 1 carries BOTH dispatchPn's contribution
    /// (its high bits) AND paddr's low byte (its low 8 bits) -- the low byte is
    /// generally nonzero (an earlier draft of this file wrongly assumed it was
    /// always 0, which produces a different, wrong uaddr for any target whose low
    /// byte isn't itself 0) and must be extracted with halfWordIndex &amp; 0xFF below,
    /// not discarded.
    /// </summary>
    private static uint UaddrToPaddr(uint uaddr)
    {
        uint halfWordIndex = uaddr >> 1;
        uint dispatchPn = (halfWordIndex >> 8) + 0x3E00;
        uint paddrLowByte = halfWordIndex & 0xFF;
        return (dispatchPn << 8) | paddrLowByte;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
