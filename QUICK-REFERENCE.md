# USIM Quick Reference Guide

## Quick Start

```bash
# Build
dotnet build

# Run simulator
dotnet run --project usim-cs

# Run with configuration
dotnet run --project usim-cs -- --config usim.ini

# Get help
dotnet run --project usim-cs -- --help
```

## Interactive Commands

| Command | Shortcut | Description | Example |
|---------|----------|-------------|---------|
| `help` | `?` | Show commands | `help` |
| `status` | `st` | Machine status | `status` |
| `reset` | - | Reset machine | `reset` |
| `halt` | - | Halt execution | `halt` |
| `step [n]` | `s` | Step N instructions | `step 10` |
| `run [addr]` | `r` | Run from address | `run 1000` |
| `break <addr>` | `b` | Set breakpoint | `break 2000` |
| `examine <addr>` | `x` | Read memory | `examine 1000` |
| `deposit <addr> <val>` | `d` | Write memory | `deposit 1000 DEADBEEF` |
| `disasm <addr> [n]` | `dis` | Disassemble code | `disasm 1000 20` |
| `trace <cat> [on\|off]` | - | Control tracing | `trace memory on` |
| `perf` | - | Show statistics | `perf` |
| `save <file>` | - | Save state | `save state.bin` |
| `load <file>` | - | Load state | `load state.bin` |
| `quit` | `q` | Exit | `quit` |

## Trace Categories

| Category | Description |
|----------|-------------|
| `MicroCode` | Microcode execution |
| `Memory` | Memory read/write |
| `Disk` | Disk operations |
| `Display` | Display updates |
| `Keyboard` | Keyboard input |
| `Mouse` | Mouse input |
| `Network` | Network packets |
| `IOBus` | I/O bus operations |
| `All` | Enable all categories |
| `None` | Disable all categories |

**Usage:**
```
usim> trace memory on       # Enable memory tracing
usim> trace disk on         # Enable disk tracing
usim> trace all off         # Disable all tracing
```

## Trace Levels

| Level | Description |
|-------|-------------|
| `Error` | Only errors |
| `Warning` | Warnings and above |
| `Info` | Informational and above (default) |
| `Debug` | Debug information |
| `Verbose` | Detailed trace output |

**Configuration:**
```ini
[trace]
categories = Memory,Disk
level = Debug
```

## Tools Reference

### diskmaker - Create Disk Images

```bash
# Syntax
dotnet run --project usim-cs-tools -- diskmaker --output <file> --size <MB>

# Examples
dotnet run --project usim-cs-tools -- diskmaker --output disk0.img --size 100
dotnet run --project usim-cs-tools -- diskmaker --output system.img --size 1000
```

### readmcr - Read Microcode Files

```bash
# Syntax
dotnet run --project usim-cs-tools -- readmcr --input <file> [--verbose]

# Examples
dotnet run --project usim-cs-tools -- readmcr --input prom.mcr
dotnet run --project usim-cs-tools -- readmcr --input sys/ubin/prom.mcr --verbose
```

### showmcr - Display Microcode

```bash
# Syntax
dotnet run --project usim-cs-tools -- showmcr --input <file> [--verbose] [--disasm]

# Examples
dotnet run --project usim-cs-tools -- showmcr --input prom.mcr
dotnet run --project usim-cs-tools -- showmcr --input prom.mcr --verbose
dotnet run --project usim-cs-tools -- showmcr --input prom.mcr --disasm false
```

### lod - Load Files

```bash
# Syntax
dotnet run --project usim-cs-tools -- lod --disk <disk> --file <file> [--address <addr>]

# Examples
dotnet run --project usim-cs-tools -- lod --disk disk0.img --file boot.bin
dotnet run --project usim-cs-tools -- lod --disk disk0.img --file system.bin --address 0x10000
```

### dump - Dump Contents

```bash
# Syntax
dotnet run --project usim-cs-tools -- dump --source <file> [--start <addr>] [--length <words>] [--format <fmt>]

# Examples (hex format)
dotnet run --project usim-cs-tools -- dump --source disk0.img --start 0 --length 256 --format hex

# Examples (binary format)
dotnet run --project usim-cs-tools -- dump --source disk0.img --start 0x1000 --length 100 --format binary

# Examples (text format)
dotnet run --project usim-cs-tools -- dump --source disk0.img --start 0x5000 --length 512 --format text
```

## Configuration File (usim.ini)

```ini
[memory]
size = 8388608        # 8MB memory
paging = true         # Enable virtual memory

[disk]
unit0 = disk0.img     # Disk unit 0
unit1 = disk1.img     # Disk unit 1
unit2 = disk2.img     # Disk unit 2 (optional)

[trace]
categories = None     # Trace categories (comma-separated or All/None)
level = Info          # Trace level (Error/Warning/Info/Debug/Verbose)
```

