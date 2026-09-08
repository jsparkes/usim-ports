// DiskUnitTests.cs - Tests for the faithful DiskUnit port (usim/disk-unit.c).

using System;
using System.IO;

namespace Usim;

public static class DiskUnitTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== DiskUnit Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestConfigureUnknownType()) passed++; else failed++;
        if (TestConfigureMissingFile()) passed++; else failed++;
        if (TestConfigureWrongSizeFile()) passed++; else failed++;
        if (TestConfigureNotConfigured()) passed++; else failed++;
        if (TestSeekSuccessAndAlreadyThere()) passed++; else failed++;
        if (TestSeekOutOfRange()) passed++; else failed++;
        if (TestSeekNextLbaRollover()) passed++; else failed++;
        if (TestReadWriteRoundTrip()) passed++; else failed++;
        if (TestReadWritePastEndOfFile()) passed++; else failed++;
        if (TestDa()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    // Creates a sparse file of exactly the size a real T-80 pack expects
    // (815*5*17*1024 = 70,941,200 bytes) without writing that many real bytes.
    private static string MakeSparseT80Image()
    {
        string path = Path.GetTempFileName();
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
        {
            fs.SetLength(815L * 5 * 17 * 1024);
        }
        return path;
    }

    private static bool TestConfigureUnknownType()
    {
        Console.WriteLine("Test: Configure throws on an unknown disk unit type");
        try
        {
            var unit = new DiskUnit(0);
            bool threw = false;
            try { unit.Configure("NotARealType", "whatever.img"); }
            catch (InvalidOperationException) { threw = true; }
            Assert(threw, "unknown type throws InvalidOperationException");
            Assert(!unit.Online, "unit stays offline");

            Console.WriteLine("  Unknown-type test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Unknown-type test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestConfigureMissingFile()
    {
        Console.WriteLine("Test: Configure throws when the disk pack file doesn't exist");
        try
        {
            var unit = new DiskUnit(0);
            bool threw = false;
            try { unit.Configure("T-80", "/definitely/does/not/exist.img"); }
            catch (InvalidOperationException) { threw = true; }
            Assert(threw, "missing file throws InvalidOperationException");
            Assert(!unit.Online, "unit stays offline");

            Console.WriteLine("  Missing-file test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Missing-file test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestConfigureWrongSizeFile()
    {
        Console.WriteLine("Test: Configure throws when the disk pack file is the wrong size");
        string path = Path.GetTempFileName();
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                fs.SetLength(1024); // nowhere near a real T-80's expected size
            }

            var unit = new DiskUnit(0);
            bool threw = false;
            try { unit.Configure("T-80", path); }
            catch (InvalidOperationException) { threw = true; }
            Assert(threw, "wrong-size file throws InvalidOperationException");
            Assert(!unit.Online, "unit stays offline");

            Console.WriteLine("  Wrong-size-file test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Wrong-size-file test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool TestConfigureNotConfigured()
    {
        Console.WriteLine("Test: Configure with an empty type stays offline, unconfigured, no throw");
        try
        {
            var unit = new DiskUnit(0);
            unit.Configure("", "");
            Assert(!unit.Online, "stays offline");
            Assert(!unit.Configured, "stays unconfigured");

            Console.WriteLine("  Not-configured test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Not-configured test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestSeekSuccessAndAlreadyThere()
    {
        Console.WriteLine("Test: Seek succeeds and updates Lba; already-there is a no-op success");
        string path = MakeSparseT80Image();
        try
        {
            var unit = new DiskUnit(0);
            unit.Configure("T-80", path);

            Assert(unit.Seek(1, 2, 3), "seek to a valid position succeeds");
            Assert(!unit.SeekError, "no seek error");
            uint expectedLba = (1 * unit.BlocksPerCylinder) + (2 * unit.BlocksPerTrack) + 3;
            Assert(unit.Lba == expectedLba, $"Lba computed correctly, got {unit.Lba}, expected {expectedLba}");

            Assert(unit.Seek(1, 2, 3), "seeking to the same position again succeeds (no-op)");

            Console.WriteLine("  Seek-success test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Seek-success test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool TestSeekOutOfRange()
    {
        Console.WriteLine("Test: Seek sets SeekError for each out-of-range component");
        string path = MakeSparseT80Image();
        try
        {
            var unit = new DiskUnit(0);
            unit.Configure("T-80", path); // T-80: 815 cyl, 5 heads, 17 blocks/track

            Assert(!unit.Seek(815, 0, 0), "cylinder out of range fails");
            Assert(unit.SeekError, "SeekError set for cylinder");

            unit.SeekError = false;
            Assert(!unit.Seek(0, 5, 0), "head out of range fails");
            Assert(unit.SeekError, "SeekError set for head");

            unit.SeekError = false;
            Assert(!unit.Seek(0, 0, 17), "sector out of range fails");
            Assert(unit.SeekError, "SeekError set for sector");

            Console.WriteLine("  Seek-out-of-range test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Seek-out-of-range test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool TestSeekNextLbaRollover()
    {
        Console.WriteLine("Test: SeekNextLba rolls sector into head into cylinder");
        string path = MakeSparseT80Image();
        try
        {
            var unit = new DiskUnit(0);
            unit.Configure("T-80", path); // 5 heads, 17 blocks/track

            unit.Seek(0, 0, 16); // last sector of track 0
            Assert(unit.SeekNextLba(), "seek-next rolls sector -> head");
            Assert(unit.Sector == 0 && unit.Head == 1 && unit.Cylinder == 0, "rolled into head 1, sector 0");

            unit.Seek(0, 4, 16); // last sector, last head
            Assert(unit.SeekNextLba(), "seek-next rolls head -> cylinder");
            Assert(unit.Sector == 0 && unit.Head == 0 && unit.Cylinder == 1, "rolled into cylinder 1, head 0, sector 0");

            Console.WriteLine("  SeekNextLba-rollover test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  SeekNextLba-rollover test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool TestReadWriteRoundTrip()
    {
        Console.WriteLine("Test: Write then Read round-trips a 256-word block");
        string path = MakeSparseT80Image();
        try
        {
            var unit = new DiskUnit(0);
            unit.Configure("T-80", path);
            unit.Seek(3, 1, 5);

            var buffer = new uint[256];
            for (int i = 0; i < 256; i++) buffer[i] = (uint)(0x5000 + i);
            Assert(unit.Write(buffer), "write succeeds");

            var readBack = new uint[256];
            Assert(unit.Read(readBack), "read succeeds");
            for (int i = 0; i < 256; i++)
                Assert(readBack[i] == buffer[i], $"word {i} round-trips, got 0x{readBack[i]:X}");

            Console.WriteLine("  Read/write round-trip test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Read/write round-trip test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool TestReadWritePastEndOfFile()
    {
        Console.WriteLine("Test: Read/Write past end of file return false without throwing");
        try
        {
            var unit = new DiskUnit(0); // never configured -- no backing file at all
            var buffer = new uint[256];
            Assert(!unit.Read(buffer), "read with no backing file returns false");
            Assert(!unit.Write(buffer), "write with no backing file returns false");

            Console.WriteLine("  Past-end-of-file test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Past-end-of-file test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDa()
    {
        Console.WriteLine("Test: Da() encodes unit/cylinder/head/sector correctly");
        string path = MakeSparseT80Image();
        try
        {
            var unit = new DiskUnit(5);
            unit.Configure("T-80", path);
            unit.Seek(0x123, 0x2, 0x7);

            uint expected = (5u << 28) | (0x123u << 16) | (0x2u << 8) | 0x7u;
            Assert(unit.Da() == expected, $"Da() == 0x{expected:X}, got 0x{unit.Da():X}");

            Console.WriteLine("  Da() test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Da() test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
