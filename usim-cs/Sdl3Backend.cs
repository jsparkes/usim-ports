// Sdl3Backend.cs - SDL3-CS integration layer composing Sdl3Video/Sdl3Audio/
// Sdl3KeyTranslator with the existing Tv/ColorTv/Iob/Keyboard/Mouse/Idle
// classes into one backend with the same public shape as WpfBackend, so
// MachineControl's cutover (a later task) is close to a type-name swap.
//
// Faithful port of usim/sdl3-keyboard.c and usim/sdl3-mouse.c's event
// dispatch (sdl3_keyboard_key_down/sdl3_keyboard_key_up,
// sdl3_mouse_motion/sdl3_mouse_button_down/sdl3_mouse_button_up,
// sdl3_mouse_warp_if_needed) EXCEPT the one documented divergence below
// (cold_boot_scancode/warm_boot_scancode's typo fix).

using System;
using SDL3;

namespace Usim;

/// <summary>
/// SDL3 backend for video rendering, input handling, and beep audio -- the
/// SDL3-CS counterpart to WpfBackend, with the same public shape.
/// </summary>
public class Sdl3Backend : IDisposable
{
    private readonly Tv _tv;
    private readonly ColorTv _colorTv;
    private readonly Iob _iob;
    private readonly Keyboard _keyboard;
    private readonly Mouse _mouse;
    private readonly Idle _idle;
    private readonly Action _onTick;

    private readonly Sdl3Video _video = new();
    private readonly Sdl3Audio _audio = new();

    private bool _colorTvEnabled;
    private string _windowTitle = "USIM - Lisp Machine Emulator";
    private volatile bool _exitRequested;
    private long _lastTickMs;

    // ~16ms cadence, matching WpfBackend's DispatcherTimer interval.
    private const int TickIntervalMs = 16;

    public double Scale { get; set; } = 1.0;
    public bool AllowResize { get; set; } = false;
    public bool UseLinearFiltering { get; set; } = true;
    public int WindowDisplay { get; set; } = -1;
    public int WindowPositionX { get; set; } = -1;
    public int WindowPositionY { get; set; } = -1;
    public bool WindowAlwaysOnTop { get; set; } = false;
    public bool IsRunning { get; private set; }

    // --- Boot-chord constants (usim/sdl3-keyboard.c:20-21) ---
    //
    // Documented divergence #1 (see the plan's Global Constraints): real C
    // has
    //   const uint32_t cold_boot_scancode = (0b11111 << 19) | (0b111 << 16) | (0b111111 < 10) | 046;
    //   const uint32_t warm_boot_scancode = (0b11111 << 19) | (0b111 << 16) | (0b111111 < 10) | 062;
    // `(0b111111 < 10)` is a typo for `(0b111111 << 10)` -- `<` instead of
    // `<<` -- which always evaluates to 0 (63 < 10 is false). This port uses
    // the shift the surrounding bit pattern (NKB<23:19>, NKB<18:16>, then a
    // field at bits 10 and up) clearly intends, not real C's zeroed bug.
    internal const uint ColdBootScancode =
        (0b11111u << 19) | (0b111u << 16) | (0b111111u << 10) | 0x26; // 046 octal
    internal const uint WarmBootScancode =
        (0b11111u << 19) | (0b111u << 16) | (0b111111u << 10) | 0x32; // 062 octal

    // Cadet scancodes (Sdl3KeyTranslator.TranslateKey's return values) used
    // for boot-chord detection: ctrl/meta for the "all ctrl and meta down"
    // check, rubout (backspace)/return for which boot scancode to send.
    private const int CadetScancodeCtrl = 0x10;
    private const int CadetScancodeMeta = 0x25;
    private const int CadetScancodeRubout = 0x13;
    private const int CadetScancodeReturn = 0x5E;

    // usim/sdl3-keyboard.c's sdl3_keyboard_disable_up_codes_for_boot: once a
    // boot is signalled, key-up codes are suppressed until the next key-down.
    private bool _disableUpCodesForBoot;

