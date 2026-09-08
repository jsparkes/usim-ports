# Disk Subsystem Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the entirely-invented `DiskController.cs`/`DiskUnit.cs` with a faithful port of `usim/disk-controller.c` + `usim/disk-unit.c`, wired for real into `BusAdaptor`, `MainMemory`, and config.

**Architecture:** Four tasks, ordered by dependency: (1) `MainMemory` gets page-granular transfer methods, (2) `DiskUnit.cs` and `DiskController.cs` are rewritten together (they must land in the same commit — the old, still-invented `DiskController.cs` file currently declares a top-level `DiskUnit` class with the exact same name as the new faithful one, and the old `DiskController` class's body references that old `DiskUnit`'s API; there is no way to replace one without the other mid-task without leaving the build broken), (3) config gets a real per-unit `[disk]` section wired through the config path that's actually live (`Program.ApplyConfiguration` → `UsimState`, not the dead `ConfigManager`), (4) everything is wired together in `BusAdaptor`/`MachineControl` (depends on 2 and 3).

**Tech Stack:** C#, .NET 8.0, no new dependencies.

**Spec:** `docs/superpowers/specs/2026-09-08-disk-subsystem-design.md`

## Global Constraints

- No new NuGet dependencies.
- Every octal literal in this plan's code was independently verified via script against the real C — see the spec's "Octal Literals" table. Use the hex forms given; do not re-derive by hand.
- Synchronous transfer only (no threading), `FileStream` not `mmap`, `idle_disk_activity()`/`disk_unit_rotate()`/`LABEL_LABL` not ported — see spec's Decisions for why.
- `DiskUnit.ReadOnly` is permanently `false` — no config option for it exists in the real C for disk units (unlike tape).
- A disk-pack file that doesn't exist, or whose size doesn't match its type's expected size, is a **fatal error** (throw), not a silent skip or a warning — matches the real C's `errx()` calls exactly. The old invented code's `FileMode.OpenOrCreate` behavior (silently creating an empty file) must not be preserved.
- Read-all/write-all commands (`cmd & 0xF == 0x2` / `0xB`) are fatal/unimplemented, matching the real C.
- **Important, expected behavior change, not a regression:** today, `BusAdaptor`'s disk-control stub always reports "ready, online" regardless of whether any disk is configured. After this plan, with no `[disk]` config section, the real `DiskController` correctly reports "not active, offline" — matching real hardware with no disk attached. A boot sequence that polls for the disk to come online (the real CADR boot PROM's `AWAIT-DISK-ON-LINE`) will correctly wait/hang without a configured disk, same as real hardware. This is the intended outcome of a faithful port, not a bug to work around.

---

### Task 1: `MainMemory.cs` page-transfer methods

**Files:**
- Modify: `usim-cs/MainMemory.cs`
- Test: `usim-cs/MainMemoryTests.cs`

**Interfaces:**
- Produces (for Task 2): `public bool ReadPage(uint physicalAddress, uint[] buffer)`, `public bool WritePage(uint physicalAddress, uint[] buffer)` (both `buffer.Length` must be `PAGE_SIZE` = 256), `public bool TryReadWord(uint physicalAddress, out uint value)`.

- [ ] **Step 1: Add the three methods to `MainMemory.cs`**

Add immediately after the existing `WritePhysical` method (`usim-cs/MainMemory.cs`, after the closing brace that follows `LogOutOfRangeAccess(pn, "write");` inside `WritePhysical`):

```csharp
    /// <summary>
    /// Faithful port of main_memory_read_page (usim/main-memory.c:123-139) --
    /// the real C's DMA-style page transfer used by the disk controller.
    /// Unlike ReadPhysical/WritePhysical, indexes by the masked page number
    /// only (never the raw physicalAddress), so no separate upper-bound check
    /// is needed: pn is already masked to 14 bits (max 16383), and
    /// pn*PAGE_SIZE+PAGE_SIZE can never exceed PHYSICAL_MEM_SIZE.
    /// </summary>
    public bool ReadPage(uint physicalAddress, uint[] buffer)
    {
        uint pn = (physicalAddress >> PAGE_SIZE_BITS) & 0x3FFF;
        if (pn < _npages)
        {
            Array.Copy(_physicalMemory, (int)(pn * PAGE_SIZE), buffer, 0, PAGE_SIZE);
            return true;
        }
        LogOutOfRangeAccess(pn, "read page");
        return false;
    }

    /// <summary>
    /// Faithful port of main_memory_write_page (usim/main-memory.c:141-155).
    /// </summary>
    public bool WritePage(uint physicalAddress, uint[] buffer)
    {
        uint pn = (physicalAddress >> PAGE_SIZE_BITS) & 0x3FFF;
        if (pn < _npages)
        {
            Array.Copy(buffer, 0, _physicalMemory, (int)(pn * PAGE_SIZE), PAGE_SIZE);
            return true;
        }
        LogOutOfRangeAccess(pn, "write page");
        return false;
    }

    /// <summary>
    /// Faithful port of main_memory_read (usim/main-memory.c:59-89), with a
    /// real bool success/failure -- unlike ReadPhysical's 0xFFFFFFFF-sentinel
    /// convention, needed here because the disk controller's channel-command-word
    /// read must tell "out of range" apart from "the word happens to be all-ones",
    /// which the sentinel convention cannot.
    /// </summary>
    public bool TryReadWord(uint physicalAddress, out uint value)
    {
        uint pn = (physicalAddress >> PAGE_SIZE_BITS) & 0x3FFF;
        if (pn < _npages && physicalAddress < PHYSICAL_MEM_SIZE)
        {
            value = _physicalMemory[physicalAddress];
            return true;
        }
        LogOutOfRangeAccess(pn, "read");
        value = 0xFFFFFFFF;
        return false;
    }
```

- [ ] **Step 2: Add tests to `MainMemoryTests.cs`**

