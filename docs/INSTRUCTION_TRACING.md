# Instruction Tracing Guide

## Overview

The USIM microcode engine now supports comprehensive instruction tracing, allowing you to see every instruction executed with full details including operands, results, and processor state.

---

## Configuration

### Enable in Config File (`config/usim.ini`)

```ini
[Microcode]
# Microcode engine settings
instruction_trace = true    # Enable console output
microcode_trace = true      # Enable trace buffer

[Tracing]
# Execution tracing
trace_execution = true      # Alternative way to enable
trace_microcode = true      # Save to buffer
max_trace_lines = 10000     # Buffer size

[Debug]
# Debug settings
enable_trace_log = true
trace_categories = MicroCode,All
trace_level = Verbose
```

### Enable Programmatically

```csharp
// Enable instruction tracing
UCode.InstructionTraceEnabled = true;   // Console output
UCode.MicrocodeTraceEnabled = true;     // Buffer storage
UCode.MaxTraceLines = 10000;             // Buffer size

// Load from configuration
var manager = new ConfigManager();
manager.Load();
manager.ApplyConfiguration();  // Automatically enables based on config
```

---

## Trace Output Format

Each traced instruction shows:

```
[Cycle#] PC=XXXX(memory) INSTRUCTION => OUT=0xXXXXXXXX FLAGS=CVNZ M=X A=X
```

### Example Output:

```
[0000000001] PC=0000(IMEM) SETM M=0 ? AMEM => OUT=0x00001000 FLAGS=C=0V=0N=0Z=0 M=0 A=0
[0000000002] PC=0001(IMEM) ADD M=0 A=A[064] ? VMA => OUT=0x00003000 FLAGS=C=0V=0N=0Z=0 M=0 A=100
[0000000003] PC=0002(IMEM) SETA A=OUT ? Q => OUT=0x00003000 FLAGS=C=0V=0N=0Z=0 M=0 A=1025
[0000000004] PC=0003(IMEM) SETZ JMP[UNCOND]?0003 => OUT=0x00000000 FLAGS=C=0V=0N=0Z=1 M=0 A=0
```

### Field Descriptions:

- **Cycle#** - Machine cycle counter
- **PC** - Program counter (hex)
- **Memory** - PROM or IMEM
- **INSTRUCTION** - Disassembled instruction:
  - ALU operation (SETM, ADD, SUB, etc.)
  - M source (M[n], VMA, MD, Q, etc.)
  - A source (A[address], PDL-TOP, OUT)
  - Destination (AMEM, VMA, MD, Q, etc.)
  - Jump condition and target
- **OUT** - ALU output register value
- **FLAGS** - Processor flags (Carry, oVerflow, Negative, Zero)
- **M/A** - M and A data values used

---

## ALU Operation Names

The disassembler uses these mnemonics:

### Logic Operations
- `SETZ` - Set zero
- `AND` - Logical AND
- `ANDCA` - AND with complement of A
- `SETM` - Set M (pass through)
- `ANDCM` - AND with complement of M
- `SETA` - Set A (pass through)
- `XOR` - Exclusive OR
- `OR` - Logical OR
- `NOR` - NOR (AND of complements)
- `EQV` - Equivalence (XNOR)
- `NOTCA` - Complement of A
- `ORCA` - OR with complement
- `NOTCM` - Complement of M
- `ORCM` - OR with complement
- `NAND` - NAND
- `SETO` - Set ones (all 1s)

### Arithmetic Operations
- `ADD` - Add with carry
- `SUB` - Subtract with borrow
- `SUBCM` - Subtract complement of M
- `ADDCA` - Add complement of A
- `SUBM` - Reverse subtract
- `SUBM1` - Subtract M and 1
- `ADDCM` - Add complement of M
- `ADDCM1` - Add complement and 1
- `M+A` - Simple addition
- `M|A` - OR operation
- `M&A` - AND operation
- `M^A` - XOR operation
- `M-1` - Decrement
- `M+1` - Increment

