// MiscUtils.cs - Miscellaneous utility functions
// Converted from misc.h and misc.c

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Usim;

/// <summary>
/// Miscellaneous utility functions for USIM
/// </summary>
public static class MiscUtils
{
    /// <summary>
    /// Compare two strings for equality
    /// </summary>
    public static bool StrEq(string? a, string? b)
    {
        return string.Equals(a, b, StringComparison.Ordinal);
    }
    
    /// <summary>
    /// Convert string to lowercase
    /// </summary>
    public static string StrLwr(string str)
    {
        return str.ToLowerInvariant();
    }
    
    /// <summary>
    /// Dump memory for debugging
    /// </summary>
    public static void DumpMem(byte[] data, int length)
    {
        for (int i = 0; i < length; i += 16)
        {
            Console.Write($"{i:X8}: ");
            
            // Hex output
            for (int j = 0; j < 16 && i + j < length; j++)
            {
                Console.Write($"{data[i + j]:X2} ");
            }
            
            // ASCII output
            Console.Write(" ");
            for (int j = 0; j < 16 && i + j < length; j++)
            {
                char c = (char)data[i + j];
                Console.Write(char.IsControl(c) ? '.' : c);
            }
            
            Console.WriteLine();
        }
    }
    
    /// <summary>
    /// Read 16-bit little-endian value
    /// </summary>
    public static ushort Read16LE(Stream stream)
    {
        byte[] buffer = new byte[2];
        stream.Read(buffer, 0, 2);
        return (ushort)(buffer[0] | (buffer[1] << 8));
    }
    