Add new test methods (following this file's existing pattern — check an existing test method for the exact `Assert`/reporting style used) covering:

```csharp
private static bool TestReadWritePageRoundTrip()
{
    Console.WriteLine("Test: ReadPage/WritePage round-trip a real 256-word buffer");
    try
    {
        var mem = new MainMemory();
        var buffer = new uint[256];
        for (int i = 0; i < 256; i++) buffer[i] = (uint)(0x1000 + i);

        Assert(mem.WritePage(0x1200, buffer), "WritePage succeeds for a populated page");

        var readBack = new uint[256];
        Assert(mem.ReadPage(0x1200, readBack), "ReadPage succeeds for a populated page");
        for (int i = 0; i < 256; i++)
            Assert(readBack[i] == buffer[i], $"word {i} round-trips, got 0x{readBack[i]:X}");

        Console.WriteLine("  ReadPage/WritePage round-trip test passed\n");
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ReadPage/WritePage round-trip test failed: {ex.Message}\n");
        return false;
    }
}

private static bool TestPageMethodsOutOfRange()
{
    Console.WriteLine("Test: ReadPage/WritePage return false for an unpopulated page");
    try
    {
        var mem = new MainMemory(npages: 1); // only page 0 populated
        var buffer = new uint[256];

        Assert(!mem.ReadPage(0x100, buffer), "ReadPage fails for page 1 when npages=1");
        Assert(!mem.WritePage(0x100, buffer), "WritePage fails for page 1 when npages=1");

        Console.WriteLine("  Page-methods out-of-range test passed\n");
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Page-methods out-of-range test failed: {ex.Message}\n");
        return false;
    }
}

private static bool TestTryReadWordDiscriminates()
{
    Console.WriteLine("Test: TryReadWord's bool return discriminates in-range from out-of-range");
    try
    {
        var mem = new MainMemory(npages: 1);
        mem.WritePhysical(0x42, 0xABCD1234);

        Assert(mem.TryReadWord(0x42, out uint inRange), "TryReadWord returns true for a populated page");
        Assert(inRange == 0xABCD1234, $"in-range value correct, got 0x{inRange:X}");

        Assert(!mem.TryReadWord(0x100, out uint outOfRange), "TryReadWord returns false for page 1 when npages=1");
        Assert(outOfRange == 0xFFFFFFFF, $"out-of-range sentinel value, got 0x{outOfRange:X}");

        Console.WriteLine("  TryReadWord discrimination test passed\n");
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  TryReadWord discrimination test failed: {ex.Message}\n");
        return false;
    }
}
```

Register all three in `RunAllTests()` alongside the existing `if (TestX()) passed++; else failed++;` lines, and add their `Environment.Exit(0)`-style entries to `Program.cs` only if this file already has its own dedicated `--test-main-memory` CLI switch (check `Program.cs` — if `MainMemoryTests` already has a CLI case, no `Program.cs` change is needed here since it already calls `RunAllTests()`).

- [ ] **Step 3: Build and test**

Run: `dotnet build usim-cs` then `dotnet run --project usim-cs -- --test-main-memory` (or whatever CLI switch already runs `MainMemoryTests`, per Step 2).
Expected: builds clean, all `MainMemoryTests` pass including the 3 new ones.

- [ ] **Step 4: Commit**

```bash
git add usim-cs/MainMemory.cs usim-cs/MainMemoryTests.cs
git commit -m "Add MainMemory page-transfer methods for disk DMA (ReadPage/WritePage/TryReadWord)"
```

---

### Task 2: `DiskUnit.cs` + `DiskController.cs` — full faithful rewrite of both

**Files:**
- Create: `usim-cs/DiskUnit.cs`
- Modify: `usim-cs/DiskController.cs` (replace entirely — both the invented `DiskController` class AND the invented, unrelated top-level `DiskUnit` class it currently co-declares in the same file/namespace)
- Test: `usim-cs/DiskUnitTests.cs` (new file), `usim-cs/DiskControllerTests.cs` (new file)
- Modify: `usim-cs/Program.cs` (CLI wiring for both new test suites)

**Interfaces:**
- Consumes: `MainMemory.ReadPage`/`WritePage`/`TryReadWord` (Task 1). `UCode.AssertXbusInterrupt`/`DeassertXbusInterrupt` (already real, from Phase 8 — no change needed).
- Produces (for Tasks 3 and 4): `public class DiskUnit` — constructor `DiskUnit(uint unit)`, properties `Unit`/`TypeName`/`Cylinders`/`Heads`/`BlocksPerTrack`/`BlocksPerCylinder`/`Configured`/`Online`/`ReadOnly`(settable)/`SeekError`(settable)/`HasFault`(settable)/`Attention`(settable)/`LastMemoryAddress`(settable)/`Cylinder`/`Head`/`Sector`/`Lba`, methods `Configure(string typeName, string filename)`/`Read(uint[])`/`Write(uint[])`/`Seek(uint,uint,uint)`/`SeekNextLba()`/`RaiseAttention()`/`Da()`/`Quit()`. `public class DiskController` — constructor `DiskController(MainMemory mainMemory, UCode ucode)`, `public const int NUMBER_OF_DISK_UNITS = 8`, `public void ConfigureUnit(uint unit, string typeName, string filename)`, `public uint Read(uint offset)`, `public void Write(uint offset, uint v)`, `public void BusReset()`.

- [ ] **Step 1: Write `usim-cs/DiskUnit.cs`**

```csharp
// DiskUnit.cs - Faithful port of usim/disk-unit.c (a single Trident disk drive).
// See docs/superpowers/specs/2026-09-08-disk-subsystem-design.md.

using System;
using System.IO;

namespace Usim;

public class DiskUnit
{
    private const int BLOCK_SIZE_BYTES = 1024; // usim/disk-unit.c's BLOCKSIZE

    // full name, short name, ncylinders, nheads, nblocks_per_track --
    // usim/disk-unit.c:50-55, do not change (real hardware geometry).
    private static readonly (string Name, string ShortName, uint Cylinders, uint Heads, uint BlocksPerTrack)[] DiskUnitTypes =
    {
        ("Trident T-80",  "T-80",  815, 5,  17),
        ("Trident T-300", "T-300", 815, 19, 17),
    };

    public uint Unit { get; }
    public string TypeName { get; private set; } = "";
    public uint Cylinders { get; private set; }
    public uint Heads { get; private set; }
    public uint BlocksPerTrack { get; private set; }
    public uint BlocksPerCylinder { get; private set; }

    public bool Configured { get; private set; }
    public bool Online { get; private set; }
    public bool ReadOnly { get; set; } // always false in this port -- see spec's Decisions

    public bool SeekError { get; set; }
    public bool HasFault { get; set; }
    public bool Attention { get; set; }

    public uint LastMemoryAddress { get; set; }
    public uint Cylinder { get; private set; }
    public uint Head { get; private set; }
    public uint Sector { get; private set; }
    public uint Lba { get; private set; }

    private FileStream? _file;
    private long _fileSize;

    public DiskUnit(uint unit)
    {
        Unit = unit;
    }

    /// <summary>
    /// Faithful port of disk_unit_init (usim/disk-unit.c:197-337), minus the
    /// config-string parsing (already split into typeName/filename by the
    /// caller -- see spec's Config Decisions). A no-comma real-C config value
    /// (bare filename, type defaults to "T-300") is handled by the CALLER,
    /// not here -- by the time this is called, typeName is never empty
    /// unless the whole unit is genuinely unconfigured.
    /// Fatal (throws) on: unknown type, missing file, wrong file size --
    /// matching the real C's errx() calls, not a silent skip.
    /// </summary>
    public void Configure(string typeName, string filename)
    {
        Online = false;

        if (string.IsNullOrEmpty(typeName))
        {
            TraceLog.Instance.Info(TraceCategory.Disk, $"disk-unit {Unit}: offline (not configured)");
            return;
        }

        Configured = true;

        var type = Array.Find(DiskUnitTypes, t =>
            string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.ShortName, typeName, StringComparison.OrdinalIgnoreCase));

        if (type == default)
        {
            throw new InvalidOperationException($"disk-unit {Unit}: invalid disk unit type: '{typeName}'");
        }

        TypeName = type.ShortName;
        Cylinders = type.Cylinders;
        Heads = type.Heads;
        BlocksPerTrack = type.BlocksPerTrack;
        BlocksPerCylinder = Heads * BlocksPerTrack;

        if (string.IsNullOrEmpty(filename))
        {
            TraceLog.Instance.Info(TraceCategory.Disk, $"disk-unit {Unit}: [{TypeName}]: offline (no disk pack)");
            return;
        }

        if (!File.Exists(filename))
        {
            throw new InvalidOperationException($"disk-unit {Unit}: [{TypeName}]: offline (cannot open disk pack: {filename})");
        }

        _file = new FileStream(filename, FileMode.Open, FileAccess.ReadWrite);
        _fileSize = _file.Length;

        long expectedSize = (long)Cylinders * BlocksPerCylinder * BLOCK_SIZE_BYTES;
        if (_fileSize != expectedSize)
        {
            _file.Dispose();
            _file = null;
            throw new InvalidOperationException(
                $"disk-unit {Unit}: [{TypeName}]: disk pack ({filename}) size ({_fileSize}) is not the expected one ({expectedSize})");
        }

        Cylinder = 0;
        Head = 0;
        Sector = 0;
        Lba = 0;
        Online = true;

        TraceLog.Instance.Info(TraceCategory.Disk, $"disk-unit {Unit}: [{TypeName}]: online ({filename})");
    }

    public void Quit()
    {
        _file?.Dispose();
        _file = null;
    }

    /// <summary>Faithful port of disk_unit_read (usim/disk-unit.c:57-75).</summary>
    public bool Read(uint[] buffer)
    {
        long offset = (long)Lba * BLOCK_SIZE_BYTES;
        if (_file == null || offset >= _fileSize)
        {
            TraceLog.Instance.Warning(TraceCategory.Disk,
                $"disk-unit {Unit}: reading offset ({offset}) past end of disk image ({_fileSize})");
            return false;
        }

        _file.Seek(offset, SeekOrigin.Begin);
        byte[] bytes = new byte[BLOCK_SIZE_BYTES];
        _file.ReadExactly(bytes, 0, BLOCK_SIZE_BYTES);
        Buffer.BlockCopy(bytes, 0, buffer, 0, BLOCK_SIZE_BYTES);
        return true;
    }

    /// <summary>Faithful port of disk_unit_write (usim/disk-unit.c:77-95).</summary>
    public bool Write(uint[] buffer)
    {
        long offset = (long)Lba * BLOCK_SIZE_BYTES;
        if (_file == null || offset >= _fileSize)
        {
            TraceLog.Instance.Warning(TraceCategory.Disk,
                $"disk-unit {Unit}: writing offset ({offset}) past end of disk image ({_fileSize})");
            return false;
        }

        byte[] bytes = new byte[BLOCK_SIZE_BYTES];
        Buffer.BlockCopy(buffer, 0, bytes, 0, BLOCK_SIZE_BYTES);
        _file.Seek(offset, SeekOrigin.Begin);
        _file.Write(bytes, 0, BLOCK_SIZE_BYTES);
        _file.Flush();
        return true;
    }

    /// <summary>Faithful port of disk_unit_seek (usim/disk-unit.c:97-135).</summary>
    public bool Seek(uint cylinder, uint head, uint sector)
    {
        if (cylinder == Cylinder && head == Head && sector == Sector)
        {
            return true;
        }

        if (cylinder >= Cylinders) { SeekError = true; return false; }
        if (head >= Heads) { SeekError = true; return false; }
        if (sector >= BlocksPerTrack) { SeekError = true; return false; }

        Cylinder = cylinder;
        Head = head;
        Sector = sector;
        Lba = (cylinder * BlocksPerCylinder) + (head * BlocksPerTrack) + sector;
        return true;
    }

    /// <summary>Faithful port of disk_unit_seek_next_lba (usim/disk-unit.c:140-159).</summary>
    public bool SeekNextLba()
    {
        uint cylinder = Cylinder;
        uint head = Head;
        uint sector = Sector + 1;
        if (sector == BlocksPerTrack)
        {
            sector = 0;
            head++;
            if (head == Heads)
            {
                head = 0;
                cylinder++;
            }
        }
        return Seek(cylinder, head, sector);
    }

    /// <summary>
    /// Faithful port of disk_unit_raise_attention (usim/disk-unit.c:167-172),
    /// minus the stored function-pointer callback -- DiskController is the
    /// only caller either way, so it calls its own attention-check method
    /// explicitly right after this, instead of this method invoking a stored
    /// delegate. See spec's Wiring section.
    /// </summary>
    public void RaiseAttention() => Attention = true;

    /// <summary>Faithful port of disk_unit_da (usim/disk-unit.c:174-178).</summary>
    public uint Da() => (Unit << 28) | (Cylinder << 16) | (Head << 8) | Sector;
}
```

- [ ] **Step 2: Write `usim-cs/DiskUnitTests.cs`**

```csharp
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
```

- [ ] **Step 3: Replace `usim-cs/DiskController.cs` entirely**

This deletes both the old invented `DiskController` class AND the old invented top-level `DiskUnit` class currently co-located in this same file (which Step 1 makes obsolete and name-colliding).

```csharp
// DiskController.cs - Faithful port of usim/disk-controller.c. Synchronous
// (blocking-mode) transfers only -- see docs/superpowers/specs/2026-09-08-disk-subsystem-design.md
// for why (this codebase has no other real threading; the real C's
// non-blocking pthread mode is a separate, unported mode).

using System;

namespace Usim;

public class DiskController
{
    public const int NUMBER_OF_DISK_UNITS = 8;

    private readonly MainMemory _mainMemory;
    private readonly UCode _ucode;
    private readonly DiskUnit[] _units;

    // --- registers (usim/disk-controller.c:70-78) ---
    private uint _cmd;
    private uint _clp;
    private uint _da;
    private bool _resetCondition;

    // --- status (usim/disk-controller.c:103-113) ---
    private bool _readCompareDifference;
    private bool _ccwCycle;
    private bool _nonexistentMemoryError;
    private bool _interruptRequest;
    private bool _notActive;

    private bool _doneInterruptEnable;
    private bool _attentionInterruptEnable;

    public DiskController(MainMemory mainMemory, UCode ucode)
    {
        _mainMemory = mainMemory;
        _ucode = ucode;
        _units = new DiskUnit[NUMBER_OF_DISK_UNITS];
        for (uint i = 0; i < NUMBER_OF_DISK_UNITS; i++) _units[i] = new DiskUnit(i);
        Reset();
    }

    /// <summary>Config-driven mount -- mirrors disk_unit_init's role (called
    /// once per configured unit at power-on), not disk_controller_init's
    /// (which only logs, and in the excluded non-blocking mode starts the
    /// thread).</summary>
    public void ConfigureUnit(uint unit, string typeName, string filename)
    {
        _units[unit].Configure(typeName, filename);
    }

    private DiskUnit SelectedUnit() => _units[(_da >> 28) & 0x7];

    // usim/disk-controller.c:115-128
    private void AssertInterrupt()
    {
        _interruptRequest = true;
        _ucode.AssertXbusInterrupt();
    }

    private void DeassertInterrupt() => _ucode.DeassertXbusInterrupt();

    // usim/disk-controller.c:130-150
    private void SetStatusNotActive()
    {
        _notActive = true;
        if (_doneInterruptEnable) AssertInterrupt();
    }

    private void SetStatusActive() => _notActive = false;

    // usim/disk-controller.c:152-171
    private void ResetStatus()
    {
        _readCompareDifference = false;
        _ccwCycle = false;
        _nonexistentMemoryError = false;
        _interruptRequest = false;
        SetStatusNotActive();
    }

    private void Reset()
    {
        _doneInterruptEnable = false;
        _attentionInterruptEnable = false;
        ResetStatus();
        _cmd = 0;
        _clp = 0;
        _da = 0;
    }

    /// <summary>Faithful port of encode_status (usim/disk-controller.c:173-220).
    /// Masks are all (1&lt;&lt;N) forms in the real C -- no octal-literal risk.
    /// "Selected unit" is recomputed from the CURRENT da on every call, never
    /// cached (matches the real C's SELECTED_UNIT_PTR() macro).</summary>
    private uint EncodeStatus()
    {
        uint v = 0;
        if (_readCompareDifference) v |= 1u << 22;
        if (_ccwCycle) v |= 1u << 21;
        if (_nonexistentMemoryError) v |= 1u << 20;

        DiskUnit p = SelectedUnit();
        if (p.SeekError) v |= 1u << 10;
        if (!p.Online) v |= 1u << 9;
        if (p.ReadOnly) v |= 1u << 7;
        if (p.HasFault) v |= 1u << 6;
        if (p.Attention) v |= 1u << 2;
        if (_interruptRequest) v |= 1u << 3;

        for (int i = 0; i < NUMBER_OF_DISK_UNITS; i++)
        {
            if (_units[i].Attention) { v |= 1u << 1; break; }
        }

        if (_notActive) v |= 1u << 0;
        return v;
    }

    /// <summary>Faithful port of decode_da (usim/disk-controller.c:222-229).
    /// Masks independently verified: octal 07=0x7, 07777=0xFFF, 0377=0xFF.</summary>
    private static void DecodeDa(uint da, out uint unit, out uint cylinder, out uint head, out uint block)
    {
        unit = (da >> 28) & 0x7;
        cylinder = (da >> 16) & 0xFFF;
        head = (da >> 8) & 0xFF;
        block = da & 0xFF;
    }

    /// <summary>Faithful port of perform_xfer (usim/disk-controller.c:254-383) --
    /// the DMA-style CCW-chain transfer, minus threading.</summary>
    private bool PerformXfer(DiskUnit p, bool read, bool compare, uint clp)
    {
        _readCompareDifference = false;
        _ccwCycle = false;
        _nonexistentMemoryError = false;

        var buffer = new uint[256];
        var bufferCompare = new uint[256];

        ushort clpOffset = 0;
        while (true)
        {
            uint currentClp = clp + clpOffset;
            p.LastMemoryAddress = currentClp;
            _ccwCycle = true;
            if (!_mainMemory.TryReadWord(currentClp, out uint ccw))
            {
                _nonexistentMemoryError = true;
                return false;
            }
            _ccwCycle = false;

            uint paddr = ccw & 0x00FFFF00u;

            if (read)
            {
                if (p.Read(buffer))
                {
                    if (compare)
                    {
                        p.LastMemoryAddress = paddr;
                        if (_mainMemory.ReadPage(paddr, bufferCompare))
                        {
                            p.LastMemoryAddress = paddr + 255;
                            if (!BuffersEqual(buffer, bufferCompare))
                            {
                                // "This error does not stop the transfer."
                                _readCompareDifference = true;
                            }
                        }
                        else
                        {
                            _nonexistentMemoryError = true;
                            return false;
                        }
                    }
                    else
                    {
                        p.LastMemoryAddress = paddr;
                        if (_mainMemory.WritePage(paddr, buffer))
                        {
                            p.LastMemoryAddress = paddr + 255;
                        }
                        else
                        {
                            _nonexistentMemoryError = true;
                            return false;
                        }
                    }
                }
                else
                {
                    p.HasFault = true;
                    return false;
                }
            }
            else
            {
                p.LastMemoryAddress = paddr;
                if (_mainMemory.ReadPage(paddr, buffer))
                {
                    p.LastMemoryAddress = paddr + 255;
                    if (!p.Write(buffer))
                    {
                        p.HasFault = true;
                        return false;
                    }
                }
                else
                {
                    _nonexistentMemoryError = true;
                    return false;
                }
            }

            // is it the last ccw?
            if ((ccw & 1) == 0) break;

            if (!p.SeekNextLba()) return false;
            clpOffset++;
        }

        return true;
    }

    private static bool BuffersEqual(uint[] a, uint[] b)
    {
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>Faithful port of submit_xfer (usim/disk-controller.c:422-440),
    /// collapsed with do_xfer since this port is synchronous-only.</summary>
    private void SubmitXfer(bool read, bool compare)
    {
        SetStatusActive();

        DecodeDa(_da, out uint unit, out uint cylinder, out uint head, out uint block);
        DiskUnit p = _units[unit];

        if (p.Seek(cylinder, head, block))
        {
            PerformXfer(p, read, compare, _clp);
            _da = p.Da();
        }
        else
        {
            TraceLog.Instance.Warning(TraceCategory.Disk, $"disk-unit {p.Unit}: seek error");
        }

        SetStatusNotActive();
    }

    private void StartRead() => SubmitXfer(true, false);
    private void StartReadCompare() => SubmitXfer(true, true);

    private void StartWrite()
    {
        // "Writing while the disk is read-only causes a fault."
        DiskUnit p = SelectedUnit();
        if (p.ReadOnly)
        {
            p.HasFault = true;
        }
        else
        {
            SubmitXfer(false, false);
        }
    }

    private void StartSeek()
    {
        SetStatusActive();
        DecodeDa(_da, out uint unit, out uint cylinder, out uint head, out uint block);
        DiskUnit p = _units[unit];
        p.Seek(cylinder, head, block);
        p.RaiseAttention();
        OnDiskUnitAttention(p);
        SetStatusNotActive();
    }

    private void StartRecalibrate()
    {
        SetStatusActive();
        DiskUnit p = SelectedUnit();
        p.Seek(0, 0, 0);
        p.HasFault = false;
        p.SeekError = false;
        p.RaiseAttention();
        OnDiskUnitAttention(p);
        SetStatusNotActive();
    }

    private void StartFaultClear()
    {
        SetStatusActive();
        SelectedUnit().HasFault = false;
        SetStatusNotActive();
    }

    private void StartAtEase()
    {
        SetStatusActive();
        SelectedUnit().Attention = false;
        SetStatusNotActive();
    }

    private void StartOffsetClear()
    {
        // "there is no concept of servo offset in usim; offset_clear is
        // simply a nop" -- usim/disk-controller.c:537-538.
        SetStatusActive();
        SetStatusNotActive();
    }

    /// <summary>Faithful port of start (usim/disk-controller.c:543-629).
    /// Command values independently verified: octal 000=0x0 read, 010=0x8
    /// read-compare, 011=0x9 write, 002=0x2 read-all (fatal, unimplemented
    /// in the real C too), 013=0xB write-all (same), 004=0x4 seek, 005=0x5
    /// at-ease (+01000=0x200 recalibrate, +00400=0x100 fault-clear), 006=0x6
    /// offset-clear. cmd mask 017=0xF.</summary>
    private void Start()
    {
        DiskUnit p = SelectedUnit();
        if (!p.Online)
        {
            TraceLog.Instance.Info(TraceCategory.Disk, $"disk controller: start, but disk unit {p.Unit} not online");
            return;
        }

        switch (_cmd & 0xF)
        {
            case 0x0: StartRead(); break;
            case 0x8: StartReadCompare(); break;
            case 0x9: StartWrite(); break;
            case 0x2: throw new InvalidOperationException("disk-controller: read all not implemented");
            case 0xB: throw new InvalidOperationException("disk-controller: write all not implemented");
            case 0x4: StartSeek(); break;
            case 0x5:
                StartAtEase();
                // "1405_This probably does both a Recalibrate and a Fault Clear."
                if ((_cmd & 0x200) != 0) StartRecalibrate();
                if ((_cmd & 0x100) != 0) StartFaultClear();
                break;
            case 0x6: StartOffsetClear(); break;
            default:
                throw new InvalidOperationException($"disk-controller: start, cmd (0x{_cmd:X}) unknown");
        }
    }

    /// <summary>Faithful port of disk_controller_get_attention
    /// (usim/disk-controller.c:633-640) -- "not used at the moment but if a
    /// disk unit is for example changes state while idle, this can be
    /// used".</summary>
    private void OnDiskUnitAttention(DiskUnit p)
    {
        if (_notActive && _attentionInterruptEnable) AssertInterrupt();
    }

    /// <summary>Faithful port of disk_controller_read (usim/disk-controller.c:724-767).
    /// While reset is in effect, EVERY read (all four offsets) returns 0 --
    /// this check happens before the offset switch in the real C, not
    /// per-offset.</summary>
    public uint Read(uint offset)
    {
        if (_resetCondition) return 0;

        switch (offset)
        {
            case 0: return EncodeStatus();
            case 1: return SelectedUnit().LastMemoryAddress;
            case 2: return _da;
            case 3: return 0; // no ECC errors modeled in usim
            default:
                throw new InvalidOperationException($"disk-controller: unknown read {offset}");
        }
    }

    /// <summary>Faithful port of disk_controller_write (usim/disk-controller.c:769-834).
    /// Reset magic value 016 octal = 0xE.</summary>
    public void Write(uint offset, uint v)
    {
        switch (offset)
        {
            case 0:
                // "Reset. ... After storing a Reset command you should store
                // 0 in the command register to turn off the reset condition."
                if (v == 0) { _cmd = 0; _resetCondition = false; break; }
                if (v == 0xE) { Reset(); _resetCondition = true; break; }
                if (_resetCondition) break;
                _cmd = v;
                _doneInterruptEnable = (v & 0x800) != 0;
                _attentionInterruptEnable = (v & 0x400) != 0;
                if (!_doneInterruptEnable && !_attentionInterruptEnable) DeassertInterrupt();
                break;

            case 1:
                if (_resetCondition) break;
                _clp = v;
                break;

            case 2:
                if (_resetCondition) break;
                // "Storing into the Disk Address register momentarily
                // deselects the current unit ..."
                _da = v;
                break;

            case 3:
                if (_resetCondition) break;
                Start();
                break;

            default:
                throw new InvalidOperationException($"disk-controller: unknown write {offset}");
        }
    }

    /// <summary>disk_controller_bus_reset (usim/disk-controller.c:836-839) --
    /// empty in the real C too.</summary>
    public void BusReset() { }
}
```

- [ ] **Step 4: Write `usim-cs/DiskControllerTests.cs`**

Cover, following this codebase's established `RunAllTests`/`Assert` test-file pattern (check an existing `*Tests.cs` file for the exact boilerplate — this file needs its own local sparse-T80-image helper, duplicated from Step 2's `MakeSparseT80Image`; do not add a cross-file dependency on `DiskUnitTests`'s private helper):

- A helper that builds a `DiskController` wired to a real `MainMemory` and `UCode`, with unit 0 configured against a sparse T-80 image.
- Every `Start()` command path: write `0x800`|cmd-bits appropriately to exercise read/read-compare/write/seek/recalibrate/fault-clear/at-ease/offset-clear, and the combined at-ease+recalibrate+fault-clear bit pattern (`cmd = 0x5 | 0x200 | 0x100`).
- `EncodeStatus()` (via `Read(0)`) bit-for-bit: set each of `SeekError`/`HasFault`/`Attention` on the unit directly and confirm the corresponding bit appears; confirm bit 9 flips with `Online`.
- Reset protocol: `Write(0, 0xE)` then confirm `Read(0)==0` and `Read(2)==0` (any offset returns 0 while reset); `Write(0, 0)` exits it, subsequent reads/writes work normally again.
- Read-only unit + write command (`_cmd & 0xF == 0x9`) sets `HasFault` on the unit, does not call `SubmitXfer` (verify no transfer happened — e.g. `LastMemoryAddress` unchanged).
- A full read/write transfer through `MainMemory`: build a 2-block CCW chain in memory (first CCW's low bit set to chain to a second, second CCW's low bit clear to end), issue a real write command, confirm both physical-memory pages ended up in the disk image (read back via `DiskUnit.Read` directly, bypassing the controller) and the disk-side ends up matching a subsequent real read command's output. Confirm `LastMemoryAddress` matches the documented convention (set to the CCW read address first, then the transfer's start address, then `start+255` on success).
- Interrupt assert/deassert: enable done-interrupt (`v & 0x800`), issue a command that completes, confirm `UCode.InterruptStatusReg`/`InterruptPendingFlag` reflect the assert (via the real `UCode` instance passed into the constructor, not a mock); confirm writing `cmd` with neither interrupt-enable bit set deasserts.