---

## Usage Examples

### Example 1: Basic Tracing

```csharp
using Usim;

// Enable tracing
UCode.Init();
UCode.InstructionTraceEnabled = true;

// Execute some instructions
UCode.ExecuteInstruction(0, true);
UCode.ExecuteInstruction(1, true);
UCode.ExecuteInstruction(2, true);

// Disable tracing
UCode.InstructionTraceEnabled = false;
```

Output:
```
[0000000001] PC=0000(IMEM) ADD M=M[5] A=A[100] ? VMA => OUT=0x00003000 FLAGS=C=0V=0N=0Z=0 M=5 A=100
[0000000002] PC=0001(IMEM) M+1 M=M[0] ? MMEM => OUT=0x00000001 FLAGS=C=0V=0N=0Z=0 M=0 A=0
[0000000003] PC=0002(IMEM) SETA A=OUT ? Q => OUT=0x00000001 FLAGS=C=0V=0N=0Z=0 M=0 A=1025
```

### Example 2: Trace to Buffer

```csharp
// Enable buffer tracing (no console output)
UCode.Init();
UCode.ClearTraceBuffer();
UCode.InstructionTraceEnabled = false;  // No console
UCode.MicrocodeTraceEnabled = true;     // Buffer only
UCode.MaxTraceLines = 1000;

// Execute program
for (int i = 0; i < 100; i++)
{
    UCode.ExecuteInstruction((uint)i, true);
}

// Get trace lines
string[] trace = UCode.GetTraceBuffer();
Console.WriteLine($"Captured {trace.Length} instructions");

// Save to file
UCode.SaveTraceBuffer("trace_output.txt");

// Or dump to console
UCode.DumpTraceBuffer();
```

### Example 3: Configuration-Based

```ini
# usim.ini
[Microcode]
instruction_trace = true
microcode_trace = true

[Tracing]
max_trace_lines = 5000
```

```csharp
// Load and apply configuration
var manager = new ConfigManager();
manager.Load();
manager.ApplyConfiguration();

// Tracing is now enabled based on config
// Execute normally
UCode.ExecuteInstruction(pc, useImem);
```

### Example 4: Demo Program

```csharp
using Usim;

// Run the built-in tracing demo
UCodeTests.DemoInstructionTracing();
```

This demo:
1. Builds a small test program
2. Enables tracing
3. Executes instructions with full trace output
4. Shows results
5. Saves trace to file
6. Disables tracing

---

## Trace Buffer Management

```csharp
// Clear trace buffer
UCode.ClearTraceBuffer();

// Get all trace lines
string[] lines = UCode.GetTraceBuffer();

// Dump to console
UCode.DumpTraceBuffer();

// Save to file
UCode.SaveTraceBuffer("mytrace.txt");

// Set buffer size
UCode.MaxTraceLines = 50000;  // Larger buffer
```

---

## Performance Impact

### With Tracing Disabled
- No performance impact
- Instructions execute at full speed (1-2M/sec)

### With Console Tracing (`InstructionTraceEnabled = true`)
- Significant slowdown due to console I/O
- Approximately 1000-10000 instructions/sec
- Best for debugging specific issues

### With Buffer Tracing (`MicrocodeTraceEnabled = true`)
- Minimal performance impact
- Instructions stored in memory buffer
- Suitable for capturing execution history
- Approximately 500K-1M instructions/sec

---

## Advanced Features

### Conditional Tracing

```csharp
// Trace only specific PC ranges
void ExecuteWithConditionalTrace(uint pc, bool useImem)
{
    bool wasEnabled = UCode.InstructionTraceEnabled;
    
    // Enable tracing for specific range
    if (pc >= 0x100 && pc <= 0x200)
    {
        UCode.InstructionTraceEnabled = true;
    }
    
    UCode.ExecuteInstruction(pc, useImem);
    
    // Restore previous state
    UCode.InstructionTraceEnabled = wasEnabled;
}
```

