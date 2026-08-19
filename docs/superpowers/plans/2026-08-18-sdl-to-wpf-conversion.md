# SDL2-to-WPF Graphics Backend Conversion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the SDL2-based display/keyboard/mouse/audio backend in `usim-cs` with a WPF-based one, making `usim-cs` a Windows-only WPF application.

**Architecture:** `Keyboard`, `Mouse`, and `Display` stay toolkit-agnostic and unchanged in their public contracts (aside from two small fixes below). All SDL-specific code in `SDL2Backend.cs` is deleted and replaced by `WpfBackend.cs`, which owns a WPF `Window`/`WriteableBitmap`/`DispatcherTimer` and translates WPF input events into the same calls `SDL2Backend` used to make. `MachineControl.Run()` changes from a manual polling `while` loop into a call that pumps WPF's own message loop (`Application.Run`).

**Tech Stack:** .NET 8.0 (`net8.0-windows`), WPF (`UseWPF`), `System.Media.SoundPlayer` for beep audio (no new NuGet dependency).

**Spec:** [docs/superpowers/specs/2026-08-18-sdl-to-wpf-conversion-design.md](../specs/2026-08-18-sdl-to-wpf-conversion-design.md)

## Global Constraints

- `usim-cs` targets `net8.0-windows` with `<UseWPF>true</UseWPF>` — no more `net8.0`/SDL2-CS reference.
- No new third-party dependencies (beep audio uses the built-in `System.Media.SoundPlayer`).
- `Keyboard.KeyDown/KeyUp(uint keysym, int modifiers)` and `Mouse.UpdatePosition/ButtonDown/ButtonUp` keep their existing signatures — the WPF backend must call them exactly as `SDL2Backend` did.
- The existing hand-rolled test convention (a `static class *Tests` with a `RunAllTests()` method, a private `Assert(bool, string)` helper, wired to a `--test-*` CLI flag in `Program.cs`) is followed for all new automated tests — this codebase has no xunit/nunit.

---

## Task 1: Retarget project to WPF, remove SDL2, stub the new backend

This task makes the solution compile against WPF instead of SDL2. It does **not** make the display functional yet — `WpfBackend.Initialize()` is a stub that throws — but headless mode (`--headless`) keeps working, giving a safe checkpoint.

**Files:**
- Delete: `usim-cs/SDL2Backend.cs`
- Delete: `usim-cs/SDL2VerificationTest.cs`
- Create: `usim-cs/WpfBackend.cs` (stub)
- Modify: `usim-cs/Usim.csproj`
- Modify: `usim-cs/MachineControl.cs`
- Modify: `usim-cs/Program.cs`

**Interfaces:**
- Produces: `WpfBackend(Display display, Keyboard keyboard, Mouse mouse, Action onTick)` constructor; `Scale`/`AllowResize`/`UseLinearFiltering`/`IsRunning` properties; `Initialize()`, `RunMessageLoop()`, `RequestExit()`, `SetWindowTitle(string)`, `Beep(int, int)`, `Dispose()` methods. Task 5 fills in real bodies but does not change this shape.
- Produces: `MachineControl.DisplayBackend` (type `WpfBackend?`), `MachineControl.InitializeDisplay(bool allowResize = false, double scale = 1.0)`.

- [ ] **Step 1: Delete the SDL2 files**

Delete `usim-cs/SDL2Backend.cs` and `usim-cs/SDL2VerificationTest.cs`.

- [ ] **Step 2: Retarget the project file**

Modify `usim-cs/Usim.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <LangVersion>latest</LangVersion>
    <RootNamespace>Usim</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\chaos-cs\Chaos.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create the `WpfBackend` stub**

Create `usim-cs/WpfBackend.cs`:

```csharp
// WpfBackend.cs - WPF video, keyboard, mouse, and beep backend
// Replaces SDL2Backend.cs

using System;

namespace Usim;

/// <summary>
/// WPF backend for video rendering, input handling, and beep audio.
/// </summary>
public class WpfBackend : IDisposable
{
    private readonly Display _display;
    private readonly Keyboard _keyboard;
    private readonly Mouse _mouse;
    private readonly Action _onTick;

    public double Scale { get; set; } = 1.0;
    public bool AllowResize { get; set; } = false;
    public bool UseLinearFiltering { get; set; } = true;
    public bool IsRunning { get; private set; }

    public WpfBackend(Display display, Keyboard keyboard, Mouse mouse, Action onTick)
    {
        _display = display ?? throw new ArgumentNullException(nameof(display));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _mouse = mouse ?? throw new ArgumentNullException(nameof(mouse));
        _onTick = onTick ?? throw new ArgumentNullException(nameof(onTick));
    }

    public void Initialize()
    {
        throw new NotImplementedException("WPF backend rendering is not implemented yet (see Task 5).");
    }

    public void RunMessageLoop()
    {
        throw new NotImplementedException("WPF backend rendering is not implemented yet (see Task 5).");
    }

    public void RequestExit() { }