- [ ] **Step 5: Wire both test suites into `Program.cs`**

Add `--test-disk-unit` and `--test-disk-controller` CLI cases, usage-help lines, and `RunAllTests()` entries, mirroring the existing `--test-bus-interface`/`BusInterfaceTests` pattern exactly (one case in the CLI switch, one usage-help `Console.WriteLine`, one block in `RunAllTests()`, per suite).

- [ ] **Step 6: Build and test**

Run: `dotnet build usim-cs`
Expected: builds clean.

Run: `dotnet run --project usim-cs -- --test-disk-unit`
Expected: `Passed: 10`, `Failed: 0`.

Run: `dotnet run --project usim-cs -- --test-disk-controller`
Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add usim-cs/DiskUnit.cs usim-cs/DiskUnitTests.cs usim-cs/DiskController.cs usim-cs/DiskControllerTests.cs usim-cs/Program.cs
git commit -m "Replace invented DiskController/DiskUnit with a faithful port of
usim/disk-controller.c + disk-unit.c

Standalone classes + tests -- not yet wired into BusAdaptor/
MachineControl (a later task)."
```

---

### Task 3: Real `[disk]` config, wired through the live config path

**Files:**
- Modify: `usim-cs/UsimConstants.cs` (add `UsimState.DiskUnits`)
- Modify: `usim-cs/Program.cs` (`ApplyConfiguration` gains disk-section parsing; visibility change to `internal`)
- Modify: `usim-cs/ConfigManager.cs` (delete dead `GetDiskConfig()`/`DiskConfig`)
- Modify: `usim-cs/ConfigParser.cs` (add a small test-only `SetValue` helper, if none exists)
- Test: `usim-cs/ConfigTests.cs`

**Interfaces:**
- Consumes: `DiskController.NUMBER_OF_DISK_UNITS` (Task 2 — already landed, since Task 2 precedes this one).
- Produces (for Task 4): `UsimState.DiskUnits` — `public static (uint Unit, string TypeName, string Filename)[] DiskUnits { get; set; }`, populated by `Program.ApplyConfiguration`. Absent/unconfigured units are simply not present in the array (no placeholder entries).

- [ ] **Step 1: Add `UsimState.DiskUnits`**

In `usim-cs/UsimConstants.cs`, inside the `UsimState` static class, add:

```csharp
    public static (uint Unit, string TypeName, string Filename)[] DiskUnits { get; set; } = Array.Empty<(uint, string, string)>();
