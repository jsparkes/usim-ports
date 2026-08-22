// DebugCommands.cs - Interactive debugging commands

using System;
using System.IO;

namespace Usim;

/// <summary>
/// Interactive debugging command processor
/// </summary>
public class DebugCommands
{
    private readonly MachineControl _machine;
    private readonly PerformanceCounter _perfCounter = new();
    
    public DebugCommands(MachineControl machine)
    {
        _machine = machine;
    }
    
    /// <summary>
    /// Execute debug command
    /// </summary>
    public void Execute(string commandLine)
    {
        var parts = commandLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;
        
        string cmd = parts[0].ToLowerInvariant();
        
        try
        {
            switch (cmd)
            {
                case "help":
                case "?":
                    ShowHelp();
                    break;
                    
                case "status":
                case "st":
                    _machine.PrintStatus();
                    break;
                    
                case "reset":
                    _machine.Reset();
                    Console.WriteLine("Machine reset");
                    break;
                    
                case "halt":
                    _machine.Halt();
                    Console.WriteLine("Machine halted");
                    break;
                    
                case "step":
                case "s":
                    StepCommand(parts);
                    break;
                    
                case "run":
                case "r":
                    RunCommand(parts);
                    break;
                    
                case "break":
                case "b":
                    BreakCommand(parts);
                    break;
                    
                case "examine":
                case "x":
                    ExamineCommand(parts);
                    break;
                    
                case "deposit":
                case "d":
                    DepositCommand(parts);
                    break;
                    
                case "disasm":
                case "dis":
                    DisasmCommand(parts);
                    break;
                    
                case "trace":
                    TraceCommand(parts);
                    break;
                    
                case "perf":
                    _perfCounter.PrintStatistics();
                    break;
                    
                case "save":
                    SaveCommand(parts);
                    break;
                    
                case "load":
                    LoadCommand(parts);
                    break;
                    
                case "quit":
                case "q":
                case "exit":
                    // Handled by main loop
                    break;
                    
                default:
                    Console.WriteLine($"Unknown command: {cmd}");
                    Console.WriteLine("Type 'help' for commands");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
    
    private void ShowHelp()
    {
        Console.WriteLine("Available commands:");
        Console.WriteLine("  help, ?           - Show this help");
        Console.WriteLine("  status, st        - Show machine status");
        Console.WriteLine("  reset             - Reset machine");
        Console.WriteLine("  halt              - Halt machine");
        Console.WriteLine("  step [n]          - Step N instructions");
        Console.WriteLine("  run [addr]        - Run from address");
        Console.WriteLine("  break [addr]      - Set breakpoint");
        Console.WriteLine("  examine <addr>    - Examine memory");
        Console.WriteLine("  deposit <addr> <value> - Deposit to memory");
        Console.WriteLine("  disasm <addr> [n] - Disassemble N instructions");
        Console.WriteLine("  trace <category>  - Enable/disable trace");
        Console.WriteLine("  perf              - Show performance stats");
        Console.WriteLine("  save <file>       - Save machine state");
        Console.WriteLine("  load <file>       - Load machine state");
        Console.WriteLine("  quit, q, exit     - Exit simulator");
    }
    
    private void StepCommand(string[] parts)
    {
        int count = 1;
        if (parts.Length > 1 && int.TryParse(parts[1], out int n))
        {
            count = n;
        }
        
        Console.WriteLine($"Stepping {count} instruction(s)...");
        
        for (int i = 0; i < count; i++)
        {
            _perfCounter.Start("step");
            _machine.UCode.Step();
            _perfCounter.Stop("step");

            Console.WriteLine($"PC: 0x{_machine.UCode.Npc:X4}");
        }
    }
    
    private void RunCommand(string[] parts)
    {
        if (parts.Length > 1 && uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out uint addr))
        {
            _machine.UCode.Npc = addr;
        }

        Console.WriteLine($"Running from PC=0x{_machine.UCode.Npc:X4}");
        Console.WriteLine("Press Ctrl+C to stop");
        
        // Start continuous execution
        _machine.PowerOn();
    }
    
    private void BreakCommand(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("Usage: break <address>");
            return;
        }
        
        if (uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out uint addr))
        {
            Console.WriteLine($"Breakpoint set at 0x{addr:X4}");
            // TODO: Implement breakpoint system
        }
        else
        {
            Console.WriteLine("Invalid address");
        }
    }
    
