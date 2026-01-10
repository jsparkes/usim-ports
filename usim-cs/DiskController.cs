// DiskController.cs - Disk controller emulation
// Converted from disk-controller.h and disk-controller.c

using System;
using System.IO;

namespace Usim;

/// <summary>
/// Disk controller states
/// </summary>
public enum DiskControllerState
{
    Idle,
    Reading,
    Writing,
    Seeking,
    Error
}

/// <summary>
/// Disk controller emulation for CADR
/// </summary>
public class DiskController
{
    // Controller configuration
    public const int MAX_UNITS = 8;
    public const int SECTOR_SIZE = 256;        // Words per sector
    public const int SECTORS_PER_TRACK = 16;
    public const int TRACKS_PER_CYLINDER = 8;
    
    // Controller state
    public DiskControllerState State { get; private set; }
    public int CurrentUnit { get; private set; }
    public int CurrentCylinder { get; private set; }
    public int CurrentHead { get; private set; }
    public int CurrentSector { get; private set; }
    
    // Status registers
    public uint StatusRegister { get; private set; }
    public uint ErrorRegister { get; private set; }
    public uint DataRegister { get; private set; }
    
    // Disk units
    private readonly DiskUnit[] _units;
    
    // Statistics
    public ulong ReadOperations { get; private set; }
    public ulong WriteOperations { get; private set; }
    public ulong SeekOperations { get; private set; }
    
    public DiskController()
    {
        _units = new DiskUnit[MAX_UNITS];
        for (int i = 0; i < MAX_UNITS; i++)
        {
            _units[i] = new DiskUnit(i);
        }
        
        Initialize();
    }
    
    /// <summary>
    /// Initialize disk controller
    /// </summary>
    public void Initialize()
    {
        State = DiskControllerState.Idle;
        CurrentUnit = 0;
        CurrentCylinder = 0;
        CurrentHead = 0;
        CurrentSector = 0;
        
        StatusRegister = 0;
        ErrorRegister = 0;
        DataRegister = 0;
        
        ReadOperations = 0;
        WriteOperations = 0;
        SeekOperations = 0;
    }
    
    /// <summary>
    /// Mount disk image on unit
    /// </summary>
    public bool Mount(int unit, string filename)
    {
        if (unit < 0 || unit >= MAX_UNITS)
        {
            Console.WriteLine($"Invalid unit number: {unit}");
            return false;
        }
        
        return _units[unit].Mount(filename);
    }
    
    /// <summary>
    /// Unmount disk from unit
    /// </summary>
    public void Unmount(int unit)
    {
        if (unit >= 0 && unit < MAX_UNITS)
        {
            _units[unit].Unmount();
        }
    }
    
    /// <summary>
    /// Select disk unit
    /// </summary>
    public void SelectUnit(int unit)
    {
        if (unit >= 0 && unit < MAX_UNITS)
        {
            CurrentUnit = unit;
        }
    }
    
    /// <summary>
    /// Seek to cylinder
    /// </summary>
    public void Seek(int cylinder, int head)
    {
        SeekOperations++;
        State = DiskControllerState.Seeking;
        
        CurrentCylinder = cylinder;
        CurrentHead = head;
        
        // Simulate seek time (instant for now)
        State = DiskControllerState.Idle;
        StatusRegister |= 0x80; // Set ready bit
    }
    
    /// <summary>
    /// Read sector
    /// </summary>
    public uint[] ReadSector(int sector)
    {
        ReadOperations++;
        State = DiskControllerState.Reading;
        
        TraceLog.Instance.Debug(TraceCategory.Disk, 
            $"Read unit={CurrentUnit} C={CurrentCylinder} H={CurrentHead} S={sector}");
        
        TraceLog.Instance.Debug(TraceCategory.Disk, 
            $"Read unit={CurrentUnit} C={CurrentCylinder} H={CurrentHead} S={sector}");
        
        CurrentSector = sector;
        
        var unit = _units[CurrentUnit];
        if (!unit.IsMounted)
        {
            State = DiskControllerState.Error;
            ErrorRegister = 0x01; // Unit not ready
            return new uint[SECTOR_SIZE];
        }
        
        var data = unit.ReadSector(CurrentCylinder, CurrentHead, sector);
        
        State = DiskControllerState.Idle;
        StatusRegister |= 0x80; // Set ready bit
        
        return data;
    }
    