```

(Add `using System;` at the top of the file if not already present, for `Array.Empty`.)

- [ ] **Step 2: Delete `ConfigManager.cs`'s dead disk config**

In `usim-cs/ConfigManager.cs`, delete the entire `GetDiskConfig()` method and the entire `DiskConfig` class (confirmed zero callers anywhere in `usim-cs` — verify this yourself with a repo-wide grep for `GetDiskConfig` and `DiskConfig` before deleting, matching what the spec already confirmed).

- [ ] **Step 3: Parse the real `[disk]` section in `Program.cs`'s `ApplyConfiguration`**

Change `ApplyConfiguration`'s signature from `private static void ApplyConfiguration(ConfigParser config)` to `internal static void ApplyConfiguration(ConfigParser config)` (needed so a test can call it directly — same reasoning as this codebase's other internal test-only entry points).

Add, inside `ApplyConfiguration`, after its existing lines:

```csharp
        var diskUnits = new List<(uint, string, string)>();
        for (uint i = 0; i < DiskController.NUMBER_OF_DISK_UNITS; i++)
        {
            string line = config.GetString("disk", $"disk{i}", "");
            if (string.IsNullOrEmpty(line)) continue;

            string typeName;
            string filename;
            string[] parts = line.Split(',', 2);
            if (parts.Length == 1)
            {
                // Real C's one-token fallback (usim/disk-unit.c:236-241):
                // a bare filename with no comma defaults the type to T-300.
                typeName = "T-300";
                filename = parts[0].Trim();
            }
            else
            {
                typeName = parts[0].Trim();
                filename = parts[1].Trim();
            }

            diskUnits.Add((i, typeName, filename));
        }
        UsimState.DiskUnits = diskUnits.ToArray();
