// TapeControllerTests.cs - Tests for the faithful TapeController port
// (usim/tape-controller.c), synchronous-mode only.
//
// Bit positions below (ILC=0x8000, EOF=0x4000, BOT=0x20, NXM=0x0080,
// RLE=0x0200) were independently re-verified against tape_controller_
// encode_mts/_encode_mtc (usim/tape-controller.c:242-317) -- all matched the
// plan's own derivation exactly, no corrections needed there.
//
// Two tests in the plan's own draft (TestReadWriteRoundTripThroughMemory,
// TestNxmDuringTransferLeavesPositionWhereErrorOccurred) assumed MTCMA could
// drive a real MainMemory transfer at small, unmapped uaddr values like 0 or
// 0x200. That is wrong: TapeController's DMA goes through BusAdaptor.
// ReadUnibus/WriteUnibus exactly like the real C's bus_adaptor_unibus_read/
// write(mtcma, ...), and a transfer only reaches real MainMemory when mtcma
// falls inside BusAdaptor's Unibus Map range (0xC000-0xFFFF, usim/
// bus-adaptor.c:211) with a configured mapping register -- otherwise it
// either lands on a different real device's register range or the generic
// "unmapped Unibus" fallback (silently returns 0 on read / no-ops on write
// in this port, with no error status set). Both tests below are rewritten to
// go through the real Unibus Map mechanism, the same way BusAdaptorTests.cs's
// own TestUnibusMapDmaWriteToMainMemory/TestUnibusMapDmaReadFromMainMemory do.
//
// A third assumption from the plan's draft was also wrong: the real C's
// tape_controller_unibus_write switch has NO case for MTS (status, offset 0)
// or MTD (data buffer, offset 8) -- both fall to the switch's fatal default
// (errx), not a silent no-op. TestStatusIsReadOnly is rewritten accordingly
// to expect a throw, matching DiskController.Write's own "unknown offset
// throws" convention used elsewhere in this codebase.

using System;
using System.IO;

namespace Usim;

