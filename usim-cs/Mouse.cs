// Mouse.cs - Port of the real SDL3 mouse path (usim/sdl3-mouse.c):
// position is delivered by writing fixnum x/y straight into microcode
// A-memory (A-MOUSE-X/A-MOUSE-Y, plus A-MOUSE-WAKEUP = A-V-TRUE), located by
// name in the loaded microcode symbols; the IOB X/Y registers are always
// sent as 0 (the microcode treats them as relative counters) and only the
// button levels travel through iob_set_mouse_ready. See
// docs/superpowers/specs/2026-09-18-iob-design.md.

using System;

namespace Usim;

[Flags]
public enum MouseButtons
{
    None = 0,
    Left = 1,
    Middle = 2,
    Right = 4
}

public class Mouse
{
    public int X { get; private set; }
    public int Y { get; private set; }

    private int _lastX;
    private int _lastY;

    public MouseButtons Buttons { get; private set; }
    private MouseButtons _lastButtons;

    // Display bounds -- hardcoded to the other monitor's real dimensions (the
    // actual real-system default monitor type) as a reasonable standalone
    // default; MachineControl overrides both to the actual configured Tv's
    // dimensions once both exist (see the monochrome TV plan's Global
    // Constraints for why Mouse itself must not depend on Tv).
    public int MaxX { get; set; } = 768;
    public int MaxY { get; set; } = 963;

    private Iob? _iob;
    private UCode? _ucode;

    // Lazily resolved A-memory addresses of the microcode variables the
    // SDL3 path injects into (sdl3_mouse_init's INTERN() calls), cached per
    // symbol table instance.
    private SymbolTable? _resolvedFor;
    private bool _symbolsOk;
    private bool _warnedMissingSymbols;
    private uint _aMouseX, _aMouseY, _aMouseWakeup, _aVTrue;

    // DTP_FIX from sdl3-mouse.c: (x & ((1 << 24) - 1)) | (5 << 25).
    private static uint DtpFix(int x) => ((uint)x & ((1u << 24) - 1)) | (5u << 25);


    public ulong MoveCount { get; private set; }
    public ulong ClickCount { get; private set; }

    public event Action<int, int>? Moved;
    public event Action<MouseButtons>? ButtonPressed;
    public event Action<MouseButtons>? ButtonReleased;

    public Mouse()
    {
        Initialize();
    }

    public void WireIob(Iob iob)
    {
        _iob = iob;
    }

    /// <summary>
    /// Give the mouse access to the microcode's A-memory for position
    /// injection (sdl3-mouse.c writes amem[] directly).
    /// </summary>
    public void WireMicrocode(UCode ucode)
    {
        _ucode = ucode;
    }

    public void Initialize()
    {
        X = MaxX / 2;
        Y = MaxY / 2;
        _lastX = X;
        _lastY = Y;
        Buttons = MouseButtons.None;
        _lastButtons = MouseButtons.None;
        MoveCount = 0;
        ClickCount = 0;
    }

    public void MoveTo(int x, int y)
    {
        _lastX = X;
        _lastY = Y;

        X = Math.Clamp(x, 0, MaxX - 1);
        Y = Math.Clamp(y, 0, MaxY - 1);

        if (X != _lastX || Y != _lastY)
        {
            MoveCount++;
            Moved?.Invoke(X, Y);
        }

        ReportToIob();
    }

    public void MoveBy(int dx, int dy)
    {
        MoveTo(X + dx, Y + dy);
    }

    /// <summary>
    /// Track a button's held state. Level-based (1 = held): the held state is
    /// reported on every update, so a redundant press/release is harmless.
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

        ReportToIob();
    }

    public bool IsButtonPressed(MouseButtons button)
    {
        return (Buttons & button) != 0;
    }

    public (int dx, int dy) GetDelta()
    {
        int dx = X - _lastX;
        int dy = Y - _lastY;
        _lastX = X;
        _lastY = Y;
        return (dx, dy);
    }

    public void UpdatePosition(int x, int y)
    {
        MoveTo(x, y);
        TraceLog.Instance.Trace(TraceCategory.Mouse, TraceLevel.Verbose,
            $"Mouse moved to ({x}, {y})");
    }

    public void ButtonDown(MouseButtons button)
    {
        SetButton(button, true);
        TraceLog.Instance.Trace(TraceCategory.Mouse, TraceLevel.Debug,
            $"Mouse button down: {button}");
    }

    public void ButtonUp(MouseButtons button)
    {
        SetButton(button, false);
        TraceLog.Instance.Trace(TraceCategory.Mouse, TraceLevel.Debug,
            $"Mouse button up: {button}");
    }

    /// <summary>
    /// Port of sdl3_mouse_set_mouse_ready: inject position into A-memory, then
    /// send the IOB x=y=0 with the current button levels.
    /// </summary>
    private void ReportToIob()
    {
        InjectPosition();
        _iob?.SetMouseReady(0, 0,
            (Buttons & MouseButtons.Left) != 0,
            (Buttons & MouseButtons.Middle) != 0,
            (Buttons & MouseButtons.Right) != 0);
    }

    private void InjectPosition()
    {
        if (_ucode == null || !ResolveSymbols())
        {
            return;
        }

        // Host coordinates are already unscaled (sdl3_video_scale == 1).
        _ucode.AMem[_aMouseX] = DtpFix(X);
        _ucode.AMem[_aMouseY] = DtpFix(Y);
        _ucode.AMem[_aMouseWakeup] = _ucode.AMem[_aVTrue];
    }

    // Looks the four variables up by name (sdl3_mouse_init's INTERN()), once
    // per symbol table instance. Never throws: missing symbols just disable
    // injection, with at most one warning.
    private bool ResolveSymbols()
    {
        SymbolTable? table = Disassembler.Symbols;
        if (table == null)
        {
            return false;
        }
        if (!ReferenceEquals(table, _resolvedFor))
        {
            _resolvedFor = table;
            _symbolsOk =
                table.TryFindByName(SymbolType.AMem, "A-MOUSE-X", out _aMouseX) &&
                table.TryFindByName(SymbolType.AMem, "A-MOUSE-Y", out _aMouseY) &&
                table.TryFindByName(SymbolType.AMem, "A-MOUSE-WAKEUP", out _aMouseWakeup) &&
                table.TryFindByName(SymbolType.AMem, "A-V-TRUE", out _aVTrue);
            if (_symbolsOk)
            {
                // Mask like UCode.ReadAMem so a bad symbol value cannot index out of range.
                _aMouseX &= 0x3FF;
                _aMouseY &= 0x3FF;
                _aMouseWakeup &= 0x3FF;
                _aVTrue &= 0x3FF;
            }
            else if (!_warnedMissingSymbols)
            {
                _warnedMissingSymbols = true;
                TraceLog.Instance.Trace(TraceCategory.Mouse, TraceLevel.Warning,
                    "Microcode symbols A-MOUSE-X/A-MOUSE-Y/A-MOUSE-WAKEUP/A-V-TRUE not found; mouse position injection disabled");
            }
        }
        return _symbolsOk;
    }
}