```

Add `using System.Collections.Generic;` at the top of `Program.cs` if not already present. `DiskController.NUMBER_OF_DISK_UNITS` is a real, already-landed constant by this point (Task 2 precedes this task) — reference it directly, no placeholder value needed.

- [ ] **Step 4: Add tests to `ConfigTests.cs`**

```csharp
private static bool TestApplyConfigurationParsesDiskUnits()
{
    Console.WriteLine("Test: ApplyConfiguration parses the [disk] section into UsimState.DiskUnits");
    try
    {
        var parser = new ConfigParser();
        // Simulate a loaded config with a [disk] section, two units configured,
        // one bare-filename (no comma, type defaults to T-300), rest absent.
        parser.SetValue("disk", "disk0", "T-80,/path/to/unit0.img");
        parser.SetValue("disk", "disk3", "/path/to/unit3-bare.img");

        Program.ApplyConfiguration(parser);

        Assert(UsimState.DiskUnits.Length == 2, $"exactly 2 units configured, got {UsimState.DiskUnits.Length}");

        var unit0 = Array.Find(UsimState.DiskUnits, u => u.Unit == 0);
        Assert(unit0.TypeName == "T-80", $"unit 0 type is T-80, got {unit0.TypeName}");
        Assert(unit0.Filename == "/path/to/unit0.img", $"unit 0 filename correct, got {unit0.Filename}");

        var unit3 = Array.Find(UsimState.DiskUnits, u => u.Unit == 3);
        Assert(unit3.TypeName == "T-300", $"unit 3 (bare filename) defaults to T-300, got {unit3.TypeName}");
        Assert(unit3.Filename == "/path/to/unit3-bare.img", $"unit 3 filename correct, got {unit3.Filename}");

        Assert(Array.Find(UsimState.DiskUnits, u => u.Unit == 1).Filename == null,
            "unit 1 (never configured) is absent from the array");

        Console.WriteLine("  ApplyConfiguration disk-units test passed\n");
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ApplyConfiguration disk-units test failed: {ex.Message}\n");
        return false;
    }
}
```

Check `ConfigParser.cs` for a `SetValue`-shaped method to populate a section/key programmatically for a test (rather than writing a temp `.ini` file to disk) — if none exists, add a minimal `internal void SetValue(string section, string key, string value)` to `ConfigParser.cs` (creating the section dictionary if absent), used only by this test. Register the new test in `ConfigTests.cs`'s `RunAllTests()`.

- [ ] **Step 5: Build and test**

Run: `dotnet build usim-cs` then `dotnet run --project usim-cs -- --test-config` (or whatever CLI switch runs `ConfigTests`).
Expected: builds clean, all `ConfigTests` pass including the new one.

- [ ] **Step 6: Commit**

```bash
git add usim-cs/UsimConstants.cs usim-cs/Program.cs usim-cs/ConfigManager.cs usim-cs/ConfigTests.cs usim-cs/ConfigParser.cs
git commit -m "Add real per-unit [disk] config section, wired through ApplyConfiguration

