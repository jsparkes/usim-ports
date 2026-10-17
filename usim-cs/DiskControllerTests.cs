// DiskControllerTests.cs - Tests for the faithful DiskController port
// (usim/disk-controller.c), synchronous-mode only.

using System;
using System.IO;
using System.Security.Cryptography;

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
        if (TestLastMemoryAddressSetBeforeCcwReadFails()) passed++; else failed++;
        if (TestInterruptAssertAndDeassert()) passed++; else failed++;
        if (TestStartOnOfflineUnitIsSilentNoOp()) passed++; else failed++;

        // Real usim/disk.img tests -- usim/disk.img is a large untracked
        // reference file, not guaranteed to exist in every checkout, so
        // these follow the same convention as SymbolTableTests.cs's
        // TestLoadRealPromhSymFile/TestLoadRealUcadrSymFileWithNegativeValues
        // and UCodeFetchDecodeTests.cs's TestPromDecodeSanity: they live in
        // the normal suite (no separate CLI switch) and print SKIPPED +
        // return true (not a failure) when the file is absent.
        if (TestRealDiskImageLabelBlockMatchesRawRead()) passed++; else failed++;
        if (TestRealDiskImageCcwChainCrossesHeadBoundary()) passed++; else failed++;
        if (TestRealDiskImageReadNeverMutatesPackFile()) passed++; else failed++;

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

    private static bool TestLastMemoryAddressSetBeforeCcwReadFails()
    {
        Console.WriteLine("Test: LastMemoryAddress is set to the CCW address itself before the very first CCW read is attempted, even when that read then fails");
        var (dc, mem, ucode, path) = MakeController();
        try
        {
            // MainMemory defaults to 8192 populated pages; a clp whose page
            // number is >= npages makes the very first
            // _mainMemory.TryReadWord(currentClp, ...) call inside
            // PerformXfer fail, before any data transfer is attempted.
            uint invalidClp = 8192 * 256; // page 8192, one past the last populated page

            dc.Write(1, invalidClp);
            dc.Write(2, EncodeDa(0, 1, 1, 1));
            dc.Write(0, 0x9); // write -- fails at the first CCW fetch, never reaches a data page
            dc.Write(3, 0);

            Assert(dc.GetUnitForTest(0).LastMemoryAddress == invalidClp,
                $"LastMemoryAddress holds the failing CCW address itself, got 0x{dc.GetUnitForTest(0).LastMemoryAddress:X}, expected 0x{invalidClp:X}");
            Assert((dc.Read(0) & (1u << 20)) != 0, "nonexistent-memory-error bit set for the failed CCW fetch");

            Console.WriteLine("  LastMemoryAddress-CCW-fetch-stage test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  LastMemoryAddress-CCW-fetch-stage test failed: {ex.Message}\n");
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

    // --- Real usim/disk.img tests ---
    // usim/disk.img is a real CADR T-300 pack image (confirmed-exact
    // geometry: 815 cylinders x 19 heads x 17 blocks/track x 1024 bytes =
    // 269,562,880 bytes). It lives at the repo root's usim/disk.img and is
    // untracked, so it may not exist in every checkout -- each test below
    // skips gracefully rather than failing when it's absent.

    private const string RealDiskImagePath = "usim/disk.img";

    // BusAdaptor's DiskControlLo (usim/bus-adaptor.c's disk-control XBus-I/O
    // range, 017377774-017377777 octal -- see BusAdaptor.cs's own constant
    // and derivation comment). Hardcoded here deliberately, same as
    // UCodeBusAdaptorTests.cs independently re-derives BusAdaptor paddrs/
    // uaddrs rather than reaching into BusAdaptor's private consts.
    private const uint DiskControlPaddr = 0x3DFFFC;

    // Builds a DiskController wired to a real MainMemory/UCode/BusAdaptor,
    // with unit 0 configured as "T-300" against imagePath -- mirrors
    // MachineControl.cs's own real wiring (UCode(mem) -> DiskController(mem,
    // ucode) -> ucode.BusAdaptor.WireDiskController(dc)), not a shortcut.
    private static (DiskController Controller, MainMemory Memory, UCode UCode, BusAdaptor BusAdaptor) MakeRealT300Controller(string imagePath)
    {
        var mem = new MainMemory();
        var ucode = new UCode(mem);
        var dc = new DiskController(mem, ucode);
        ucode.BusAdaptor.WireDiskController(dc);
        dc.ConfigureUnit(0, "T-300", imagePath);
        return (dc, mem, ucode, ucode.BusAdaptor);
    }

    // Issues a disk-controller register write through BusAdaptor's real
    // register interface (paddr DiskControlPaddr+offset) -- never
    // DiskController.Write directly.
    private static void BusWrite(BusAdaptor busAdaptor, uint offset, uint value)
    {
        bool promDisabled = false;
        busAdaptor.Write(DiskControlPaddr + offset, value, ref promDisabled);
    }

    private static uint BusRead(BusAdaptor busAdaptor, uint offset) =>
        busAdaptor.Read(DiskControlPaddr + offset);

    // Independent raw read of one 256-word (1024-byte) block at the given
    // LBA, bypassing DiskUnit/DiskController/MainMemory entirely -- the
    // cross-check reference for the controller-mediated reads below. Native
    // (little-endian on x86) byte->uint32 packing, same convention
    // DiskUnit.Read itself uses via Buffer.BlockCopy.
    private static uint[] ReadRawBlock(string path, long lba)
    {
        var words = new uint[256];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        fs.Seek(lba * 1024, SeekOrigin.Begin);
        byte[] bytes = new byte[1024];
        fs.ReadExactly(bytes, 0, 1024);
        Buffer.BlockCopy(bytes, 0, words, 0, 1024);
        return words;
    }

    private static string Md5Hex(string path)
    {
        using var md5 = MD5.Create();
        // Explicit FileShare.ReadWrite|Delete (File.OpenRead's default
        // FileShare.Read is NOT enough -- it conflicts with DiskUnit's own
        // still-open FileAccess.ReadWrite handle on this same path and
        // throws a sharing-violation IOException on Windows).
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(md5.ComputeHash(fs));
    }

    // Copies usim/disk.img to a fresh temp file -- every real-image test
    // operates on this copy, never the repo's own usim/disk.img.
    private static string CopyRealDiskImage()
    {
        string path = Path.GetTempFileName();
        File.Copy(RealDiskImagePath, path, overwrite: true);
        return path;
    }

    private static bool TestRealDiskImageLabelBlockMatchesRawRead()
    {
        Console.WriteLine("Test: a real BusAdaptor-issued read of the real T-300 pack's cylinder 0/head 0/block 0 decodes the real 'LABL' label and matches an independent raw read (skipped if usim/disk.img is absent)");
        if (!File.Exists(RealDiskImagePath))
        {
            Console.WriteLine($"  SKIPPED: {RealDiskImagePath} not present\n");
            return true;
        }

        string diskPath = CopyRealDiskImage();
        try
        {
            var (dc, mem, ucode, busAdaptor) = MakeRealT300Controller(diskPath);

            uint targetPage = 0x000600; // an arbitrary, otherwise-unused page in MainMemory
            uint clp = 0x005000;
            mem.WritePhysical(clp, targetPage | 0); // single CCW, chain bit clear

            BusWrite(busAdaptor, 1, clp);
            BusWrite(busAdaptor, 2, EncodeDa(0, 0, 0, 0));
            BusWrite(busAdaptor, 0, 0x0); // read, no interrupt
            BusWrite(busAdaptor, 3, 0); // start

            Assert((BusRead(busAdaptor, 0) & (1u << 0)) != 0, "controller reports idle after the read completes");
            Assert(BusRead(busAdaptor, 1) == targetPage + 255, $"LastMemoryAddress convention holds, got 0x{BusRead(busAdaptor, 1):X}");

            var fromController = new uint[256];
            for (int i = 0; i < 256; i++) fromController[i] = mem.ReadPhysical(targetPage + (uint)i);

            Assert(fromController[0] == 0x4C42414C, $"word0 is the real pack's 'LABL' label, got 0x{fromController[0]:X}");
            Assert(fromController[1] == 0x00000001, $"word1 is 0x00000001, got 0x{fromController[1]:X}");

            var rawBlock = ReadRawBlock(diskPath, lba: 0);
            for (int i = 0; i < 256; i++)
                Assert(fromController[i] == rawBlock[i], $"word {i} matches an independent raw FileStream read, got 0x{fromController[i]:X}, expected 0x{rawBlock[i]:X}");

            Console.WriteLine("  Real-disk-image label-block test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Real-disk-image label-block test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(diskPath);
        }
    }

    private static bool TestRealDiskImageCcwChainCrossesHeadBoundary()
    {
        Console.WriteLine("Test: a 2-block CCW-chain read starting at a track's last block round-trips real data and exercises SeekNextLba's head rollover against real T-300 geometry (skipped if usim/disk.img is absent)");
        if (!File.Exists(RealDiskImagePath))
        {
            Console.WriteLine($"  SKIPPED: {RealDiskImagePath} not present\n");
            return true;
        }

        string diskPath = CopyRealDiskImage();
        try
        {
            var (dc, mem, ucode, busAdaptor) = MakeRealT300Controller(diskPath);
            var unit = dc.GetUnitForTest(0);

            // Start at cylinder 0, head 0, the LAST block of the track
            // (BlocksPerTrack-1) -- the second CCW's SeekNextLba call must
            // roll over to head 1, block 0 (not throw, not wrap cylinder).
            uint cyl = 0, head = 0, lastBlock = unit.BlocksPerTrack - 1;
            long lbaFirst = (cyl * unit.BlocksPerCylinder) + (head * unit.BlocksPerTrack) + lastBlock;
            long lbaSecond = lbaFirst + 1; // head 0 -> head 1, block 0 -- still cylinder 0

            uint pageA = 0x000700;
            uint pageB = 0x000800;
            uint clp = 0x006000;
            mem.WritePhysical(clp + 0, pageA | 1); // chains to the second CCW
            mem.WritePhysical(clp + 1, pageB | 0); // ends the chain

            BusWrite(busAdaptor, 1, clp);
            BusWrite(busAdaptor, 2, EncodeDa(0, cyl, head, lastBlock));
            BusWrite(busAdaptor, 0, 0x0); // read
            BusWrite(busAdaptor, 3, 0); // start

            Assert(BusRead(busAdaptor, 1) == pageB + 255, $"LastMemoryAddress convention holds across the chain, got 0x{BusRead(busAdaptor, 1):X}");
            uint expectedFinalDa = EncodeDa(0, cyl, head + 1, 0);
            Assert(BusRead(busAdaptor, 2) == expectedFinalDa, $"da register reflects the head-rollover position, got 0x{BusRead(busAdaptor, 2):X}, expected 0x{expectedFinalDa:X}");

            var fromControllerA = new uint[256];
            var fromControllerB = new uint[256];
            for (int i = 0; i < 256; i++)
            {
                fromControllerA[i] = mem.ReadPhysical(pageA + (uint)i);
                fromControllerB[i] = mem.ReadPhysical(pageB + (uint)i);
            }

            var rawA = ReadRawBlock(diskPath, lbaFirst);
            var rawB = ReadRawBlock(diskPath, lbaSecond);
            for (int i = 0; i < 256; i++)
            {
                Assert(fromControllerA[i] == rawA[i], $"first block (last block of track) word {i} matches raw read, got 0x{fromControllerA[i]:X}, expected 0x{rawA[i]:X}");
                Assert(fromControllerB[i] == rawB[i], $"second block (rolled over to head {head + 1}) word {i} matches raw read, got 0x{fromControllerB[i]:X}, expected 0x{rawB[i]:X}");
            }

            Console.WriteLine("  Real-disk-image head-rollover CCW-chain test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Real-disk-image head-rollover CCW-chain test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(diskPath);
        }
    }

    private static bool TestRealDiskImageReadNeverMutatesPackFile()
    {
        Console.WriteLine("Test: issuing read commands through BusAdaptor against the real pack never mutates the pack file (skipped if usim/disk.img is absent)");
        if (!File.Exists(RealDiskImagePath))
        {
            Console.WriteLine($"  SKIPPED: {RealDiskImagePath} not present\n");
            return true;
        }

        string diskPath = CopyRealDiskImage();
        try
        {
            string hashBefore = Md5Hex(diskPath);

            var (dc, mem, ucode, busAdaptor) = MakeRealT300Controller(diskPath);
            var unit = dc.GetUnitForTest(0);

            uint clp = 0x007000;
            mem.WritePhysical(clp + 0, 0x000900u | 1);
            mem.WritePhysical(clp + 1, 0x000A00u | 0);

            // A handful of varied real reads: block 0, the last block of a
            // track (head-rollover), and a read-compare.
            BusWrite(busAdaptor, 1, clp);
            BusWrite(busAdaptor, 2, EncodeDa(0, 0, 0, 0));
            BusWrite(busAdaptor, 0, 0x0);
            BusWrite(busAdaptor, 3, 0);

            BusWrite(busAdaptor, 2, EncodeDa(0, 0, 0, unit.BlocksPerTrack - 1));
            BusWrite(busAdaptor, 0, 0x0);
            BusWrite(busAdaptor, 3, 0);

            BusWrite(busAdaptor, 2, EncodeDa(0, 1, 2, 3));
            BusWrite(busAdaptor, 0, 0x8); // read-compare
            BusWrite(busAdaptor, 3, 0);

            string hashAfter = Md5Hex(diskPath);
            Assert(hashBefore == hashAfter, $"pack file's MD5 is unchanged by read commands, before={hashBefore}, after={hashAfter}");

            Console.WriteLine("  Real-disk-image read-never-mutates test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Real-disk-image read-never-mutates test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(diskPath);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
