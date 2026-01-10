// DiskMaker.cs - Create disk images

using System;
using System.IO;

namespace UsimTools;

public static class DiskMaker
{
    public static void CreateDisk(string filename, int sizeMB)
    {
        Console.WriteLine($"Creating disk image: {filename}");
        Console.WriteLine($"Size: {sizeMB} MB");
        
        long sizeBytes = (long)sizeMB * 1024 * 1024;
        long sizeWords = sizeBytes / 4; // 32-bit words
        
        try
        {
            using var stream = File.Create(filename);
            using var writer = new BinaryWriter(stream);
            
            // Write empty disk
            byte[] block = new byte[4096];
            long blocksToWrite = sizeBytes / block.Length;
            
            for (long i = 0; i < blocksToWrite; i++)
            {
                writer.Write(block);
                
                if (i % 1024 == 0)
                {
                    Console.Write($"\rWriting: {(i * 100 / blocksToWrite)}%");
                }
            }
            
            Console.WriteLine($"\rWriting: 100%");
            Console.WriteLine($"Created disk image: {filename}");
            Console.WriteLine($"Size: {sizeWords:N0} words ({sizeMB} MB)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating disk: {ex.Message}");
        }
    }
}