Deletes the dead ConfigManager.GetDiskConfig()/DiskConfig (zero callers)."
```

---

### Task 4: Wire everything together

**Files:**
- Modify: `usim-cs/BusAdaptor.cs`
- Modify: `usim-cs/MachineControl.cs`
- Modify: `usim-cs/BusAdaptorTests.cs`

**Interfaces:**
- Consumes: `DiskController` (Task 2), `UsimState.DiskUnits` (Task 3).

- [ ] **Step 1: Wire `DiskController` into `BusAdaptor`**

In `usim-cs/BusAdaptor.cs`, add a field and a settable-once wiring method (not a constructor parameter — `BusAdaptor` is built inside `UCode`'s own constructor, before `DiskController` can exist, since `DiskController` itself needs the already-fully-constructed `UCode` for interrupts):

```csharp
    private DiskController? _diskController;

    public void WireDiskController(DiskController diskController)
    {
        _diskController = diskController;
    }
```

Change `ReadXbusIo`'s disk-control branch from:

```csharp
        if (paddr >= DiskControlLo && paddr <= DiskControlHi)
        {
            uint offset = paddr - DiskControlLo;
            // Faithful port of encode_status() (usim/disk-controller.c:174-219) for
            // the two bits the boot PROM's DISK-RECALIBRATE polls: bit0=not_active
            // (ready/idle)=1, bit9=!online=0 (i.e. online). Every other status bit
            // (seek_error, read_only, has_fault, attention, interrupt_request, any
            // real error condition) defaults to 0 -- this is not a real disk, just
            // "no errors, ready, online". Offsets 1 (memory address), 2 (disk
            // address), 3 (ECC, "no ECC errors in usim, so this always returns 0")
            // have no real disk state to report either.
            return offset == 0 ? 1u : 0u;
        }
