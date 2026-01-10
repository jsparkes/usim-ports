// McrReader.cs - MCR (microcode) file reader

using System;
using System.IO;

namespace UsimTools;

public static class McrReader
{
    public static void ReadMcr(string filename, bool verbose)
    {
        if (!File.Exists(filename))
        {
            Console.WriteLine($"File not found: {filename}");
            return;
        }
        
        Console.WriteLine($"Reading MCR file: {filename}");
        
        try
        {
            using var stream = File.OpenRead(filename);
            
            // Read header (simplified format)
            byte[] header = new byte[16];
            stream.Read(header, 0, 16);
            
            Console.WriteLine($"File size: {stream.Length} bytes");
            Console.WriteLine($"Instructions: {(stream.Length - 16) / 8}");
            
            if (verbose)
            {
                Console.WriteLine("\nHeader:");
                Console.WriteLine($"  Magic: {BitConverter.ToUInt32(header, 0):X8}");
                Console.WriteLine($"  Version: {BitConverter.ToUInt32(header, 4):X8}");
                Console.WriteLine($"  Count: {BitConverter.ToUInt32(header, 8)}");
            }
            
            Console.WriteLine("\nMicrocode loaded successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading MCR: {ex.Message}");
        }
    }
    
    public static void ShowMcr(string filename, bool verbose, bool disasm)
    {
        if (!File.Exists(filename))
        {
            Console.WriteLine($"File not found: {filename}");
            return;
        }
        
        Console.WriteLine($"MCR File: {filename}\n");
        
        try
        {
            using var stream = File.OpenRead(filename);
            using var reader = new BinaryReader(stream);
            
            // Skip header
            reader.ReadBytes(16);
            
            int addr = 0;
            while (stream.Position < stream.Length)
            {
                if (stream.Length - stream.Position < 8)
                    break;
                
                ulong instruction = reader.ReadUInt64();
                
                Console.Write($"{addr:X4}: {instruction:X16}");
                
                if (disasm)
                {
                    Console.Write($"  ; {DisassembleInstruction(instruction)}");
                }
                
                Console.WriteLine();
                
                addr++;
                
                if (!verbose && addr > 100)
                {
                    Console.WriteLine("... (use --verbose to see all)");
                    break;
                }
            }
            
            Console.WriteLine($"\nTotal instructions: {addr}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
    
    private static string DisassembleInstruction(ulong inst)
    {
        // Simplified disassembly
        int op = (int)((inst >> 43) & 0x1F);
        int dest = (int)((inst >> 38) & 0x1F);
        
        return $"OP={op:X2} DEST={dest:X2}";
    }
}