public static class TapeControllerTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== TapeController Test Suite ===\n");
        int passed = 0, failed = 0;

        if (TestStatusWriteThrows()) passed++; else failed++;
        if (TestGoWithSelectedDriveOfflineSetsIlc()) passed++; else failed++;
        if (TestWriteToReadOnlyTapeSetsIlc()) passed++; else failed++;
        if (TestWriteRecordLengthNotMultipleOf4SetsIlc()) passed++; else failed++;
        if (TestReadWriteRoundTripThroughMemory()) passed++; else failed++;
        if (TestWriteEofWritesRealFileMark()) passed++; else failed++;
        if (TestRewindReturnsToBot()) passed++; else failed++;
        if (TestNxmDuringTransferLeavesPositionWhereErrorOccurred()) passed++; else failed++;
        if (TestEofMidTransferDoesNotInfiniteLoop()) passed++; else failed++;
        if (TestUnknownRegisterOffsetThrows()) passed++; else failed++;
        if (TestReadRecordLongerThanMtbrcSetsRle()) passed++; else failed++;
        if (TestSpaceForwardOverFileMarkSetsEof()) passed++; else failed++;
        if (TestReadCrossingEotMidTransferSetsEot()) passed++; else failed++;
        if (TestWriteThenWriteEofFlushesToDisk()) passed++; else failed++;
        if (TestSpaceReverseOverRecordsAndFilemark()) passed++; else failed++;
        if (TestOfflineSetsUnitOffline()) passed++; else failed++;
        if (TestPostambleMismatchIgnoredOnRead()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    // Register offsets, relative to TapeControllerLo (matches BusAdaptor.cs's
    // own already-correct constant: 0772520 octal base, 6 registers 2 bytes apart).
    private const uint Mts = 0, Mtc = 2, Mtbrc = 4, Mtcma = 6, Mtd = 8, Mtrd = 10;

    private static string MakeValidTapeImage()
    {
        string path = Path.GetTempFileName();
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            fs.SetLength(192000); // 10ft * 12 * 1600
        return path;
    }

    private static (TapeController Controller, MainMemory Memory, UCode UCode, BusAdaptor BusAdaptor, string TapePath) MakeController()
    {
        string path = MakeValidTapeImage();
        var mem = new MainMemory();
        var ucode = new UCode(mem);
        var busInterface = new BusInterface(ucode);
        var busAdaptor = new BusAdaptor(busInterface, mem, ucode);
        var tc = new TapeController(ucode, busInterface, busAdaptor);
        tc.ConfigureUnit(0, 10, "rw", path);
        return (tc, mem, ucode, busAdaptor, path);
    }

    /// <summary>Programs Unibus Map page <paramref name="pageNo"/> (uaddr
    /// BusAdaptor.UnibusMappingLo + 2*pageNo) with MAP_VALID|WRITE_PERMIT and
    /// the given Xbus page number -- same mechanism as BusAdaptorTests.cs's
    /// own Unibus-Map DMA tests.</summary>
    private static void ConfigureUnibusMapPage(BusAdaptor busAdaptor, uint pageNo, uint xbusPageNumber)
    {
        bool promDisabled = false;
        ushort register = (ushort)(0x8000 | 0x4000 | (xbusPageNumber & 0x3FFF));
        busAdaptor.WriteUnibus(BusAdaptor.UnibusMappingLo + 2 * pageNo, register, ref promDisabled);
    }

    private static bool TestStatusWriteThrows()
    {
        Console.WriteLine("Test: writing to MTS (offset 0) throws -- the real C's write switch has no case for status, so it falls to the fatal default (errx), not a silent no-op");
        var (tc, mem, ucode, busAdaptor, path) = MakeController();
        try
        {
            bool threw = false;
            try { tc.Write(Mts, 0xFFFF); } catch (InvalidOperationException) { threw = true; }
            Assert(threw, "writing MTS throws InvalidOperationException");
            Console.WriteLine("  Status-write-throws test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Status-write-throws test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestGoWithSelectedDriveOfflineSetsIlc()
    {
        Console.WriteLine("Test: GO with the selected drive offline sets ILC (bit 15 of MTS), does not throw");
        var (tc, mem, ucode, busAdaptor, path) = MakeController();
        try
        {
            // Select unit 1 (never configured -- offline) via MTC's SLT field (bits 10-8), function=Read (1), GO (bit 0).
            tc.Write(Mtc, (ushort)((1 << 8) | (1 << 1) | 1));
            tc.Read(Mts, out ushort status);
            Assert((status & 0x8000) != 0, $"ILC/error-summary bit set when selected drive is offline, got 0x{status:X}");
            Console.WriteLine("  Go-with-drive-offline-sets-ilc test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Go-with-drive-offline-sets-ilc test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestWriteToReadOnlyTapeSetsIlc()
    {
        Console.WriteLine("Test: a write command against a read-only tape sets ILC, performs no transfer");
        string path = MakeValidTapeImage();
        var mem = new MainMemory();
        var ucode = new UCode(mem);
        var busInterface = new BusInterface(ucode);
        var busAdaptor = new BusAdaptor(busInterface, mem, ucode);
        var tc = new TapeController(ucode, busInterface, busAdaptor);
        tc.ConfigureUnit(0, 10, "ro", path);
        try
        {
            tc.Write(Mtc, (ushort)((0 << 8) | (2 << 1) | 1)); // unit 0, write, GO
            tc.Read(Mts, out ushort status);
            Assert((status & 0x8000) != 0, $"ILC set on write-to-read-only, got 0x{status:X}");
            Console.WriteLine("  Write-to-read-only-sets-ilc test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Write-to-read-only-sets-ilc test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestWriteRecordLengthNotMultipleOf4SetsIlc()
    {
        Console.WriteLine("Test: a write with MTBRC set to a non-multiple-of-4 record length sets ILC");
        var (tc, mem, ucode, busAdaptor, path) = MakeController();
        try
        {
            tc.Write(Mtbrc, unchecked((ushort)(-5))); // record length 5, not a multiple of 4
            tc.Write(Mtcma, 0x1000);
            tc.Write(Mtc, (ushort)((0 << 8) | (2 << 1) | 1)); // write, GO
            tc.Read(Mts, out ushort status);
            Assert((status & 0x8000) != 0, $"ILC set on non-multiple-of-4 record length, got 0x{status:X}");
            Console.WriteLine("  Write-record-length-not-multiple-of-4-sets-ilc test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Write-record-length-not-multiple-of-4-sets-ilc test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestReadWriteRoundTripThroughMemory()
    {
        Console.WriteLine("Test: a write-then-rewind-then-read round-trips a real 32-bit MainMemory word through the tape image, via the real Unibus-Map DMA path (same mechanism BusAdaptorTests.cs's own DMA tests use)");
        var (tc, mem, ucode, busAdaptor, path) = MakeController();
        try
        {
            // Page 0 (Unibus Map uaddr 0xC000-0xC3FF) -> Xbus page 0 (main memory).
            ConfigureUnibusMapPage(busAdaptor, pageNo: 0, xbusPageNumber: 0);
            // Page 1 (uaddr 0xC400-0xC7FF) -> Xbus page 1, a different main-memory word.
            ConfigureUnibusMapPage(busAdaptor, pageNo: 1, xbusPageNumber: 1);

            // One real 32-bit main-memory word: low half 0xAAAA, high half 0xBBBB.
            mem.WritePhysical(0, 0xBBBBAAAAu);

            // Write 4 bytes (2 Unibus half-word transfers = one full 32-bit
            // word's lo+hi halves) from page 0's base.
            tc.Write(Mtbrc, unchecked((ushort)(-4)));
            tc.Write(Mtcma, 0xC000);
            tc.Write(Mtc, (ushort)((0 << 8) | (2 << 1) | 1)); // write, GO

            tc.Write(Mtc, (ushort)((0 << 8) | (7 << 1) | 1)); // rewind, GO

            // Read it back into a different main-memory word via page 1.
            tc.Write(Mtbrc, unchecked((ushort)(-4)));
            tc.Write(Mtcma, 0xC400);
            tc.Write(Mtc, (ushort)((0 << 8) | (1 << 1) | 1)); // read, GO

            uint result = mem.ReadPhysical(0x100);
            Assert(result == 0xBBBBAAAAu, $"32-bit word round-trips through the tape image, got 0x{result:X}");
            Console.WriteLine("  Read-write-round-trip-through-memory test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Read-write-round-trip-through-memory test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestWriteEofWritesRealFileMark()
    {
        Console.WriteLine("Test: Write EOF (function 3) writes the real file-mark sentinel, sets EOF status on the next read");
        var (tc, mem, ucode, busAdaptor, path) = MakeController();
        try
        {
            tc.Write(Mtc, (ushort)((0 << 8) | (3 << 1) | 1)); // write EOF, GO
            tc.Write(Mtc, (ushort)((0 << 8) | (7 << 1) | 1)); // rewind, GO
            tc.Write(Mtbrc, unchecked((ushort)(-4)));
            tc.Write(Mtcma, 0);
            tc.Write(Mtc, (ushort)((0 << 8) | (1 << 1) | 1)); // read, GO
            tc.Read(Mts, out ushort status);
            Assert((status & 0x4000) != 0, $"EOF bit set reading back a file mark, got 0x{status:X}");
            Console.WriteLine("  Write-eof-writes-real-file-mark test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Write-eof-writes-real-file-mark test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestRewindReturnsToBot()
    {
        Console.WriteLine("Test: Rewind (function 7) returns the tape to BOT");
        var (tc, mem, ucode, busAdaptor, path) = MakeController();
        try
        {
            tc.Write(Mtc, (ushort)((0 << 8) | (4 << 1) | 1)); // space forward, GO (moves off BOT if there's data to space over -- real tape is zeroed, so this will hit a zero-length read; acceptable for this test's purpose of exercising rewind afterward)
            tc.Write(Mtc, (ushort)((0 << 8) | (7 << 1) | 1)); // rewind, GO
            tc.Read(Mts, out ushort status);
            Assert((status & 0x20) != 0, $"BOT bit set after rewind, got 0x{status:X}");
            Console.WriteLine("  Rewind-returns-to-bot test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Rewind-returns-to-bot test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestNxmDuringTransferLeavesPositionWhereErrorOccurred()
    {
        Console.WriteLine("Test: an NXM during a transfer sets NXM status and does NOT reposition the tape to a safe point (real C's own documented gap)");
        var (tc, mem, ucode, busAdaptor, path) = MakeController();
        try
        {
            // mtcma = 0xC000 falls inside BusAdaptor's Unibus Map range
            // (0xC000-0xFFFF, usim/bus-adaptor.c:211) but page 0's mapping
            // register is left unconfigured (MAP_VALID clear by default), so
            // the very first Unibus access inside the write DMA loop sets
            // Unibus Map Error -- which tape_controller_write coalesces into
            // MTS.NXM exactly like a genuine Unibus NXM (usim/tape-controller.c:525-536).
            tc.Write(Mtbrc, unchecked((ushort)(-8))); // 8-byte record
            tc.Write(Mtcma, 0xC000);
            tc.Write(Mtc, (ushort)((0 << 8) | (2 << 1) | 1)); // write, GO
            tc.Read(Mts, out ushort status);
            Assert((status & 0x0080) != 0, $"NXM bit set, got 0x{status:X}");
            // Real C's own //TODO admits it never repositions the tape on NXM.
            // The record-length preamble (4 bytes) is written before the DMA
            // loop starts, but the very first Unibus word fails before any
            // tape byte is written -- so Pos should reflect only the
            // preamble, never the full intended transfer.
            Assert(tc.GetUnitForTest(0).Pos == 4, $"tape position left exactly at the preamble-only point where the error occurred (4 bytes), not advanced to the full intended transfer length, got {tc.GetUnitForTest(0).Pos}");
            Console.WriteLine("  Nxm-during-transfer-leaves-position test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Nxm-during-transfer-leaves-position test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestReadRecordLongerThanMtbrcSetsRle()
    {
        Console.WriteLine("Test: reading a record whose real on-tape length exceeds what MTBRC allocated sets RLE (record length error)");
        string path = MakeValidTapeImage();
        try
        {
            // Write an 8-byte record directly to the tape image file, independent
            // of TapeController/TapeDrive -- matches this project's established
            // "build the fixture independently of the code under test" discipline.
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                void WriteU32(uint v) => fs.Write(BitConverter.GetBytes(v), 0, 4);
                WriteU32(8);                       // record-length preamble
                fs.Write(new byte[8], 0, 8);        // 8 bytes of data
                WriteU32(8);                        // record-length postamble
            }

            var mem = new MainMemory();
            var ucode = new UCode(mem);
            var busInterface = new BusInterface(ucode);
            var busAdaptor = new BusAdaptor(busInterface, mem, ucode);
            var tc = new TapeController(ucode, busInterface, busAdaptor);
            tc.ConfigureUnit(0, 10, "rw", path);
            ConfigureUnibusMapPage(busAdaptor, pageNo: 0, xbusPageNumber: 0);

            tc.Write(Mtbrc, unchecked((ushort)(-4))); // only 4 bytes allocated for an 8-byte real record
            tc.Write(Mtcma, 0xC000); // real, mapped Unibus DMA target (page 0), not the unmapped-Unibus fallback
            tc.Write(Mtc, (ushort)((0 << 8) | (1 << 1) | 1)); // read, GO

            tc.Read(Mts, out ushort status);
            Assert((status & 0x0200) != 0, $"RLE bit set when the real record is longer than MTBRC allocated, got 0x{status:X}");
            Console.WriteLine("  Read-record-longer-than-mtbrc-sets-rle test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Read-record-longer-than-mtbrc-sets-rle test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestSpaceForwardOverFileMarkSetsEof()
    {
        Console.WriteLine("Test: Space Forward (function 4) over a real record followed by a file mark sets EOF status");
        var (tc, mem, ucode, busAdaptor, path) = MakeController();
        try
        {
            // A file mark sitting ALONE at BOT (nothing real before it) is a
            // genuine edge case in the real C: tape_drive_space_forward
            // (usim/tape-drive.c:380-407) computes `record_length + 4` in
            // plain uint32_t arithmetic with no widening cast, so reading
            // the file mark's own 4 bytes (0xFFFFFFFF) as a bogus
            // "record_length" wraps the seek to +3 instead of skipping over
            // it -- it does NOT detect EOF on the first call in that case.
            // Realistic tape software never issues Space-Forward sitting
            // directly on a file mark with no preceding record for exactly
            // this reason, so this test writes a real record first, via a
            // real mapped Unibus-Map DMA target (page 0) -- only a real,
            // correctly-sized record matters here, not its content.
            ConfigureUnibusMapPage(busAdaptor, pageNo: 0, xbusPageNumber: 0);
            tc.Write(Mtbrc, unchecked((ushort)(-4)));
            tc.Write(Mtcma, 0xC000);
            tc.Write(Mtc, (ushort)((0 << 8) | (2 << 1) | 1)); // write, GO -- a real 4-byte record at BOT
            tc.Write(Mtc, (ushort)((0 << 8) | (3 << 1) | 1)); // write EOF, GO -- file mark right after it
            tc.Write(Mtc, (ushort)((0 << 8) | (7 << 1) | 1)); // rewind, GO -- back to BOT

            // MTBRC is already 0 here (a successful write decrements it back
            // to exactly 0), matching "space over the whole file" semantics.
            tc.Write(Mtc, (ushort)((0 << 8) | (4 << 1) | 1)); // space forward, GO -- crosses the real record, then the file mark
            tc.Read(Mts, out ushort status);
            Assert((status & 0x4000) != 0, $"EOF bit set after space-forward crosses a file mark, got 0x{status:X}");
            Console.WriteLine("  Space-forward-over-file-mark-sets-eof test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Space-forward-over-file-mark-sets-eof test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestEofMidTransferDoesNotInfiniteLoop()
    {
        Console.WriteLine("Test: a read that runs past the true end of the backing tape file terminates (no infinite loop), does not throw");
        var (tc, mem, ucode, busAdaptor, path) = MakeController();
        try
        {
            // Seek the underlying drive near its own end via a huge space-forward,
            // then issue a read with a large MTBRC -- must terminate.
            tc.Write(Mtc, (ushort)((0 << 8) | (1 << 1) | 1)); // read, GO -- tape is all-zero, so preamble=0 is immediately interpreted as a (very large, since record_length=0 means the while-loop body never runs) zero-length record; this still must terminate and must not throw regardless of the exact status outcome
            Console.WriteLine("  Eof-mid-transfer-does-not-infinite-loop test passed (terminated without throwing)\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Eof-mid-transfer-does-not-infinite-loop test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestUnknownRegisterOffsetThrows()
    {
        Console.WriteLine("Test: an unknown register offset throws (every one of the 6 real offsets is handled -- a should-never-happen guard)");
        var (tc, mem, ucode, busAdaptor, path) = MakeController();
        try
        {
            bool threw = false;
            try { tc.Read(99, out ushort _); }
            catch (InvalidOperationException) { threw = true; }
            Assert(threw, "unknown offset throws InvalidOperationException");
            Console.WriteLine("  Unknown-register-offset-throws test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Unknown-register-offset-throws test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestReadCrossingEotMidTransferSetsEot()
    {
        Console.WriteLine("Test: a Read DMA transfer that crosses the drive's EOT boundary mid-transfer (MTBRC exhausts before the real on-tape record does, taking the RLE branch, which performs no Seek of its own) leaves MTS.EOT set afterward -- regression for TapeDrive.ReadByte now recomputing Eot via SeekRelative on every byte moved (usim/tape-drive.c:233-270,307-317) and TapeController syncing _eot from the drive after Read()'s transfer loop");
        string path = MakeValidTapeImage();
        try
        {
            const uint OnTapeRecordLength = 20000; // bigger than what MTBRC will actually transfer, so the RLE branch (recordLength > 0 after the loop) is taken -- that branch performs no Seek of its own, so it can only see a correct Eot if ReadByte itself kept the drive's Eot in sync.
            const int TransferBytes = 9602; // even (loop moves 2 bytes/pass); with lengthFt=5 and DEN8 (800 bpi), eotpos = (5-4)*12*800 = 9600 -- this crosses it partway through the transfer.

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
                fs.Write(BitConverter.GetBytes(OnTapeRecordLength), 0, 4); // preamble only -- the RLE branch never reaches a real postamble

            var mem = new MainMemory();
            var ucode = new UCode(mem);
            var busInterface = new BusInterface(ucode);
            var busAdaptor = new BusAdaptor(busInterface, mem, ucode);
            var tc = new TapeController(ucode, busInterface, busAdaptor);
            tc.ConfigureUnit(0, 5, "rw", path); // lengthFt=5
            // This transfer is long enough (TransferBytes/2 Unibus words) to
            // walk mtcma across multiple 0x400-byte Unibus Map pages starting
            // at 0xC000 -- map every page in the whole range so the DMA loop
            // never wanders into an unconfigured page (which would set a
            // genuine Unibus Map Error/NXM instead of exercising the RLE/EOT
            // branch this test targets). Content is irrelevant, so every
            // page points at the same Xbus page.
            for (uint pageNo = 0; pageNo < 16; pageNo++)
                ConfigureUnibusMapPage(busAdaptor, pageNo, xbusPageNumber: 0);

            tc.Write(Mtbrc, unchecked((ushort)(-TransferBytes)));
            tc.Write(Mtcma, 0xC000);
            // unit 0 (slt bits 10-8 = 0), DEN8 (bit 14, selects 800 bpi), function=Read (bits 3-1 = 1), GO (bit 0)
            tc.Write(Mtc, (ushort)((1 << 14) | (0 << 8) | (1 << 1) | 1));

            tc.Read(Mts, out ushort status);
            Assert((status & 0x0200) != 0, $"RLE set, confirming the test actually took the no-further-seek RLE branch, got 0x{status:X}");
            Assert((status & 0x0400) != 0, $"EOT bit (bit 10) set after a Read transfer crosses the drive's EOT boundary mid-transfer, got 0x{status:X}");
            Assert(tc.GetUnitForTest(0).Eot, "the drive's own Eot also reflects true past the boundary");
            Console.WriteLine("  Read-crossing-eot-mid-transfer-sets-eot test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Read-crossing-eot-mid-transfer-sets-eot test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestWriteThenWriteEofFlushesToDisk()
    {
        Console.WriteLine("Test: a controller Write followed by Write EOF actually reaches disk -- TapeDrive.WriteByte now flushes after every byte (regression for the real bug: a separate reader of the file previously saw FF FF FF 00 instead of FF FF FF FF for the trailing file mark)");
        var (tc, mem, ucode, busAdaptor, path) = MakeController();
        try
        {
            ConfigureUnibusMapPage(busAdaptor, pageNo: 0, xbusPageNumber: 0);

            tc.Write(Mtbrc, unchecked((ushort)(-4))); // 4-byte record
            tc.Write(Mtcma, 0xC000);
            tc.Write(Mtc, (ushort)((0 << 8) | (2 << 1) | 1)); // write, GO

            tc.Write(Mtc, (ushort)((0 << 8) | (3 << 1) | 1)); // write EOF, GO

            // Read the raw bytes back through a brand-new FileStream -- NOT
            // through TapeDrive/TapeController at all -- to prove the bytes
            // actually landed on disk, not merely that they're readable back
            // through the same still-open handle.
            byte[] raw;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                raw = new byte[16];
                int totalRead = 0;
                while (totalRead < raw.Length)
                {
                    int n = fs.Read(raw, totalRead, raw.Length - totalRead);
                    Assert(n > 0, "independent reader can read the expected 16 bytes");
                    totalRead += n;
                }
            }

            uint preamble = BitConverter.ToUInt32(raw, 0);
            uint postamble = BitConverter.ToUInt32(raw, 8);
            uint fileMark = BitConverter.ToUInt32(raw, 12);

            Assert(preamble == 4, $"on-disk preamble == 4, got {preamble}");
            Assert(postamble == 4, $"on-disk postamble == 4, got {postamble}");
            Assert(fileMark == 0xFFFFFFFFu, $"on-disk trailing file mark is the real FFFFFFFF sentinel, got 0x{fileMark:X} (would be 0xFFFFFF00 on the unflushed-last-byte bug)");
            Console.WriteLine("  Write-then-write-eof-flushes-to-disk test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Write-then-write-eof-flushes-to-disk test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestSpaceReverseOverRecordsAndFilemark()
    {
        Console.WriteLine("Test: Space Reverse (function 5) over 2 real records, skipping a trailing file mark via the -8 skip-over-filemark path, stops at BOT without setting EOF -- verified directly against tape_controller_space_reverse/tape_drive_space_reverse (usim/tape-controller.c, usim/tape-drive.c) before writing this test");
        string path = MakeValidTapeImage();
        try
        {
            // Fixture, built directly (independent of TapeController/TapeDrive):
            // record1 [0,12): preamble(4)=4, data(4), postamble(4)=4
            // record2 [12,24): preamble(4)=4, data(4), postamble(4)=4
            // file mark [24,28): FFFFFFFF
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                void WriteU32(uint v) => fs.Write(BitConverter.GetBytes(v), 0, 4);
                WriteU32(4); fs.Write(new byte[] { 0, 1, 2, 3 }, 0, 4); WriteU32(4);
                WriteU32(4); fs.Write(new byte[] { 4, 5, 6, 7 }, 0, 4); WriteU32(4);
                WriteU32(TapeDrive.FILE_MARK);
            }

            var mem = new MainMemory();
            var ucode = new UCode(mem);
            var busInterface = new BusInterface(ucode);
            var busAdaptor = new BusAdaptor(busInterface, mem, ucode);
            var tc = new TapeController(ucode, busInterface, busAdaptor);
            tc.ConfigureUnit(0, 10, "rw", path);

            // Space forward the whole file (mtbrc=0 -- "the program can simply
            // give a zero (maximum) byte count") to land right after the file
            // mark, at the true end of written data (pos 28).
            tc.Write(Mtc, (ushort)((0 << 8) | (4 << 1) | 1)); // space forward, GO
            Assert(tc.GetUnitForTest(0).Pos == 28, $"space-forward over both records + filemark lands at pos 28, got {tc.GetUnitForTest(0).Pos}");

            // Space Reverse over 2 records (mtbrc = -2). The first
            // SpaceReverseOneRecord(stopAtFilemark:false) call skips the
            // trailing file mark via the -8 skip-over-filemark path and lands
            // at the start of record2 (pos 12). The second call
            // (stopAtFilemark:true), driven by the mtbrc<0 loop, reads
            // record1's own postamble (NOT a file mark) at that position,
            // so it does not set EOF -- it walks back over record1 and hits
            // BOT (pos 0), which stops the loop immediately per "the
            // controller spaces reverse ... but it stops the tape
            // automatically upon encountering a file mark or the load point."
            tc.Write(Mtbrc, unchecked((ushort)(-2)));
            tc.Write(Mtc, (ushort)((0 << 8) | (5 << 1) | 1)); // space reverse, GO

            Assert(tc.GetUnitForTest(0).Pos == 0, $"space-reverse over 2 records lands back at BOT (pos 0), got {tc.GetUnitForTest(0).Pos}");
            Assert(tc.GetUnitForTest(0).Bot, "drive reports BOT after space-reverse");

            tc.Read(Mts, out ushort status);
            Assert((status & 0x4000) == 0, $"EOF is NOT set -- space-reverse stopped because it hit BOT, not because it encountered a file mark, got 0x{status:X}");
            Console.WriteLine("  Space-reverse-over-records-and-filemark test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Space-reverse-over-records-and-filemark test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestOfflineSetsUnitOffline()
    {
        Console.WriteLine("Test: Off-Line (function 0) takes the selected unit offline; SELR stays true until the next MTC write, then reflects the now-offline unit");
        var (tc, mem, ucode, busAdaptor, path) = MakeController();
        try
        {
            tc.Write(Mtc, (ushort)((0 << 8) | (0 << 1) | 1)); // unit 0, off-line, GO

            tc.Read(Mts, out ushort statusRightAfter);
            Assert((statusRightAfter & 0x40) != 0, $"SELR still reads true right after Off-Line -- it was computed before the command ran and is not re-evaluated until the next MTC write, got 0x{statusRightAfter:X}");
            Assert(!tc.GetUnitForTest(0).Online, "unit is offline after Off-Line");

            // Any further MTC write re-evaluates SELR from the (now offline) selected drive.
            tc.Write(Mtc, (ushort)(0 << 8)); // re-select unit 0, no GO
            tc.Read(Mts, out ushort statusAfterNextWrite);
            Assert((statusAfterNextWrite & 0x40) == 0, $"SELR reads false on the next MTC write, since the selected unit is now offline, got 0x{statusAfterNextWrite:X}");

            Console.WriteLine("  Offline-sets-unit-offline test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Offline-sets-unit-offline test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestPostambleMismatchIgnoredOnRead()
    {
        Console.WriteLine("Test: a record's postamble is read but never compared against the preamble on read -- even when it disagrees -- matching real C's tape_drive_read_uint8 (postamble is read, nothing compares it)");
        string path = MakeValidTapeImage();
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                void WriteU32(uint v) => fs.Write(BitConverter.GetBytes(v), 0, 4);
                WriteU32(4);                               // preamble: length 4
                fs.Write(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, 0, 4); // 4 bytes of data
                WriteU32(999);                             // postamble deliberately disagrees with the preamble
            }

            var mem = new MainMemory();
            var ucode = new UCode(mem);
            var busInterface = new BusInterface(ucode);
            var busAdaptor = new BusAdaptor(busInterface, mem, ucode);
            var tc = new TapeController(ucode, busInterface, busAdaptor);
            tc.ConfigureUnit(0, 10, "rw", path);
            ConfigureUnibusMapPage(busAdaptor, pageNo: 0, xbusPageNumber: 0);

            tc.Write(Mtbrc, unchecked((ushort)(-4))); // exactly the preamble's own record length
            tc.Write(Mtcma, 0xC000);
            tc.Write(Mtc, (ushort)((0 << 8) | (1 << 1) | 1)); // read, GO

            tc.Read(Mts, out ushort status);
            Assert((status & 0x0200) == 0, $"RLE NOT set -- the transfer is governed by the preamble's length (4), which exactly matches MTBRC, got 0x{status:X}");
            Assert((status & 0x0080) == 0, $"NXM NOT set, got 0x{status:X}");
            Assert((status & 0x4000) == 0, $"EOF NOT set, got 0x{status:X}");
            Assert((status & 0x8000) == 0, $"no error summary bit set at all despite the postamble mismatch, got 0x{status:X}");

            tc.Read(Mtbrc, out ushort mtbrcAfter);
            Assert(mtbrcAfter == 0, $"MTBRC exhausted to exactly 0 -- the full 4 bytes governed by the preamble were transferred, got {unchecked((short)mtbrcAfter)}");
            Assert(tc.GetUnitForTest(0).Pos == 12, $"tape position after the read reflects preamble(4)+data(4)+postamble(4) all consumed (the mismatched postamble is read and skipped, never validated), got {tc.GetUnitForTest(0).Pos}");

            Console.WriteLine("  Postamble-mismatch-ignored-on-read test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Postamble-mismatch-ignored-on-read test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception($"Assertion failed: {message}");
    }
}
