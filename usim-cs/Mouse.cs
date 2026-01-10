// Mouse.cs - Mouse input handling
// Converted from mouse.h and mouse.c

using System;

namespace Usim;

/// <summary>
/// Mouse button states
/// </summary>
[Flags]
public enum MouseButtons
{
    None = 0,
    Left = 1,
    Middle = 2,
    Right = 4
}

/// <summary>
/// Mouse state and input handling
/// </summary>
public class Mouse
{
    // Mouse position
    public int X { get; private set; }
    public int Y { get; private set; }
    
    // Previous position (for delta calculation)
    private int _lastX;
    private int _lastY;
    
    // Button states
    public MouseButtons Buttons { get; private set; }
    private MouseButtons _lastButtons;
    
    // Display bounds
    public int MaxX { get; set; } = 1024;
    public int MaxY { get; set; } = 768;
    
    // Raw hardware registers (CADR mouse interface)
    public ushort MouseX { get; private set; }
    public ushort MouseY { get; private set; }
    public ushort MouseButtonBits { get; private set; }
    
    // Statistics
    public ulong MoveCount { get; private set; }
    public ulong ClickCount { get; private set; }
    
    // Events
    public event Action<int, int>? Moved;
    public event Action<MouseButtons>? ButtonPressed;
    public event Action<MouseButtons>? ButtonReleased;
    
    public Mouse()
    {
        Initialize();
    }
    
    /// <summary>
    /// Initialize mouse
    /// </summary>
    public void Initialize()
    {
        X = MaxX / 2;
        Y = MaxY / 2;
        _lastX = X;
        _lastY = Y;
        Buttons = MouseButtons.None;
        _lastButtons = MouseButtons.None;
        
        UpdateHardwareRegisters();
        
        MoveCount = 0;
        ClickCount = 0;
    }
    
    /// <summary>
    /// Move mouse to absolute position
    /// </summary>
    public void MoveTo(int x, int y)
    {
        _lastX = X;
        _lastY = Y;
        
        X = Math.Clamp(x, 0, MaxX - 1);
        Y = Math.Clamp(y, 0, MaxY - 1);
        
        if (X != _lastX || Y != _lastY)
        {
            MoveCount++;
            UpdateHardwareRegisters();
            Moved?.Invoke(X, Y);
        }
    }
    
    /// <summary>
    /// Move mouse by relative amount
    /// </summary>
    public void MoveBy(int dx, int dy)
    {
        MoveTo(X + dx, Y + dy);
    }
    
    /// <summary>
    /// Set button state
    /// </summary>
    public void SetButton(MouseButtons button, bool pressed)
    {
        _lastButtons = Buttons;
        
        if (pressed)
        {
            Buttons |= button;
            if ((Buttons & button) != (_lastButtons & button))
            {
                ClickCount++;
                ButtonPressed?.Invoke(button);
            }
        }
        else
        {
            Buttons &= ~button;
            if ((Buttons & button) != (_lastButtons & button))
            {
                ButtonReleased?.Invoke(button);
            }
        }
        
        UpdateHardwareRegisters();
    }
    
    /// <summary>
    /// Update hardware mouse registers
    /// CADR mouse interface uses 11-bit coordinates
    /// </summary>
    private void UpdateHardwareRegisters()
    {
        // Scale coordinates to 11-bit range (0-2047)
        MouseX = (ushort)((X * 2048) / MaxX);
        MouseY = (ushort)((Y * 2048) / MaxY);
        
        // Encode buttons in lower bits
        ushort buttonBits = 0;
        if ((Buttons & MouseButtons.Left) != 0)
            buttonBits |= 0x01;
        if ((Buttons & MouseButtons.Middle) != 0)
            buttonBits |= 0x02;
        if ((Buttons & MouseButtons.Right) != 0)
            buttonBits |= 0x04;
        
        MouseButtonBits = buttonBits;
    }
    
    /// <summary>
    /// Read mouse X register (for hardware emulation)
    /// </summary>
    public ushort ReadXRegister()
    {
        return MouseX;
    }
    
    /// <summary>
    /// Read mouse Y register (for hardware emulation)
    /// </summary>
    public ushort ReadYRegister()
    {
        return MouseY;
    }
    
    /// <summary>
    /// Read mouse buttons register (for hardware emulation)
    /// </summary>
    public ushort ReadButtonsRegister()
    {
        return MouseButtonBits;
    }
    
    /// <summary>
    /// Check if button is pressed
    /// </summary>
    public bool IsButtonPressed(MouseButtons button)
    {
        return (Buttons & button) != 0;
    }
    
    /// <summary>
    /// Get mouse delta since last read
    /// </summary>
    public (int dx, int dy) GetDelta()
    {
        int dx = X - _lastX;
        int dy = Y - _lastY;
        _lastX = X;
        _lastY = Y;
        return (dx, dy);
    }
    
    /// <summary>
    /// Update position from SDL2 event
    /// </summary>
    public void UpdatePosition(int x, int y)
    {
        MoveTo(x, y);
        TraceLog.Instance.Trace(TraceCategory.Mouse, TraceLevel.Verbose,
            $"Mouse moved to ({x}, {y})");
    }
    
    /// <summary>
    /// Handle button down event
    /// </summary>
    public void ButtonDown(MouseButtons button)
    {
        SetButton(button, true);
        TraceLog.Instance.Trace(TraceCategory.Mouse, TraceLevel.Debug,
            $"Mouse button down: {button}");
    }
    
    /// <summary>
    /// Handle button up event
    /// </summary>
    public void ButtonUp(MouseButtons button)
    {
        SetButton(button, false);
        TraceLog.Instance.Trace(TraceCategory.Mouse, TraceLevel.Debug,
            $"Mouse button up: {button}");
    }
}
