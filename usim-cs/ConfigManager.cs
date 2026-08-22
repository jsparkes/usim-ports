// ConfigManager.cs - Configuration management and validation
// Provides centralized configuration loading for USIM

using System;
using System.IO;

namespace Usim;

/// <summary>
/// Manages configuration loading and validation for the CADR emulator
/// </summary>
public class ConfigManager
{
    private readonly ConfigParser _config;
    private readonly UCode _uCode = new UCode();

    public ConfigManager()
    {
        _config = new ConfigParser();
    }
    
    /// <summary>
    /// Load configuration from file with fallback to defaults
    /// </summary>
    public bool Load(string? filename = null)
    {
        // Try provided filename first
        if (!string.IsNullOrEmpty(filename) && File.Exists(filename))
        {
            return _config.Load(filename);
        }
        
        // Try default locations
        string[] defaultPaths = new[]
        {
            "usim.ini",
            "config/usim.ini",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".usim", "config.ini"),
            "/etc/usim/config.ini"
        };
        
        foreach (var path in defaultPaths)
        {
            if (File.Exists(path))
            {
                Console.WriteLine($"Loading configuration from: {path}");
                return _config.Load(path);
            }
        }
        
        Console.WriteLine("No configuration file found, using defaults");
        return false;
    }
    
    /// <summary>
    /// Apply configuration to system components
    /// </summary>
    public void ApplyConfiguration()
    {
        // General settings
        UsimState.Headless = _config.GetBool("General", "headless", false);
        UsimState.AutoBoot = _config.GetBool("General", "auto_boot", false);
        UsimState.AutoPowerOff = _config.GetBool("General", "auto_poweroff", false);
        UsimState.VerboseDumpStateFlag = _config.GetBool("General", "verbose", false);
        
        // Paths
        UsimState.SysDirectory = _config.GetPath("Paths", "sys_directory", "./sys");
        UsimState.FsRootDirectory = _config.GetPath("Paths", "fs_root", "./fs");
        UsimState.StateFilename = _config.GetPath("Paths", "state_file", "usim.state");
        
        // Display
        UsimState.ColorTvEnabled = _config.GetBool("Display", "color_tv", true);
        
        // Microcode tracing
        _uCode.InstructionTraceEnabled = _config.GetBool("Microcode", "instruction_trace", false);
        _uCode.MicrocodeTraceEnabled = _config.GetBool("Microcode", "microcode_trace", false);

        // Tracing settings
        var traceExecution = _config.GetBool("Tracing", "trace_execution", false);
        var traceMicrocode = _config.GetBool("Tracing", "trace_microcode", false);
        var maxTraceLines = _config.GetInt("Tracing", "max_trace_lines", 10000);

        if (traceExecution || traceMicrocode)
        {
            _uCode.InstructionTraceEnabled = traceExecution;
            _uCode.MicrocodeTraceEnabled = traceMicrocode;
            _uCode.MaxTraceLines = maxTraceLines;
        }

        // Debug
        var traceCategories = _config.GetString("Debug", "trace_categories", "");
        var traceLevel = _config.GetString("Debug", "trace_level", "Info");

        Console.WriteLine("Configuration applied successfully");

        // Report trace settings
        if (_uCode.InstructionTraceEnabled)
        {
            Console.WriteLine("Instruction tracing enabled (console output)");
        }
        if (_uCode.MicrocodeTraceEnabled)
        {
            Console.WriteLine($"Microcode tracing enabled (buffer size: {_uCode.MaxTraceLines})");
        }
    }
    
    /// <summary>
    /// Get display configuration
    /// </summary>
    public DisplayConfig GetDisplayConfig()
    {
        return new DisplayConfig
        {
            Width = _config.GetInt("Display", "width", 1024),
            Height = _config.GetInt("Display", "height", 808),
            Scale = _config.GetDouble("Display", "scale", 1.0),
            UseLinearFiltering = _config.GetBool("Display", "use_linear_filtering", true),
            AllowResize = _config.GetBool("Display", "allow_resize", false),
            ColorTv = _config.GetBool("Display", "color_tv", true),
            FrameRate = _config.GetInt("Display", "frame_rate", 60)
        };
    }
    
    /// <summary>
    /// Get memory configuration
    /// </summary>
    public MemoryConfig GetMemoryConfig()
    {
        return new MemoryConfig
        {
            MainMemorySize = _config.GetHex("Memory", "main_memory_size", 0x2000000),
            UsePhysicalMemory = _config.GetBool("Memory", "use_physical_memory", false),
            EnablePaging = _config.GetBool("Memory", "enable_paging", true),
            PageSize = _config.GetHex("Memory", "page_size", 0x400)
        };
    }
    
    /// <summary>
    /// Get microcode configuration
    /// </summary>
    public MicrocodeConfig GetMicrocodeConfig()
    {
        return new MicrocodeConfig
        {
            EnableProm = _config.GetBool("Microcode", "enable_prom", true),
            LoadMicrocode = _config.GetBool("Microcode", "load_microcode", true),
            MicrocodeTrace = _config.GetBool("Microcode", "microcode_trace", false),
            InstructionTrace = _config.GetBool("Microcode", "instruction_trace", false),
            EnableStats = _config.GetBool("Microcode", "enable_stats", true),
            PromFile = _config.GetPath("Paths", "prom_file", "./sys/prom.bin"),
            MicrocodeFile = _config.GetPath("Paths", "microcode_file", "./sys/microcode.bin"),
            DispatchRomFile = _config.GetPath("Paths", "dispatch_rom", "./sys/dispatch.bin")
        };
    }
    
    /// <summary>
    /// Get disk configuration
    /// </summary>
    public DiskConfig GetDiskConfig()
    {
        return new DiskConfig
        {
            DiskImage = _config.GetPath("Disk", "disk_image", "./disk.img"),
            DiskSize = _config.GetHex("Disk", "disk_size", 0x40000000),
            ReadOnly = _config.GetBool("Disk", "read_only", false),
            CacheSize = _config.GetInt("Disk", "cache_size", 4096),
            EnableDma = _config.GetBool("Disk", "enable_dma", true)
        };
    }
    
    /// <summary>
    /// Get keyboard configuration
    /// </summary>
    public KeyboardConfig GetKeyboardConfig()
    {
        return new KeyboardConfig
        {
            KeyboardType = _config.GetString("Keyboard", "keyboard_type", "cadr"),
            RepeatDelay = _config.GetInt("Keyboard", "repeat_delay", 500),
            RepeatRate = _config.GetInt("Keyboard", "repeat_rate", 30),
            MetaIsAlt = _config.GetBool("Keyboard", "meta_is_alt", true),
            SuperIsWindows = _config.GetBool("Keyboard", "super_is_windows", true)
        };
    }
    
    /// <summary>
    /// Get mouse configuration
    /// </summary>
    public MouseConfig GetMouseConfig()
    {
        return new MouseConfig
        {
            EnableMouse = _config.GetBool("Mouse", "enable_mouse", true),
            Sensitivity = _config.GetDouble("Mouse", "mouse_sensitivity", 1.0),
            InvertY = _config.GetBool("Mouse", "invert_y", false),
            ButtonSwap = _config.GetBool("Mouse", "button_swap", false)
        };
    }
    
    /// <summary>
    /// Get performance configuration
    /// </summary>
    public PerformanceConfig GetPerformanceConfig()
    {
        return new PerformanceConfig
        {
            MaxCyclesPerFrame = _config.GetLong("Performance", "max_cycles_per_frame", 1000000),
            EnableSleep = _config.GetBool("Performance", "enable_sleep", true),
            SleepGranularity = _config.GetInt("Performance", "sleep_granularity", 1),
            InstructionLimit = _config.GetLong("Performance", "instruction_limit", 0)
        };
    }
    
    /// <summary>
    /// Get debug configuration
    /// </summary>
    public DebugConfig GetDebugConfig()
    {
        return new DebugConfig
        {
            EnableTraceLog = _config.GetBool("Debug", "enable_trace_log", false),
            TraceCategories = _config.GetStringArray("Debug", "trace_categories"),
            TraceLevel = _config.GetString("Debug", "trace_level", "Info"),
            LogFile = _config.GetPath("Debug", "log_file", "usim.log"),
            DumpStateOnExit = _config.GetBool("Debug", "dump_state_on_exit", false),
            VerboseDump = _config.GetBool("Debug", "verbose_dump", false)
        };
    }
    
    /// <summary>
    /// Validate configuration and show warnings
    /// </summary>
    public bool Validate()
    {
        bool valid = true;
        
        // Check required files
        var microcodeConfig = GetMicrocodeConfig();
        if (microcodeConfig.EnableProm && !File.Exists(microcodeConfig.PromFile))
        {
            Console.WriteLine($"Warning: PROM file not found: {microcodeConfig.PromFile}");
        }
        
        // Check memory size
        var memoryConfig = GetMemoryConfig();
        if (memoryConfig.MainMemorySize < 0x100000) // 1MB minimum
        {
            Console.WriteLine("Warning: Main memory size is very small");
        }
        
        // Check display settings
        var displayConfig = GetDisplayConfig();
        if (displayConfig.Width < 640 || displayConfig.Height < 480)
        {
            Console.WriteLine("Warning: Display resolution is very small");
        }
        
        if (displayConfig.Scale < 0.1 || displayConfig.Scale > 10.0)
        {
            Console.WriteLine("Warning: Display scale is out of reasonable range");
            valid = false;
        }
        
        return valid;
    }
    
    /// <summary>
    /// Save current configuration to file
    /// </summary>
    public bool Save(string filename)
    {
        return _config.Save(filename);
    }
    
    /// <summary>
    /// Dump configuration to console
    /// </summary>
    public void Dump()
    {
        _config.Dump();
    }
    
    /// <summary>
    /// Get underlying ConfigParser for direct access
    /// </summary>
    public ConfigParser Config => _config;
}

