// TraceLog.cs - Tracing and logging utilities

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Usim;

/// <summary>
/// Trace categories
/// </summary>
[Flags]
public enum TraceCategory
{
    None = 0,
    MicroCode = 1,
    Memory = 2,
    Disk = 4,
    Display = 8,
    Keyboard = 16,
    Mouse = 32,
    Network = 64,
    IOBus = 128,
    All = 0xFFFF
}

/// <summary>
/// Trace level
/// </summary>
public enum TraceLevel
{
    Error,
    Warning,
    Info,
    Debug,
    Verbose
}

/// <summary>
/// Tracing and logging system
/// </summary>
public class TraceLog
{
    private static TraceLog? _instance;
    public static TraceLog Instance => _instance ??= new TraceLog();
    
    public TraceCategory EnabledCategories { get; set; } = TraceCategory.None;
    public TraceLevel MinimumLevel { get; set; } = TraceLevel.Info;
    
    private readonly StreamWriter? _logWriter;
    private readonly object _lock = new();
    
    public TraceLog(string? logFile = null)
    {
        if (logFile != null)
        {
            try
            {
                _logWriter = new StreamWriter(logFile, append: true)
                {
                    AutoFlush = true
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open log file: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Log a trace message
    /// </summary>
    public void Trace(TraceCategory category, TraceLevel level, string message)
    {
        if ((EnabledCategories & category) == 0)
            return;
        
        if (level < MinimumLevel)
            return;
        
        lock (_lock)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string levelStr = level.ToString().ToUpperInvariant().PadRight(7);
            string categoryStr = category.ToString().PadRight(10);
            string line = $"[{timestamp}] {levelStr} {categoryStr}: {message}";
            
            Console.WriteLine(line);
            _logWriter?.WriteLine(line);
        }
    }
    
    /// <summary>
    /// Log error
    /// </summary>
    public void Error(TraceCategory category, string message)
    {
        Trace(category, TraceLevel.Error, message);
    }
    
    /// <summary>
    /// Log warning
    /// </summary>
    public void Warning(TraceCategory category, string message)
    {
        Trace(category, TraceLevel.Warning, message);
    }
    
    /// <summary>
    /// Log info
    /// </summary>
    public void Info(TraceCategory category, string message)
    {
        Trace(category, TraceLevel.Info, message);
    }
    
    /// <summary>
    /// Log debug
    /// </summary>
    public void Debug(TraceCategory category, string message)
    {
        Trace(category, TraceLevel.Debug, message);
    }
    
    /// <summary>
    /// Log verbose
    /// </summary>
    public void Verbose(TraceCategory category, string message)
    {
        Trace(category, TraceLevel.Verbose, message);
    }
    
    /// <summary>
    /// Enable category
    /// </summary>
    public void Enable(TraceCategory category)
    {
        EnabledCategories |= category;
    }
    
    /// <summary>
    /// Disable category
    /// </summary>
    public void Disable(TraceCategory category)
    {
        EnabledCategories &= ~category;
    }
    
    /// <summary>
    /// Close log file
    /// </summary>
    public void Close()
    {
        _logWriter?.Close();
    }
}

/// <summary>
/// Performance measurement utilities
/// </summary>
public class PerformanceCounter
{
    private readonly Dictionary<string, Stopwatch> _timers = new();
    private readonly Dictionary<string, long> _counts = new();
    private readonly Dictionary<string, long> _totals = new();
    
    /// <summary>
    /// Start timing an operation
    /// </summary>
    public void Start(string name)
    {
        if (!_timers.ContainsKey(name))
        {
            _timers[name] = new Stopwatch();
        }
        
        _timers[name].Restart();
    }
    
    /// <summary>
    /// Stop timing and record
    /// </summary>
    public void Stop(string name)
    {
        if (_timers.TryGetValue(name, out var timer))
        {
            timer.Stop();
            
            if (!_counts.ContainsKey(name))
            {
                _counts[name] = 0;
                _totals[name] = 0;
            }
            
            _counts[name]++;
            _totals[name] += timer.ElapsedMilliseconds;
        }
    }
    
    /// <summary>
    /// Increment a counter
    /// </summary>
    public void Increment(string name, long amount = 1)
    {
        if (!_counts.ContainsKey(name))
        {
            _counts[name] = 0;
        }
        
        _counts[name] += amount;
    }
    
    /// <summary>
    /// Get counter value
    /// </summary>
    public long GetCount(string name)
    {
        return _counts.TryGetValue(name, out long value) ? value : 0;
    }
    
    /// <summary>
    /// Get average time
    /// </summary>
    public double GetAverageTime(string name)
    {
        if (_counts.TryGetValue(name, out long count) && count > 0)
        {
            if (_totals.TryGetValue(name, out long total))
            {
                return (double)total / count;
            }
        }
        return 0;
    }
    
    /// <summary>
    /// Print all statistics
    /// </summary>
    public void PrintStatistics()
    {
        Console.WriteLine("=== Performance Statistics ===");
        
        foreach (var name in _counts.Keys.OrderBy(k => k))
        {
            long count = _counts[name];
            
            if (_totals.ContainsKey(name))
            {
                double avg = GetAverageTime(name);
                Console.WriteLine($"{name,-30} Count: {count,10:N0}  Avg: {avg,8:F3}ms");
            }
            else
            {
                Console.WriteLine($"{name,-30} Count: {count,10:N0}");
            }
        }
    }
    
    /// <summary>
    /// Reset all counters
    /// </summary>
    public void Reset()
    {
        _timers.Clear();
        _counts.Clear();
        _totals.Clear();
    }
}
