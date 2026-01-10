# Configuration System Guide

## Overview

The USIM configuration system provides a flexible, INI-style configuration interface for the CADR Lisp Machine emulator. It supports multiple data types, validation, and structured configuration management.

---

## Components

### 1. ConfigParser - Low-Level Parser
Core INI file parser with type-safe data access.

### 2. ConfigManager - High-Level Manager
Structured configuration with validation and typed configuration objects.

### 3. Configuration Files
Human-readable INI files with sections and key-value pairs.

---

## Configuration File Format

```ini
# Comments start with # or ;
# Sections are in [brackets]
# Key-value pairs use = separator

[SectionName]
key = value
number = 42
enabled = true
path = ./data
hex_value = 0xDEADBEEF
array = one, two, three
```

---

## ConfigParser API

### Basic Operations

```csharp
var config = new ConfigParser();

// Load from file
config.Load("config.ini");

// String values
config.SetString("General", "name", "CADR");
string name = config.GetString("General", "name", "default");

// Integer values
config.SetInt("Display", "width", 1024);
int width = config.GetInt("Display", "width", 800);

// Boolean values
config.SetBool("Debug", "enabled", true);
bool enabled = config.GetBool("Debug", "enabled", false);

// Save to file
config.Save("config.ini");
```

### Advanced Types

```csharp
// Double/Float
config.SetDouble("Display", "scale", 1.5);
double scale = config.GetDouble("Display", "scale", 1.0);

// Long integers
long bigNum = config.GetLong("Memory", "size", 0);

// Hexadecimal values
config.SetHex("Memory", "address", 0xDEADBEEF);
uint addr = config.GetHex("Memory", "address", 0);

// Paths (with expansion)
string path = config.GetPath("Paths", "data_dir", "./data");

// Enums
enum LogLevel { Error, Warning, Info, Debug }
config.SetEnum("Debug", "level", LogLevel.Info);
var level = config.GetEnum("Debug", "level", LogLevel.Warning);

// Arrays (comma-separated)
string[] items = new[] { "one", "two", "three" };
config.SetStringArray("Test", "items", items);
string[] result = config.GetStringArray("Test", "items");
```

### Section Operations

```csharp
// Check if key exists
bool exists = config.HasKey("General", "name");

// Get all keys in section
IEnumerable<string> keys = config.GetKeys("General");

// Get all sections
IEnumerable<string> sections = config.GetSections();

// Count sections and keys
int sectionCount = config.SectionCount;
int keyCount = config.GetKeyCount("General");

// Remove key
config.RemoveKey("General", "obsolete");

// Remove section
config.RemoveSection("OldSection");

// Clear all
config.Clear();

// Dump to console (debug)
config.Dump();
```

---

## ConfigManager API

### Loading Configuration

```csharp
var manager = new ConfigManager();

// Load with automatic fallback
manager.Load(); // Tries: usim.ini, config/usim.ini, ~/.usim/config.ini, /etc/usim/config.ini

// Load specific file
manager.Load("myconfig.ini");

// Apply to system
manager.ApplyConfiguration();

// Validate configuration
bool valid = manager.Validate();
```

### Structured Configuration Objects

```csharp
// Display configuration
DisplayConfig display = manager.GetDisplayConfig();
Console.WriteLine($"Resolution: {display.Width}x{display.Height}");
Console.WriteLine($"Scale: {display.Scale}");

// Memory configuration
MemoryConfig memory = manager.GetMemoryConfig();
Console.WriteLine($"Memory Size: 0x{memory.MainMemorySize:X}");

// Microcode configuration
MicrocodeConfig microcode = manager.GetMicrocodeConfig();
if (microcode.EnableProm)
{
    UCode.LoadPromFromFile(microcode.PromFile);
}

// SDL2 configuration
SDL2Config sdl = manager.GetSDL2Config();
if (sdl.Enabled)
{
    // Initialize SDL2
}

// Disk configuration
DiskConfig disk = manager.GetDiskConfig();
Console.WriteLine($"Disk Image: {disk.DiskImage}");

// Keyboard configuration
KeyboardConfig keyboard = manager.GetKeyboardConfig();
Console.WriteLine($"Repeat Rate: {keyboard.RepeatRate}Hz");

// Mouse configuration
MouseConfig mouse = manager.GetMouseConfig();
Console.WriteLine($"Sensitivity: {mouse.Sensitivity}");

// Performance configuration
PerformanceConfig perf = manager.GetPerformanceConfig();
Console.WriteLine($"Max Cycles: {perf.MaxCyclesPerFrame}");

// Debug configuration
DebugConfig debug = manager.GetDebugConfig();
if (debug.EnableTraceLog)
{
    // Setup tracing
}
```

