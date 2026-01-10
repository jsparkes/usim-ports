# USIM and Chaos C# Conversion

This is a C# conversion of the MIT CADR Lisp Machine simulator (USIM) and Chaosnet implementation.

## Original Projects

- **USIM**: MIT CADR simulator by Brad Parker <brad@heeltoe.com>
  - Original source: https://tumbleweed.nu/r/usim/
  - Emulates the MIT CADR microprocessor and hardware peripherals

- **Chaos**: Chaosnet implementation for Unix
  - Pre-ethernet computer network developed at MIT
  - Used to connect PDP-10s running ITS and Lisp Machines

## Project Structure

```
LispMachine.sln          - Visual Studio solution file
chaos-cs/                - Chaos network library (C#)
  Chaos.csproj          - Chaos project file
  Chaos.cs              - Main constants and definitions
  ChAlloc.cs            - Memory allocation utilities
usim-cs/                - USIM simulator (C#)
  Usim.csproj           - USIM project file
  Program.cs            - Main entry point
  UsimConstants.cs      - System constants
  UCode.cs              - Microcode execution engine
  MiscUtils.cs          - Utility functions
  SymbolTable.cs        - Symbol table management
  Disassembler.cs       - Microcode disassembler
usim-cs-tools/          - Utility tools
  UsimTools.csproj      - Tools project file
  Program.cs            - Command-line interface
  DiskMaker.cs          - Disk image creation
  McrReader.cs          - Microcode file reader
  Loader.cs             - File loader
  Dumper.cs             - Memory/disk dumper
chaos/                  - Original C chaos source
usim/                   - Original C usim source
```

## Features

The C# port includes:

- **Core Emulation**: Microcode execution engine, memory management with virtual memory paging, machine state management
- **Devices**: Keyboard (CADR keycodes with modifiers), Mouse (3-button with hardware registers), Display (768x896, B&W and color modes), Disk controller (8 units with CHS addressing), I/O bus with device registration
- **Chaos Network**: Complete Chaosnet protocol implementation with connection management, packet handling, FILE server for remote file access
- **Interactive Debugging**: Command-line debugger with step, run, breakpoint, examine/deposit memory, disassembly, trace control, performance monitoring
- **Configuration**: INI-style configuration files for system settings and tracing
- **Tools**: Disk image creator (diskmaker), Microcode reader (readmcr/showmcr), File loader (lod), Memory/disk dumper (dump)
- **Build System**: Cross-platform build scripts (Windows batch, Unix shell)

### Current Status

✅ Complete:
- Project structure and build system
- Memory management with paging
- Disk controller with 8-unit support
- Chaos network protocol and FILE server
- Keyboard, mouse, and display emulation
- I/O bus architecture
- Interactive debugging system with 15+ commands
- Utility tools package (5 tools)
- Configuration system with INI files
- Tracing and performance monitoring (8 categories, 5 levels)
- Comprehensive documentation

⏳ In Progress:
- Full microcode interpreter implementation
- SDL integration for actual rendering
- Complete instruction set implementation
- Chaos network transmission layer

See [CONVERSION-STATUS.md](CONVERSION-STATUS.md) for detailed implementation status.

## Building

### Prerequisites

- .NET 8.0 SDK or later
- Windows, Linux, or macOS

### Build Instructions

```bash
# Restore dependencies
dotnet restore

# Build all projects
dotnet build

# Build in Release mode
dotnet build -c Release

# Run USIM
dotnet run --project usim-cs

# Run with options
dotnet run --project usim-cs -- --help
dotnet run --project usim-cs -- --config usim.ini --headless
```

### Using Visual Studio

1. Open `LispMachine.sln` in Visual Studio 2022 or later
2. Build the solution (Ctrl+Shift+B)
3. Run the Usim project (F5)

### Using Visual Studio Code

1. Open the workspace folder in VS Code
2. Install the C# extension
3. Press F5 to build and run

## Conversion Notes

### Major Changes from C to C#

1. **Memory Management**
   - Replaced manual malloc/free with managed memory
   - Used `Marshal` class for unmanaged memory when needed
   - Implemented safe wrappers for pointer operations

2. **Data Structures**
   - Converted C structs to C# structs and classes
   - Used properties instead of fields where appropriate
   - Implemented enums for constants

