// Program.cs - Main entry point for USIM
// Converted from usim.c

using System;
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
                    
                case "--test-microcode":
                    UCodeTests.RunAllTests();
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

                case "--demo-execution":
                    UCodeTests.DemoInstructionExecution();
                    Environment.Exit(0);
                    break;
                    
                case "--demo-tracing":
                    UCodeTests.DemoInstructionTracing();
                    Environment.Exit(0);
                    break;
                    
                case "--benchmark":
                    UCodeTests.RunBenchmark();
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
        Console.WriteLine("  --test-microcode        Run microcode tests only");
        Console.WriteLine("  --test-config           Run configuration tests only");
        Console.WriteLine("  --test-wpf              Run WPF backend tests only");
        Console.WriteLine("  --demo-execution        Demo instruction execution");
        Console.WriteLine("  --demo-tracing          Demo instruction tracing");
        Console.WriteLine("  --benchmark             Run performance benchmark");
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
    private static void ApplyConfiguration(ConfigParser config)
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

        // Draw test pattern to show display is working (must run after
        // PowerOn(), since PowerOn -> InitializeComponents() -> Display.Initialize()
        // clears video memory and would otherwise erase this)
        if (!UsimState.Headless)
        {
            _machine.Display.DrawTestPattern();
            _machine.Display.Update();
        }

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
        
        // Run microcode tests
        Console.WriteLine("Running Microcode Tests...\n");
        UCodeTests.RunAllTests();
        Console.WriteLine();
        
        // Run configuration tests
        Console.WriteLine("Running Configuration Tests...\n");
        ConfigTests.RunAllTests();
        Console.WriteLine();

        // Run WPF backend tests
        Console.WriteLine("Running WPF Backend Tests...\n");
        WpfBackendTests.RunAllTests();
        Console.WriteLine();

        Console.WriteLine("=== All Tests Complete ===");
    }
}
