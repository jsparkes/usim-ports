# USIM Tools Usage Guide

## Overview

The USIM Tools package provides utilities for working with MIT CADR simulator disk images, microcode files, and other data files.

## Building

```bash
# Windows
build.bat

# Linux/macOS
./build.sh
```

## Tools

### diskmaker

Create disk images for USIM.

**Usage:**
```bash
dotnet run --project usim-cs-tools -- diskmaker --output disk0.img --size 100
```

**Options:**
- `--output <filename>` - Output disk image filename (required)
- `--size <MB>` - Size in megabytes (default: 1000)

**Example:**
```bash
# Create a 500MB disk image
dotnet run --project usim-cs-tools -- diskmaker --output mydisk.img --size 500
```

### readmcr

Read and display information about MCR (microcode) files.

**Usage:**
```bash
dotnet run --project usim-cs-tools -- readmcr --input prom.mcr [--verbose]
```

**Options:**
- `--input <filename>` - Input MCR file (required)
- `--verbose` - Show detailed information

**Example:**
```bash
dotnet run --project usim-cs-tools -- readmcr --input sys/ubin/prom.mcr
```

### showmcr

Display MCR file contents with disassembly.

**Usage:**
```bash
dotnet run --project usim-cs-tools -- showmcr --input prom.mcr [--verbose] [--disasm]
```

**Options:**
- `--input <filename>` - Input MCR file (required)
- `--verbose` - Show all instructions (default: first 100)
- `--disasm` - Show disassembly (default: true)

**Example:**
```bash
# Show first 100 instructions with disassembly
dotnet run --project usim-cs-tools -- showmcr --input prom.mcr

# Show all instructions
dotnet run --project usim-cs-tools -- showmcr --input prom.mcr --verbose
```

### lod

Load files into disk images.

**Usage:**
```bash
dotnet run --project usim-cs-tools -- lod --disk disk0.img --file data.bin [--address 0x1000]
```

**Options:**
- `--disk <filename>` - Target disk image (required)
- `--file <filename>` - File to load (required)
- `--address <addr>` - Load address in words (default: 0)

**Example:**
```bash
# Load system file at address 0x10000
dotnet run --project usim-cs-tools -- lod --disk disk0.img --file sys.bin --address 0x10000
```

### dump

Dump memory or disk contents in various formats.

**Usage:**
```bash
dotnet run --project usim-cs-tools -- dump --source disk0.img [--start 0x1000] [--length 256] [--format hex]
```

**Options:**
- `--source <filename>` - Source file (required)
- `--start <addr>` - Start address in words (default: 0)
- `--length <count>` - Length in words (default: 256)
- `--format <fmt>` - Output format: hex, binary, text (default: hex)

**Examples:**
```bash
# Dump 256 words starting at address 0x1000 in hex format
dotnet run --project usim-cs-tools -- dump --source disk0.img --start 0x1000 --length 256 --format hex

# Dump as binary
dotnet run --project usim-cs-tools -- dump --source disk0.img --start 0 --length 100 --format binary

# Try to interpret as text
dotnet run --project usim-cs-tools -- dump --source disk0.img --start 0x5000 --length 100 --format text
```

## Common Workflows

### Creating a new disk image

```bash
# Create blank 1GB disk
dotnet run --project usim-cs-tools -- diskmaker --output blank.img --size 1000
```

### Inspecting microcode files

```bash
# Quick view
dotnet run --project usim-cs-tools -- readmcr --input prom.mcr

# Detailed disassembly
dotnet run --project usim-cs-tools -- showmcr --input prom.mcr --verbose
```

### Loading system files

```bash
# Create disk
dotnet run --project usim-cs-tools -- diskmaker --output system.img --size 500

# Load boot file
dotnet run --project usim-cs-tools -- lod --disk system.img --file boot.bin --address 0

# Load system
dotnet run --project usim-cs-tools -- lod --disk system.img --file lisp.bin --address 0x10000
```

### Examining disk contents

```bash
# Dump boot sector
dotnet run --project usim-cs-tools -- dump --source system.img --start 0 --length 128 --format hex

# Look for text data
dotnet run --project usim-cs-tools -- dump --source system.img --start 0x10000 --length 512 --format text
```

## File Formats

### MCR Files

MCR files contain CADR microcode in binary format:
- 16-byte header (magic, version, count)
- 64-bit microcode instructions
- Each instruction is a packed 48-bit word

### Disk Images

Disk images are raw binary files:
- 32-bit words (little-endian)
- Word-addressed (address 0x100 = byte offset 0x400)
- No filesystem structure in raw image

## Notes

- All addresses are in word units (32-bit words)
- Hexadecimal values can be prefixed with `0x` or not
- Disk images are not filesystem-aware; structure depends on OS
- MCR disassembly is simplified; use actual CADR documentation for details
