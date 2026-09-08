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

        // FileShare.Delete (beyond the .NET default of none) lets a test's
        // File.Delete succeed on Windows even while this handle is still
        // open -- a pure hosting-OS/test-hygiene concern, invisible to the
        // emulated hardware (POSIX open() has no share-mode concept at all).
        _file = new FileStream(filename, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
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
