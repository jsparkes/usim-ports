// BusAdaptorTests.cs - Tests for the deliberately-minimal bus-adaptor device
// routing (Phase 5B of the microcode engine port). Covers the two real
// registers (disk-controller status, diagnostic-interface mode register) and
// confirms every placeholder path is non-fatal (never throws).

using System;
using System.IO;

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
        if (TestTvScreenReadWriteThroughBusAdaptor()) passed++; else failed++;
        if (TestTvModeRegisterThroughBusAdaptor()) passed++; else failed++;
        if (TestTvCheckerboardScreenshotThroughBusAdaptor()) passed++; else failed++;
        if (TestTvControlHiAndTvScreenBoundaries()) passed++; else failed++;

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
            var ucode = new UCode(new MainMemory());
            var busAdaptor = new BusAdaptor(ucode.BusInterface);
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

    private static bool TestTvScreenReadWriteThroughBusAdaptor()
    {
        Console.WriteLine("Test: TV screen read/write through BusAdaptor's real register interface round-trips, including the last valid word");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            bool promDisabled = false;

            // TvScreenLo = 0x3C0000 (BusAdaptor's own constant) -- offset 0 and offset 0x7FFF (last valid word).
            uint tvScreenLo = 0x3C0000;
            ucode.BusAdaptor.Write(tvScreenLo + 0, 0xDEADBEEF, ref promDisabled);
            uint v0 = ucode.BusAdaptor.Read(tvScreenLo + 0);
            Assert(v0 == 0xDEADBEEF, $"offset 0 round-trips through BusAdaptor, got 0x{v0:X}");

            ucode.BusAdaptor.Write(tvScreenLo + 0x7FFF, 0x12345678, ref promDisabled);
            uint vLast = ucode.BusAdaptor.Read(tvScreenLo + 0x7FFF);
            Assert(vLast == 0x12345678, $"last valid word (offset 0x7FFF) round-trips through BusAdaptor, got 0x{vLast:X}");

            Console.WriteLine("  TV-screen-read-write-through-BusAdaptor test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  TV-screen-read-write-through-BusAdaptor test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestTvModeRegisterThroughBusAdaptor()
    {
        Console.WriteLine("Test: TV mode register read/write through BusAdaptor's real register interface round-trips");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            bool promDisabled = false;

            // TvControlLo = 0x3DFFF0 (BusAdaptor's own constant), offset 0 is the mode register.
            uint tvControlLo = 0x3DFFF0;
            ucode.BusAdaptor.Write(tvControlLo + 0, 0xFFFFFFFF, ref promDisabled);
            uint v = ucode.BusAdaptor.Read(tvControlLo + 0);
            Assert(v == 0xFFFFFFFF, $"mode register round-trips through BusAdaptor with no masking, got 0x{v:X}");

            Console.WriteLine("  TV-mode-register-through-BusAdaptor test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  TV-mode-register-through-BusAdaptor test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestTvCheckerboardScreenshotThroughBusAdaptor()
    {
        Console.WriteLine("Test: a checkerboard pattern written through BusAdaptor and saved via Tv.SaveScreenshotPbm matches an independently-computed PBM");
        string path = Path.GetTempFileName();
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            bool promDisabled = false;
            uint tvScreenLo = 0x3C0000;

            // Row-dependent pattern -- alternate the word value by row (0xAAAAAAAA on
            // even rows, 0x55555555 on odd rows), not the same value everywhere. Width
            // (768) is a multiple of 32 (24 words per row), so a single repeated word
            // value would make every row identical -- unable to catch a row-stride or
            // word-permutation bug, only basic bit order.
            uint wordsPerRow = ucode.BusAdaptor.Tv.Width / 32;
            uint visibleWords = ucode.BusAdaptor.Tv.Width * ucode.BusAdaptor.Tv.Height / 32;
            for (uint i = 0; i < visibleWords; i++)
            {
                uint row = i / wordsPerRow;
                uint wordValue = (row % 2 == 0) ? 0xAAAAAAAAu : 0x55555555u;
                ucode.BusAdaptor.Write(tvScreenLo + i, wordValue, ref promDisabled);
            }

            ucode.BusAdaptor.Tv.SaveScreenshotPbm(path);

            var expected = new System.Text.StringBuilder();
            uint width = ucode.BusAdaptor.Tv.Width, height = ucode.BusAdaptor.Tv.Height;
            expected.Append("P1\n").Append(width).Append(' ').Append(height).Append('\n');
            for (uint i = 0; i < width * height; i++)
            {
                uint wordIndex = i / 32;
                uint row = wordIndex / wordsPerRow;
                int bitpos = (int)(i % 32);
                uint wordValue = (row % 2 == 0) ? 0xAAAAAAAAu : 0x55555555u;
                bool bit = (wordValue & (1u << bitpos)) != 0;
                expected.Append(bit ? '1' : '0');
                if (i % 70 == 0 && i > 0) expected.Append('\n');
            }

            string actual = File.ReadAllText(path);
            Assert(actual == expected.ToString(), "checkerboard screenshot written through BusAdaptor matches independently-computed PBM exactly");

            Console.WriteLine("  TV-checkerboard-screenshot-through-BusAdaptor test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  TV-checkerboard-screenshot-through-BusAdaptor test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestTvControlHiAndTvScreenBoundaries()
    {
        Console.WriteLine("Test: TvControlHi (last control offset, invalid) sets Xbus NXM; TvScreenHi+1 falls through to the generic placeholder, not Tv");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            bool promDisabled = false;

            // (a) TvControlHi = TvControlLo + 7 (0x3DFFF7) -- offset 7 relative to
            // TvControlLo, which is NOT one of Tv.ControlRead/ControlWrite's handled
            // offsets (0-3 only; 4-7 all hit the default case). Confirm this reaches
            // Tv's real default case and sets Xbus NXM via BusAdaptor's real register
            // interface, not a stub.
            uint tvControlHi = 0x3DFFF7;
            Assert(!ucode.BusInterface.IsXbusNxm(), "sanity: Xbus NXM starts clear");
            ucode.BusAdaptor.Write(tvControlHi, 0x1, ref promDisabled);
            Assert(ucode.BusInterface.IsXbusNxm(), "write to TvControlHi (invalid offset 7) sets Xbus NXM");

            uint readAfter = ucode.BusAdaptor.Read(tvControlHi);
            Assert(readAfter == 0, $"read from TvControlHi (invalid offset 7) returns 0 (documented deviation), got 0x{readAfter:X}");
            Assert(!promDisabled, "TV control writes never touch promDisabled");

            // (b) TvScreenHi + 1 (0x3C8000) is one past the last valid TV-screen
            // address -- it must NOT reach Tv at all (by definition, since it's
            // outside TvScreenLo..TvScreenHi). Confirm it falls through to
            // BusAdaptor's generic placeholder path instead: a safe-default read of
            // 0 and a non-throwing write, with the TV screen buffer itself
            // untouched (Tv has no way to receive this address, so there's nothing
            // of Tv's to assert on here -- only the generic fallback's behavior).
            uint tvScreenHiPlusOne = 0x3C8000;
            uint unmappedRead = ucode.BusAdaptor.Read(tvScreenHiPlusOne);
            Assert(unmappedRead == 0, $"TvScreenHi+1 read falls through to the generic placeholder's safe default (0), got 0x{unmappedRead:X}");
            ucode.BusAdaptor.Write(tvScreenHiPlusOne, 0xDEADBEEF, ref promDisabled);
            Assert(!promDisabled, "TvScreenHi+1 write (generic placeholder) never touches promDisabled");

            Console.WriteLine("  TvControlHi/TvScreenHi-boundary tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  TvControlHi/TvScreenHi-boundary tests failed: {ex.Message}\n");
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
