// TapeDriveTests.cs - Tests for the faithful TapeDrive port (usim/tape-drive.c).

using System;
using System.IO;

namespace Usim;

public static class TapeDriveTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== TapeDrive Test Suite ===\n");
        int passed = 0, failed = 0;

        if (TestConfigureMissingFileLeavesOffline()) passed++; else failed++;
        if (TestConfigureUndersizedFileLeavesOffline()) passed++; else failed++;
        if (TestConfigureValidFileComesOnline()) passed++; else failed++;
        if (TestConfigureUnopenableFileLeavesOffline()) passed++; else failed++;
        if (TestUnitIndexOverflowThrows()) passed++; else failed++;
        if (TestReadWriteByteRoundTrip()) passed++; else failed++;
        if (TestReadUInt32LittleEndian()) passed++; else failed++;
        if (TestReadPastEndOfFileReturnsFalse()) passed++; else failed++;
        if (TestSeekClampsToFileBoundsAndSetsBot()) passed++; else failed++;
        if (TestSeekPastEotSetsEot()) passed++; else failed++;
        if (TestWriteFileMarkWritesRealSentinel()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    // A minimal valid tape image: all-zero bytes, sized for a short tape at
    // low density so the minimum-required-size check (length_ft*12*1600)
    // passes with a small file. 10 ft * 12 * 1600 = 192000 bytes.
    private static string MakeValidTapeImage(long sizeBytes = 192000)
    {
        string path = Path.GetTempFileName();
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            fs.SetLength(sizeBytes);
        return path;
    }

    private static bool TestConfigureMissingFileLeavesOffline()
    {
        Console.WriteLine("Test: Configure with a nonexistent file leaves the unit offline, does not throw");
        try
        {
            var td = new TapeDrive(0);
            td.Configure(10, "rw", Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".img"));
            Assert(!td.Online, "unit stays offline after a missing-file configure");
            Console.WriteLine("  Missing-file-leaves-offline test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Missing-file-leaves-offline test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestConfigureUndersizedFileLeavesOffline()
    {
        Console.WriteLine("Test: Configure with a too-small file leaves the unit offline, does not throw");
        string path = MakeValidTapeImage(100); // far below 10ft*12*1600
        try
        {
            var td = new TapeDrive(0);
            td.Configure(10, "rw", path);
            Assert(!td.Online, "unit stays offline when the file is smaller than the minimum required size");
            Console.WriteLine("  Undersized-file-leaves-offline test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Undersized-file-leaves-offline test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestConfigureValidFileComesOnline()
    {
        Console.WriteLine("Test: Configure with a correctly-sized file comes online");
        string path = MakeValidTapeImage();
        try
        {
            var td = new TapeDrive(0);
            td.Configure(10, "rw", path);
            Assert(td.Online, "unit comes online with a valid file");
            Assert(td.Bot, "a freshly-configured unit starts at BOT");
            Console.WriteLine("  Valid-file-comes-online test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Valid-file-comes-online test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestConfigureUnopenableFileLeavesOffline()
    {
        Console.WriteLine("Test: Configure on a file that cannot be opened read-write (permission denied) leaves the unit offline, does not throw");
        string path = MakeValidTapeImage();
        File.SetAttributes(path, FileAttributes.ReadOnly); // opening ReadWrite now throws UnauthorizedAccessException on Windows
        try
        {
            var td = new TapeDrive(0);
            td.Configure(10, "rw", path);
            Assert(!td.Online, "unit stays offline when the file cannot be opened read-write");
            Console.WriteLine("  Unopenable-file-leaves-offline test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Unopenable-file-leaves-offline test failed: {ex.Message}\n"); return false; }
        finally { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
    }

    private static bool TestUnitIndexOverflowThrows()
    {
        Console.WriteLine("Test: constructing a TapeDrive with unit >= NUMBER_OF_TAPE_DRIVES throws");
        try
        {
            bool threw = false;
            try { var td = new TapeDrive(TapeDrive.NUMBER_OF_TAPE_DRIVES); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            Assert(threw, "unit index >= 8 throws ArgumentOutOfRangeException");
            Console.WriteLine("  Unit-index-overflow-throws test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Unit-index-overflow-throws test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestReadWriteByteRoundTrip()
    {
        Console.WriteLine("Test: WriteByte then ReadByte (after seeking back) round-trips");
        string path = MakeValidTapeImage();
        try
        {
            var td = new TapeDrive(0);
            td.Configure(10, "rw", path);
            Assert(td.WriteByte(0xAB), "write succeeds");
            td.SeekAbsolute(0);
            Assert(td.ReadByte(out byte v), "read succeeds");
            Assert(v == 0xAB, $"round-tripped byte matches, got 0x{v:X}");
            Console.WriteLine("  Read-write-byte-round-trip test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Read-write-byte-round-trip test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestReadUInt32LittleEndian()
    {
        Console.WriteLine("Test: WriteUInt32 then ReadUInt32 round-trips as little-endian 4 bytes");
        string path = MakeValidTapeImage();
        try
        {
            var td = new TapeDrive(0);
            td.Configure(10, "rw", path);
            Assert(td.WriteUInt32(0x12345678), "write succeeds");
            td.SeekAbsolute(0);
            Assert(td.ReadUInt32(out uint v), "read succeeds");
            Assert(v == 0x12345678, $"round-tripped uint32 matches, got 0x{v:X}");
            Console.WriteLine("  Read-uint32-little-endian test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Read-uint32-little-endian test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestReadPastEndOfFileReturnsFalse()
    {
        Console.WriteLine("Test: ReadByte at the true end of the backing file returns false, does not throw");
        string path = MakeValidTapeImage(4); // tiny, 4 bytes
        try
        {
            var td = new TapeDrive(0);
            td.Configure(1, "rw", path); // 1ft*12*1600=19200 min -- too small, so configure manually bypassing the size check via direct field access isn't available; use SeekAbsolute to force pos to file end regardless of Online state for this unit test of ReadByte's own bounds check
            // Configure will leave this unit offline (file too small) -- that's fine,
            // ReadByte's own bounds check against the backing stream's length must still
            // work correctly once a stream exists. Re-configure with a correctly-sized
            // file instead, then seek to true EOF.
            File.Delete(path);
            path = MakeValidTapeImage(192000);
            td.Configure(10, "rw", path);
            td.SeekAbsolute(192000); // exactly at end
            Assert(!td.ReadByte(out byte _), "ReadByte at true EOF returns false");
            Console.WriteLine("  Read-past-end-of-file-returns-false test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Read-past-end-of-file-returns-false test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestSeekClampsToFileBoundsAndSetsBot()
    {
        Console.WriteLine("Test: SeekAbsolute/SeekRelative clamp to [0, fileSize] and update Bot as a side effect");
        string path = MakeValidTapeImage();
        try
        {
            var td = new TapeDrive(0);
            td.Configure(10, "rw", path);
            td.SeekRelative(-100); // would go negative
            Assert(td.Pos == 0, $"negative seek clamps to 0, got {td.Pos}");
            Assert(td.Bot, "clamped-to-0 position sets Bot");

            td.SeekAbsolute(192000 + 1000); // past file size
            Assert(td.Pos == 192000, $"past-end seek clamps to file size, got {td.Pos}");
            Assert(!td.Bot, "non-zero position clears Bot");

            Console.WriteLine("  Seek-clamps-and-sets-bot test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Seek-clamps-and-sets-bot test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestSeekPastEotSetsEot()
    {
        Console.WriteLine("Test: seeking at/past the computed EOT position sets Eot");
        string path = MakeValidTapeImage();
        try
        {
            var td = new TapeDrive(0);
            td.Configure(10, "rw", path); // 10ft, default bpi
            td.SetBpi(1600); // eotpos = (10-4)*12*1600 = 115200
            td.SeekAbsolute(115199);
            Assert(!td.Eot, "just before EOT, Eot is false");
            td.SeekAbsolute(115200);
            Assert(td.Eot, "at the computed EOT position, Eot is true");
            Console.WriteLine("  Seek-past-eot-sets-eot test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Seek-past-eot-sets-eot test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestWriteFileMarkWritesRealSentinel()
    {
        Console.WriteLine("Test: WriteFileMark writes the real 0xFFFFFFFF sentinel (not the real C comment's wrong 0x00000000)");
        string path = MakeValidTapeImage();
        try
        {
            var td = new TapeDrive(0);
            td.Configure(10, "rw", path);
            td.WriteFileMark();
            td.SeekAbsolute(0);
            Assert(td.ReadUInt32(out uint v), "read succeeds");
            Assert(v == 0xFFFFFFFF, $"file mark is the real TAPE_FILE_MARK sentinel 0xFFFFFFFF, got 0x{v:X}");
            Console.WriteLine("  Write-file-mark-writes-real-sentinel test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Write-file-mark-writes-real-sentinel test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception($"Assertion failed: {message}");
    }
}
