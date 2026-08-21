// MicrocodeDebugger.cs - Interactive microcode debugging tool
using System;
using System.Collections.Generic;
using System.Linq;

namespace Usim;

/// <summary>
/// Interactive debugger for microcode execution
/// </summary>
public class MicrocodeDebugger
{
    #region State
    
    private bool _running;
    private bool _stepMode;
    private uint _breakpoint = 0xFFFF; // No breakpoint
    private readonly HashSet<uint> _breakpoints = new();
    private readonly Dictionary<uint, string> _pcLabels = new();
    private int _stepCount;
    private int _targetSteps;
    
    // Watch points
    private readonly HashSet<uint> _aMemWatchPoints = new();
    private readonly HashSet<uint> _mMemWatchPoints = new();
    private readonly Dictionary<uint, uint> _lastAMemValues = new();
    private readonly Dictionary<uint, uint> _lastMMemValues = new();
    
    #endregion
    
    #region Configuration
    
    public bool ShowRegisters { get; set; } = true;
    public bool ShowFlags { get; set; } = true;
    public bool ShowMemory { get; set; } = false;
    public bool ShowStack { get; set; } = false;
    public bool ShowDisassembly { get; set; } = true;
    public bool AutoStep { get; set; } = false;
    public int AutoStepDelay { get; set; } = 100; // milliseconds
    
    #endregion
    
