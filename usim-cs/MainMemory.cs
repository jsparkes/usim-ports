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
    
    // Virtual memory configuration
    public const int VIRTUAL_PAGES = 32768;     // Total virtual pages (32K)
    public const uint MAP_BITS = 23;            // Bits for map entry
    
    // Memory arrays
    private readonly uint[] _physicalMemory;
    private readonly uint[] _pageMap;           // Virtual to physical mapping
    
    // Statistics
    public ulong ReadCount { get; private set; }
    public ulong WriteCount { get; private set; }
    public ulong PageFaultCount { get; private set; }
    
    public MainMemory()
    {
        _physicalMemory = new uint[PHYSICAL_MEM_SIZE];
        _pageMap = new uint[VIRTUAL_PAGES];
        
        Initialize();
    }
    
    /// <summary>
    /// Initialize memory system
    /// </summary>
    public void Initialize()
    {
        Array.Clear(_physicalMemory);
        Array.Clear(_pageMap);
        
        ReadCount = 0;
        WriteCount = 0;
        PageFaultCount = 0;
        
        // Initialize identity mapping for lower pages
        for (int i = 0; i < 256; i++)
        {
            _pageMap[i] = (uint)i;
        }
    }
    
    /// <summary>
    /// Read word from virtual address
    /// </summary>
    public uint Read(uint virtualAddress)
    {
        ReadCount++;
        
        if (TraceLog.Instance.EnabledCategories.HasFlag(TraceCategory.Memory))
        {
            TraceLog.Instance.Verbose(TraceCategory.Memory, $"Read VA=0x{virtualAddress:X8}");
        }
        
        uint physicalAddress = TranslateAddress(virtualAddress);
        if (physicalAddress >= PHYSICAL_MEM_SIZE)
        {
            Console.WriteLine($"Memory read out of bounds: VA=0x{virtualAddress:X} PA=0x{physicalAddress:X}");
            return 0;
        }
        
        return _physicalMemory[physicalAddress];
    }
    
    /// <summary>
    /// Write word to virtual address
    /// </summary>
    public void Write(uint virtualAddress, uint value)
    {
        WriteCount++;
        
        if (TraceLog.Instance.EnabledCategories.HasFlag(TraceCategory.Memory))
        {
            TraceLog.Instance.Verbose(TraceCategory.Memory, $"Write VA=0x{virtualAddress:X8} value=0x{value:X8}");
        }
        
        if (TraceLog.Instance.EnabledCategories.HasFlag(TraceCategory.Memory))
        {
            TraceLog.Instance.Verbose(TraceCategory.Memory, $"Write VA=0x{virtualAddress:X8} value=0x{value:X8}");
        }
        
        uint physicalAddress = TranslateAddress(virtualAddress);
        if (physicalAddress >= PHYSICAL_MEM_SIZE)
        {
            Console.WriteLine($"Memory write out of bounds: VA=0x{virtualAddress:X} PA=0x{physicalAddress:X}");
            return;
        }
        
        _physicalMemory[physicalAddress] = value;
    }
    
    /// <summary>
    /// Translate virtual address to physical address
    /// </summary>
    private uint TranslateAddress(uint virtualAddress)
    {
        uint page = virtualAddress >> PAGE_SIZE_BITS;
        uint offset = virtualAddress & ((1u << PAGE_SIZE_BITS) - 1);
        
        if (page >= VIRTUAL_PAGES)
        {
            PageFaultCount++;
            return 0; // Invalid page
        }
        
        uint physicalPage = _pageMap[page] & ((1u << PAGE_SIZE_BITS) - 1);
        return (physicalPage << PAGE_SIZE_BITS) | offset;
    }
    
    /// <summary>
    /// Set page mapping
    /// </summary>
    public void SetPageMap(uint virtualPage, uint physicalPage)
    {
        if (virtualPage < VIRTUAL_PAGES)
        {
            _pageMap[virtualPage] = physicalPage;
        }
    }
    
    /// <summary>
    /// Get page mapping
    /// </summary>
    public uint GetPageMap(uint virtualPage)
    {
        return virtualPage < VIRTUAL_PAGES ? _pageMap[virtualPage] : 0;
    }
    
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
        Console.WriteLine($"  Page Faults: {PageFaultCount:N0}");
    }
}