3. **Platform-Specific Code**
   - Removed Unix-specific system calls
   - Used .NET BCL equivalents where possible
   - Platform abstraction through .NET APIs

4. **Unsafe Code**
   - Enabled unsafe code blocks for low-level operations
   - Used sparingly and only where necessary
   - Wrapped in safe APIs

5. **SDL Integration**
   - Using SDL2-CS wrapper for SDL functionality
   - Display, keyboard, and mouse support
   - Audio support through SDL

### Limitations of Current Conversion

This is a partial conversion demonstrating the framework. A complete conversion would require:

1. **Complete Device Emulation**
   - Disk controller and units
   - Tape controller and drives
   - Display system (TV, Color TV)
   - Keyboard and mouse
   - Network (Chaos)
   - IOB (I/O Board)

2. **Full Microcode Execution**
   - Complete microcode interpreter
   - All instruction implementations
   - Proper pipeline simulation
   - Memory management unit

3. **Chaos Network Stack**
   - Complete NCP implementation
   - All protocol handlers
   - Device drivers
   - File server
   - Other services (MAIL, SEND, etc.)

4. **Utilities**
   - diskmaker - Create disk images
   - readmcr - Read MCR files
   - showmcr - Display MCR contents
   - lod - Load files
   - di - Diagnostic interface

## Command Line Options

```
Usage: usim [options]

Options:
  -h, --help              Show this help message
  -c, --config <file>     Configuration file
  -s, --state <file>      State file
  -d, --dump              Dump state on exit
  --headless              Run without display
  --auto-boot             Automatically boot
  --colortv               Enable color TV
  -v, --verbose           Verbose output
```

## Interactive Commands

When running USIM, you can use these debug commands:

### Basic Commands
- `help, ?` - Show available commands
- `status, st` - Display machine status
- `reset` - Reset the machine
- `halt` - Halt execution
- `quit, q, exit` - Power off and exit

### Execution Control
- `step [n]` - Step N instructions (default: 1)
- `run [addr]` - Run from address (or current PC)
- `break <addr>` - Set breakpoint at address

### Memory Access
- `examine <addr>` - Examine memory at address (hex)
- `deposit <addr> <value>` - Write value to memory (hex)
- `disasm <addr> [n]` - Disassemble N instructions

### Debugging
- `trace <category> [on|off]` - Enable/disable trace category
  - Categories: `microcode`, `memory`, `disk`, `display`, `keyboard`, `mouse`, `network`, `iobus`, `all`, `none`
- `perf` - Show performance statistics

### State Management
- `save <file>` - Save machine state
- `load <file>` - Load machine state

Example session:
```
usim> status
Machine Status: Running
usim> examine 1000
0x00001000: 0x00000000 (0)
usim> deposit 1000 DEADBEEF
0x00001000 <- 0xDEADBEEF
usim> trace memory on
Trace memory enabled
usim> step 5
Stepping 5 instruction(s)...
PC: 0x0000
usim> perf
=== Performance Statistics ===
step                           Count:          5  Avg:    0.123ms
usim> quit
```

## Tools

The `usim-cs-tools` project provides utilities for working with disk images and microcode files:

- **diskmaker**: Create blank disk images
- **readmcr**: Read MCR (microcode) file headers
- **showmcr**: Display MCR files with disassembly
- **lod**: Load files into disk images
- **dump**: Dump memory/disk contents in hex, binary, or text format

See [TOOLS-USAGE.md](TOOLS-USAGE.md) for detailed usage information.

Example:
```bash
# Create a 500MB disk image
dotnet run --project usim-cs-tools -- diskmaker --output disk0.img --size 500

# Inspect microcode file
dotnet run --project usim-cs-tools -- showmcr --input prom.mcr

# Load file into disk
dotnet run --project usim-cs-tools -- lod --disk disk0.img --file boot.bin --address 0

# Dump disk contents
dotnet run --project usim-cs-tools -- dump --source disk0.img --start 0 --length 256 --format hex
```

## Configuration

Edit `usim.ini` to configure the simulator:

```ini
[memory]
size = 8388608
paging = true

[disk]
unit0 = disk0.img
unit1 = disk1.img

[trace]
categories = None
level = Info
```