---

## Configuration Sections

### [General]
```ini
[General]
system_version = 300        # System version (78, 98, 99, 300)
verbose = false             # Verbose output
headless = false            # Run without display
auto_boot = false           # Automatically boot on start
auto_poweroff = false       # Shutdown when done
```

### [Paths]
```ini
[Paths]
sys_directory = ./sys       # System files directory
fs_root = ./fs              # Filesystem root
state_file = usim.state     # State save file
prom_file = ./sys/prom.bin  # PROM image
microcode_file = ./sys/microcode.bin  # Microcode image
dispatch_rom = ./sys/dispatch.bin     # Dispatch ROM
```

### [Memory]
```ini
[Memory]
main_memory_size = 0x2000000  # 32MB
use_physical_memory = false    # Use physical memory mapping
enable_paging = true           # Enable virtual memory
page_size = 0x400              # Page size (1024 words)
```

### [Microcode]
```ini
[Microcode]
enable_prom = true           # Enable PROM
load_microcode = true        # Load microcode from file
microcode_trace = false      # Trace microcode execution
instruction_trace = false    # Trace each instruction
enable_stats = true          # Performance statistics
```

### [Display]
```ini
[Display]
width = 1024                 # Display width
height = 808                 # Display height
scale = 1.0                  # Window scale factor
use_linear_filtering = true  # Linear vs nearest filtering
allow_resize = false         # Allow window resize
color_tv = true              # Color vs monochrome
frame_rate = 60              # Target frame rate
```

### [SDL2]
```ini
[SDL2]
enable_sdl2 = true           # Enable SDL2 backend
video_driver = auto          # Video driver (auto, windows, x11, wayland)
renderer = auto              # Renderer (auto, software, opengl, etc.)
vsync = true                 # Vertical sync
fullscreen = false           # Fullscreen mode
```

### [Keyboard]
```ini
[Keyboard]
keyboard_type = cadr         # cadr or knight
repeat_delay = 500           # Initial repeat delay (ms)
repeat_rate = 30             # Repeat rate (Hz)
meta_is_alt = true           # Map Meta to Alt
super_is_windows = true      # Map Super to Windows key
```

### [Mouse]
```ini
[Mouse]
enable_mouse = true          # Enable mouse
mouse_sensitivity = 1.0      # Sensitivity multiplier
invert_y = false             # Invert Y axis
button_swap = false          # Swap left/right buttons
```

### [Disk]
```ini
[Disk]
disk_image = ./disk.img      # Disk image file
disk_size = 0x40000000       # 1GB
read_only = false            # Read-only mode
cache_size = 4096            # Cache size (blocks)
enable_dma = true            # Enable DMA
```

### [Performance]
```ini
[Performance]
max_cycles_per_frame = 1000000  # Maximum cycles per frame
enable_sleep = true             # Sleep between frames
sleep_granularity = 1           # Sleep time (ms)
instruction_limit = 0           # Instruction limit (0=unlimited)
```

### [Debug]
```ini
[Debug]
enable_trace_log = false     # Enable trace logging
trace_categories = Display,Keyboard,Mouse  # Categories to trace
trace_level = Info           # Error, Warning, Info, Debug, Verbose
log_file = usim.log          # Log file path
dump_state_on_exit = false   # Dump state when exiting
verbose_dump = false         # Verbose state dump
```

---

## Usage Examples

### Example 1: Basic Configuration

```csharp
using Usim;

class Program
{
    static void Main()
    {
        var config = new ConfigParser();
        
        // Create default configuration
        config.SetString("General", "name", "USIM");
        config.SetInt("Display", "width", 1024);
        config.SetInt("Display", "height", 808);
        config.SetBool("Debug", "enabled", false);
        
        // Save
        config.Save("config.ini");
        
        // Load later
        config.Load("config.ini");
        
        int width = config.GetInt("Display", "width");
        Console.WriteLine($"Width: {width}");
    }
}
```

### Example 2: Using ConfigManager

