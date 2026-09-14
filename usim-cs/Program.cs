// Program.cs - Main entry point for USIM
// Converted from usim.c

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Usim;

/// <summary>
/// Main CADR simulator program
/// </summary>
public class Program
{
    private static string? _configFilename;
    private static bool _dumpStateFlag;
    private static bool _dumpRunningConfigFlag;
    private static string? _dumpRunningConfigFilename;
    
    /// <summary>
    /// Application result codes
    /// </summary>
    private enum AppResult
    {
        Failure,
        Success,
        Continue
    }
    
    /// <summary>
    /// Main entry point
    /// </summary>
    [System.STAThread]
    public static int Main(string[] args)
    {
        Console.WriteLine("USIM - MIT CADR Simulator (C# Port)");
        Console.WriteLine("Original by Brad Parker <brad@heeltoe.com>");
        Console.WriteLine("C# conversion");
        Console.WriteLine();
        
        // Parse command line arguments
        if (!ParseArguments(args))
        {
            PrintUsage();
            return 1;
        }
        
        // Initialize subsystems
        try
        {
            Initialize();
            
            // Run the simulator
            Run();
            
            // Cleanup
            Shutdown();
            
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
    
    /// <summary>
    /// Parse command line arguments
    /// </summary>
    private static bool ParseArguments(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h":
                case "--help":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                    
                case "-c":
                case "--config":
                    if (i + 1 < args.Length)
                    {
                        _configFilename = args[++i];
                    }
                    else
                    {
                        Console.Error.WriteLine("Error: --config requires a filename");
                        return false;
                    }
                    break;
                    
                case "-s":
                case "--state":
                    if (i + 1 < args.Length)
                    {
                        UsimState.StateFilename = args[++i];
                    }
                    else
                    {
                        Console.Error.WriteLine("Error: --state requires a filename");
                        return false;
                    }
                    break;
                    
                case "-d":
                case "--dump":
                    _dumpStateFlag = true;
                    break;
                    
                case "--headless":
                    UsimState.Headless = true;
                    break;
                    
                case "--auto-boot":
                    UsimState.AutoBoot = true;
                    break;
                    
                case "--colortv":
                    UsimState.ColorTvEnabled = true;
                    break;
                    
                case "-v":
                case "--verbose":
                    UsimState.VerboseDumpStateFlag = true;
                    break;
                    
                case "--test":
                case "--test-all":
                    RunAllTests();
                    Environment.Exit(0);
                    break;
                    
                case "--test-config":
                    ConfigTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-wpf":
                    WpfBackendTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-microcode-decode":
                    UCodeFetchDecodeTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-microcode-alu":
                    UCodeAluTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-microcode-jump":
                    UCodeJumpTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-microcode-mregisters":
                    UCodeMRegisterTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-uvmem":
                    UvmemTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-microcode-vm":
                    UCodeVirtualMemoryTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-bus-adaptor":
                    BusAdaptorTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-bus-interface":
                    BusInterfaceTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-tv":
                    TvTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-colortv":
                    ColorTvTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-unibus-mapping":
                    UnibusMappingTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-microcode-bus-adaptor":
                    UCodeBusAdaptorTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-microcode-dispatch":
                    UCodeDispatchTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-microcode-byte":
                    UCodeByteTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-microcode-interrupt":
                    UCodeInterruptTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-microcode-disassembler":
                    DisassemblerTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-machine-control":
                    MachineControlTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-main-memory":
                    MainMemoryTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-symbol-table":
                    SymbolTableTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-disk-unit":
                    DiskUnitTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--test-disk-controller":
                    DiskControllerTests.RunAllTests();
                    Environment.Exit(0);
                    break;

                case "--debug-microcode":
                case "--debug-ucode":
                    var debugger = new MicrocodeDebugger(new UCode());
                    debugger.StartDebugSession();
                    Environment.Exit(0);
                    break;

                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Print usage information
    /// </summary>
    private static void PrintUsage()
    {
        Console.WriteLine("Usage: usim [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -h, --help              Show this help message");
        Console.WriteLine("  -c, --config <file>     Configuration file");
        Console.WriteLine("  -s, --state <file>      State file");
        Console.WriteLine("  -d, --dump              Dump state on exit");
        Console.WriteLine("  --headless              Run without display");
        Console.WriteLine("  --auto-boot             Automatically boot");
        Console.WriteLine("  --colortv               Enable color TV");
        Console.WriteLine("  -v, --verbose           Verbose output");
        Console.WriteLine();
        Console.WriteLine("Testing & Demos:");
        Console.WriteLine("  --test, --test-all      Run all tests");
        Console.WriteLine("  --test-config           Run configuration tests only");
        Console.WriteLine("  --test-wpf              Run WPF backend tests only");
        Console.WriteLine("  --test-microcode-decode Run microcode fetch/decode tests only");
        Console.WriteLine("  --test-microcode-alu    Run microcode ALU tests only");
        Console.WriteLine("  --test-microcode-jump   Run microcode jump tests only");
        Console.WriteLine("  --test-microcode-mregisters Run microcode M-register tests only");
        Console.WriteLine("  --test-uvmem            Run virtual memory (Uvmem) tests only");
        Console.WriteLine("  --test-microcode-vm     Run microcode virtual memory tests only");
        Console.WriteLine("  --test-bus-adaptor      Run bus adaptor tests only");
        Console.WriteLine("  --test-bus-interface    Run bus interface tests only");
        Console.WriteLine("  --test-tv               Run TV tests only");
        Console.WriteLine("  --test-colortv          Run color TV tests only");
        Console.WriteLine("  --test-unibus-mapping   Run Unibus mapping register tests only");
        Console.WriteLine("  --test-microcode-bus-adaptor Run UCode/BusAdaptor wiring tests only");
        Console.WriteLine("  --test-microcode-dispatch Run microcode dispatch tests only");
        Console.WriteLine("  --test-microcode-byte   Run microcode byte tests only");
        Console.WriteLine("  --test-microcode-interrupt Run microcode interrupt tests only");
        Console.WriteLine("  --test-microcode-disassembler Run microcode disassembler tests only");
        Console.WriteLine("  --test-machine-control  Run MachineControl wiring tests only");
        Console.WriteLine("  --test-main-memory      Run MainMemory tests only");
        Console.WriteLine("  --test-symbol-table     Run SymbolTable tests only");
        Console.WriteLine("  --test-disk-unit        Run DiskUnit tests only");
        Console.WriteLine("  --test-disk-controller  Run DiskController tests only");
        Console.WriteLine();
        Console.WriteLine("Debugging:");
        Console.WriteLine("  --debug-microcode       Interactive microcode debugger");
    }
    
    private static MachineControl? _machine;
    private static ConfigParser? _config;
    private static DebugCommands? _debugCommands;
    
    /// <summary>
    /// Initialize all simulator subsystems
    /// </summary>
    private static void Initialize()
    {
        Console.WriteLine("Initializing USIM...");
        
        // Load configuration if specified
        if (_configFilename != null)
        {
            _config = new ConfigParser();
            if (_config.Load(_configFilename))
            {
                ApplyConfiguration(_config);
            }
        }
        
        // Create machine control
        _machine = new MachineControl();
        
        // Create debug commands
        _debugCommands = new DebugCommands(_machine);
        
        // Configure tracing if config available
        if (_config != null)
        {
            string traceCategories = _config.GetString("trace", "categories", "None");
            if (traceCategories != "None")
            {
                if (Enum.TryParse<TraceCategory>(traceCategories, out var categories))
                {
                    TraceLog.Instance.EnabledCategories = categories;
                }
            }
            
            string traceLevel = _config.GetString("trace", "level", "Info");
            if (Enum.TryParse<TraceLevel>(traceLevel, out var level))
            {
                TraceLog.Instance.MinimumLevel = level;
            }
        }
        
        Console.WriteLine("Initialization complete.");
    }
    
    /// <summary>
    /// Apply configuration settings
    /// </summary>
    internal static void ApplyConfiguration(ConfigParser config)
    {
        UsimState.SysDirectory = config.GetString("Paths", "sys-directory", "./sys");
        UsimState.FsRootDirectory = config.GetString("Paths", "fs-root-directory", "./fs");
        UsimState.StateFilename = config.GetString("Paths", "state-file", "usim.state");
        UsimState.WindowTitle = config.GetString("Display", "window-title", "CADR Lisp Machine");
        UsimState.ColorTvEnabled = config.GetBool("Display", "colortv", false);
        UsimState.Headless = config.GetBool("Execution", "headless", false);
        UsimState.AutoBoot = config.GetBool("Execution", "auto-boot", false);
        UsimState.AutoPowerOff = config.GetBool("Execution", "auto-power-off", false);
        UsimState.VerboseDumpStateFlag = config.GetBool("Debug", "verbose-dump", false);

        string monitor = config.GetString("usim", "monitor", "other");
        switch (monitor)
        {
            case "cpt":
                UsimState.TvWidth = 768;
                UsimState.TvHeight = 896;
                break;
            case "other":
                UsimState.TvWidth = 768;
                UsimState.TvHeight = 963;
                break;
            default:
                Console.WriteLine($"Warning: unknown monitor type '{monitor}', using cpt");
                UsimState.TvWidth = 768;
                UsimState.TvHeight = 896;
                break;
        }

        var diskUnits = new List<(uint, string, string)>();
        for (uint i = 0; i < DiskController.NUMBER_OF_DISK_UNITS; i++)
        {
            string line = config.GetString("disk", $"disk{i}", "");
            if (string.IsNullOrEmpty(line)) continue;

            string typeName;
            string filename;
            string[] parts = line.Split(',', 2);
            if (parts.Length == 1)
            {
                // Real C's one-token fallback (usim/disk-unit.c:236-241):
                // a bare filename with no comma defaults the type to T-300.
                typeName = "T-300";
                filename = parts[0].Trim();
            }
            else
            {
                typeName = parts[0].Trim();
                filename = parts[1].Trim();
            }

            diskUnits.Add((i, typeName, filename));
        }
        UsimState.DiskUnits = diskUnits.ToArray();
    }
    
    /// <summary>
    /// Main simulator run loop
    /// </summary>
    private static void Run()
    {
        if (_machine == null || _debugCommands == null)
        {
            Console.WriteLine("Error: Machine not initialized");
            return;
        }
        
        Console.WriteLine("Starting CADR simulation...");

        // Initialize WPF display backend unless headless
        if (!UsimState.Headless)
        {
            Console.WriteLine("Initializing WPF display backend...");
            _machine.InitializeDisplay(allowResize: false, scale: 1.0);

            if (_machine.DisplayBackend != null)
            {
                _machine.DisplayBackend.SetWindowTitle(UsimState.WindowTitle);
            }
        }
        else
        {
            Console.WriteLine("Running in headless mode (no display)");
        }

        // Power on the machine
        var bootMode = UsimState.WarmBootFlag ? BootMode.Warm : BootMode.Cold;
        _machine.PowerOn(bootMode);

        if (UsimState.AutoBoot)
        {
            Console.WriteLine("Auto-boot enabled");
        }
        
        Console.WriteLine("\nSimulation started.");

        if (_machine.DisplayBackend != null)
        {
            Console.WriteLine("WPF window opened. Close window or press Ctrl+C to exit.");
            Console.WriteLine("Type commands in terminal for interactive debugging.\n");

            // Start background thread for console input if we have a display
            bool consoleRunning = true;
            var consoleThread = new System.Threading.Thread(() =>
            {
                while (consoleRunning && _machine.IsRunning)
                {
                    Console.Write("usim> ");
                    string? input = Console.ReadLine();

                    if (input == null || input.Trim().ToLowerInvariant() is "quit" or "q" or "exit")
                    {
                        consoleRunning = false;
                        _machine.Shutdown();
                        break;
                    }

                    string command = input.Trim();
                    if (!string.IsNullOrEmpty(command))
                    {
                        _debugCommands.Execute(command);
                    }
                }
            });
            consoleThread.IsBackground = true;
            consoleThread.Start();

            // Run main loop with display
            _machine.Run();

            consoleRunning = false;
        }
        else
        {
            // Headless mode - interactive command loop
            Console.WriteLine("Type 'help' for available commands\n");
            
            while (_machine.IsRunning)
            {
                Console.Write("usim> ");
                string? input = Console.ReadLine();
                
                if (input == null || input.Trim().ToLowerInvariant() is "quit" or "q" or "exit")
                {
                    break;
                }
                
                string command = input.Trim();
                if (!string.IsNullOrEmpty(command))
                {
                    _debugCommands.Execute(command);
                }
            }
        }
    }
    
    /// <summary>
    /// Shutdown and cleanup
    /// </summary>
    private static void Shutdown()
    {
        Console.WriteLine("\nShutting down USIM...");

        if (_machine != null)
        {
            _machine.Shutdown();

            // Dispose display backend if initialized
            _machine.DisplayBackend?.Dispose();
        }

        TraceLog.Instance.Close();

        Console.WriteLine("Shutdown complete.");
    }
    
    /// <summary>
    /// Run all test suites
    /// </summary>
    private static void RunAllTests()
    {
        Console.WriteLine("=== USIM Complete Test Suite ===\n");

        // Run MainMemory tests
        Console.WriteLine("Running MainMemory Tests...\n");
        MainMemoryTests.RunAllTests();
        Console.WriteLine();

        // Run microcode fetch/decode tests
        Console.WriteLine("Running Microcode Fetch/Decode Tests...\n");
        UCodeFetchDecodeTests.RunAllTests();
        Console.WriteLine();

        // Run microcode ALU tests
        Console.WriteLine("Running Microcode ALU Tests...\n");
        UCodeAluTests.RunAllTests();
        Console.WriteLine();

        // Run microcode jump tests
        Console.WriteLine("Running Microcode Jump Tests...\n");
        UCodeJumpTests.RunAllTests();
        Console.WriteLine();

        // Run microcode M-register tests
        Console.WriteLine("Running Microcode M-Register Tests...\n");
        UCodeMRegisterTests.RunAllTests();
        Console.WriteLine();

        // Run configuration tests
        Console.WriteLine("Running Configuration Tests...\n");
        ConfigTests.RunAllTests();
        Console.WriteLine();

        // Run WPF backend tests
        Console.WriteLine("Running WPF Backend Tests...\n");
        WpfBackendTests.RunAllTests();
        Console.WriteLine();

        // Run virtual memory (Uvmem) tests
        Console.WriteLine("Running Uvmem Tests...\n");
        UvmemTests.RunAllTests();
        Console.WriteLine();

        // Run microcode virtual memory tests
        Console.WriteLine("Running Microcode Virtual Memory Tests...\n");
        UCodeVirtualMemoryTests.RunAllTests();
        Console.WriteLine();

        // Run bus adaptor tests
        Console.WriteLine("Running Bus Adaptor Tests...\n");
        BusAdaptorTests.RunAllTests();
        Console.WriteLine();

        // Run bus interface tests
        Console.WriteLine("Running Bus Interface Tests...\n");
        BusInterfaceTests.RunAllTests();
        Console.WriteLine();

        // Run TV tests
        Console.WriteLine("Running TV Tests...\n");
        TvTests.RunAllTests();
        Console.WriteLine();

        // Run color TV tests
        Console.WriteLine("Running Color TV Tests...\n");
        ColorTvTests.RunAllTests();
        Console.WriteLine();

        // Run Unibus mapping tests
        Console.WriteLine("Running Unibus Mapping Tests...\n");
        UnibusMappingTests.RunAllTests();
        Console.WriteLine();

        // Run UCode/BusAdaptor wiring tests
        Console.WriteLine("Running Microcode BusAdaptor Wiring Tests...\n");
        UCodeBusAdaptorTests.RunAllTests();
        Console.WriteLine();

        // Run microcode dispatch tests
        Console.WriteLine("Running Microcode Dispatch Tests...\n");
        UCodeDispatchTests.RunAllTests();
        Console.WriteLine();

        // Run microcode byte tests
        Console.WriteLine("Running Microcode Byte Tests...\n");
        UCodeByteTests.RunAllTests();
        Console.WriteLine();

        // Run microcode interrupt tests
        Console.WriteLine("Running Microcode Interrupt Tests...\n");
        UCodeInterruptTests.RunAllTests();
        Console.WriteLine();

        // Run MachineControl wiring tests
        Console.WriteLine("Running MachineControl Tests...\n");
        MachineControlTests.RunAllTests();
        Console.WriteLine();

        // Run microcode disassembler tests
        Console.WriteLine("Running Microcode Disassembler Tests...\n");
        DisassemblerTests.RunAllTests();
        Console.WriteLine();

        // Run symbol table tests
        Console.WriteLine("Running Symbol Table Tests...\n");
        SymbolTableTests.RunAllTests();
        Console.WriteLine();

        // Run DiskUnit tests
        Console.WriteLine("Running DiskUnit Tests...\n");
        DiskUnitTests.RunAllTests();
        Console.WriteLine();

        // Run DiskController tests
        Console.WriteLine("Running DiskController Tests...\n");
        DiskControllerTests.RunAllTests();
        Console.WriteLine();

        Console.WriteLine("=== All Tests Complete ===");
    }
}
