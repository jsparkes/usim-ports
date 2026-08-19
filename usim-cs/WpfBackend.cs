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