```

to:

```csharp
        if (paddr >= DiskControlLo && paddr <= DiskControlHi)
        {
            uint offset = paddr - DiskControlLo;
            return _diskController!.Read(offset);
        }
```

Change `WriteXbusIo`'s disk-control branch from:

```csharp
        if (paddr >= DiskControlLo && paddr <= DiskControlHi)
        {
            // Command/CLP/DA writes: a no-op. This is NOT a working disk -- no real
            // transfer happens. Tracked as a deliberate, out-of-scope gap in the
            // Phase 5B spec section, not silently implied to work.
            return;
        }
```

to:

```csharp
        if (paddr >= DiskControlLo && paddr <= DiskControlHi)
        {
            uint offset = paddr - DiskControlLo;
            _diskController!.Write(offset, v);
            return;
        }
```

- [ ] **Step 2: Wire construction order in `MachineControl.cs`**

Change the constructor from:

```csharp
    public MachineControl()
    {
        Memory = new MainMemory();
        DiskController = new DiskController();
        Keyboard = new Keyboard();
        Mouse = new Mouse();
        Display = new Display();
        IOBus = new IOBus();
        UCode = new UCode(Memory);

        State = PowerState.Off;
        IsStopped = true;
    }
```

to:

```csharp
    public MachineControl()
    {
        Memory = new MainMemory();
        UCode = new UCode(Memory);
        DiskController = new DiskController(Memory, UCode);
        UCode.BusAdaptor.WireDiskController(DiskController);
        Keyboard = new Keyboard();
        Mouse = new Mouse();
        Display = new Display();
        IOBus = new IOBus();

        State = PowerState.Off;
        IsStopped = true;
    }
