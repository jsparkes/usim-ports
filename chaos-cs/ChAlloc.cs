// ChAlloc.cs - Memory allocation for Chaos
// Converted from chunix/challoc.c

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Chaos;

/// <summary>
/// Memory allocation utilities for Chaos network operations
/// </summary>
public static class ChAlloc
{
    /// <summary>
    /// Allocate memory for chaos operations
    /// </summary>
    /// <param name="size">Size in bytes to allocate</param>
    /// <param name="canWait">Whether operation can wait (ignored in managed code)</param>
    /// <returns>Allocated memory block</returns>
    public static IntPtr Alloc(int size, bool canWait = true)
    {
        // In C# managed code, we allocate through Marshal for unmanaged interop
        // or just use managed arrays. For now, using Marshal to maintain compatibility
        IntPtr ptr = Marshal.AllocHGlobal(size);
        
        // Zero out the memory
        unsafe
        {
            byte* p = (byte*)ptr;
            for (int i = 0; i < size; i++)
            {
                p[i] = 0;
            }
        }
        
        return ptr;
    }
    
    /// <summary>
    /// Free allocated memory
    /// </summary>
    /// <param name="ptr">Pointer to free</param>
    public static void Free(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
    
    /// <summary>
    /// Get the size of an allocated block (approximation)
    /// </summary>
    /// <param name="ptr">Pointer to check</param>
    /// <returns>Size in bytes, or -1 if unknown</returns>
    public static int Size(IntPtr ptr)
    {
        // In managed C#, we don't have direct access to allocation size
        // This would need to be tracked separately if needed
        return -1;
    }
    
    /// <summary>
    /// Check if an address is bad (always returns false in managed code)
    /// </summary>
    /// <param name="ptr">Address to check</param>
    /// <returns>false (managed code handles bad addresses via exceptions)</returns>
    public static bool BadAddr(IntPtr ptr)
    {
        return false;
    }
    
    /// <summary>
    /// Allocate buffer pool (no-op in managed implementation)
    /// </summary>
    public static void BufAlloc()
    {
        // No-op in managed code
    }
    
    /// <summary>
    /// Free buffer pool (no-op in managed implementation)
    /// </summary>
    public static void BufFree()
    {
        // No-op in managed code
    }
}

/// <summary>
/// Generic memory allocation helper with tracking
/// </summary>
/// <typeparam name="T">Type to allocate</typeparam>
public class ManagedChAlloc<T> where T : struct
{
    private readonly List<T> _allocations = new();
    
    public T Allocate()
    {
        var item = new T();
        _allocations.Add(item);
        return item;
    }
    
    public void Free(T item)
    {
        _allocations.Remove(item);
    }
    
    public void FreeAll()
    {
        _allocations.Clear();
    }
    
    public int Count => _allocations.Count;
}
