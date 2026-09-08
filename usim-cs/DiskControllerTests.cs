// DiskControllerTests.cs - Tests for the faithful DiskController port
// (usim/disk-controller.c), synchronous-mode only.

using System;
using System.IO;

namespace Usim;

public static class DiskControllerTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== DiskController Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestEveryStartCommandPathRuns()) passed++; else failed++;
        if (TestUnimplementedAndUnknownCommandsThrow()) passed++; else failed++;
        if (TestEncodeStatusBitsDirect()) passed++; else failed++;
        if (TestResetProtocol()) passed++; else failed++;
        if (TestReadOnlyWriteSetsHasFaultNoTransfer()) passed++; else failed++;
        if (TestFullReadWriteTransferThroughMemory()) passed++; else failed++;
        if (TestNonexistentMemoryError()) passed++; else failed++;
        if (TestInterruptAssertAndDeassert()) passed++; else failed++;
        if (TestStartOnOfflineUnitIsSilentNoOp()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    // Duplicated from DiskUnitTests.cs deliberately -- no cross-file test
    // dependency. Creates a sparse file of exactly the size a real T-80
    // pack expects (815*5*17*1024 = 70,941,200 bytes).
    private static string MakeSparseT80Image()
    {
        string path = Path.GetTempFileName();
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
        {
            fs.SetLength(815L * 5 * 17 * 1024);
        }
        return path;
    }

    // Builds a DiskController wired to a real MainMemory and UCode, with
    // unit 0 configured against a sparse T-80 image.
    private static (DiskController Controller, MainMemory Memory, UCode UCode, string DiskPath) MakeController()
    {
        string path = MakeSparseT80Image();
        var mem = new MainMemory();
        var ucode = new UCode(mem);
        var dc = new DiskController(mem, ucode);
        dc.ConfigureUnit(0, "T-80", path);
        return (dc, mem, ucode, path);
    }

    private static uint EncodeDa(uint unit, uint cylinder, uint head, uint block) =>
        (unit << 28) | (cylinder << 16) | (head << 8) | block;

    private static bool TestEveryStartCommandPathRuns()
    {
        Console.WriteLine("Test: every Start() command path runs and produces the expected state transition");
        var (dc, mem, ucode, path) = MakeController();
        try
        {
            // Point clp at a single, non-chained CCW targeting an in-range page,
            // so read/write/read-compare commands have something harmless to do.
            uint clp = 0x002000;
            uint paddr = 0x000500; // page 5
            mem.WritePhysical(clp, paddr | 0); // chain bit clear -- last (only) ccw

            uint validDa = EncodeDa(0, 1, 2, 3);
            dc.Write(1, clp);
            dc.Write(2, validDa);

            // Read (cmd 0x0), done-interrupt enabled per brief's guidance.
            dc.Write(0, 0x800 | 0x0);
            dc.Write(3, 0);
            Assert((ucode.InterruptStatusReg & 0x4000) != 0, "read command's completion asserts the xbus interrupt (done-interrupt enabled)");
            Assert((dc.Read(0) & (1u << 0)) != 0, "controller reports idle (not-active) after read completes");

            // Read-compare (cmd 0x8).
            dc.Write(0, 0x800 | 0x8);
            dc.Write(3, 0);

            // Write (cmd 0x9).
            dc.Write(0, 0x800 | 0x9);
            dc.Write(3, 0);

            // Seek (cmd 0x4) to a valid position -- sets Attention, no SeekError.
            dc.Write(2, EncodeDa(0, 10, 2, 5));
            dc.Write(0, 0x800 | 0x4);
            dc.Write(3, 0);
            Assert((dc.Read(0) & (1u << 2)) != 0, "valid seek raises Attention on the selected unit");
            Assert((dc.Read(0) & (1u << 10)) == 0, "valid seek does not set SeekError");

            // At-ease (cmd 0x5) clears Attention.
            dc.Write(0, 0x800 | 0x5);
            dc.Write(3, 0);
            Assert((dc.Read(0) & (1u << 2)) == 0, "at-ease clears Attention");

            // Seek (cmd 0x4) OUT OF RANGE -- sets SeekError (and still raises Attention).
            dc.Write(2, EncodeDa(0, 900, 0, 0)); // cylinder 900 >= 815
            dc.Write(0, 0x800 | 0x4);
            dc.Write(3, 0);
            Assert((dc.Read(0) & (1u << 10)) != 0, "out-of-range seek sets SeekError");
            Assert((dc.Read(0) & (1u << 2)) != 0, "out-of-range seek still raises Attention");

            // Recalibrate (cmd 0x5 | 0x200) clears SeekError and re-raises Attention.
            dc.Write(0, 0x800 | 0x5 | 0x200);
            dc.Write(3, 0);
            Assert((dc.Read(0) & (1u << 10)) == 0, "recalibrate clears SeekError");
            Assert((dc.Read(0) & (1u << 2)) != 0, "recalibrate raises Attention");

            // Fault-clear (cmd 0x5 | 0x100): induce a fault directly (no public
            // API can set HasFault -- see GetUnitForTest's doc comment), then
            // confirm the command clears it.
            dc.GetUnitForTest(0).HasFault = true;
            dc.Write(0, 0x800 | 0x5 | 0x100);
            dc.Write(3, 0);
            Assert(!dc.GetUnitForTest(0).HasFault, "fault-clear command clears HasFault");

            // Combined at-ease + recalibrate + fault-clear bit pattern.
            dc.GetUnitForTest(0).HasFault = true;
            dc.Write(2, EncodeDa(0, 900, 0, 0)); // out of range again, to give SeekError something to clear
            dc.Write(0, 0x800 | 0x5 | 0x200 | 0x100);
            dc.Write(3, 0);
            Assert((dc.Read(0) & (1u << 10)) == 0, "combined command clears SeekError");
            Assert(!dc.GetUnitForTest(0).HasFault, "combined command clears HasFault");
            Assert((dc.Read(0) & (1u << 2)) != 0, "combined command raises Attention (recalibrate half)");

            // Offset-clear (cmd 0x6): a documented no-op.
            dc.Write(0, 0x800 | 0x6);
            dc.Write(3, 0);
            Assert((dc.Read(0) & (1u << 0)) != 0, "offset-clear completes (idle) without throwing");

            Console.WriteLine("  Every-command-path test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Every-command-path test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool TestUnimplementedAndUnknownCommandsThrow()
    {
        Console.WriteLine("Test: read-all/write-all (unimplemented) and unrecognized cmd values throw");
        var (dc, mem, ucode, path) = MakeController();
        try
        {
            bool ThrowsOnStart(uint cmd)
            {
                dc.Write(0, cmd);
                try { dc.Write(3, 0); return false; }
                catch (InvalidOperationException) { return true; }
            }

            Assert(ThrowsOnStart(0x2), "read-all (cmd 0x2) throws (unimplemented in the real C too)");
            Assert(ThrowsOnStart(0xB), "write-all (cmd 0xB) throws (unimplemented in the real C too)");
            Assert(ThrowsOnStart(0x1), "an unrecognized cmd value throws");

            Console.WriteLine("  Unimplemented/unknown-command test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Unimplemented/unknown-command test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool TestEncodeStatusBitsDirect()
    {
        Console.WriteLine("Test: EncodeStatus (via Read(0)) reflects each unit flag bit-for-bit");
        var (dc, mem, ucode, path) = MakeController();
        try
        {
            var unit = dc.GetUnitForTest(0);

            Assert((dc.Read(0) & (1u << 9)) == 0, "bit9 clear while unit is online");

            unit.SeekError = true;
            Assert((dc.Read(0) & (1u << 10)) != 0, "bit10 set when SeekError is true");
            unit.SeekError = false;
            Assert((dc.Read(0) & (1u << 10)) == 0, "bit10 clears when SeekError is false");

            unit.HasFault = true;
            Assert((dc.Read(0) & (1u << 6)) != 0, "bit6 set when HasFault is true");
            unit.HasFault = false;
            Assert((dc.Read(0) & (1u << 6)) == 0, "bit6 clears when HasFault is false");

            unit.ReadOnly = true;
            Assert((dc.Read(0) & (1u << 7)) != 0, "bit7 set when ReadOnly is true");
            unit.ReadOnly = false;
            Assert((dc.Read(0) & (1u << 7)) == 0, "bit7 clears when ReadOnly is false");

            unit.Attention = true;
            Assert((dc.Read(0) & (1u << 2)) != 0, "bit2 (selected unit attention) set");
            Assert((dc.Read(0) & (1u << 1)) != 0, "bit1 (any unit attention) set");
            unit.Attention = false;
            Assert((dc.Read(0) & (1u << 2)) == 0, "bit2 clears");
            Assert((dc.Read(0) & (1u << 1)) == 0, "bit1 clears once no unit has attention");

            // Select unit 1, which was never configured -- offline.
            dc.Write(2, EncodeDa(1, 0, 0, 0));
            Assert((dc.Read(0) & (1u << 9)) != 0, "bit9 set for an unconfigured/offline unit");

            Console.WriteLine("  EncodeStatus-bits test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  EncodeStatus-bits test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool TestResetProtocol()
    {
        Console.WriteLine("Test: reset protocol -- 0xE asserts reset (every read returns 0), 0 clears it");
        var (dc, mem, ucode, path) = MakeController();
        try
        {
            dc.Write(2, EncodeDa(0, 5, 1, 2));
            Assert(dc.Read(2) != 0, "sanity: da register holds a nonzero value before reset");

            dc.Write(0, 0xE);
            Assert(dc.Read(0) == 0, "Read(0) returns 0 while reset is asserted");
            Assert(dc.Read(1) == 0, "Read(1) returns 0 while reset is asserted");
            Assert(dc.Read(2) == 0, "Read(2) returns 0 while reset is asserted (register itself was cleared too)");
            Assert(dc.Read(3) == 0, "Read(3) returns 0 while reset is asserted");

            // While reset is in effect, other register writes are ignored.
            dc.Write(2, EncodeDa(0, 7, 3, 4));
            Assert(dc.Read(2) == 0, "writes to other registers are ignored while reset is asserted");

            dc.Write(0, 0); // turn off the reset condition
            Assert(dc.Read(0) == (1u << 0), "after reset clears, controller reports idle normally");

            dc.Write(2, EncodeDa(0, 7, 3, 4));
            Assert(dc.Read(2) == EncodeDa(0, 7, 3, 4), "register writes take effect again after reset clears");

            Console.WriteLine("  Reset-protocol test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Reset-protocol test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool TestReadOnlyWriteSetsHasFaultNoTransfer()
    {
        Console.WriteLine("Test: a write command against a read-only unit sets HasFault and performs no transfer");
        var (dc, mem, ucode, path) = MakeController();
        try
        {
            var unit = dc.GetUnitForTest(0);
            unit.ReadOnly = true;
            uint lastMemoryAddressBefore = unit.LastMemoryAddress;

            uint clp = 0x002000;
            mem.WritePhysical(clp, 0x000500 | 0); // a well-formed ccw -- should never be consulted
            dc.Write(1, clp);
            dc.Write(2, EncodeDa(0, 1, 1, 1));
            dc.Write(0, 0x9); // write
            dc.Write(3, 0);

            Assert(unit.HasFault, "write against a read-only unit sets HasFault");
            Assert(unit.LastMemoryAddress == lastMemoryAddressBefore, "no transfer occurred (LastMemoryAddress unchanged)");

            Console.WriteLine("  Read-only-write test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Read-only-write test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool TestFullReadWriteTransferThroughMemory()
    {
        Console.WriteLine("Test: a 2-block CCW-chain write, then a matching read, round-trip real data through MainMemory and the disk image");
        var (dc, mem, ucode, path) = MakeController();
        try
        {
            // Source pages in memory (page 0 and page 1).
            uint pageA = 0x000000;
            uint pageB = 0x000100;
            var patternA = new uint[256];
            var patternB = new uint[256];
            for (int i = 0; i < 256; i++)
            {
                patternA[i] = (uint)(0xA000 + i);
                patternB[i] = (uint)(0xB000 + i);
                mem.WritePhysical(pageA + (uint)i, patternA[i]);
                mem.WritePhysical(pageB + (uint)i, patternB[i]);
            }

            // A 2-CCW chain: first chains (bit0 set) to pageA, second (bit0
            // clear) ends the chain and targets pageB.
            uint clpWrite = 0x002000;
            mem.WritePhysical(clpWrite + 0, pageA | 1);
            mem.WritePhysical(clpWrite + 1, pageB | 0);

            uint cyl = 2, head = 1, sector = 3;
            uint da = EncodeDa(0, cyl, head, sector);
            dc.Write(1, clpWrite);
            dc.Write(2, da);
            dc.Write(0, 0x9); // write
            dc.Write(3, 0);

            uint expectedFinalDa = EncodeDa(0, cyl, head, sector + 1); // SeekNextLba advanced by one block
            Assert(dc.Read(2) == expectedFinalDa, $"da register reflects the final seeked position, got 0x{dc.Read(2):X}, expected 0x{expectedFinalDa:X}");
            Assert(dc.Read(1) == pageB + 255, $"LastMemoryAddress convention: start+255 on success, got 0x{dc.Read(1):X}, expected 0x{pageB + 255:X}");

            // Bypass the controller entirely: read the disk unit's own two
            // blocks directly and confirm they hold what we wrote.
            var unit = dc.GetUnitForTest(0);
            var diskReadBack = new uint[256];
            Assert(unit.Seek(cyl, head, sector), "direct seek to first block succeeds");
            Assert(unit.Read(diskReadBack), "direct disk read of first block succeeds");
            for (int i = 0; i < 256; i++)
                Assert(diskReadBack[i] == patternA[i], $"disk block 0 word {i} matches pageA, got 0x{diskReadBack[i]:X}");

            Assert(unit.Seek(cyl, head, sector + 1), "direct seek to second block succeeds");
            Assert(unit.Read(diskReadBack), "direct disk read of second block succeeds");
            for (int i = 0; i < 256; i++)
                Assert(diskReadBack[i] == patternB[i], $"disk block 1 word {i} matches pageB, got 0x{diskReadBack[i]:X}");

            // Now issue a real read command through the controller into two
            // fresh memory pages, and confirm it matches what was written.
            uint pageC = 0x000A00;
            uint pageD = 0x000B00;
            uint clpRead = 0x003000;
            mem.WritePhysical(clpRead + 0, pageC | 1);
            mem.WritePhysical(clpRead + 1, pageD | 0);

            dc.Write(1, clpRead);
            dc.Write(2, da); // re-seek back to the original starting block
            dc.Write(0, 0x0); // read
            dc.Write(3, 0);

            Assert(dc.Read(1) == pageD + 255, $"LastMemoryAddress convention holds for reads too, got 0x{dc.Read(1):X}");

            for (int i = 0; i < 256; i++)
            {
                Assert(mem.ReadPhysical(pageC + (uint)i) == patternA[i], $"read command: pageC word {i} matches original pageA");
                Assert(mem.ReadPhysical(pageD + (uint)i) == patternB[i], $"read command: pageD word {i} matches original pageB");
            }

            Console.WriteLine("  Full-transfer round-trip test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Full-transfer round-trip test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool TestNonexistentMemoryError()
    {
        Console.WriteLine("Test: a CCW pointing past MainMemory's populated-page limit sets the nonexistent-memory-error bit");
        var (dc, mem, ucode, path) = MakeController();
        try
        {
            // MainMemory defaults to 8192 populated pages; page 8192 (word
            // address 0x200000) is in-bounds for the raw array but past
            // npages, so ReadPage/WritePage report failure.
            uint outOfRangePaddr = 8192 * 256;
            uint clp = 0x004000;
            mem.WritePhysical(clp, (outOfRangePaddr & 0x00FFFF00u) | 0);

            dc.Write(1, clp);
            dc.Write(2, EncodeDa(0, 1, 1, 1));
            dc.Write(0, 0x9); // write -- reads from paddr via ReadPage, which fails
            dc.Write(3, 0);

            Assert((dc.Read(0) & (1u << 20)) != 0, "nonexistent-memory-error bit set");

            Console.WriteLine("  Nonexistent-memory-error test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Nonexistent-memory-error test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool TestInterruptAssertAndDeassert()
    {
        Console.WriteLine("Test: done-interrupt enable asserts the xbus interrupt on completion; disabling both bits deasserts it");
        var (dc, mem, ucode, path) = MakeController();
        try
        {
            uint clp = 0x002000;
            mem.WritePhysical(clp, 0x000200 | 0);
            dc.Write(1, clp);
            dc.Write(2, EncodeDa(0, 0, 0, 0));

            Assert(!ucode.InterruptPendingFlag, "no interrupt pending before any command runs");

            dc.Write(0, 0x800 | 0x0); // read, done-interrupt enabled
            dc.Write(3, 0);
            Assert(ucode.InterruptPendingFlag, "InterruptPendingFlag set after a done-interrupt-enabled command completes");
            Assert((ucode.InterruptStatusReg & 0x4000) != 0, "InterruptStatusReg's xbus-interrupt bit set");

            // Writing cmd with neither interrupt-enable bit set deasserts,
            // independent of Start() -- this happens directly in the offset-0
            // write handler (usim/disk-controller.c's real behavior).
            dc.Write(0, 0x4); // seek, no interrupt-enable bits
            Assert(!ucode.InterruptPendingFlag, "InterruptPendingFlag clears once neither interrupt-enable bit is set");
            Assert((ucode.InterruptStatusReg & 0x4000) == 0, "InterruptStatusReg's xbus-interrupt bit clears");

            Console.WriteLine("  Interrupt-assert/deassert test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Interrupt-assert/deassert test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool TestStartOnOfflineUnitIsSilentNoOp()
    {
        Console.WriteLine("Test: Start() against an offline/unconfigured unit logs and returns, without throwing");
        var (dc, mem, ucode, path) = MakeController();
        try
        {
            dc.Write(2, EncodeDa(1, 0, 0, 0)); // unit 1 was never configured
            dc.Write(0, 0x9); // an otherwise-valid write command
            dc.Write(3, 0); // must not throw

            Assert((dc.Read(0) & (1u << 9)) != 0, "status still reports unit 1 offline");

            Console.WriteLine("  Offline-unit-start test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Offline-unit-start test failed: {ex.Message}\n");
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
