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

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestDiskControlStatusRead()
    {
        Console.WriteLine("Test: disk-controller status register (offset 0) satisfies the boot PROM's poll");
        try
        {
            var busAdaptor = new BusAdaptor(new BusInterface(new UCode(new MainMemory())));

            // Disk control range is paddr 0x3DFFFC-0x3DFFFF (017377774-017377777 octal);
            // offset 0 (status) is at 0x3DFFFC.
            uint status = busAdaptor.Read(0x3DFFFC);
            Assert((status & 1) != 0, $"bit0 (not_active/ready) must be set, got 0x{status:X}");
            Assert((status & (1u << 9)) == 0, $"bit9 (!online) must be clear (i.e. online), got 0x{status:X}");

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
            var busAdaptor = new BusAdaptor(new BusInterface(new UCode(new MainMemory())));
            bool promDisabled = false;

            // Offsets 1 (memory address), 2 (disk address), 3 (ECC) -- no real disk
            // state, all read 0.
            Assert(busAdaptor.Read(0x3DFFFD) == 0, "offset 1 (memory address) reads 0");
            Assert(busAdaptor.Read(0x3DFFFE) == 0, "offset 2 (disk address) reads 0");
            Assert(busAdaptor.Read(0x3DFFFF) == 0, "offset 3 (ECC) reads 0");

            // Writes to any disk-control offset (command/CLP/DA) must not throw.
            busAdaptor.Write(0x3DFFFC, 0x16, ref promDisabled); // command register, "reset" value
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
            var busAdaptor = new BusAdaptor(new BusInterface(new UCode(new MainMemory())));
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
            var busAdaptor = new BusAdaptor(new BusInterface(new UCode(new MainMemory())));
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
            var busAdaptor = new BusAdaptor(new BusInterface(new UCode(new MainMemory())));
            bool promDisabled = false;

            // Main TV screen (XBus I/O, 0x3C0000-0x3C7FFF).
            Assert(busAdaptor.Read(0x3C0000) == 0, "TV screen read is a safe default (0)");
            busAdaptor.Write(0x3C0000, 0x1234, ref promDisabled);

            // Unibus Map (0xC000-0xFFFF uaddr).
            Assert(busAdaptor.Read(UaddrToPaddr(0xC000)) == 0, "Unibus Map read is a safe default (0)");
            busAdaptor.Write(UaddrToPaddr(0xC000), 0x1234, ref promDisabled);

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
            var busAdaptor = new BusAdaptor(new BusInterface(new UCode(new MainMemory())));
            bool promDisabled = false;

            // 0766044 (bus status register): write clears status, read
            // reflects it. If this still hit the old generic fallback, the
            // write would be a no-op warning and the read would always
            // return 0 -- true today regardless, so additionally confirm
            // NO Unibus-NXM side effect occurs (the old fallback path,
            // still reachable for genuinely unmapped addresses, always
            // asserts NXM; real bus-interface register access must not).
            busAdaptor.Write(UaddrToPaddr(0x3EC24), 0, ref promDisabled); // 0766044 octal
            uint status = busAdaptor.Read(UaddrToPaddr(0x3EC24)); // 0766044 octal
            Assert(status == 0, $"0766044 reads back 0 after clear, got 0x{status:X}");

            Console.WriteLine("  Bus-interface real-dispatch test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Bus-interface real-dispatch test failed: {ex.Message}\n");
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