    public void SetWindowTitle(string title) { }

    public void Beep(int halfWavelengthMicros, int durationMicros) { }

    public void Dispose()
    {
        IsRunning = false;
    }
}
```

- [ ] **Step 4: Rename the backend property/method on `MachineControl` and restructure `Run()`**

In `usim-cs/MachineControl.cs`, replace the `SDL2Backend` property (around line 55):

```csharp
    // Display backend (optional)
    public WpfBackend? DisplayBackend { get; private set; }
```

Replace `InitializeSDL2` (around lines 83-103):

```csharp
    /// <summary>
    /// Initialize WPF backend for video/input
    /// </summary>
    public void InitializeDisplay(bool allowResize = false, double scale = 1.0)
    {
        if (DisplayBackend != null)
        {
            TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Warning, "Display backend already initialized");
            return;
        }

        DisplayBackend = new WpfBackend(Display, Keyboard, Mouse, onTick: () => { })
        {
            AllowResize = allowResize,
            Scale = scale,
            UseLinearFiltering = true
        };

        DisplayBackend.Initialize();
        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Info, "Display backend initialized");
    }
```

Replace `Shutdown()` (around line 197) so quitting from the console command thread also closes the window:

```csharp
    public void Shutdown()
    {
        _stopRequested = true;
        PowerOff();
        DisplayBackend?.RequestExit();
    }
```

Replace the body of `Run()` (around lines 286-321):

```csharp
    /// <summary>
    /// Main run loop - pumps the WPF message loop (or a plain poll loop if headless)
    /// </summary>
    public void Run()
    {
        if (State != PowerState.Running)
        {
            Console.WriteLine("Machine is not running. Call PowerOn() first.");
            return;
        }

        Console.WriteLine("Entering main run loop...");

        if (DisplayBackend != null)
        {
            DisplayBackend.RunMessageLoop(); // blocks until the window closes
        }
        else
        {
            while (State == PowerState.Running && !_stopRequested)
            {
                Display.Update();
                System.Threading.Thread.Sleep(16);
            }
        }

        Console.WriteLine("Exiting main run loop");
    }
```

- [ ] **Step 5: Update `Program.cs` call sites**

Add `[STAThread]` above `Main` (WPF requires an STA thread):

```csharp
    [STAThread]
    public static int Main(string[] args)
```

Replace the SDL2 references in `Run()` (around lines 293-326):

```csharp
        // Initialize WPF display backend unless headless
        if (!UsimState.Headless)
        {
            Console.WriteLine("Initializing WPF display backend...");
            _machine.InitializeDisplay(allowResize: false, scale: 1.0);

            if (_machine.DisplayBackend != null)
            {
                _machine.DisplayBackend.SetWindowTitle(UsimState.WindowTitle);
            }

            // Draw test pattern to show display is working
            _machine.Display.DrawTestPattern();
            _machine.Display.Update();
        }
        else
        {
            Console.WriteLine("Running in headless mode (no display)");
        }
