// Keyboard.cs - Keyboard input handling
// Converted from kbd.h and kbd.c

using System;
using System.Collections.Generic;

namespace Usim;

/// <summary>
/// Keyboard key codes (CADR keycodes)
/// </summary>
public enum CadrKeyCode
{
    // Special keys
    KEY_BREAK = 1,         // 0o001
    KEY_CLEAR = 2,         // 0o002
    KEY_CALL = 3,          // 0o003
    KEY_TERMINAL = 4,      // 0o004
    KEY_MACRO = 5,         // 0o005
    KEY_HELP = 6,          // 0o006
    KEY_RUBOUT = 7,        // 0o007
    KEY_OVERSTRIKE = 8,    // 0o010
    KEY_TAB = 9,           // 0o011
    KEY_LINE = 10,         // 0o012
    KEY_DELETE = 11,       // 0o013
    KEY_VT = 12,           // 0o014
    KEY_FORM = 13,         // 0o015
    KEY_RETURN = 14,       // 0o016
    
    // Control characters
    KEY_QUOTE = 17,        // 0o021
    KEY_HOLD_OUTPUT = 18,  // 0o022
    KEY_STOP_OUTPUT = 19,  // 0o023
    KEY_ABORT = 20,        // 0o024
    KEY_RESUME = 21,       // 0o025
    KEY_STATUS = 22,       // 0o026
    KEY_END = 23,          // 0o027
    KEY_ROMAN_I = 24,      // 0o030
    KEY_ROMAN_II = 25,     // 0o031
    KEY_ROMAN_III = 26,    // 0o032
    KEY_ROMAN_IV = 27,     // 0o033
    
    // Function keys
    KEY_SYSTEM = 28,       // 0o034
    KEY_NETWORK = 29,      // 0o035
    KEY_ESCAPE = 30,       // 0o036
    KEY_FUNCTION = 31,     // 0o037
    
    // Shift keys (not actual keys, modifiers)
    KEY_SHIFT = 128,       // 0o200
    KEY_TOP = 256,         // 0o400
    KEY_CONTROL = 512,     // 0o1000
    KEY_META = 1024,       // 0o2000
    KEY_SUPER = 2048,      // 0o4000
    KEY_HYPER = 4096       // 0o10000
}

/// <summary>
/// Keyboard state and input handling
/// </summary>
public class Keyboard
{
    // Keyboard buffer
    private readonly Queue<ushort> _keyBuffer = new();
    private const int MAX_BUFFER_SIZE = 256;
    
    // Modifier state
    private bool _shiftPressed;
    private bool _controlPressed;
    private bool _metaPressed;
    private bool _superPressed;
    private bool _hyperPressed;
    private bool _topPressed;
    
    // Statistics
    public ulong KeysPressed { get; private set; }
    public ulong KeysOverflow { get; private set; }
    
    // Event for key presses (for debugging)
    public event Action<ushort>? KeyPressed;
    
    public Keyboard()
    {
        Initialize();
    }
    
    /// <summary>
    /// Initialize keyboard
    /// </summary>
    public void Initialize()
    {
        _keyBuffer.Clear();
        _shiftPressed = false;
        _controlPressed = false;
        _metaPressed = false;
        _superPressed = false;
        _hyperPressed = false;
        _topPressed = false;
        KeysPressed = 0;
        KeysOverflow = 0;
    }
    