```csharp
using Usim;

class Program
{
    static void Main()
    {
        var manager = new ConfigManager();
        
        // Load configuration
        if (manager.Load())
        {
            // Apply to system
            manager.ApplyConfiguration();
            
            // Validate
            if (!manager.Validate())
            {
                Console.WriteLine("Configuration has warnings");
            }
            
            // Get specific configs
            var display = manager.GetDisplayConfig();
            var memory = manager.GetMemoryConfig();
            
            // Initialize system with config
            InitializeSystem(display, memory);
        }
    }
    
    static void InitializeSystem(DisplayConfig display, MemoryConfig memory)
    {
        Console.WriteLine($"Initializing {display.Width}x{display.Height} display");
        Console.WriteLine($"Memory: {memory.MainMemorySize:X} bytes");
    }
}
```

### Example 3: Dynamic Configuration

```csharp
var manager = new ConfigManager();
manager.Load();

// Get configuration object
DisplayConfig display = manager.GetDisplayConfig();

// Modify and apply
display.Scale = 2.0;
display.AllowResize = true;

// Save back
manager.Config.SetDouble("Display", "scale", display.Scale);
manager.Config.SetBool("Display", "allow_resize", display.AllowResize);
manager.Save("config.ini");
```

---

## Testing

Run configuration tests:

```csharp
ConfigTests.RunAllTests();
```

Tests include:
- Basic read/write operations
- All data types (string, int, bool, double, hex, enum, arrays)
- Path expansion
- Section operations
- ConfigManager integration
- Validation

---

## Best Practices

1. **Use ConfigManager** for structured configuration
2. **Validate** configuration after loading
3. **Provide defaults** for all values
4. **Document** configuration options in comments
5. **Use typed access** methods (GetInt, GetBool, etc.)
6. **Handle missing files** gracefully
7. **Test configuration** changes

---

## Error Handling

```csharp
try
{
    var manager = new ConfigManager();
    
    if (!manager.Load("config.ini"))
    {
        Console.WriteLine("Using default configuration");
    }
    
    if (!manager.Validate())
    {
        Console.WriteLine("Configuration has issues (continuing anyway)");
    }
    
    manager.ApplyConfiguration();
}
catch (Exception ex)
{
    Console.WriteLine($"Configuration error: {ex.Message}");
    // Use safe defaults
}
```

---

## Configuration Inheritance

For advanced setups, support multiple configuration files:

```csharp
var config = new ConfigParser();

// Load base configuration
config.Load("default.ini");

// Override with user configuration
if (File.Exists("user.ini"))
{
    var userConfig = new ConfigParser();
    userConfig.Load("user.ini");
    
    // Merge user settings (user config takes precedence)
    foreach (var section in userConfig.GetSections())
    {
        foreach (var key in userConfig.GetKeys(section))
        {
            var value = userConfig.GetString(section, key);
            config.SetString(section, key, value);
        }
    }
}
```

---

## Reference

### ConfigParser Methods
- `Load(filename)` - Load from file
- `Save(filename)` - Save to file
- `GetString/SetString` - String values
- `GetInt/SetInt` - Integer values
- `GetBool/SetBool` - Boolean values
- `GetDouble/SetDouble` - Floating point values
- `GetLong` - 64-bit integers
- `GetHex/SetHex` - Hexadecimal values
- `GetPath` - Path with expansion
- `GetEnum/SetEnum` - Enum values
- `GetStringArray/SetStringArray` - Arrays
- `HasKey` - Check key existence
- `GetKeys` - List keys in section
- `GetSections` - List all sections
- `RemoveKey` - Remove key
- `RemoveSection` - Remove section
- `Clear` - Clear all configuration
- `Dump` - Debug output

### ConfigManager Methods
- `Load(filename?)` - Load with fallback
- `ApplyConfiguration()` - Apply to system
- `Validate()` - Validate configuration
- `GetDisplayConfig()` - Display settings
- `GetMemoryConfig()` - Memory settings
- `GetMicrocodeConfig()` - Microcode settings
- `GetSDL2Config()` - SDL2 settings
- `GetDiskConfig()` - Disk settings
- `GetKeyboardConfig()` - Keyboard settings
- `GetMouseConfig()` - Mouse settings
- `GetPerformanceConfig()` - Performance settings
- `GetDebugConfig()` - Debug settings
- `Save(filename)` - Save configuration
- `Dump()` - Debug output

---

**Configuration System Status:** ? Complete and Tested

*For more information, see the USIM documentation.*
