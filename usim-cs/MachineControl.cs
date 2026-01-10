// MachineControl.cs - Machine control and state management
// Converted from machine-control.h and machine-control.c

using System;
using System.IO;

namespace Usim;

/// <summary>
/// Machine power state
/// </summary>
public enum PowerState
{
    Off,
    Booting,
    Running,
    Halted,
    ShuttingDown
}

/// <summary>
/// Boot mode
/// </summary>
public enum BootMode
{
    Cold,
    Warm,
    Debug
}

/// <summary>
/// Machine control and lifecycle management
/// </summary>
public class MachineControl
{
    // Machine state
    public PowerState State { get; private set; }
    public BootMode CurrentBootMode { get; private set; }
    
    // Run control
    public bool IsRunning => State == PowerState.Running;
    public bool IsStopped { get; private set; }
    private bool _stopRequested;
    
    // Components
    public MainMemory Memory { get; private set; }
    public DiskController DiskController { get; private set; }
    public Keyboard Keyboard { get; private set; }
    public Mouse Mouse { get; private set; }
    public Display Display { get; private set; }
    public IOBus IOBus { get; private set; }
    public UCode UCode { get; private set; }
    
    // SDL2 backend (optional)
    public SDL2Backend? SDL2Backend { get; private set; }
    
    // Timing
    public DateTime StartTime { get; private set; }
    public DateTime StopTime { get; private set; }
    public TimeSpan RunTime => State == PowerState.Running ? 
        DateTime.Now - StartTime : 
        StopTime - StartTime;
    
    // Events
    public event Action? Started;
    public event Action? Stopped;
    public event Action? Halted;
    
    public MachineControl()
    {
        Memory = new MainMemory();
        DiskController = new DiskController();
        Keyboard = new Keyboard();
        Mouse = new Mouse();
        Display = new Display();
        IOBus = new IOBus();
        UCode = new UCode();
        
        State = PowerState.Off;
        IsStopped = true;
    }
    
    /// <summary>
    /// Initialize SDL2 backend for video/input
    /// </summary>
    public void InitializeSDL2(bool allowResize = false, double scale = 1.0)
    {
        if (SDL2Backend != null)
        {
            TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Warning, "SDL2 backend already initialized");
            return;
        }
        
        SDL2Backend = new SDL2Backend(Display, Keyboard, Mouse)
        {
            AllowResize = allowResize,
            Scale = scale,
            UseLinearFiltering = true
        };
        