    /// <summary>
    /// Add key to buffer
    /// </summary>
    public void AddKey(ushort keycode)
    {
        if (_keyBuffer.Count >= MAX_BUFFER_SIZE)
        {
            KeysOverflow++;
            return;
        }
        
        // Apply modifiers
        ushort modifiedKey = keycode;
        
        if (_shiftPressed)
            modifiedKey |= (ushort)CadrKeyCode.KEY_SHIFT;
        if (_topPressed)
            modifiedKey |= (ushort)CadrKeyCode.KEY_TOP;
        if (_controlPressed)
            modifiedKey |= (ushort)CadrKeyCode.KEY_CONTROL;
        if (_metaPressed)
            modifiedKey |= (ushort)CadrKeyCode.KEY_META;
        if (_superPressed)
            modifiedKey |= (ushort)CadrKeyCode.KEY_SUPER;
        if (_hyperPressed)
            modifiedKey |= (ushort)CadrKeyCode.KEY_HYPER;
        
        _keyBuffer.Enqueue(modifiedKey);
        KeysPressed++;
        
        KeyPressed?.Invoke(modifiedKey);
    }
    
    /// <summary>
    /// Add character key (ASCII)
    /// </summary>
    public void AddChar(char ch)
    {
        ushort keycode = (ushort)ch;
        AddKey(keycode);
    }
    
    /// <summary>
    /// Set modifier state
    /// </summary>
    public void SetModifier(CadrKeyCode modifier, bool pressed)
    {
        switch (modifier)
        {
            case CadrKeyCode.KEY_SHIFT:
                _shiftPressed = pressed;
                break;
            case CadrKeyCode.KEY_CONTROL:
                _controlPressed = pressed;
                break;
            case CadrKeyCode.KEY_META:
                _metaPressed = pressed;
                break;
            case CadrKeyCode.KEY_SUPER:
                _superPressed = pressed;
                break;
            case CadrKeyCode.KEY_HYPER:
                _hyperPressed = pressed;
                break;
            case CadrKeyCode.KEY_TOP:
                _topPressed = pressed;
                break;
        }
    }
    
    /// <summary>
    /// Get next key from buffer
    /// </summary>
    public ushort? GetKey()
    {
        if (_keyBuffer.Count > 0)
        {
            return _keyBuffer.Dequeue();
        }
        return null;
    }
    
    /// <summary>
    /// Check if keys are available
    /// </summary>
    public bool HasKeys()
    {
        return _keyBuffer.Count > 0;
    }
    
    /// <summary>
    /// Get buffer count
    /// </summary>
    public int BufferCount => _keyBuffer.Count;
    
    /// <summary>
    /// Clear buffer
    /// </summary>
    public void ClearBuffer()
    {
        _keyBuffer.Clear();
    }
    
    /// <summary>
    /// Handle key down event (from SDL2 or other input)
    /// </summary>
    public void KeyDown(uint keysym, int modifiers)
    {
        // Update modifier state based on modifiers mask
        _shiftPressed = (modifiers & (1 << 0)) != 0;
        _controlPressed = (modifiers & (1 << 2)) != 0;
        _metaPressed = (modifiers & (1 << 3)) != 0;
        _superPressed = (modifiers & (1 << 6)) != 0;
        
        // Add key to buffer
        AddKey((ushort)(keysym & 0xFFFF));
        
        TraceLog.Instance.Trace(TraceCategory.Keyboard, TraceLevel.Debug,
            $"KeyDown: keysym=0x{keysym:X4}, modifiers=0x{modifiers:X}");
    }
    
    /// <summary>
    /// Handle key up event (from SDL2 or other input)
    /// </summary>
    public void KeyUp(uint keysym, int modifiers)
    {
        // Update modifier state
        _shiftPressed = (modifiers & (1 << 0)) != 0;
        _controlPressed = (modifiers & (1 << 2)) != 0;
        _metaPressed = (modifiers & (1 << 3)) != 0;
        _superPressed = (modifiers & (1 << 6)) != 0;
        
        TraceLog.Instance.Trace(TraceCategory.Keyboard, TraceLevel.Debug,
            $"KeyUp: keysym=0x{keysym:X4}, modifiers=0x{modifiers:X}");
    }
    
    /// <summary>
    /// Process keyboard events (called from main loop)
    /// </summary>
    public void ProcessEvents()
    {
        // This is called from SDL2Backend.ProcessEvents()
        // Currently just a placeholder for any periodic processing
    }
}