Trace categories can be combined with commas or use `All` to enable everything:
```ini
[trace]
categories = Memory,Disk,Display
level = Debug
```

Available trace levels: `Error`, `Warning`, `Info`, `Debug`, `Verbose`

## Chaos Network

The Chaos network implementation includes:

- **Protocol**: Full connection lifecycle (CSRFCSENT, CSOPEN, CSRFCRCVD, CSCLOSEWAIT, CSCLOSED)
- **Packet Types**: RFC, OPN, CLS, FWD, ANS, SNS, STS, RUT, LSN, BRD, LOS, UNC
- **Connection Management**: Window-based flow control, retransmission, packet queuing
- **Services**: FILE server for remote file access

### FILE Server

The Chaos FILE server provides basic file operations:

```csharp
var network = new ChaosNetwork();
var fileServer = new ChaosFileServer(network, "./fs");
```

Supported operations:
- `OPEN <filename> [WRITE]` - Open file for reading or writing
- `CLOSE` - Close current file
- `READ [count]` - Read bytes from file
- `WRITE` - Write data to file
- `DELETE <filename>` - Delete file
- `DIRECTORY [pattern]` - List files matching pattern

## Performance Monitoring

The performance counter tracks operation counts and timing:

```
usim> perf
=== Performance Statistics ===
Memory.Read                    Count:     15,234  Avg:    0.001ms
Memory.Write                   Count:      8,192  Avg:    0.001ms
Disk.Read                      Count:         45  Avg:    2.340ms
Disk.Write                     Count:         12  Avg:    2.567ms
```

## Architecture

### CADR Architecture

The CADR is a microcoded processor with:

- 16K x 48-bit microcode memory (IMem)
- 512 x 48-bit PROM
- 1K A-memory (general purpose registers)
- 32-word M-memory (micro-memory)
- 2K D-memory (dispatch memory)
- 1K PDL (push-down list/stack)
- Virtual memory with paging

### Execution Pipeline

1. Fetch microinstruction from IMem/PROM
2. Decode and execute in pipeline stages
3. Memory operations (A, M, D, PDL)
4. ALU operations
5. Update registers and PC

### Chaos Network

- 16-bit addresses (subnet + host)
- Packet-based protocol
- Multiple opcodes (RFC, OPEN, CLOSE, DATA, etc.)
- Connection-oriented and connectionless modes

## Development

### Adding New Features

1. Implement device in separate class
2. Add initialization to `Program.Initialize()`
3. Add main loop processing to `Program.Run()`
4. Wire up I/O to microcode execution

### Testing

```bash
# Run tests (when implemented)
dotnet test

# Run specific test
dotnet test --filter "TestCategory=MicroCode"
```

### Debugging

Use Visual Studio debugger or:

```bash
# Run with debugging
dotnet run --project usim-cs --configuration Debug

# Verbose output
dotnet run --project usim-cs -- --verbose
```

## License

The original USIM and Chaos code were written by Brad Parker and contributors.
This C# conversion maintains compatibility with the original license terms.

## Contributing

1. Follow C# coding conventions
2. Maintain compatibility with original behavior
3. Add unit tests for new functionality
4. Document significant changes

## References

- [CADR Documentation](https://tumbleweed.nu/r/lm-3/uv/cadr.html)
- [Chaosnet Documentation](http://chaosnet.net/)
- [MIT AI Lab Memos](https://dspace.mit.edu/handle/1721.1/5688)
- [Lisp Machine Manual](https://hanshuebner.github.io/lmman/)

## Status

### Completed
- ✅ Project structure and build system
- ✅ Core constants and type definitions
- ✅ Basic utility functions
- ✅ Microcode data structures
- ✅ Symbol table management
- ✅ Memory allocation framework
- ✅ Command line parsing
- ✅ Configuration file support

### In Progress
- 🔄 Microcode execution engine
- 🔄 Device emulation
- 🔄 Display system

### TODO
- ⬜ Complete instruction set implementation
- ⬜ Disk I/O
- ⬜ Network (Chaos) integration
- ⬜ Keyboard/mouse input
- ⬜ Tape support
- ⬜ All utility tools
- ⬜ Complete Chaos network stack
- ⬜ Full testing suite

## Support

For questions about the original C implementation, see the original USIM repository.

For questions about this C# conversion, open an issue on the project repository.