        SDL2Backend.Initialize();
        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Info, "SDL2 backend initialized");
    }
    
    /// <summary>
    /// Power on and boot the machine
    /// </summary>
    public void PowerOn(BootMode mode = BootMode.Cold)
    {
        if (State != PowerState.Off)
        {
            Console.WriteLine("Machine is already powered on");
            return;
        }
        
        Console.WriteLine($"Powering on machine ({mode} boot)...");
        
        State = PowerState.Booting;
        CurrentBootMode = mode;
        
        // Initialize components
        InitializeComponents();
        
        // Register I/O devices
        RegisterIODevices();
        
        // Load system files if cold boot
        if (mode == BootMode.Cold)
        {
            LoadSystemFiles();
        }
        else if (mode == BootMode.Warm)
        {
            if (!string.IsNullOrEmpty(UsimState.StateFilename))
                RestoreState(UsimState.StateFilename);
        }
        
        State = PowerState.Running;
        StartTime = DateTime.Now;
        IsStopped = false;
        _stopRequested = false;
        
        Console.WriteLine("Machine powered on");
        Started?.Invoke();
    }
    
    /// <summary>
    /// Power off the machine
    /// </summary>
    public void PowerOff()
    {
        if (State == PowerState.Off)
            return;
        
        Console.WriteLine("Powering off machine...");
        
        State = PowerState.ShuttingDown;
        
        // Save state if requested
        if (UsimState.StateFilename != string.Empty)
        {
            SaveState(UsimState.StateFilename);
        }
        
        State = PowerState.Off;
        StopTime = DateTime.Now;
        IsStopped = true;
        
        Console.WriteLine("Machine powered off");
        Stopped?.Invoke();
    }
    
    /// <summary>
    /// Halt the machine
    /// </summary>
    public void Halt(string? reason = null)
    {
        if (State != PowerState.Running)
            return;
        
        Console.WriteLine($"Machine halted{(reason != null ? $": {reason}" : "")}");
        
        State = PowerState.Halted;
        StopTime = DateTime.Now;
        
        Halted?.Invoke();
        
        if (UsimState.AutoPowerOff)
        {
            PowerOff();
        }
    }
    
    /// <summary>
    /// Request shutdown
    /// </summary>
    public void Shutdown()
    {
        _stopRequested = true;
        PowerOff();
    }
    
    /// <summary>
    /// Reset the machine
    /// </summary>
    public void Reset()
    {
        Console.WriteLine("Resetting machine...");
        
        UCode.Init();
        Memory.Initialize();
        Keyboard.Initialize();
        Mouse.Initialize();
        Display.Initialize();
        IOBus.Reset();
        
        Console.WriteLine("Machine reset complete");
    }
    
    /// <summary>
    /// Initialize all components
    /// </summary>
    private void InitializeComponents()
    {
        Console.WriteLine("Initializing components...");
        
        UCode.Init();
        Memory.Initialize();
        DiskController.Initialize();
        Keyboard.Initialize();
        Mouse.Initialize();
        Display.Initialize();
        IOBus.Initialize();
        
        Console.WriteLine("Components initialized");
    }
    
    /// <summary>
    /// Register I/O devices on the bus
    /// </summary>
    private void RegisterIODevices()
    {
        // Register clock
        IOBus.RegisterDevice(new ClockDevice(0x10000));
        
        // Additional devices would be registered here
    }
    
    /// <summary>
    /// Load system files
    /// </summary>
    private void LoadSystemFiles()
    {
        Console.WriteLine("Loading system files...");
        
        // Load microcode
        string microcodeFile = Path.Combine(UsimState.SysDirectory, "microcode.mcr");
        if (File.Exists(microcodeFile))
        {
            Console.WriteLine($"Loading microcode from {microcodeFile}");
            // Load microcode implementation
        }
        
        // Load PROM
        string promFile = Path.Combine(UsimState.SysDirectory, "prom.prom");
        if (File.Exists(promFile))
        {
            Console.WriteLine($"Loading PROM from {promFile}");
            UCode.LoadPromFromFile(promFile);
        }
        
        // Mount disk images
        string diskImage = Path.Combine(UsimState.SysDirectory, "disk.img");
        if (File.Exists(diskImage))
        {
            Console.WriteLine($"Mounting disk: {diskImage}");
            DiskController.Mount(0, diskImage);
        }
        
        Console.WriteLine("System files loaded");
    }
    
    /// <summary>
    /// Main run loop - execute machine cycles and process events
    /// </summary>
    public void Run()
    {
        if (State != PowerState.Running)
        {
            Console.WriteLine("Machine is not running. Call PowerOn() first.");
            return;
        }
        
        Console.WriteLine("Entering main run loop...");
        
        while (State == PowerState.Running && !_stopRequested)
        {
            // Process SDL2 events if backend is initialized
            if (SDL2Backend != null)
            {
                SDL2Backend.ProcessEvents();
                
                // Exit if window closed
                if (!SDL2Backend.IsRunning)
                {
                    _stopRequested = true;
                    break;
                }
            }
            
            // Execute microcode cycles
            // In a real implementation, this would execute many cycles per frame
            // For now, just update display
            Display.Update();
            
            // Small delay to limit CPU usage
            System.Threading.Thread.Sleep(16); // ~60 FPS
        }
        
        Console.WriteLine("Exiting main run loop");
    }
    
    /// <summary>
    /// Single step execution
    /// </summary>
    public void Step()
    {
        if (State != PowerState.Running && State != PowerState.Halted)
        {
            Console.WriteLine("Machine must be running or halted to step");
            return;
        }
        
        // Execute one microcode instruction
        UCode.Step();
    }
    
    /// <summary>
    /// Save machine state
    /// </summary>
    public void SaveState(string filename)
    {
        if (string.IsNullOrEmpty(filename))
            return;
        
        Console.WriteLine($"Saving state to {filename}");
        
        try
        {
            using var stream = File.Create(filename);
            using var writer = new BinaryWriter(stream);
            
            // Write header
            writer.Write("USIM".ToCharArray());
            writer.Write((uint)1); // Version
            
            // Write machine cycles
            writer.Write(UCode.MachineCycles);
            
            // Write registers
            writer.Write(UCode.Npc);
            writer.Write(UCode.PdlPointer);
            writer.Write(UCode.VmaReg);
            writer.Write(UCode.MdReg);
            
            // Would write more state here...
            
            Console.WriteLine("State saved successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving state: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Restore machine state
    /// </summary>
    public void RestoreState(string filename)
    {
        if (string.IsNullOrEmpty(filename))
            return;
        
        if (!File.Exists(filename))
        {
            Console.WriteLine($"State file not found: {filename}");
            return;
        }
        
        Console.WriteLine($"Restoring state from {filename}");
        
        try
        {
            using var stream = File.OpenRead(filename);
            using var reader = new BinaryReader(stream);
            
            // Read header
            char[] magic = reader.ReadChars(4);
            if (new string(magic) != "USIM")
            {
                Console.WriteLine("Invalid state file");
                return;
            }
            
            uint version = reader.ReadUInt32();
            
            // Read machine cycles
            UCode.MachineCycles = reader.ReadUInt64();
            
            // Read registers
            UCode.Npc = reader.ReadUInt32();
            UCode.PdlPointer = reader.ReadUInt32();
            UCode.VmaReg = reader.ReadUInt32();
            UCode.MdReg = reader.ReadUInt32();
            
            // Would read more state here...
            
            Console.WriteLine("State restored successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error restoring state: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Print machine status
    /// </summary>
    public void PrintStatus()
    {
        Console.WriteLine("=== Machine Status ===");
        Console.WriteLine($"State:      {State}");
        Console.WriteLine($"Boot Mode:  {CurrentBootMode}");
        Console.WriteLine($"Run Time:   {RunTime}");
        Console.WriteLine($"Cycles:     {UCode.MachineCycles:N0}");
        Console.WriteLine();
        
        Memory.PrintStatistics();
        Console.WriteLine();
        
        DiskController.PrintStatistics();
        Console.WriteLine();
        
        IOBus.PrintStatistics();
        Console.WriteLine();
        
        Console.WriteLine($"Display:    {Display.WIDTH}x{Display.HEIGHT} @ {Display.CurrentFPS:F1} FPS");
        Console.WriteLine($"Frames:     {Display.FrameCount:N0}");
        Console.WriteLine();
        
        Console.WriteLine($"Keyboard:   {Keyboard.BufferCount} keys buffered, {Keyboard.KeysPressed:N0} total");
        Console.WriteLine($"Mouse:      ({Mouse.X}, {Mouse.Y}) {Mouse.Buttons}");
    }
}
