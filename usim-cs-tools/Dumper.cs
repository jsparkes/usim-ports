// Dumper.cs - Dump memory/disk contents

using System;
using System.IO;

namespace UsimTools;

public static class Dumper
{
    public static void DumpFile(string filename, uint start, uint length, string format)
    {
        if (!File.Exists(filename))
        {
            Console.WriteLine($"File not found: {filename}");
            return;
        }
        
        Console.WriteLine($"Dumping {filename}");
        Console.WriteLine($"Start: 0x{start:X8}, Length: {length} words\n");
        
        try
        {
            using var stream = File.OpenRead(filename);
            using var reader = new BinaryReader(stream);
            
            // Seek to start address
            stream.Seek(start * 4, SeekOrigin.Begin);
            
            switch (format.ToLowerInvariant())
            {
                case "hex":
                    DumpHex(reader, start, length);
                    break;
                    
                case "binary":
                    DumpBinary(reader, start, length);
                    break;
                    
                case "text":
                    DumpText(reader, start, length);
                    break;
                    
                default:
                    Console.WriteLine($"Unknown format: {format}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
    
    private static void DumpHex(BinaryReader reader, uint start, uint length)
    {
        for (uint i = 0; i < length && reader.BaseStream.Position < reader.BaseStream.Length; i++)
        {
            if (i % 8 == 0)
            {
                if (i > 0)
                    Console.WriteLine();
                Console.Write($"{start + i:X8}: ");
            }
            
            uint word = reader.ReadUInt32();
            Console.Write($"{word:X8} ");
        }
        Console.WriteLine();
    }
    
    private static void DumpBinary(BinaryReader reader, uint start, uint length)
    {
        for (uint i = 0; i < length && reader.BaseStream.Position < reader.BaseStream.Length; i++)
        {
            uint word = reader.ReadUInt32();
            Console.WriteLine($"{start + i:X8}: {Convert.ToString(word, 2).PadLeft(32, '0')}");
        }
    }
    
    private static void DumpText(BinaryReader reader, uint start, uint length)
    {
        for (uint i = 0; i < length && reader.BaseStream.Position < reader.BaseStream.Length; i++)
        {
            uint word = reader.ReadUInt32();
            
            // Try to interpret as ASCII
            char[] chars = new char[4];
            chars[0] = (char)((word >> 24) & 0xFF);
            chars[1] = (char)((word >> 16) & 0xFF);
            chars[2] = (char)((word >> 8) & 0xFF);
            chars[3] = (char)(word & 0xFF);
            
            // Replace non-printable with '.'
            for (int j = 0; j < 4; j++)
            {
                if (chars[j] < 32 || chars[j] > 126)
                    chars[j] = '.';
            }
            
            Console.WriteLine($"{start + i:X8}: {word:X8}  {new string(chars)}");
        }
    }
}