```

and:

```csharp
        if (_machine.DisplayBackend != null)
        {
            Console.WriteLine("WPF window opened. Close window or press Ctrl+C to exit.");
            Console.WriteLine("Type commands in terminal for interactive debugging.\n");
```

Replace the SDL2 dispose call in `Shutdown()` (around line 395):

```csharp
            // Dispose display backend if initialized
            _machine.DisplayBackend?.Dispose();
```

Remove the `--test-sdl2` case (around lines 163-166):

```csharp
                case "--test-sdl2":
                    SDL2VerificationTest.RunTest();
                    Environment.Exit(0);
                    break;
```

Remove its `PrintUsage()` line (around line 205):

```csharp
        Console.WriteLine("  --test-sdl2             Test SDL2-CS .NET 8.0 integration");
```

- [ ] **Step 6: Build**

Run: `dotnet build LispMachine.sln`
Expected: Build succeeds, 0 errors. (Running with `--headless` should still work; running without it will throw `NotImplementedException` until Task 5 — that's expected at this checkpoint.)

- [ ] **Step 7: Commit**

```bash
git add usim-cs/Usim.csproj usim-cs/MachineControl.cs usim-cs/Program.cs usim-cs/WpfBackend.cs
git rm usim-cs/SDL2Backend.cs usim-cs/SDL2VerificationTest.cs
git commit -m "Retarget usim-cs to WPF and stub the WPF backend"
```

---

## Task 2: Key/modifier translation (`WpfKeyTranslator`)

**Files:**
- Create: `usim-cs/WpfKeyTranslator.cs`
- Create: `usim-cs/WpfBackendTests.cs`
- Modify: `usim-cs/Program.cs`

**Interfaces:**
- Consumes: nothing new (pure logic).
- Produces: `WpfKeyTranslator.TranslateKey(System.Windows.Input.Key key, bool shift) -> int` (X11-keysym-style code, 0 if unmapped), `WpfKeyTranslator.GetModifierState(System.Windows.Input.ModifierKeys modifiers, bool capsLock) -> int` (same bitmask scheme `SDL2Backend.GetModifierState` used: bit0=Shift, bit1=CapsLock, bit2=Control, bit3=Alt, bit6=Windows/Super). Task 5 consumes both.

- [ ] **Step 1: Write the failing tests**

Create `usim-cs/WpfBackendTests.cs`:

```csharp
// WpfBackendTests.cs - Tests for the WPF backend's pure-logic helpers
// (key translation, beep WAV generation) plus the Display/Mouse fixes
// made as part of the SDL2-to-WPF conversion.

using System;
using System.Text;
using System.Windows.Input;

namespace Usim;

public static class WpfBackendTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== WPF Backend Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestKeyTranslation()) passed++; else failed++;
        if (TestModifierState()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestKeyTranslation()
    {
        Console.WriteLine("Test: Key Translation");
        try
        {
            Assert(WpfKeyTranslator.TranslateKey(Key.Escape, shift: false) == 0xFF1B, "Escape -> XK_Escape");
            Assert(WpfKeyTranslator.TranslateKey(Key.F1, shift: false) == 0xFFBE, "F1 -> XK_F1");
            Assert(WpfKeyTranslator.TranslateKey(Key.A, shift: false) == 0x0061, "a (no shift) -> 'a'");
            Assert(WpfKeyTranslator.TranslateKey(Key.A, shift: true) == 0x0041, "a (shift) -> 'A'");
            Assert(WpfKeyTranslator.TranslateKey(Key.D1, shift: false) == 0x0031, "1 (no shift) -> '1'");
            Assert(WpfKeyTranslator.TranslateKey(Key.D1, shift: true) == 0x0021, "1 (shift) -> '!'");
            Assert(WpfKeyTranslator.TranslateKey(Key.Space, shift: false) == 0x0020, "Space -> 0x20");
            Assert(WpfKeyTranslator.TranslateKey(Key.Scroll, shift: false) == 0, "Unmapped key -> XK_VoidSymbol");

            Console.WriteLine("  Key Translation tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Key Translation tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestModifierState()
    {
        Console.WriteLine("Test: Modifier State");
        try
        {
            Assert(WpfKeyTranslator.GetModifierState(ModifierKeys.Shift, capsLock: false) == (1 << 0), "Shift bit");
            Assert(WpfKeyTranslator.GetModifierState(ModifierKeys.None, capsLock: true) == (1 << 1), "CapsLock bit");
            Assert(WpfKeyTranslator.GetModifierState(ModifierKeys.Control, capsLock: false) == (1 << 2), "Control bit");
            Assert(WpfKeyTranslator.GetModifierState(ModifierKeys.Alt, capsLock: false) == (1 << 3), "Alt bit");
            Assert(WpfKeyTranslator.GetModifierState(ModifierKeys.Windows, capsLock: false) == (1 << 6), "Windows bit");

            Console.WriteLine("  Modifier State tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Modifier State tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
```

- [ ] **Step 2: Wire a `--test-wpf` CLI flag**

In `usim-cs/Program.cs`, add a case next to `--test-config` (around line 146):

```csharp
                case "--test-wpf":
                    WpfBackendTests.RunAllTests();
                    Environment.Exit(0);
                    break;
```

Add it to the master `RunAllTests()` (around line 417):

```csharp
        // Run WPF backend tests
        Console.WriteLine("Running WPF Backend Tests...\n");
        WpfBackendTests.RunAllTests();
        Console.WriteLine();
```

Add a usage line next to `--test-config` (around line 204):

```csharp
        Console.WriteLine("  --test-wpf              Run WPF backend tests only");
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet build LispMachine.sln` — this is expected to **fail** with `CS0246: The type or namespace name 'WpfKeyTranslator' could not be found`, since the type doesn't exist yet.

- [ ] **Step 4: Implement `WpfKeyTranslator`**

Create `usim-cs/WpfKeyTranslator.cs`:

```csharp
// WpfKeyTranslator.cs - Translates WPF key events into the CADR keyboard's
// X11-keysym-style codes and modifier bitmask (same scheme SDL2Backend used).
//
// NOTE: System.Windows.Input.Keyboard (the static input-state helper) shares
// its simple name with Usim.Keyboard (this app's keyboard model). Any WPF
// caller of this class must fully qualify System.Windows.Input.Keyboard.

using System.Windows.Input;

namespace Usim;

public static class WpfKeyTranslator
{
    /// <summary>
    /// Translate a WPF Key into an X11 keysym-style code. Returns 0
    /// (XK_VoidSymbol) for keys with no mapping, matching
    /// SDL2Backend.TranslateKeycode's behavior.
    /// </summary>
    public static int TranslateKey(Key key, bool shift)
    {
        switch (key)
        {
            case Key.Escape: return 0xFF1B; // XK_Escape
            case Key.F1: return 0xFFBE;
            case Key.F2: return 0xFFBF;
            case Key.F3: return 0xFFC0;
            case Key.F4: return 0xFFC1;
            case Key.F5: return 0xFFC2;
            case Key.F6: return 0xFFC3;
            case Key.F7: return 0xFFC4;
            case Key.F8: return 0xFFC5;
            case Key.F9: return 0xFFC6;
            case Key.F10: return 0xFFC7;
            case Key.F11: return 0xFFC8;
            case Key.F12: return 0xFFC9;

            case Key.PageUp: return 0xFF55;
            case Key.PageDown: return 0xFF56;
            case Key.Home: return 0xFF50;
            case Key.End: return 0xFF57;
            case Key.Left: return 0xFF51;
            case Key.Right: return 0xFF53;
            case Key.Up: return 0xFF52;
            case Key.Down: return 0xFF54;

            case Key.Insert: return 0xFF63;
            case Key.Delete: return 0xFFFF;
            case Key.Back: return 0xFF08;
            case Key.Tab: return 0xFF09;
            case Key.Return: return 0xFF0D;
            case Key.Space: return 0x0020;

            case Key.LeftShift: return 0xFFE1;
            case Key.RightShift: return 0xFFE2;
            case Key.LeftCtrl: return 0xFFE3;
            case Key.RightCtrl: return 0xFFE4;
            case Key.LeftAlt: return 0xFFE9;
            case Key.RightAlt: return 0xFFEA;
            case Key.LWin: return 0xFFE7;
            case Key.RWin: return 0xFFE8;
            case Key.CapsLock: return 0xFFE5;

            case Key.OemTilde: return shift ? 0x007E : 0x0060;
            case Key.D1: return shift ? 0x0021 : 0x0031;
            case Key.D2: return shift ? 0x0040 : 0x0032;
            case Key.D3: return shift ? 0x0023 : 0x0033;
            case Key.D4: return shift ? 0x0024 : 0x0034;
            case Key.D5: return shift ? 0x0025 : 0x0035;
            case Key.D6: return shift ? 0x005E : 0x0036;
            case Key.D7: return shift ? 0x0026 : 0x0037;
            case Key.D8: return shift ? 0x002A : 0x0038;
            case Key.D9: return shift ? 0x0028 : 0x0039;
            case Key.D0: return shift ? 0x0029 : 0x0030;
            case Key.OemMinus: return shift ? 0x005F : 0x002D;
            case Key.OemPlus: return shift ? 0x002B : 0x003D;

            case Key.A: return shift ? 0x0041 : 0x0061;
            case Key.B: return shift ? 0x0042 : 0x0062;
            case Key.C: return shift ? 0x0043 : 0x0063;
            case Key.D: return shift ? 0x0044 : 0x0064;
            case Key.E: return shift ? 0x0045 : 0x0065;
            case Key.F: return shift ? 0x0046 : 0x0066;
            case Key.G: return shift ? 0x0047 : 0x0067;
            case Key.H: return shift ? 0x0048 : 0x0068;
            case Key.I: return shift ? 0x0049 : 0x0069;
            case Key.J: return shift ? 0x004A : 0x006A;
            case Key.K: return shift ? 0x004B : 0x006B;
            case Key.L: return shift ? 0x004C : 0x006C;
            case Key.M: return shift ? 0x004D : 0x006D;
            case Key.N: return shift ? 0x004E : 0x006E;
            case Key.O: return shift ? 0x004F : 0x006F;
            case Key.P: return shift ? 0x0050 : 0x0070;
            case Key.Q: return shift ? 0x0051 : 0x0071;
            case Key.R: return shift ? 0x0052 : 0x0072;
            case Key.S: return shift ? 0x0053 : 0x0073;
            case Key.T: return shift ? 0x0054 : 0x0074;
            case Key.U: return shift ? 0x0055 : 0x0075;
            case Key.V: return shift ? 0x0056 : 0x0076;
            case Key.W: return shift ? 0x0057 : 0x0077;
            case Key.X: return shift ? 0x0058 : 0x0078;
            case Key.Y: return shift ? 0x0059 : 0x0079;
            case Key.Z: return shift ? 0x005A : 0x007A;

            case Key.OemOpenBrackets: return shift ? 0x007B : 0x005B;
            case Key.OemCloseBrackets: return shift ? 0x007D : 0x005D;
            case Key.OemBackslash: return shift ? 0x007C : 0x005C;
            case Key.OemSemicolon: return shift ? 0x003A : 0x003B;
            case Key.OemQuotes: return shift ? 0x0022 : 0x0027;
            case Key.OemComma: return shift ? 0x003C : 0x002C;
            case Key.OemPeriod: return shift ? 0x003E : 0x002E;
            case Key.OemQuestion: return shift ? 0x003F : 0x002F;

            default: return 0; // XK_VoidSymbol
        }
    }

    /// <summary>
    /// Build the same modifier bitmask SDL2Backend.GetModifierState produced.
    /// </summary>
    public static int GetModifierState(ModifierKeys modifiers, bool capsLock)
    {
        int result = 0;

        if ((modifiers & ModifierKeys.Shift) != 0)
            result |= 1 << 0; // ShiftMapIndex
        if (capsLock)
            result |= 1 << 1; // LockMapIndex
        if ((modifiers & ModifierKeys.Control) != 0)
            result |= 1 << 2; // ControlMapIndex
        if ((modifiers & ModifierKeys.Alt) != 0)
            result |= 1 << 3; // Mod1MapIndex
        if ((modifiers & ModifierKeys.Windows) != 0)
            result |= 1 << 6; // Mod4MapIndex

        return result;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet run --project usim-cs -- --test-wpf`
Expected: `Passed: 2`, `Failed: 0`.

- [ ] **Step 6: Commit**

```bash
git add usim-cs/WpfKeyTranslator.cs usim-cs/WpfBackendTests.cs usim-cs/Program.cs
git commit -m "Add WpfKeyTranslator for WPF key/modifier translation"
```

---

## Task 3: Beep WAV generation (`BeepWavBuilder`)

**Files:**
- Create: `usim-cs/BeepWavBuilder.cs`
- Modify: `usim-cs/WpfBackendTests.cs`

**Interfaces:**
- Produces: `BeepWavBuilder.SampleRate` (const int, 44100), `BeepWavBuilder.BuildWav(int halfWavelengthMicros, int durationMicros) -> byte[]` (a complete little-endian PCM WAV file). Task 5 consumes this from `WpfBackend.Beep`.

- [ ] **Step 1: Write the failing test**

In `usim-cs/WpfBackendTests.cs`, add a test method (after `TestModifierState`):

```csharp
    private static bool TestBeepWavBuilder()
    {
        Console.WriteLine("Test: Beep WAV Builder");
        try
        {
            // half-wavelength 500us -> 1000 Hz; 100ms duration -> 4410 samples
            byte[] wav = BeepWavBuilder.BuildWav(halfWavelengthMicros: 500, durationMicros: 100_000);

            int expectedSamples = (int)(BeepWavBuilder.SampleRate * 0.1);
            int expectedLength = 44 + expectedSamples * 2;

            Assert(wav.Length == expectedLength, $"WAV length is {expectedLength} bytes");
            Assert(Encoding.ASCII.GetString(wav, 0, 4) == "RIFF", "RIFF header");
            Assert(Encoding.ASCII.GetString(wav, 8, 4) == "WAVE", "WAVE header");
            Assert(Encoding.ASCII.GetString(wav, 36, 4) == "data", "data chunk header");

            Console.WriteLine("  Beep WAV Builder tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Beep WAV Builder tests failed: {ex.Message}\n");
            return false;
        }
    }
```

Add it to `RunAllTests()`:

```csharp
        if (TestBeepWavBuilder()) passed++; else failed++;
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build LispMachine.sln` — expected to **fail** with `CS0246: The type or namespace name 'BeepWavBuilder' could not be found`.

- [ ] **Step 3: Implement `BeepWavBuilder`**

Create `usim-cs/BeepWavBuilder.cs`:

```csharp
// BeepWavBuilder.cs - Builds an in-memory WAV (PCM) buffer for the CADR's
// beep tone, for playback via System.Media.SoundPlayer under WPF.

using System;
using System.IO;
using System.Text;

namespace Usim;

public static class BeepWavBuilder
{
    public const int SampleRate = 44100;
    private const short Amplitude = 28000;

    /// <summary>
    /// Build a mono 16-bit PCM WAV buffer containing a sine wave at the
    /// frequency implied by halfWavelengthMicros, for durationMicros.
    /// Matches the waveform formula SDL2Backend.AudioCallback used.
    /// </summary>
    public static byte[] BuildWav(int halfWavelengthMicros, int durationMicros)
    {
        double frequency = 1_000_000.0 / (halfWavelengthMicros * 2);
        int sampleCount = (int)(SampleRate * (durationMicros / 1_000_000.0));

        var pcm = new short[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            double time = i / (double)SampleRate;
            pcm[i] = (short)(Amplitude * Math.Sin(2.0 * Math.PI * frequency * time));
        }

        return WrapPcmInWavContainer(pcm);
    }

    private static byte[] WrapPcmInWavContainer(short[] pcm)
    {
        int dataSize = pcm.Length * 2;
        int fileSize = 36 + dataSize;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(fileSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // fmt chunk size
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2); // byte rate = SampleRate * block align
        writer.Write((short)2); // block align (16-bit mono)
        writer.Write((short)16); // bits per sample
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        foreach (short sample in pcm)
            writer.Write(sample);

        writer.Flush();
        return stream.ToArray();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project usim-cs -- --test-wpf`
Expected: `Passed: 3`, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/BeepWavBuilder.cs usim-cs/WpfBackendTests.cs
git commit -m "Add BeepWavBuilder for in-memory beep WAV generation"
```

---

## Task 4: Fix `Display` frame-buffer byte order and `Mouse` default bounds

**Files:**
- Modify: `usim-cs/Display.cs`
- Modify: `usim-cs/Mouse.cs`
- Modify: `usim-cs/WpfBackendTests.cs`

**Interfaces:**
- Consumes: none new.
- Produces: `Display.FrameBuffer` now holds bytes in B,G,R,A order per pixel (was A,R,G,B) — Task 5's `WpfBackend` writes this buffer straight into a `PixelFormats.Pbgra32` `WriteableBitmap` with no conversion. `Mouse.MaxX`/`Mouse.MaxY` now default to `Display.WIDTH`/`Display.HEIGHT` (was 1024/768) — Task 5's mouse handling relies on this to match the `Image` control's logical pixel space.

- [ ] **Step 1: Write the failing tests**

In `usim-cs/WpfBackendTests.cs`, add two test methods (after `TestBeepWavBuilder`):

```csharp
    private static bool TestDisplayByteOrder()
    {
        Console.WriteLine("Test: Display Frame Buffer Byte Order");
        try
        {
            var display = new Display { Mode = DisplayMode.Color };
            // Pixel (0,0) = color index 1 (Blue: R=0, G=0, B=170) in the
            // top 4 bits of the first video memory word.
            display.WriteVideoMemory(display.VideoMemoryBase, 0x10000000u);
            display.Update();

            byte b = display.FrameBuffer[0];
            byte g = display.FrameBuffer[1];
            byte r = display.FrameBuffer[2];
            byte a = display.FrameBuffer[3];

            Assert(b == 170 && g == 0 && r == 0 && a == 255,
                $"Frame buffer is B,G,R,A order (got B={b},G={g},R={r},A={a})");

            Console.WriteLine("  Display Frame Buffer Byte Order tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Display Frame Buffer Byte Order tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMouseDefaults()
    {
        Console.WriteLine("Test: Mouse Default Bounds");
        try
        {
            var mouse = new Mouse();
            Assert(mouse.MaxX == Display.WIDTH, "MaxX defaults to Display.WIDTH");
            Assert(mouse.MaxY == Display.HEIGHT, "MaxY defaults to Display.HEIGHT");

            Console.WriteLine("  Mouse Default Bounds tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Mouse Default Bounds tests failed: {ex.Message}\n");
            return false;
        }
    }
```

Add both to `RunAllTests()`:

```csharp
        if (TestDisplayByteOrder()) passed++; else failed++;
        if (TestMouseDefaults()) passed++; else failed++;
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet run --project usim-cs -- --test-wpf`
Expected: `Failed: 2` — `TestDisplayByteOrder` fails because the buffer is still A,R,G,B (byte 0 is currently 255, not 170); `TestMouseDefaults` fails because `MaxX`/`MaxY` are still 1024/768.

- [ ] **Step 3: Fix `Display.cs` byte order**

In `usim-cs/Display.cs`, replace the pixel-writing lines in `RenderBlackAndWhite` (around lines 164-167):

```csharp
                    FrameBuffer[bufferIndex++] = value; // B
                    FrameBuffer[bufferIndex++] = value; // G
                    FrameBuffer[bufferIndex++] = value; // R
                    FrameBuffer[bufferIndex++] = 255;   // A
```

And in `RenderColor` (around lines 203-206):

```csharp
                    FrameBuffer[bufferIndex++] = b;
                    FrameBuffer[bufferIndex++] = g;
                    FrameBuffer[bufferIndex++] = r;
                    FrameBuffer[bufferIndex++] = 255; // A
```

Update the doc comments above both methods (around lines 139-143 and 173-177) from "SDL2 expects ARGB8888 format" to "WPF's WriteableBitmap expects Pbgra32 (B,G,R,A byte order)".

- [ ] **Step 4: Fix `Mouse.cs` default bounds**

In `usim-cs/Mouse.cs`, replace the `MaxX`/`MaxY` property declarations (around lines 38-39):

```csharp
    public int MaxX { get; set; } = Display.WIDTH;
    public int MaxY { get; set; } = Display.HEIGHT;
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet run --project usim-cs -- --test-wpf`
Expected: `Passed: 5`, `Failed: 0`.

- [ ] **Step 6: Run the full test suite as a regression check**

Run: `dotnet run --project usim-cs -- --test-all`
Expected: all of `UCodeTests`, `ConfigTests`, and `WpfBackendTests` report `Failed: 0` (microcode/config tests are display-independent and should be unaffected).

- [ ] **Step 7: Commit**

```bash
git add usim-cs/Display.cs usim-cs/Mouse.cs usim-cs/WpfBackendTests.cs
git commit -m "Fix Display frame buffer byte order and Mouse default bounds for WPF"
```

---

## Task 5: Implement `WpfBackend` and verify end-to-end

This replaces the Task 1 stub with the real window/render/input/audio implementation, and is verified manually (per the spec, there are no existing automated tests for the SDL backend to port, and a live window can't be meaningfully unit-tested).

**Files:**
- Modify: `usim-cs/WpfBackend.cs`

**Interfaces:**
- Consumes: `WpfKeyTranslator.TranslateKey`/`GetModifierState` (Task 2), `BeepWavBuilder.BuildWav` (Task 3), `Display.FrameBuffer` in B,G,R,A order and `Mouse.MaxX/MaxY == Display.WIDTH/HEIGHT` (Task 4), `Keyboard.KeyDown/KeyUp(uint, int)` and `Mouse.UpdatePosition/ButtonDown/ButtonUp` (unchanged).
- Produces: the same public shape declared in Task 1 (no signature changes), now fully implemented.

- [ ] **Step 1: Implement `WpfBackend`**

Replace the full contents of `usim-cs/WpfBackend.cs`:

```csharp
// WpfBackend.cs - WPF video, keyboard, mouse, and beep backend
// Replaces SDL2Backend.cs
//
// NOTE: System.Windows.Input.Keyboard (the static input-state helper) shares
// its simple name with Usim.Keyboard (this app's keyboard model), so it is
// always referenced fully-qualified below.

using System;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Usim;

/// <summary>
/// WPF backend for video rendering, input handling, and beep audio.
/// </summary>
public class WpfBackend : IDisposable
{
    private readonly Display _display;
    private readonly Keyboard _keyboard;
    private readonly Mouse _mouse;
    private readonly Action _onTick;

    private Application? _application;
    private Window? _window;
    private Image? _image;
    private WriteableBitmap? _bitmap;
    private DispatcherTimer? _timer;
    private string _windowTitle = "USIM - Lisp Machine Emulator";

    public double Scale { get; set; } = 1.0;
    public bool AllowResize { get; set; } = false;
    public bool UseLinearFiltering { get; set; } = true;
    public bool IsRunning { get; private set; }

    public WpfBackend(Display display, Keyboard keyboard, Mouse mouse, Action onTick)
    {
        _display = display ?? throw new ArgumentNullException(nameof(display));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _mouse = mouse ?? throw new ArgumentNullException(nameof(mouse));
        _onTick = onTick ?? throw new ArgumentNullException(nameof(onTick));
    }

    /// <summary>
    /// Create the window, bitmap, and timer, and show the window.
    /// </summary>
    public void Initialize()
    {
        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Info, "Initializing WPF backend");

        _application = Application.Current ?? new Application();

        _bitmap = new WriteableBitmap(Display.WIDTH, Display.HEIGHT, 96, 96, PixelFormats.Pbgra32, null);

        _image = new Image
        {
            Source = _bitmap,
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true,
            Width = Display.WIDTH,
            Height = Display.HEIGHT
        };
        RenderOptions.SetBitmapScalingMode(_image,
            UseLinearFiltering ? BitmapScalingMode.Linear : BitmapScalingMode.NearestNeighbor);

        var viewbox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = _image
        };

        _window = new Window
        {
            Title = _windowTitle,
            Content = viewbox,
            Width = Display.WIDTH * Scale,
            Height = Display.HEIGHT * Scale,
            ResizeMode = AllowResize ? ResizeMode.CanResize : ResizeMode.CanMinimize,
            Cursor = Cursors.None
        };

        _window.KeyDown += (_, e) => HandleKeyEvent(e, keyDown: true);
        _window.KeyUp += (_, e) => HandleKeyEvent(e, keyDown: false);
        _image.MouseMove += OnMouseMove;
        _image.MouseDown += OnMouseButton;
        _image.MouseUp += OnMouseButton;
        _window.Closed += (_, _) => IsRunning = false;

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        _window.Show();
        IsRunning = true;

        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Info, "WPF backend initialized successfully");
    }

    /// <summary>
    /// Pump the WPF message loop. Blocks until the window closes.
    /// </summary>
    public void RunMessageLoop()
    {
        _application!.Run(_window);
    }

    /// <summary>
    /// Close the window from any thread, ending RunMessageLoop().
    /// </summary>
    public void RequestExit()
    {
        if (_window == null)
            return;

        if (_window.Dispatcher.CheckAccess())
            _window.Close();
        else
            _window.Dispatcher.BeginInvoke(new Action(() => _window.Close()));
    }

    private void Tick()
    {
        _onTick();
        _display.Update();
        UpdateBitmap();
    }

    private void UpdateBitmap()
    {
        if (_bitmap == null)
            return;

        var rect = new Int32Rect(0, 0, Display.WIDTH, Display.HEIGHT);
        _bitmap.WritePixels(rect, _display.FrameBuffer, Display.WIDTH * 4, 0);
    }

    private void HandleKeyEvent(KeyEventArgs e, bool keyDown)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool shift = System.Windows.Input.Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        int keysym = WpfKeyTranslator.TranslateKey(key, shift);
        if (keysym == 0)
        {
            TraceLog.Instance.Trace(TraceCategory.Keyboard, TraceLevel.Debug,
                $"Unable to translate WPF key: {key}");
            return;
        }

        bool capsLock = System.Windows.Input.Keyboard.IsKeyToggled(Key.CapsLock);
        int modifiers = WpfKeyTranslator.GetModifierState(System.Windows.Input.Keyboard.Modifiers, capsLock);

        if (keyDown)
            _keyboard.KeyDown((uint)keysym, modifiers);
        else
            _keyboard.KeyUp((uint)keysym, modifiers);

        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        Point pos = e.GetPosition(_image);
        _mouse.UpdatePosition((int)pos.X, (int)pos.Y);
    }

    private void OnMouseButton(object sender, MouseButtonEventArgs e)
    {
        MouseButtons button = e.ChangedButton switch
        {
            System.Windows.Input.MouseButton.Left => MouseButtons.Left,
            System.Windows.Input.MouseButton.Middle => MouseButtons.Middle,
            System.Windows.Input.MouseButton.Right => MouseButtons.Right,
            _ => MouseButtons.None
        };

        if (button == MouseButtons.None)
            return;

        if (e.ButtonState == MouseButtonState.Pressed)
            _mouse.ButtonDown(button);
        else
            _mouse.ButtonUp(button);

        Point pos = e.GetPosition(_image);
        _mouse.UpdatePosition((int)pos.X, (int)pos.Y);
    }

    public void SetWindowTitle(string title)
    {
        _windowTitle = title ?? "USIM";
        if (_window != null)
            _window.Title = _windowTitle;
    }

    public void Beep(int halfWavelengthMicros, int durationMicros)
    {
        // PlaySync (not Play) so the SoundPlayer/stream aren't disposed
        // before playback finishes; this also matches SDL2Backend.Beep's
        // original behavior of blocking the caller for the duration.
        byte[] wav = BeepWavBuilder.BuildWav(halfWavelengthMicros, durationMicros);
        using var stream = new MemoryStream(wav);
        using var player = new SoundPlayer(stream);
        player.PlaySync();
    }

    public void Dispose()
    {
        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Info, "Shutting down WPF backend");

        _timer?.Stop();
        _window?.Close();
        IsRunning = false;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build LispMachine.sln`
Expected: Build succeeds, 0 errors.

- [ ] **Step 3: Run the automated test suites as a regression check**

Run: `dotnet run --project usim-cs -- --test-all`
Expected: `UCodeTests`, `ConfigTests`, and `WpfBackendTests` all report `Failed: 0`.

- [ ] **Step 4: Manual smoke test**

Run: `dotnet run --project usim-cs`

Verify, in order:
1. A window opens titled with the configured window title, showing the black-and-white checkerboard test pattern.
2. Resizing the window preserves the display's aspect ratio (letterboxing, no stretching/distortion).
3. Pressing keys (letters, shift+letters, arrow keys) produces `TraceLog` debug output (run with tracing enabled for the `Keyboard` category, or check via `--test-wpf`-style visibility) confirming keysyms/modifiers are received.
4. Moving the mouse and clicking each button over the window produces the corresponding `Mouse` trace output.
5. Triggering a beep (e.g. via a debug command that calls `MachineControl.DisplayBackend.Beep(...)`, or a temporary manual call) plays an audible tone.
6. Typing `quit` in the console window closes the WPF window and the process exits cleanly (this exercises the `MachineControl.Shutdown()` → `WpfBackend.RequestExit()` path added in Task 1).
7. Closing the WPF window directly (titlebar close button) also exits the process cleanly.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/WpfBackend.cs
git commit -m "Implement WpfBackend: window, rendering, input, and beep audio"
```

---

## Self-Review Notes

- **Spec coverage:** Full replacement (Task 1), single-threaded `DispatcherTimer` loop (Task 5's `Tick()`, no locking anywhere), `SoundPlayer`-based beep (Task 3 + Task 5's `Beep`), key/mouse translation (Task 2, Task 5), `Display`/`Mouse` fixes (Task 4), project retarget (Task 1), renames (Task 1), deletions (Task 1) — all spec sections are covered.
- **Placeholder scan:** No TBD/TODO markers; every step has literal code or an exact command.
- **Type consistency:** `WpfBackend`'s constructor and public members are declared identically in Task 1's stub and Task 5's real implementation. `WpfKeyTranslator.TranslateKey`/`GetModifierState` signatures match between Task 2's definition and Task 5's call sites. `BeepWavBuilder.BuildWav`/`SampleRate` match between Task 3 and Task 5.
- **Gap found and fixed during review (1):** the original `MachineControl.Shutdown()` (called from the console debug-command thread when the user types `quit`) relied on the old polling loop noticing `_stopRequested`; the new `Application.Run`-based loop wouldn't. Task 1 adds `WpfBackend.RequestExit()` (thread-safe via `Dispatcher.BeginInvoke`) and wires it into `Shutdown()` so `quit` still closes the window and exits the process.
- **Gap found and fixed during review (2):** Task 5's `Beep` originally called `SoundPlayer.Play()` (asynchronous) inside a `using` block, which would dispose the player/stream while the background playback thread might still be reading them. Changed to `PlaySync()`, which also matches `SDL2Backend.Beep`'s original behavior of blocking the caller for the beep's duration.
