// MainMemory.cs - Main memory management
// Converted from main-memory.h and main-memory.c

using System;
using System.IO;

namespace Usim;

/// <summary>
/// Main memory system for CADR simulator
/// Handles virtual memory, paging, and physical memory management
/// </summary>
public class MainMemory
{
    // Memory configuration
    public const int PAGE_SIZE = 256;           // Words per page
    public const int PAGE_SIZE_BITS = 8;        // log2(PAGE_SIZE)
    public const int PHYSICAL_PAGES = 16384;    // Total physical pages
    public const int PHYSICAL_MEM_SIZE = PHYSICAL_PAGES * PAGE_SIZE;
    
    // Memory arrays
    private readonly uint[] _physicalMemory;

    // Statistics
    public ulong ReadCount { get; private set; }
    public ulong WriteCount { get; private set; }

    public MainMemory()
    {
        _physicalMemory = new uint[PHYSICAL_MEM_SIZE];

        Initialize();
    }

    /// <summary>
    /// Initialize memory system
    /// </summary>
    public void Initialize()
    {
        Array.Clear(_physicalMemory);

        ReadCount = 0;
        WriteCount = 0;
    }

    /// <summary>
    /// Read word from a physical address (thin wrapper over ReadPhysical,
    /// kept for LoadFromFile/SaveToFile/Dump and the debug examine/deposit
    /// commands -- the invented virtual-paging TranslateAddress this used
    /// to go through has been retired; nothing in this codebase needs it,
    /// and it silently misbehaved past its fake 64K-word identity-mapped
    /// range).
    /// </summary>
    public uint Read(uint physicalAddress)
    {
        ReadCount++;

        if (TraceLog.Instance.EnabledCategories.HasFlag(TraceCategory.Memory))
        {
            TraceLog.Instance.Verbose(TraceCategory.Memory, $"Read PA=0x{physicalAddress:X8}");
        }

        return ReadPhysical(physicalAddress);
    }

    /// <summary>
    /// Write word to a physical address. See Read()'s doc comment.
    /// </summary>
    public void Write(uint physicalAddress, uint value)
    {
        WriteCount++;

        if (TraceLog.Instance.EnabledCategories.HasFlag(TraceCategory.Memory))
        {
            TraceLog.Instance.Verbose(TraceCategory.Memory, $"Write PA=0x{physicalAddress:X8} value=0x{value:X8}");
        }

        WritePhysical(physicalAddress, value);
    }

    /// <summary>
    /// Direct physical-memory access, bypassing this class's own (separate,
    /// invented) virtual-paging TranslateAddress -- the caller (UCode.Vm(),
    /// via Uvmem's faithful L1/L2 tables) has already resolved the physical
    /// address itself.
    /// </summary>
    public uint ReadPhysical(uint physicalAddress) => physicalAddress < PHYSICAL_MEM_SIZE ? _physicalMemory[physicalAddress] : 0;
    public void WritePhysical(uint physicalAddress, uint value) { if (physicalAddress < PHYSICAL_MEM_SIZE) _physicalMemory[physicalAddress] = value; }

    /// <summary>
    /// Load memory from file
    /// </summary>
    public void LoadFromFile(string filename, uint startAddress)
    {
        if (!File.Exists(filename))
        {
            Console.WriteLine($"Memory file not found: {filename}");
            return;
        }
        
        using var stream = File.OpenRead(filename);
        using var reader = new BinaryReader(stream);
        
        uint address = startAddress;
        while (stream.Position < stream.Length)
        {
            uint word = reader.ReadUInt32();
            Write(address++, word);
        }
        
        Console.WriteLine($"Loaded {stream.Position / 4} words from {filename}");
    }
    
    /// <summary>
    /// Save memory to file
    /// </summary>
    public void SaveToFile(string filename, uint startAddress, uint length)
    {
        using var stream = File.Create(filename);
        using var writer = new BinaryWriter(stream);
        
        for (uint i = 0; i < length; i++)
        {
            writer.Write(Read(startAddress + i));
        }
        
        Console.WriteLine($"Saved {length} words to {filename}");
    }
    
    /// <summary>
    /// Dump memory region for debugging
    /// </summary>
    public void Dump(uint startAddress, uint length)
    {
        Console.WriteLine($"Memory dump from 0x{startAddress:X8}:");
        
        for (uint i = 0; i < length; i += 8)
        {
            Console.Write($"{startAddress + i:X8}: ");
            
            for (uint j = 0; j < 8 && i + j < length; j++)
            {
                uint word = Read(startAddress + i + j);
                Console.Write($"{word:X8} ");
            }
            
            Console.WriteLine();
        }
    }
    
    /// <summary>
    /// Print memory statistics
    /// </summary>
    public void PrintStatistics()
    {
        Console.WriteLine("Memory Statistics:");
        Console.WriteLine($"  Reads:       {ReadCount:N0}");
        Console.WriteLine($"  Writes:      {WriteCount:N0}");
    }
}
