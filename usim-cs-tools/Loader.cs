// Loader.cs - Load files into disk images

using System;
using System.IO;

namespace UsimTools;

public static class Loader
{
    public static void LoadFile(string diskImage, string filename, uint address)
    {
        if (!File.Exists(filename))
        {
            Console.WriteLine($"File not found: {filename}");
            return;
        }
        
        if (!File.Exists(diskImage))
        {
            Console.WriteLine($"Disk image not found: {diskImage}");
            return;
        }
        
        Console.WriteLine($"Loading {filename} into {diskImage} at 0x{address:X}");
        
        try
        {
            byte[] data = File.ReadAllBytes(filename);
            
            using var stream = File.Open(diskImage, FileMode.Open, FileAccess.ReadWrite);
            
            // Seek to address (word address * 4 bytes)
            stream.Seek(address * 4, SeekOrigin.Begin);
            
            // Write data
            stream.Write(data, 0, data.Length);
            
            Console.WriteLine($"Loaded {data.Length} bytes ({data.Length / 4} words)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