## Memory Addressing

- **Word Size**: 32 bits (4 bytes)
- **Virtual Addresses**: Up to 32-bit
- **Physical Memory**: Default 8MB (2M words)
- **Page Size**: 256 words (1KB)
- **Address Format**: Hexadecimal (e.g., `0x1000`)

**Examples:**
```
examine 0           # Word 0
examine 100         # Word 0x100 (256 decimal)
examine 1000        # Word 0x1000 (4096 decimal)
```

## Disk Addressing

- **Units**: 0-7 (8 units supported)
- **Addressing**: Cylinder-Head-Sector (CHS)
- **Sector Size**: Variable (typically 256 words)

## Chaos Network

### FILE Server Operations

| Operation | Parameters | Description |
|-----------|------------|-------------|
| `OPEN` | `filename [WRITE]` | Open file for reading/writing |
| `CLOSE` | - | Close current file |
| `READ` | `[count]` | Read bytes from file |
| `WRITE` | - | Write data to file |
| `DELETE` | `filename` | Delete file |
| `DIRECTORY` | `[pattern]` | List files |

### Contact Names

| Service | Contact Name | Description |
|---------|--------------|-------------|
| File server | `FILE` | Remote file access |
| Time server | `TIME` | Time protocol |
| Name server | `NAME` | Name lookup |
| Mail server | `MAIL` | Mail delivery |

## Common Workflows

### Setting Up a New System

```bash
# 1. Create disk images
dotnet run --project usim-cs-tools -- diskmaker --output disk0.img --size 500
dotnet run --project usim-cs-tools -- diskmaker --output disk1.img --size 500

# 2. Configure system
# Edit usim.ini to reference disk0.img and disk1.img

# 3. Run simulator
dotnet run --project usim-cs -- --config usim.ini
```

### Debugging Memory Issues

```bash
# Start with memory tracing
usim> trace memory on
usim> trace level verbose

# Examine suspicious memory
usim> examine 1000
usim> examine 1001
usim> examine 1002

# Step through code
usim> step 10

# Check statistics
usim> perf
```

### Examining Disk Contents

```bash
# Outside simulator - dump boot sector
dotnet run --project usim-cs-tools -- dump --source disk0.img --start 0 --length 128

# Inside simulator - examine disk buffer
usim> examine 10000
usim> trace disk on
usim> # trigger disk read
```

### Performance Profiling

```bash
# Start simulator with verbose output
dotnet run --project usim-cs -- --verbose

# Inside simulator
usim> trace all on
usim> trace level info
usim> # run workload
usim> perf
usim> trace all off
```

## Keyboard Shortcuts

*(Not yet implemented - placeholder for future GUI)*

| Key | Function |
|-----|----------|
| F1 | Help |
| F5 | Run |
| F10 | Step |
| F11 | Step Into |
| Shift+F5 | Stop |
| Ctrl+R | Reset |

## Troubleshooting

### Build Errors

**Problem:** `The type or namespace name 'SDL2' could not be found`
**Solution:** Run `dotnet restore` to download packages

**Problem:** `error CS0227: Unsafe code may only appear if compiling with /unsafe`
**Solution:** Ensure `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in .csproj file

### Runtime Errors

**Problem:** `File not found: disk0.img`
**Solution:** Create disk image with diskmaker tool or update usim.ini path

**Problem:** `Configuration file not found`
**Solution:** Specify config file with `--config usim.ini` or ensure usim.ini exists

**Problem:** `Out of memory`
**Solution:** Reduce memory size in configuration or increase system memory

## Performance Tips

1. **Disable Tracing**: Turn off all tracing for maximum performance
   ```
   usim> trace all off
   ```

2. **Reduce Memory**: Use smaller memory size if not needed
   ```ini
   [memory]
   size = 4194304    # 4MB instead of 8MB
   ```

3. **Headless Mode**: Run without display for batch processing
   ```bash
   dotnet run --project usim-cs -- --headless
   ```

## File Formats

### MCR Files (Microcode)
- 16-byte header (magic, version, count)
- 64-bit microcode instructions (48-bit packed)
- Binary format

### Disk Images
- Raw binary format
- 32-bit words (little-endian)
- Word-addressed (multiply byte offset by 4)
- No filesystem structure

### State Files
- Custom binary format
- Contains all machine state
- Memory contents, registers, device state

## Additional Resources

- Main documentation: [README-CSharp.md](README-CSharp.md)
- Tools guide: [TOOLS-USAGE.md](TOOLS-USAGE.md)
- Implementation status: [CONVERSION-STATUS.md](CONVERSION-STATUS.md)
- Original USIM: https://tumbleweed.nu/r/usim/
