// IOBus.cs - I/O bus emulation
// Converted from iob.h and iob.c

using System;
using System.Collections.Generic;
using System.Linq;

namespace Usim;

/// <summary>
/// I/O bus device types
/// </summary>
public enum IODeviceType
{
    Disk,
    Tape,
    Network,
    Serial,
    Parallel,
    Clock,
    Unknown
}

/// <summary>
/// I/O bus device interface
/// </summary>
public interface IIODevice
{
    string Name { get; }
    IODeviceType DeviceType { get; }
    uint BaseAddress { get; }
    uint AddressRange { get; }
    
    void Initialize();
    uint Read(uint address);
    void Write(uint address, uint value);
    void Reset();
}

/// <summary>
/// I/O bus controller
/// Manages all I/O devices on the Unibus
/// </summary>
public class IOBus
{
    // Registered devices
    private readonly List<IIODevice> _devices = new();
    
    // Interrupt system
    private readonly Queue<int> _interruptQueue = new();
    public bool InterruptPending => _interruptQueue.Count > 0;
    
    // Statistics
    public ulong ReadCount { get; private set; }
    public ulong WriteCount { get; private set; }
    
    public IOBus()
    {
        Initialize();
    }
    
    /// <summary>
    /// Initialize I/O bus
    /// </summary>
    public void Initialize()
    {
        _devices.Clear();
        _interruptQueue.Clear();
        ReadCount = 0;
        WriteCount = 0;
    }
    
    /// <summary>
    /// Register I/O device
    /// </summary>
    public void RegisterDevice(IIODevice device)
    {
        _devices.Add(device);
        device.Initialize();
        Console.WriteLine($"Registered I/O device: {device.Name} at 0x{device.BaseAddress:X8}");
    }
    
    /// <summary>
    /// Unregister device
    /// </summary>
    public void UnregisterDevice(IIODevice device)
    {
        _devices.Remove(device);
    }
    
    /// <summary>
    /// Find device for address
    /// </summary>
    private IIODevice? FindDevice(uint address)
    {
        foreach (var device in _devices)
        {
            if (address >= device.BaseAddress && 
                address < device.BaseAddress + device.AddressRange)
            {
                return device;
            }
        }
        return null;
    }
    
    /// <summary>
    /// Read from I/O address
    /// </summary>
    public uint Read(uint address)
    {
        ReadCount++;
        
        var device = FindDevice(address);
        if (device != null)
        {
            return device.Read(address - device.BaseAddress);
        }
        
        // No device at this address
        return 0xFFFFFFFF;
    }
    
    /// <summary>
    /// Write to I/O address
    /// </summary>
    public void Write(uint address, uint value)
    {
        WriteCount++;
        
        var device = FindDevice(address);
        if (device != null)
        {
            device.Write(address - device.BaseAddress, value);
        }
    }
    
    /// <summary>
    /// Assert interrupt
    /// </summary>
    public void AssertInterrupt(int level)
    {
        if (!_interruptQueue.Contains(level))
        {
            _interruptQueue.Enqueue(level);
        }
    }
    
    /// <summary>
    /// Deassert interrupt
    /// </summary>
    public void DeassertInterrupt(int level)
    {
        // Remove from queue
        var temp = new Queue<int>();
        while (_interruptQueue.Count > 0)
        {
            int l = _interruptQueue.Dequeue();
            if (l != level)
            {
                temp.Enqueue(l);
            }
        }
        
        // Restore queue without the deasserted interrupt
        while (temp.Count > 0)
        {
            _interruptQueue.Enqueue(temp.Dequeue());
        }
    }
    
    /// <summary>
    /// Get next pending interrupt
    /// </summary>
    public int? GetNextInterrupt()
    {
        if (_interruptQueue.Count > 0)
        {
            return _interruptQueue.Dequeue();
        }
        return null;
    }
    
    /// <summary>
    /// Reset all devices
    /// </summary>
    public void Reset()
    {
        foreach (var device in _devices)
        {
            device.Reset();
        }
        _interruptQueue.Clear();
    }
    
    /// <summary>
    /// Get all registered devices
    /// </summary>
    public IEnumerable<IIODevice> GetDevices()
    {
        return _devices;
    }
    
    /// <summary>
    /// Print device list
    /// </summary>
    public void PrintDevices()
    {
        Console.WriteLine("I/O Bus Devices:");
        foreach (var device in _devices)
        {
            Console.WriteLine($"  {device.Name,-20} Type: {device.DeviceType,-10} " +
                            $"Base: 0x{device.BaseAddress:X8} Range: 0x{device.AddressRange:X}");
        }
    }
    
    /// <summary>
    /// Print statistics
    /// </summary>
    public void PrintStatistics()
    {
        Console.WriteLine("I/O Bus Statistics:");
        Console.WriteLine($"  Reads:  {ReadCount:N0}");
        Console.WriteLine($"  Writes: {WriteCount:N0}");
        Console.WriteLine($"  Devices: {_devices.Count}");
    }
}

/// <summary>
/// Simple clock device for I/O bus
/// </summary>
public class ClockDevice : IIODevice
{
    public string Name => "System Clock";
    public IODeviceType DeviceType => IODeviceType.Clock;
    public uint BaseAddress { get; }
    public uint AddressRange => 8;
    
    private uint _clockRegister;
    private DateTime _startTime;
    
    public ClockDevice(uint baseAddress = 0x10000)
    {
        BaseAddress = baseAddress;
        _startTime = DateTime.Now;
    }
    
    public void Initialize()
    {
        _startTime = DateTime.Now;
        _clockRegister = 0;
    }
    
    public uint Read(uint address)
    {
        return address switch
        {
            0 => GetClockTicks(),           // Current time in ticks
            4 => (uint)DateTime.Now.Ticks,  // Raw time
            _ => 0
        };
    }
    
    public void Write(uint address, uint value)
    {
        // Clock is read-only
    }
    
    public void Reset()
    {
        Initialize();
    }
    
    private uint GetClockTicks()
    {
        var elapsed = DateTime.Now - _startTime;
        return (uint)(elapsed.TotalMilliseconds * 60); // 60Hz ticks
    }
}