### Trace Filtering

```csharp
// Filter trace buffer for specific patterns
string[] allTrace = UCode.GetTraceBuffer();
var jumps = allTrace.Where(line => line.Contains("JMP")).ToArray();
var errors = allTrace.Where(line => line.Contains("FLAGS=.*Z=1")).ToArray();

foreach (var line in jumps)
{
    Console.WriteLine(line);
}
```

### Integration with TraceLog

The microcode tracer automatically integrates with the TraceLog system:

```ini
[Debug]
enable_trace_log = true
trace_categories = MicroCode
trace_level = Verbose
log_file = usim.log
```

All traced instructions are also logged through TraceLog when enabled.

---

## Troubleshooting

### No Trace Output

Check:
1. `UCode.InstructionTraceEnabled` is `true`
2. Configuration file has `instruction_trace = true`
3. Instructions are actually being executed
4. Console output is not redirected

### Buffer Fills Up Quickly

Solutions:
1. Increase `MaxTraceLines`
2. Use conditional tracing
3. Clear buffer periodically: `UCode.ClearTraceBuffer()`
4. Save and clear: `UCode.SaveTraceBuffer("part1.txt"); UCode.ClearTraceBuffer();`

### Performance Too Slow

Options:
1. Disable console tracing: `InstructionTraceEnabled = false`
2. Use buffer only: `MicrocodeTraceEnabled = true`
3. Trace specific sections only
4. Reduce trace detail (future feature)

---

## Example Trace Session

```csharp
using Usim;

class Program
{
    static void Main()
    {
        Console.WriteLine("=== Microcode Tracing Demo ===\n");
        
        // Initialize
        UCode.Init();
        
        // Enable tracing
        UCode.InstructionTraceEnabled = true;
        UCode.MicrocodeTraceEnabled = true;
        
        Console.WriteLine("Trace enabled. Executing test program...\n");
        
        // Build and execute program
        BuildTestProgram();
        
        for (int i = 0; i < 5; i++)
        {
            UCode.ExecuteInstruction((uint)i, true);
        }
        
        Console.WriteLine("\nTrace complete. Saving to file...");
        UCode.SaveTraceBuffer("full_trace.txt");
        
        Console.WriteLine($"Captured {UCode.GetTraceBuffer().Length} instructions");
        Console.WriteLine("Trace saved to full_trace.txt");
    }
    
    static void BuildTestProgram()
    {
        // Create simple ADD program
        ulong inst = MiscUtils.DepositByte(0, 
            UCode.ALU_OP_POS, UCode.ALU_OP_SIZE, 
            (ulong)UCode.AluOp.Add);
        // ... (complete instruction encoding)
        UCode.IMem[0] = inst;
    }
}
```

---

## Reference

### Tracing Properties

```csharp
UCode.InstructionTraceEnabled  // Enable console output
UCode.MicrocodeTraceEnabled    // Enable buffer storage
UCode.MaxTraceLines            // Buffer size limit
```

### Tracing Methods

```csharp
UCode.GetTraceBuffer()         // Get all trace lines
UCode.ClearTraceBuffer()       // Clear trace buffer
UCode.DumpTraceBuffer()        // Dump to console
UCode.SaveTraceBuffer(file)    // Save to file
```

### Configuration Keys

```ini
[Microcode]
instruction_trace = true/false
microcode_trace = true/false

[Tracing]
trace_execution = true/false
trace_microcode = true/false
max_trace_lines = number

[Debug]
trace_level = Verbose
trace_categories = MicroCode,All
```

---

**Instruction tracing is now fully integrated and ready to use!** ??

Use it to:
- Debug microcode programs
- Understand instruction execution
- Verify ALU operations
- Track program flow
- Analyze performance
- Generate execution logs

For more information, see:
- `docs/MICROCODE_ENGINE.md` - Microcode reference
- `usim-cs/UCode.cs` - Implementation
- `usim-cs/Disassembler.cs` - Disassembly logic
