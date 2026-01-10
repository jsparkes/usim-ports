// SymbolTable.cs - Symbol table support
// Converted from usym.h and usym.c

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Usim;

/// <summary>
/// Symbol table entry
/// </summary>
public class Symbol
{
    public string Name { get; set; } = string.Empty;
    public uint Address { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Symbol table for microcode and PROM symbols
/// </summary>
public class SymbolTable
{
    private readonly Dictionary<uint, Symbol> _byAddress = new();
    private readonly Dictionary<string, Symbol> _byName = new();
    
    /// <summary>
    /// Add a symbol to the table
    /// </summary>
    public void Add(string name, uint address, string? description = null)
    {
        var symbol = new Symbol
        {
            Name = name,
            Address = address,
            Description = description
        };
        
        _byAddress[address] = symbol;
        _byName[name] = symbol;
    }
    
    /// <summary>
    /// Look up symbol by address
    /// </summary>
    public Symbol? LookupByAddress(uint address)
    {
        _byAddress.TryGetValue(address, out var symbol);
        return symbol;
    }
    
    /// <summary>
    /// Look up symbol by name
    /// </summary>
    public Symbol? LookupByName(string name)
    {
        _byName.TryGetValue(name, out var symbol);
        return symbol;
    }
    
    /// <summary>
    /// Get symbol name for address, or return hex address
    /// </summary>
    public string GetNameOrHex(uint address)
    {
        var symbol = LookupByAddress(address);
        return symbol?.Name ?? $"0x{address:X}";
    }
    
    /// <summary>
    /// Clear all symbols
    /// </summary>
    public void Clear()
    {
        _byAddress.Clear();
        _byName.Clear();
    }
    
    /// <summary>
    /// Get count of symbols
    /// </summary>
    public int Count => _byAddress.Count;
    
    /// <summary>
    /// Load symbols from file
    /// </summary>
    public void LoadFromFile(string filename)
    {
        if (!File.Exists(filename))
        {
            Console.WriteLine($"Warning: Symbol file not found: {filename}");
            return;
        }
        
        var lines = File.ReadAllLines(filename);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("#"))
                continue;
                
            // Parse format: address name [description]
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                if (uint.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out uint address))
                {
                    string name = parts[1];
                    string? description = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : null;
                    Add(name, address, description);
                }
            }
        }
        
        Console.WriteLine($"Loaded {Count} symbols from {filename}");
    }
}

/// <summary>
/// Global symbol tables
/// </summary>
public static class Symbols
{
    public static SymbolTable Mcr { get; } = new SymbolTable();
    public static SymbolTable Prom { get; } = new SymbolTable();
}