#region Configuration Data Classes

public class DisplayConfig
{
    public int Width { get; set; }
    public int Height { get; set; }
    public double Scale { get; set; }
    public bool UseLinearFiltering { get; set; }
    public bool AllowResize { get; set; }
    public bool ColorTv { get; set; }
    public int FrameRate { get; set; }
}

public class MemoryConfig
{
    public uint MainMemorySize { get; set; }
    public bool UsePhysicalMemory { get; set; }
    public bool EnablePaging { get; set; }
    public uint PageSize { get; set; }
}

public class MicrocodeConfig
{
    public bool EnableProm { get; set; }
    public bool LoadMicrocode { get; set; }
    public bool MicrocodeTrace { get; set; }
    public bool InstructionTrace { get; set; }
    public bool EnableStats { get; set; }
    public string PromFile { get; set; } = "";
    public string MicrocodeFile { get; set; } = "";
    public string DispatchRomFile { get; set; } = "";
}

public class DiskConfig
{
    public string DiskImage { get; set; } = "";
    public uint DiskSize { get; set; }
    public bool ReadOnly { get; set; }
    public int CacheSize { get; set; }
    public bool EnableDma { get; set; }
}

public class KeyboardConfig
{
    public string KeyboardType { get; set; } = "";
    public int RepeatDelay { get; set; }
    public int RepeatRate { get; set; }
    public bool MetaIsAlt { get; set; }
    public bool SuperIsWindows { get; set; }
}

public class MouseConfig
{
    public bool EnableMouse { get; set; }
    public double Sensitivity { get; set; }
    public bool InvertY { get; set; }
    public bool ButtonSwap { get; set; }
}

public class PerformanceConfig
{
    public long MaxCyclesPerFrame { get; set; }
    public bool EnableSleep { get; set; }
    public int SleepGranularity { get; set; }
    public long InstructionLimit { get; set; }
}

public class DebugConfig
{
    public bool EnableTraceLog { get; set; }
    public string[] TraceCategories { get; set; } = Array.Empty<string>();
    public string TraceLevel { get; set; } = "";
    public string LogFile { get; set; } = "";
    public bool DumpStateOnExit { get; set; }
    public bool VerboseDump { get; set; }
}

#endregion