    // A boot scancode goes directly to the IOB (bypassing Keyboard's own
    // private queue, since it is a distinct hardware scancode pattern, not a
    // software-key up/down code -- see the NKB<18:16> header difference
    // between sdl3_keyboard_new_scancode() and cold_boot_scancode/
    // warm_boot_scancode). Single-slot, drained on Tick() once the IOB
    // keyboard is free, the same busy check Keyboard.Tick() uses for its own
    // queue; a second boot request before the first is delivered is dropped
    // with a warning, matching real C's queue-full warning in spirit.
    private uint? _pendingBootScancode;

    public Sdl3Backend(Tv tv, ColorTv colorTv, Iob iob, Keyboard keyboard, Mouse mouse, Idle idle, Action onTick)
    {
        _tv = tv ?? throw new ArgumentNullException(nameof(tv));
        _colorTv = colorTv ?? throw new ArgumentNullException(nameof(colorTv));
        _iob = iob ?? throw new ArgumentNullException(nameof(iob));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _mouse = mouse ?? throw new ArgumentNullException(nameof(mouse));
        _idle = idle ?? throw new ArgumentNullException(nameof(idle));
        _onTick = onTick ?? throw new ArgumentNullException(nameof(onTick));
    }

    /// <summary>
    /// Create the window(s), renderer(s), texture(s), and audio stream, and
    /// show the main window. Does not block (matches WpfBackend.Initialize);
    /// RunMessageLoop() is what blocks, pumping events and ticking.
    /// </summary>
    public void Initialize()
    {
        if (IsRunning)
            return;

        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Info, "Initializing SDL3 backend");

        _colorTvEnabled = UsimState.ColorTvEnabled;

