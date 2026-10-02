// Mouse.cs - Faithful port of the real mouse-to-IOB wire protocol
// (usim/iob.c's WITH_SDL3 iob_set_mouse_ready path / usim/mouse.c's
// mouse_event toggle semantics). The previous version of this class
// invented its own 11-bit linear XY-scaling scheme with no relation to the
// real hardware. See docs/superpowers/specs/2026-09-18-iob-design.md.

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

    // Real hardware toggle state (usim/mouse.c:46-51's mouse_tail/middle/head
    // ^= 1 on each button-1/2/3 transition -- edge-triggered, not level-
    // triggered like WPF's own Buttons flags above). Left maps to tail,
    // Middle to middle, Right to head -- a reasonable mapping given the real
    // C doesn't document which physical button is 1/2/3.
    private bool _tailToggle;
    private bool _middleToggle;
    private bool _headToggle;

    private Iob? _iob;

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
    /// Faithful port of mouse_event's toggle semantics (usim/mouse.c:36-53):
    /// each button transition (press OR release) XORs the corresponding
    /// hardware toggle bit -- it does not track "currently held", it
    /// remembers the last commanded flip.
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

        switch (button)
        {
            case MouseButtons.Left:
                _tailToggle = !_tailToggle;
                break;
            case MouseButtons.Middle:
                _middleToggle = !_middleToggle;
                break;
            case MouseButtons.Right:
                _headToggle = !_headToggle;
                break;
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

    private void ReportToIob()
    {
        _iob?.SetMouseReady(X, Y, _tailToggle, _middleToggle, _headToggle);
    }
}