    private void ExamineCommand(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("Usage: examine <address>");
            return;
        }
        
        if (uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out uint addr))
        {
            uint value = _machine.Memory.Read(addr);
            Console.WriteLine($"0x{addr:X8}: 0x{value:X8} ({value})");
        }
        else
        {
            Console.WriteLine("Invalid address");
        }
    }
    
    private void DepositCommand(string[] parts)
    {
        if (parts.Length < 3)
        {
            Console.WriteLine("Usage: deposit <address> <value>");
            return;
        }
        
        if (uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out uint addr) &&
            uint.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out uint value))
        {
            _machine.Memory.Write(addr, value);
            Console.WriteLine($"0x{addr:X8} <- 0x{value:X8}");
        }
        else
        {
            Console.WriteLine("Invalid address or value");
        }
    }
    
    private void DisasmCommand(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("Usage: disasm <address> [count]");
            return;
        }
        
        if (!uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out uint addr))
        {
            Console.WriteLine("Invalid address");
            return;
        }
        
        int count = 10;
        if (parts.Length > 2 && int.TryParse(parts[2], out int n))
        {
            count = n;
        }
        
        Console.WriteLine($"Disassembly at 0x{addr:X4}:");
        
        for (int i = 0; i < count; i++)
        {
            // TODO: Implement proper disassembly
            ulong inst = 0; // Read from microcode memory
            Console.WriteLine($"0x{addr + i:X4}: {inst:X16}");
        }
    }
    
    private void TraceCommand(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("Current trace settings:");
            Console.WriteLine($"  Categories: {TraceLog.Instance.EnabledCategories}");
            Console.WriteLine($"  Level: {TraceLog.Instance.MinimumLevel}");
            Console.WriteLine();
            Console.WriteLine("Usage: trace <category|all|none> [on|off]");
            Console.WriteLine("Categories: MicroCode, Memory, Disk, Display, Keyboard, Mouse, Network, IOBus");
            return;
        }
        
        string category = parts[1].ToLowerInvariant();
        bool enable = parts.Length < 3 || parts[2].ToLowerInvariant() != "off";
        
        switch (category)
        {
            case "all":
                TraceLog.Instance.EnabledCategories = enable ? TraceCategory.All : TraceCategory.None;
                break;
                
            case "none":
                TraceLog.Instance.EnabledCategories = TraceCategory.None;
                break;
                
            case "microcode":
                ToggleTrace(TraceCategory.MicroCode, enable);
                break;
                
            case "memory":
                ToggleTrace(TraceCategory.Memory, enable);
                break;
                
            case "disk":
                ToggleTrace(TraceCategory.Disk, enable);
                break;
                
            case "display":
                ToggleTrace(TraceCategory.Display, enable);
                break;
                
            case "keyboard":
                ToggleTrace(TraceCategory.Keyboard, enable);
                break;
                
            case "mouse":
                ToggleTrace(TraceCategory.Mouse, enable);
                break;
                
            case "network":
                ToggleTrace(TraceCategory.Network, enable);
                break;
                
            case "iobus":
                ToggleTrace(TraceCategory.IOBus, enable);
                break;
                
            default:
                Console.WriteLine($"Unknown category: {category}");
                return;
        }
        
        Console.WriteLine($"Trace {category} {(enable ? "enabled" : "disabled")}");
    }
    
    private void ToggleTrace(TraceCategory category, bool enable)
    {
        if (enable)
        {
            TraceLog.Instance.Enable(category);
        }
        else
        {
            TraceLog.Instance.Disable(category);
        }
    }
    
    private void SaveCommand(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("Usage: save <filename>");
            return;
        }
        
        string filename = parts[1];
        _machine.SaveState(filename);
        Console.WriteLine($"State saved to {filename}");
    }
    
    private void LoadCommand(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("Usage: load <filename>");
            return;
        }
        
        string filename = parts[1];
        
        if (!File.Exists(filename))
        {
            Console.WriteLine($"File not found: {filename}");
            return;
        }
        
        _machine.RestoreState(filename);
        Console.WriteLine($"State loaded from {filename}");
    }
}