    /// <summary>
    /// Start interactive debugging session
    /// </summary>
    public void StartDebugSession()
    {
        Console.WriteLine("=== CADR Microcode Debugger ===");
        Console.WriteLine("Type 'help' for command list");
        Console.WriteLine();
        
        _running = true;
        _stepMode = true;
        
        // Enable microcode tracing
        UCode.MicrocodeTraceEnabled = true;
        UCode.InstructionTraceEnabled = false; // We'll handle display
        
        while (_running)
        {
            try
            {
                if (_stepMode)
                {
                    ShowStatus();
                    ProcessCommand();
                }
                else
                {
                    // Run mode - execute until breakpoint
                    ExecuteSingleStep();
                    
                    if (_breakpoints.Contains(UCode.Npc))
                    {
                        Console.WriteLine($"\n*** Breakpoint hit at PC={UCode.Npc:X4} ***\n");
                        _stepMode = true;
                    }
                    
                    if (_targetSteps > 0)
                    {
                        _stepCount++;
                        if (_stepCount >= _targetSteps)
                        {
                            Console.WriteLine($"\n*** Completed {_stepCount} steps ***\n");
                            _stepMode = true;
                            _stepCount = 0;
                            _targetSteps = 0;
                        }
                    }
                    
                    if (AutoStep && !_stepMode)
                    {
                        System.Threading.Thread.Sleep(AutoStepDelay);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                _stepMode = true;
            }
        }
    }
    
    /// <summary>
    /// Execute a single microcode step
    /// </summary>
    private void ExecuteSingleStep()
    {
        uint pc = UCode.Npc;
        bool useImem = true; // Typically use IMEM
        
        // Check watch points before execution
        CheckWatchPoints();
        
        // Execute one instruction
        UCode.ExecuteInstruction(pc, useImem);
        
        // Update PC
        UCode.Opc = pc;
        // UCode.Npc is updated by ExecuteInstruction
    }
    
    /// <summary>
    /// Show current debugger status
    /// </summary>
    private void ShowStatus()
    {
        Console.WriteLine($"???? Cycle {UCode.MachineCycles} ??????????????????????????????");
        
        // Current instruction
        if (ShowDisassembly)
        {
            uint pc = UCode.Npc;
            ulong instruction = UCode.FetchInstruction(pc, true);
            string disasm = Disassembler.DisassembleInst2(instruction, true);
            
            string label = _pcLabels.ContainsKey(pc) ? $" ({_pcLabels[pc]})" : "";
            Console.WriteLine($"PC: {pc:X4}{label}");
            Console.WriteLine($"    {disasm}");
        }
        
        // Registers
        if (ShowRegisters)
        {
            Console.WriteLine();
            Console.WriteLine($"OUT: {UCode.Out:X8}  Q:   {UCode.Q:X8}  MD:  {UCode.MdReg:X8}");
            Console.WriteLine($"VMA: {UCode.VmaReg:X8}  LC:  {UCode.Lc:X8}  OA:  {UCode.OaRegHigh:X4}{UCode.OaRegLow:X4}");
            Console.WriteLine($"M:   {UCode.MData:X8}  A:   {UCode.AData:X8}");
        }
        
        // Flags
        if (ShowFlags)
        {
            Console.Write("FLAGS: ");
            Console.Write(UCode.CarryFlag ? "C" : "c");
            Console.Write(UCode.OverflowFlag ? "V" : "v");
            Console.Write(UCode.NegativeFlag ? "N" : "n");
            Console.Write(UCode.ZeroFlag ? "Z" : "z");
            Console.WriteLine();
        }
        
        // Stack
        if (ShowStack)
        {
            Console.WriteLine($"\nStack (PDL Ptr={UCode.PdlPointer:X3}):");
            for (int i = 0; i < 4; i++)
            {
                uint value = UCode.ReadPdl((uint)i);
                Console.WriteLine($"  [{i}]: {value:X8}");
            }
        }
        
        Console.WriteLine("???????????????????????????????????????????????");
    }
    
    /// <summary>
    /// Process debugger command
    /// </summary>
    private void ProcessCommand()
    {
        Console.Write("(ucode-dbg) ");
        string? input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            input = "s"; // Default to step
        }
        
        string[] parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string cmd = parts[0].ToLower();
        
        switch (cmd)
        {
            case "s":
            case "step":
                ExecuteSingleStep();
                break;
                
            case "n":
            case "next":
                int steps = parts.Length > 1 && int.TryParse(parts[1], out int n) ? n : 10;
                _targetSteps = steps;
                _stepCount = 0;
                _stepMode = false;
                break;
                
            case "c":
            case "continue":
                _stepMode = false;
                Console.WriteLine("Continuing... (break with Ctrl+C or at breakpoint)");
                break;
                
            case "b":
            case "break":
                if (parts.Length > 1 && uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out uint bp))
                {
                    _breakpoints.Add(bp);
                    Console.WriteLine($"Breakpoint set at PC={bp:X4}");
                }
                else
                {
                    Console.WriteLine("Usage: break <pc-hex>");
                }
                break;
                
            case "d":
            case "delete":
                if (parts.Length > 1 && uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out uint dbp))
                {
                    _breakpoints.Remove(dbp);
                    Console.WriteLine($"Breakpoint deleted at PC={dbp:X4}");
                }
                else
                {
                    _breakpoints.Clear();
                    Console.WriteLine("All breakpoints cleared");
                }
                break;
                
            case "l":
            case "list":
                ListBreakpoints();
                break;
                
            case "r":
            case "regs":
            case "registers":
                ShowRegisters = !ShowRegisters;
                Console.WriteLine($"Register display: {ShowRegisters}");
                break;
                
            case "f":
            case "flags":
                ShowFlags = !ShowFlags;
                Console.WriteLine($"Flag display: {ShowFlags}");
                break;
                
            case "k":
            case "stack":
                ShowStack = !ShowStack;
                Console.WriteLine($"Stack display: {ShowStack}");
                break;
                
            case "m":
            case "mem":
                DumpMemory(parts);
                break;
                
            case "w":
            case "watch":
                SetWatchPoint(parts);
                break;
                
            case "p":
            case "print":
                PrintExpression(parts);
                break;
                
            case "dis":
            case "disasm":
                DisassembleRange(parts);
                break;
                
            case "trace":
                ToggleTrace();
                break;
                
            case "dump":
                UCode.DumpState();
                break;
                
            case "label":
                SetLabel(parts);
                break;
                
            case "reset":
                UCode.Init();
                Console.WriteLine("Microcode engine reset");
                break;
                
            case "h":
            case "help":
            case "?":
                ShowHelp();
                break;
                
            case "q":
            case "quit":
            case "exit":
                _running = false;
                break;
                
            default:
                Console.WriteLine($"Unknown command: {cmd}. Type 'help' for command list.");
                break;
        }
    }
    
    /// <summary>
    /// Check watch points for memory changes
    /// </summary>
    private void CheckWatchPoints()
    {
        // Check A memory watch points
        foreach (var addr in _aMemWatchPoints)
        {
            uint current = UCode.ReadAMem(addr);
            if (_lastAMemValues.TryGetValue(addr, out uint last) && current != last)
            {
                Console.WriteLine($"*** Watch: A[{addr:X3}] changed: {last:X8} -> {current:X8} ***");
            }
            _lastAMemValues[addr] = current;
        }
        
        // Check M memory watch points
        foreach (var addr in _mMemWatchPoints)
        {
            uint current = UCode.ReadMMem(addr);
            if (_lastMMemValues.TryGetValue(addr, out uint last) && current != last)
            {
                Console.WriteLine($"*** Watch: M[{addr:X2}] changed: {last:X8} -> {current:X8} ***");
            }
            _lastMMemValues[addr] = current;
        }
    }
    
    /// <summary>
    /// Set a watch point
    /// </summary>
    private void SetWatchPoint(string[] parts)
    {
        if (parts.Length < 3)
        {
            Console.WriteLine("Usage: watch <a|m> <addr-hex>");
            return;
        }
        
        string memType = parts[1].ToLower();
        if (!uint.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out uint addr))
        {
            Console.WriteLine("Invalid address");
            return;
        }
        
        if (memType == "a")
        {
            _aMemWatchPoints.Add(addr);
            _lastAMemValues[addr] = UCode.ReadAMem(addr);
            Console.WriteLine($"Watch point set on A[{addr:X3}]");
        }
        else if (memType == "m")
        {
            _mMemWatchPoints.Add(addr);
            _lastMMemValues[addr] = UCode.ReadMMem(addr);
            Console.WriteLine($"Watch point set on M[{addr:X2}]");
        }
        else
        {
            Console.WriteLine("Memory type must be 'a' or 'm'");
        }
    }
    
    /// <summary>
    /// Dump memory contents
    /// </summary>
    private void DumpMemory(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("Usage: mem <a|m|d|pdl> [start] [count]");
            return;
        }
        
        string memType = parts[1].ToLower();
        uint start = parts.Length > 2 && uint.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out uint s) ? s : 0;
        int count = parts.Length > 3 && int.TryParse(parts[3], out int c) ? c : 16;
        
        Console.WriteLine($"\n{memType.ToUpper()} Memory:");
        
        for (int i = 0; i < count; i++)
        {
            uint addr = start + (uint)i;
            uint value = memType switch
            {
                "a" => UCode.ReadAMem(addr),
                "m" => UCode.ReadMMem(addr),
                "d" => UCode.ReadDMem(addr),
                "pdl" => UCode.Pdl[addr & 0x3FF],
                _ => 0
            };
            
            Console.WriteLine($"  [{addr:X4}]: {value:X8}");
        }
        Console.WriteLine();
    }
    
    /// <summary>
    /// Print expression/register value
    /// </summary>
    private void PrintExpression(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("Usage: print <expr>");
            return;
        }
        
        string expr = parts[1].ToLower();
        
        switch (expr)
        {
            case "pc": Console.WriteLine($"PC = {UCode.Npc:X4}"); break;
            case "opc": Console.WriteLine($"OPC = {UCode.Opc:X4}"); break;
            case "out": Console.WriteLine($"OUT = {UCode.Out:X8}"); break;
            case "q": Console.WriteLine($"Q = {UCode.Q:X8}"); break;
            case "vma": Console.WriteLine($"VMA = {UCode.VmaReg:X8}"); break;
            case "md": Console.WriteLine($"MD = {UCode.MdReg:X8}"); break;
            case "lc": Console.WriteLine($"LC = {UCode.Lc:X8}"); break;
            case "m": Console.WriteLine($"M = {UCode.MData:X8}"); break;
            case "a": Console.WriteLine($"A = {UCode.AData:X8}"); break;
            case "pdlptr": Console.WriteLine($"PDL Ptr = {UCode.PdlPointer:X3}"); break;
            case "cycles": Console.WriteLine($"Cycles = {UCode.MachineCycles}"); break;
            default:
                Console.WriteLine($"Unknown expression: {expr}");
                break;
        }
    }
    
    /// <summary>
    /// Disassemble instruction range
    /// </summary>
    private void DisassembleRange(string[] parts)
    {
        uint start = parts.Length > 1 && uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out uint s) ? s : UCode.Npc;
        int count = parts.Length > 2 && int.TryParse(parts[2], out int c) ? c : 10;
        
        Console.WriteLine($"\nDisassembly starting at PC={start:X4}:");
        
        for (int i = 0; i < count; i++)
        {
            uint pc = start + (uint)i;
            ulong instruction = UCode.FetchInstruction(pc, true);
            
            if (instruction == 0)
                break;
                
            string disasm = Disassembler.DisassembleInst2(instruction, true);
            string marker = (pc == UCode.Npc) ? "=>" : "  ";
            string label = _pcLabels.ContainsKey(pc) ? $" <{_pcLabels[pc]}>" : "";
            
            Console.WriteLine($"{marker} {pc:X4}: {disasm}{label}");
        }
        Console.WriteLine();
    }
    
    /// <summary>
    /// Toggle instruction trace
    /// </summary>
    private void ToggleTrace()
    {
        UCode.InstructionTraceEnabled = !UCode.InstructionTraceEnabled;
        Console.WriteLine($"Instruction trace: {UCode.InstructionTraceEnabled}");
    }
    
    /// <summary>
    /// Set label for PC address
    /// </summary>
    private void SetLabel(string[] parts)
    {
        if (parts.Length < 3)
        {
            Console.WriteLine("Usage: label <pc-hex> <name>");
            return;
        }
        
        if (uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out uint pc))
        {
            string label = string.Join(" ", parts.Skip(2));
            _pcLabels[pc] = label;
            Console.WriteLine($"Label '{label}' set at PC={pc:X4}");
        }
        else
        {
            Console.WriteLine("Invalid PC address");
        }
    }
    
    /// <summary>
    /// List all breakpoints
    /// </summary>
    private void ListBreakpoints()
    {
        if (_breakpoints.Count == 0)
        {
            Console.WriteLine("No breakpoints set");
            return;
        }
        
        Console.WriteLine($"\nBreakpoints ({_breakpoints.Count}):");
        foreach (var bp in _breakpoints.OrderBy(b => b))
        {
            string label = _pcLabels.ContainsKey(bp) ? $" ({_pcLabels[bp]})" : "";
            Console.WriteLine($"  PC={bp:X4}{label}");
        }
        Console.WriteLine();
    }
    
    /// <summary>
    /// Show help
    /// </summary>
    private void ShowHelp()
    {
        Console.WriteLine(@"
=== CADR Microcode Debugger Commands ===

Execution:
  s, step              - Execute one microcode instruction
  n, next [N]          - Execute N instructions (default: 10)
  c, continue          - Run until breakpoint
  
Breakpoints:
  b, break <pc>        - Set breakpoint at PC (hex)
  d, delete [pc]       - Delete breakpoint (or all if no PC)
  l, list              - List all breakpoints
  
Display Control:
  r, regs              - Toggle register display
  f, flags             - Toggle flag display
  k, stack             - Toggle stack display
  trace                - Toggle instruction trace to console
  
Memory:
  m, mem <type> [addr] [count]
                       - Dump memory (type: a/m/d/pdl)
  w, watch <type> <addr>
                       - Set watch point (type: a/m)
  
Information:
  p, print <expr>      - Print register/expression
                         (pc, out, q, vma, md, lc, m, a, cycles)
  dis [pc] [count]     - Disassemble from PC (default: current)
  dump                 - Dump full microcode state
  label <pc> <name>    - Set label for PC address
  
Control:
  reset                - Reset microcode engine
  h, help, ?           - Show this help
  q, quit, exit        - Exit debugger

Examples:
  break 100            - Break at PC=0x100
  mem a 0 10           - Show A memory 0x00-0x0A
  watch m 1F           - Watch M[0x1F] for changes
  next 100             - Execute 100 instructions
  dis 200 20           - Disassemble 20 instructions from 0x200
");
    }
}
