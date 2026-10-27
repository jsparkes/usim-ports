// TapeDrive.cs - Faithful port of usim/tape-drive.c. FileStream-backed, not
// mmap (matching DiskUnit.cs's own established choice). See
// docs/superpowers/specs/2026-10-26-tape-subsystem-design.md.

using System;
using System.IO;

namespace Usim;

public class TapeDrive
{
    public const int NUMBER_OF_TAPE_DRIVES = 8;
    public const uint FILE_MARK = 0xFFFFFFFF; // real C's own comment claims 0x00000000 -- the code (this value) is the authority, see spec.
    private const int EOT_OFFSET_FT = 4;

    public uint Unit { get; }
    public bool Configured { get; private set; }
    public bool Online { get; private set; }
    public bool ReadOnly { get; private set; }
    public bool Bot { get; private set; }
    public bool Eot { get; private set; }
    public bool Rws { get; private set; }
    public long Pos { get; private set; }

    private long _lengthFt;
    private long _fileSize;
    private long _eotPos;
    private FileStream? _file;

    public TapeDrive(uint unit)
    {
        if (unit >= NUMBER_OF_TAPE_DRIVES)
            throw new ArgumentOutOfRangeException(nameof(unit), $"tape-drive: {unit} exceeds the number of maximum tape drives ({NUMBER_OF_TAPE_DRIVES})");
        Unit = unit;
    }

    /// <summary>
    /// Faithful port of tape_drive_init's config-string-already-split
    /// behavior (the string-splitting itself happens in Program.cs's
    /// ApplyConfiguration, mirroring the real C's own [length,][mode,]filename
    /// token-counting logic there -- see Task 3). Non-fatal on any failure
    /// except the unit-index check already enforced by the constructor --
    /// a missing/undersized/unopenable file logs and leaves the unit offline,
    /// never throws (confirmed divergence from DiskUnit.Configure's stricter
    /// fatal-on-missing-file rule -- see Global Constraints).
    /// </summary>
    public void Configure(long lengthFeet, string mode, string filename)
    {
        Online = false;
        if (lengthFeet <= 0 || (mode != "ro" && mode != "rw") || string.IsNullOrEmpty(filename))
        {
            TraceLog.Instance.Warning(TraceCategory.Tape, $"tape-drive {Unit}: configuration is invalid");
            return;
        }

        Configured = true;
        _lengthFt = lengthFeet;
        ReadOnly = mode == "ro";

        if (!File.Exists(filename))
        {
            TraceLog.Instance.Warning(TraceCategory.Tape, $"tape-drive {Unit}: offline (cannot open tape file: {filename})");
            return;
        }

        long actualSize = new FileInfo(filename).Length;
        long minimumRequiredSize = _lengthFt * 12 * 1600;
        if (actualSize < minimumRequiredSize)
        {
            TraceLog.Instance.Warning(TraceCategory.Tape, $"tape-drive {Unit}: the size of {filename} is not right. Minimum {minimumRequiredSize} bytes required.");
            return;
        }

        try
        {
            _file = new FileStream(filename, FileMode.Open, ReadOnly ? FileAccess.Read : FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TraceLog.Instance.Warning(TraceCategory.Tape, $"tape-drive {Unit}: offline (cannot open tape file: {filename})");
            return;
        }

        _fileSize = actualSize;
        SetBpi(1600); // real C's own default before any MTC DEN-bit write
        Pos = 0;
        Bot = true;
        Online = true;
    }

    public void SetBpi(uint bpi)
    {
        _eotPos = (_lengthFt - EOT_OFFSET_FT) * 12 * bpi;
    }

    public void SeekAbsolute(long pos) => Seek(pos, relative: false);
    public void SeekRelative(long delta) => Seek(delta, relative: true);

    private void Seek(long value, bool relative)
    {
        Pos = relative ? Pos + value : value;
        if (Pos < 0) Pos = 0;
        else if (Pos > _fileSize) Pos = _fileSize;
        Bot = Pos == 0;
        Eot = Pos >= _eotPos;
    }

    public bool ReadByte(out byte value)
    {
        value = 0;
        if (_file == null || Pos >= _fileSize) return false;
        _file.Seek(Pos, SeekOrigin.Begin);
        int b = _file.ReadByte();
        if (b < 0) return false;
        value = (byte)b;
        Pos++;
        return true;
    }

    public bool WriteByte(byte value)
    {
        // ReadOnly guard: the real C maps read-only media PROT_READ, so writes
        // are never valid there; FileStream would throw NotSupportedException.
        if (_file == null || ReadOnly || Pos >= _fileSize) return false;
        _file.Seek(Pos, SeekOrigin.Begin);
        _file.WriteByte(value);
        Pos++;
        return true;
    }

    public bool ReadUInt32(out uint value)
    {
        value = 0;
        for (int i = 0; i < 4; i++)
        {
            if (!ReadByte(out byte b)) return false;
            value |= (uint)b << (i * 8);
        }
        return true;
    }

    public bool WriteUInt32(uint value)
    {
        for (int i = 0; i < 4; i++)
        {
            if (!WriteByte((byte)(value >> (i * 8)))) return false;
        }
        return true;
    }

    public void WriteRecordLength(uint length) => WriteUInt32(length);
    public void WriteFileMark() => WriteUInt32(FILE_MARK);

    public void RewindAndOffline()
    {
        Rws = true;
        SeekAbsolute(0);
        Rws = false;
        Eot = false;
        _file?.Dispose();
        _file = null;
        Online = false;
    }
}