```

- [ ] **Step 3: Replace the hardcoded single-disk mount in `LoadSystemFiles`**

Change:

```csharp
        // Mount disk images
        string diskImage = Path.Combine(UsimState.SysDirectory, "disk.img");
        if (File.Exists(diskImage))
        {
            Console.WriteLine($"Mounting disk: {diskImage}");
            DiskController.Mount(0, diskImage);
        }
```

to:

```csharp
        // Configure disk units from [disk] config (UsimState.DiskUnits,
        // populated by Program.ApplyConfiguration)
        foreach (var (unit, typeName, filename) in UsimState.DiskUnits)
        {
            Console.WriteLine($"Configuring disk unit {unit}: {typeName},{filename}");
            DiskController.ConfigureUnit(unit, typeName, filename);
        }
```

- [ ] **Step 4: Fix `BusAdaptorTests.cs`'s disk-control tests**

`BusAdaptor`'s constructor still only takes `BusInterface` (unchanged — `DiskController` is wired post-construction via `WireDiskController`, not a constructor parameter). **Every existing test that exercises the disk-control range must now also construct and wire a real `DiskController` before use**, or `_diskController!.Read(...)` will null-reference.

Replace `TestDiskControlStatusRead` entirely — its old assertions assumed the stub's hardcoded "always ready, online" fake status. With a real, unconfigured `DiskController`, unit 0 is genuinely offline (matching real hardware with no disk attached — see this plan's Global Constraints), so the correct assertion is the opposite:

```csharp
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
```

`TestDiskControlOtherOffsetsAndWrites` currently asserts offsets 1-3 read `0` and writes are no-ops with `promDisabled` untouched — with a real `DiskController` and no unit configured, offset 1 (`LastMemoryAddress` of unit 0, a freshly-constructed `DiskUnit` whose `LastMemoryAddress` defaults to 0), offset 2 (`_da`, defaults to 0), and offset 3 (always 0, no ECC modeled) all still read `0`, so those assertions still hold. Update this test's construction to build and wire a real `DiskController` the same way as above. This test's existing write to offset 0 uses `0x16` (decimal 22) as a "reset" value in its comment — **that value is wrong**: the real reset magic value is `0xE` (octal `016`), independently verified in the spec's octal table. Fix this write to use `0xE`. Writes to offsets 1/2 with arbitrary data should still not throw and should leave `promDisabled` untouched, same as before.

- [ ] **Step 5: Add a real-dispatch-confirmation test to `BusAdaptorTests.cs`**

```csharp
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
```

Register it in `RunAllTests()`.

- [ ] **Step 6: Build and run the full suite**

Run: `dotnet build usim-cs`
Expected: builds clean.

Run: `dotnet run --project usim-cs -- --test-all`
Expected: every suite passes, including `BusAdaptorTests`, `MachineControlTests`, `DiskUnitTests`, `DiskControllerTests`, `ConfigTests`, `MainMemoryTests` (all touched across this plan).

- [ ] **Step 7: Commit**

```bash
git add usim-cs/BusAdaptor.cs usim-cs/MachineControl.cs usim-cs/BusAdaptorTests.cs
git commit -m "Wire DiskController into BusAdaptor/MachineControl for real

BusAdaptor's disk-control range now dispatches to a real, faithful
DiskController instead of a hardcoded always-ready stub. Disk units
are configured from the real [disk] config section instead of a
single hardcoded disk.img path."
```