        if (!_audio.Init())
        {
            // non-fatal, matches Sdl3Audio.Init's own contract: callers
            // continue without audio.
            TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Warning,
                "sdl3: audio init failed, continuing without audio");
        }

        bool videoOk = _video.Init(
            _tv.Width, _tv.Height,
            _colorTv.Width, _colorTv.Height,
            _colorTvEnabled,
            Scale,
            AllowResize,
            WindowDisplay,
            WindowPositionX, WindowPositionY,
            WindowAlwaysOnTop,
            _windowTitle);

        if (!videoOk)
        {
            TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Error,
                "sdl3: video init failed, backend not started");
            return;
        }

        _video.SetScaleMode(UseLinearFiltering);
        _mouse.OnWarp = WarpCursor;

        _lastTickMs = Environment.TickCount64;
        _exitRequested = false;
        IsRunning = true;

        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Info, "SDL3 backend initialized successfully");
    }

    /// <summary>
    /// Pump SDL3 events and tick at a ~16ms cadence. Blocks until the main
    /// window receives a quit/close event, or RequestExit() is called.
    /// </summary>
    public void RunMessageLoop()
    {
        while (IsRunning && !_exitRequested)
        {
            while (SDL.PollEvent(out SDL.Event ev))
            {
                DispatchEvent(ev);
                if (!IsRunning)
                    break;
            }

            if (!IsRunning || _exitRequested)
                break;

            long now = Environment.TickCount64;
            if (now - _lastTickMs >= TickIntervalMs)
            {
                _lastTickMs = now;
                Tick();
            }
            else
            {
                SDL.Delay(1);
            }
        }

        IsRunning = false;
    }

    /// <summary>
    /// End RunMessageLoop() from any thread. The flag is checked at the top
    /// of each loop iteration.
    /// </summary>
    public void RequestExit()
    {
        _exitRequested = true;
    }

    private void DispatchEvent(in SDL.Event ev)
    {
        switch ((SDL.EventType)ev.Type)
        {
            case SDL.EventType.KeyDown:
                HandleKeyDown(ev.Key);
                break;
            case SDL.EventType.KeyUp:
                HandleKeyUp(ev.Key);
                break;
            case SDL.EventType.MouseMotion:
                HandleMouseMotion(ev.Motion);
                break;
            case SDL.EventType.MouseButtonDown:
                HandleMouseButton(ev.Button, down: true);
                break;
            case SDL.EventType.MouseButtonUp:
                HandleMouseButton(ev.Button, down: false);
                break;
            case SDL.EventType.Quit:
            case SDL.EventType.WindowCloseRequested:
                IsRunning = false;
                break;
        }
    }

    private void Tick()
    {
        _onTick();
        _tv.Tick();
        _colorTv.Tick();
        _iob.Tick();
        _keyboard.Tick();
        DrainPendingBootScancode();
        _video.PresentMain(_tv.FrameBuffer);
        if (_colorTvEnabled)
        {
            _video.PresentColor(_colorTv.FrameBuffer);
        }
    }

    /// <summary>
    /// Port of sdl3_keyboard_key_down's dispatch (sdl3-keyboard.c:262-395),
    /// minus the special-key machine-control combos (scalemode/dump), which
    /// are out of this task's scope.
    /// </summary>
    private void HandleKeyDown(SDL.KeyboardEvent key)
    {
        _idle.Activity();

        int cadetScancode = Sdl3KeyTranslator.TranslateKey(key.Scancode);
        if (cadetScancode == 0)
        {
            TraceLog.Instance.Trace(TraceCategory.Keyboard, TraceLevel.Debug,
                $"Unable to translate SDL3 key: {key.Scancode}");
            return;
        }

        bool capsLock = (key.Mod & SDL.Keymod.Caps) != 0;
        int modifiers = Sdl3KeyTranslator.GetModifierState(key.Mod, capsLock);

        ReadOnlySpan<bool> state = SDL.GetKeyboardState(out _);
        bool chordDown = AreAllCtrlAndMetaDownForBoot(state);

        bool sendColdBoot = false;
        bool sendWarmBoot = false;
        if (chordDown)
        {
            if (cadetScancode == CadetScancodeRubout)
            {
                TraceLog.Instance.Trace(TraceCategory.Keyboard, TraceLevel.Info, "kbd: cold boot requested");
                sendColdBoot = true;
            }
            else if (cadetScancode == CadetScancodeReturn)
            {
                TraceLog.Instance.Trace(TraceCategory.Keyboard, TraceLevel.Info, "kbd: warm boot requested");
                sendWarmBoot = true;
            }
        }

        _keyboard.KeyDown((uint)cadetScancode, modifiers);

        if (sendColdBoot)
        {
            _disableUpCodesForBoot = true;
            EnqueueBootScancode(ColdBootScancode);
        }
        else if (sendWarmBoot)
        {
            _disableUpCodesForBoot = true;
            EnqueueBootScancode(WarmBootScancode);
        }
        else
        {
            _disableUpCodesForBoot = false;
        }
    }

    /// <summary>
    /// Port of sdl3_keyboard_key_up's up-down-code path, simplified to match
    /// Keyboard.KeyUp's own existing (already-simplified) API -- it does not
    /// replicate real C's separate "all-keys-up" composite code, the same
    /// simplification WpfBackend.HandleKeyEvent already made.
    /// </summary>
    private void HandleKeyUp(SDL.KeyboardEvent key)
    {
        _idle.Activity();

        // sdl3-keyboard.c:404: after boot is signalled, no up code is sent.
        if (_disableUpCodesForBoot)
            return;

        int cadetScancode = Sdl3KeyTranslator.TranslateKey(key.Scancode);
        if (cadetScancode == 0)
        {
            TraceLog.Instance.Trace(TraceCategory.Keyboard, TraceLevel.Debug,
                $"Unable to translate SDL3 key: {key.Scancode}");
            return;
        }

        bool capsLock = (key.Mod & SDL.Keymod.Caps) != 0;
        int modifiers = Sdl3KeyTranslator.GetModifierState(key.Mod, capsLock);

        _keyboard.KeyUp((uint)cadetScancode, modifiers);
    }

    /// <summary>
    /// Port of sdl3_keyboard_are_all_ctrl_and_meta_down_for_boot: requires at
    /// least two SDL scancodes mapped to the cadet ctrl scancode, and at
    /// least two mapped to the cadet meta scancode, all currently down.
    /// </summary>
    internal static bool AreAllCtrlAndMetaDownForBoot(ReadOnlySpan<bool> state)
    {
        int nctrl = 0;
        int nmeta = 0;
        for (int i = 0; i < state.Length; i++)
        {
            int cadet = Sdl3KeyTranslator.TranslateKey((SDL.Scancode)i);
            if (cadet == CadetScancodeCtrl)
            {
                if (!state[i])
                    return false;
                nctrl++;
            }
            if (cadet == CadetScancodeMeta)
            {
                if (!state[i])
                    return false;
                nmeta++;
            }
        }
        return nctrl >= 2 && nmeta >= 2;
    }

    private void EnqueueBootScancode(uint scancode)
    {
        if (_pendingBootScancode.HasValue)
        {
            TraceLog.Instance.Trace(TraceCategory.Keyboard, TraceLevel.Warning,
                $"sdl3: boot scancode {scancode} is missed, previous boot scancode still pending");
            return;
        }
        _pendingBootScancode = scancode;
    }

    private void DrainPendingBootScancode()
    {
        if (!_pendingBootScancode.HasValue)
            return;
        if (_iob.IsKeyboardReadySet)
            return; // retried next tick, like Keyboard.Tick()'s own pump
        _iob.SetKeyboardReady(_pendingBootScancode.Value);
        _pendingBootScancode = null;
    }

    /// <summary>
    /// Port of SDL_WarpMouseInWindow for tv:mouse-warp (sdl3_mouse_warp_if_needed,
    /// sdl3-mouse.c:34-51): move the real cursor to guest pixel (x, y) inside
    /// the main window. Unlike WpfBackend.WarpCursor (a Windows-only
    /// SetCursorPos P/Invoke this port cannot keep), this uses SDL3's own
    /// cross-platform warp call directly, matching real C exactly.
    /// </summary>
    private void WarpCursor(int x, int y)
    {
        nint mainWindow = _video.MainWindowHandle;
        if (mainWindow == 0)
            return;

        SDL.WarpMouseInWindow(mainWindow, x, y);
    }

    private void HandleMouseMotion(SDL.MouseMotionEvent motion)
    {
        _idle.Activity();
        var (x, y) = UnscaleMotion((int)motion.X, (int)motion.Y, Scale);
        _mouse.UpdatePosition(x, y);
    }

    private void HandleMouseButton(SDL.MouseButtonEvent button, bool down)
    {
        _idle.Activity();

        MouseButtons mouseButton = button.Button switch
        {
            1 => MouseButtons.Left,
            2 => MouseButtons.Middle,
            3 => MouseButtons.Right,
            _ => MouseButtons.None
        };

        if (mouseButton == MouseButtons.None)
            return;

        if (down)
            _mouse.ButtonDown(mouseButton);
        else
            _mouse.ButtonUp(mouseButton);
    }

    /// <summary>
    /// Port of sdl3-mouse.c:62-63's unscaling: when the window is explicitly
    /// scaled (Scale != 1.0), mouse_x/mouse_y arrive in scaled window
    /// coordinates, but the CADR expects unscaled guest pixel coordinates.
    /// Pure and directly unit-testable without a live window/event loop.
    /// </summary>
    internal static (int X, int Y) UnscaleMotion(int rawX, int rawY, double scale)
    {
        return ((int)(rawX / scale), (int)(rawY / scale));
    }

    public void SetWindowTitle(string title)
    {
        _windowTitle = title ?? "USIM";
        _video.SetWindowTitle(_windowTitle);
    }

    public void Beep(int halfWavelengthMicros, int durationMicros)
    {
        _audio.Beep(halfWavelengthMicros, durationMicros);
    }

    public void Dispose()
    {
        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Info, "Shutting down SDL3 backend");

        IsRunning = false;
        _video.Dispose();
        _audio.Dispose();
    }
}
