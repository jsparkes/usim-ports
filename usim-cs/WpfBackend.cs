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
        if (_window != null)
            return;

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

        viewbox.Width = Display.WIDTH * Scale;
        viewbox.Height = Display.HEIGHT * Scale;

        _window = new Window
        {
            Title = _windowTitle,
            Content = viewbox,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = AllowResize ? ResizeMode.CanResize : ResizeMode.CanMinimize,
            Cursor = Cursors.None
        };

        _window.Loaded += (_, _) =>
        {
            _window.SizeToContent = SizeToContent.Manual;
            viewbox.Width = double.NaN;
            viewbox.Height = double.NaN;
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

        if (_window != null && !_window.Dispatcher.CheckAccess())
        {
            _window.Dispatcher.Invoke(() => Dispose());
            return;
        }

        _timer?.Stop();
        _window?.Close();
        IsRunning = false;
    }
}