    /// <summary>
    /// Read 32-bit little-endian value
    /// </summary>
    public static uint Read32LE(Stream stream)
    {
        byte[] buffer = new byte[4];
        stream.Read(buffer, 0, 4);
        return (uint)(buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24));
    }
    
    /// <summary>
    /// Read 32-bit PDP-endian value
    /// </summary>
    public static uint Read32PDP(Stream stream)
    {
        byte[] buffer = new byte[4];
        stream.Read(buffer, 0, 4);
        // PDP endian: swap pairs of bytes
        return (uint)((buffer[1] << 8) | buffer[0] | (buffer[3] << 24) | (buffer[2] << 16));
    }
    
    /// <summary>
    /// Write 16-bit little-endian value
    /// </summary>
    public static void Write16LE(Stream stream, ushort value)
    {
        byte[] buffer = new byte[2];
        buffer[0] = (byte)(value & 0xFF);
        buffer[1] = (byte)((value >> 8) & 0xFF);
        stream.Write(buffer, 0, 2);
    }
    
    /// <summary>
    /// Write 32-bit little-endian value
    /// </summary>
    public static void Write32LE(Stream stream, uint value)
    {
        byte[] buffer = new byte[4];
        buffer[0] = (byte)(value & 0xFF);
        buffer[1] = (byte)((value >> 8) & 0xFF);
        buffer[2] = (byte)((value >> 16) & 0xFF);
        buffer[3] = (byte)((value >> 24) & 0xFF);
        stream.Write(buffer, 0, 4);
    }
    
    /// <summary>
    /// Convert 4 characters to a 32-bit value
    /// </summary>
    public static uint Str4(string str)
    {
        if (str.Length < 4)
            str = str.PadRight(4);
            
        return (uint)((str[0] << 24) | (str[1] << 16) | (str[2] << 8) | str[3]);
    }
    
    /// <summary>
    /// Convert 32-bit value to 4-character string
    /// </summary>
    public static string UnStr4(uint value)
    {
        char[] chars = new char[4];
        chars[0] = (char)((value >> 24) & 0xFF);
        chars[1] = (char)((value >> 16) & 0xFF);
        chars[2] = (char)((value >> 8) & 0xFF);
        chars[3] = (char)(value & 0xFF);
        return new string(chars);
    }
    
    /// <summary>
    /// Load a byte from a word with position and size
    /// </summary>
    public static ulong LoadByte(ulong word, int position, int size)
    {
        ulong mask = (1UL << size) - 1;
        return (word >> position) & mask;
    }
    
    /// <summary>
    /// Deposit a byte into a word at position
    /// </summary>
    public static ulong DepositByte(ulong word, int position, int size, ulong value)
    {
        ulong mask = (1UL << size) - 1;
        word &= ~(mask << position);
        word |= (value & mask) << position;
        return word;
    }
    
    /// <summary>
    /// LDB - Load byte from word
    /// </summary>
    public static uint Ldb(int ppss, uint word)
    {
        int pos = (ppss >> 6) & 0x3f;
        int size = ppss & 0x3f;
        uint mask = (uint)((1 << size) - 1);
        return (word >> pos) & mask;
    }
    
    /// <summary>
    /// DPB - Deposit byte into word
    /// </summary>
    public static uint Dpb(uint value, int ppss, uint word)
    {
        int pos = (ppss >> 6) & 0x3f;
        int size = ppss & 0x3f;
        uint mask = (uint)((1 << size) - 1);
        word &= ~(mask << pos);
        word |= (value & mask) << (pos);
        return word;
    }
    
    /// <summary>
    /// Test a bit in a word
    /// </summary>
    public static uint BitTest(uint word, uint bit)
    {
        return (word >> (int)bit) & 1;
    }
    
    /// <summary>
    /// Test if LDB result is non-zero
    /// </summary>
    public static bool LdbTest(int ppss, uint word)
    {
        return Ldb(ppss, word) != 0;
    }
    
    /// <summary>
    /// Rotate left (circular shift) for 32-bit unsigned integer
    /// </summary>
    public static uint Rol32(uint value, int count)
    {
        count &= 0x1F; // Mask to 0-31 bits
        return (value << count) | (value >> (32 - count));
    }
    
    /// <summary>
    /// Rotate right (circular shift) for 32-bit unsigned integer
    /// </summary>
    public static uint Ror32(uint value, int count)
    {
        count &= 0x1F; // Mask to 0-31 bits
        return (value >> count) | (value << (32 - count));
    }
    
    /// <summary>
    /// Rotate left with carry for 32-bit unsigned integer
    /// </summary>
    public static uint RolWithCarry(uint value, int count, ref bool carry)
    {
        count &= 0x1F;
        if (count == 0)
            return value;
            
        uint result = (value << count) | (value >> (32 - count));
        carry = ((value >> (32 - count)) & 1) != 0;
        return result;
    }
    
    /// <summary>
    /// Rotate right with carry for 32-bit unsigned integer
    /// </summary>
    public static uint RorWithCarry(uint value, int count, ref bool carry)
    {
        count &= 0x1F;
        if (count == 0)
            return value;
            
        uint result = (value >> count) | (value << (32 - count));
        carry = ((value >> (count - 1)) & 1) != 0;
        return result;
    }
    
    /// <summary>
    /// Arithmetic shift right (sign-extending)
    /// </summary>
    public static uint Asr32(uint value, int count)
    {
        count &= 0x1F;
        int signedValue = (int)value;
        return (uint)(signedValue >> count);
    }
    
    /// <summary>
    /// Logical shift right
    /// </summary>
    public static uint Lsr32(uint value, int count)
    {
        count &= 0x1F;
        return value >> count;
    }
    
    /// <summary>
    /// Logical shift left
    /// </summary>
    public static uint Lsl32(uint value, int count)
    {
        count &= 0x1F;
        return value << count;
    }
    
    /// <summary>
    /// Count leading zeros
    /// </summary>
    public static int CountLeadingZeros(uint value)
    {
        if (value == 0)
            return 32;
            
        int count = 0;
        if ((value & 0xFFFF0000) == 0) { count += 16; value <<= 16; }
        if ((value & 0xFF000000) == 0) { count += 8; value <<= 8; }
        if ((value & 0xF0000000) == 0) { count += 4; value <<= 4; }
        if ((value & 0xC0000000) == 0) { count += 2; value <<= 2; }
        if ((value & 0x80000000) == 0) { count += 1; }
        return count;
    }
    
    /// <summary>
    /// Count trailing zeros
    /// </summary>
    public static int CountTrailingZeros(uint value)
    {
        if (value == 0)
            return 32;
            
        int count = 0;
        if ((value & 0x0000FFFF) == 0) { count += 16; value >>= 16; }
        if ((value & 0x000000FF) == 0) { count += 8; value >>= 8; }
        if ((value & 0x0000000F) == 0) { count += 4; value >>= 4; }
        if ((value & 0x00000003) == 0) { count += 2; value >>= 2; }
        if ((value & 0x00000001) == 0) { count += 1; }
        return count;
    }
    
    /// <summary>
    /// Count number of set bits (population count)
    /// </summary>
    public static int PopCount(uint value)
    {
        value = value - ((value >> 1) & 0x55555555);
        value = (value & 0x33333333) + ((value >> 2) & 0x33333333);
        value = (value + (value >> 4)) & 0x0F0F0F0F;
        value = value + (value >> 8);
        value = value + (value >> 16);
        return (int)(value & 0x3F);
    }
    
    /// <summary>
    /// Sign extend from specified bit position
    /// </summary>
    public static uint SignExtend(uint value, int bits)
    {
        int shift = 32 - bits;
        return (uint)((int)(value << shift) >> shift);
    }
    
    /// <summary>
    /// Trim string to n characters
    /// </summary>
    public static string StrNTrim(string src, int n)
    {
        if (src.Length <= n)
            return src;
        return src.Substring(0, n).TrimEnd();
    }
    
    /// <summary>
    /// Convert boolean to string representation
    /// </summary>
    public static string BoolToStr(bool value)
    {
        return value ? "true" : "false";
    }
}