    /// <summary>
    /// Write sector
    /// </summary>
    public void WriteSector(int sector, uint[] data)
    {
        WriteOperations++;
        State = DiskControllerState.Writing;
        
        CurrentSector = sector;
        
        var unit = _units[CurrentUnit];
        if (!unit.IsMounted)
        {
            State = DiskControllerState.Error;
            ErrorRegister = 0x01; // Unit not ready
            return;
        }
        
        unit.WriteSector(CurrentCylinder, CurrentHead, sector, data);
        
        State = DiskControllerState.Idle;
        StatusRegister |= 0x80; // Set ready bit
    }
    
    /// <summary>
    /// Get unit status
    /// </summary>
    public bool IsUnitReady(int unit)
    {
        return unit >= 0 && unit < MAX_UNITS && _units[unit].IsMounted;
    }
    
    /// <summary>
    /// Print statistics
    /// </summary>
    public void PrintStatistics()
    {
        Console.WriteLine("Disk Controller Statistics:");
        Console.WriteLine($"  Reads:  {ReadOperations:N0}");
        Console.WriteLine($"  Writes: {WriteOperations:N0}");
        Console.WriteLine($"  Seeks:  {SeekOperations:N0}");
        
        for (int i = 0; i < MAX_UNITS; i++)
        {
            if (_units[i].IsMounted)
            {
                Console.WriteLine($"  Unit {i}: {_units[i].Filename}");
            }
        }
    }
}

/// <summary>
/// Individual disk unit
/// </summary>
public class DiskUnit
{
    public int UnitNumber { get; }
    public bool IsMounted { get; private set; }
    public string? Filename { get; private set; }
    
    private FileStream? _imageStream;
    private const int BYTES_PER_WORD = 4;
    
    public DiskUnit(int unitNumber)
    {
        UnitNumber = unitNumber;
    }
    
    /// <summary>
    /// Mount disk image
    /// </summary>
    public bool Mount(string filename)
    {
        try
        {
            if (_imageStream != null)
            {
                Unmount();
            }
            
            _imageStream = File.Open(filename, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            Filename = filename;
            IsMounted = true;
            
            Console.WriteLine($"Mounted disk unit {UnitNumber}: {filename}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error mounting disk: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Unmount disk image
    /// </summary>
    public void Unmount()
    {
        if (_imageStream != null)
        {
            _imageStream.Close();
            _imageStream = null;
        }
        
        IsMounted = false;
        Filename = null;
    }
    
    /// <summary>
    /// Calculate sector offset in file
    /// </summary>
    private long GetSectorOffset(int cylinder, int head, int sector)
    {
        long sectorNumber = (cylinder * DiskController.TRACKS_PER_CYLINDER * DiskController.SECTORS_PER_TRACK) +
                           (head * DiskController.SECTORS_PER_TRACK) +
                           sector;
        
        return sectorNumber * DiskController.SECTOR_SIZE * BYTES_PER_WORD;
    }
    
    /// <summary>
    /// Read sector from disk
    /// </summary>
    public uint[] ReadSector(int cylinder, int head, int sector)
    {
        var data = new uint[DiskController.SECTOR_SIZE];
        
        if (!IsMounted || _imageStream == null)
            return data;
        
        try
        {
            long offset = GetSectorOffset(cylinder, head, sector);
            _imageStream.Seek(offset, SeekOrigin.Begin);
            
            byte[] buffer = new byte[DiskController.SECTOR_SIZE * BYTES_PER_WORD];
            int bytesRead = _imageStream.Read(buffer, 0, buffer.Length);
            
            for (int i = 0; i < DiskController.SECTOR_SIZE && i * BYTES_PER_WORD < bytesRead; i++)
            {
                data[i] = BitConverter.ToUInt32(buffer, i * BYTES_PER_WORD);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading sector: {ex.Message}");
        }
        
        return data;
    }
    
    /// <summary>
    /// Write sector to disk
    /// </summary>
    public void WriteSector(int cylinder, int head, int sector, uint[] data)
    {
        if (!IsMounted || _imageStream == null)
            return;
        
        try
        {
            long offset = GetSectorOffset(cylinder, head, sector);
            _imageStream.Seek(offset, SeekOrigin.Begin);
            
            byte[] buffer = new byte[DiskController.SECTOR_SIZE * BYTES_PER_WORD];
            for (int i = 0; i < DiskController.SECTOR_SIZE; i++)
            {
                byte[] wordBytes = BitConverter.GetBytes(data[i]);
                Array.Copy(wordBytes, 0, buffer, i * BYTES_PER_WORD, BYTES_PER_WORD);
            }
            
            _imageStream.Write(buffer, 0, buffer.Length);
            _imageStream.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing sector: {ex.Message}");
        }
    }
}
