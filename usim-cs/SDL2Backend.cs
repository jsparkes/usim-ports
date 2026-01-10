// SDL2Backend.cs - SDL2 video, keyboard, and mouse backend
// Converted from sdl2.c and sdl2.h
//
// NOTE: This requires SDL2-CS bindings and native SDL2 library.
// To use SDL2 backend:
// 1. Install SDL2-CS from https://github.com/flibitijibibo/SDL2-CS
// 2. Ensure native SDL2.dll is available (see SDL2-CS documentation)
// 3. Uncomment the #define ENABLE_SDL2 below

#define ENABLE_SDL2

using System;

#if ENABLE_SDL2
using System.Runtime.InteropServices;
using SDL2;
using static SDL2.SDL;
#endif

namespace Usim;

#if !ENABLE_SDL2
/// <summary>
/// SDL2 backend stub - SDL2 support is disabled
/// Enable by defining ENABLE_SDL2 and installing SDL2-CS
/// </summary>
public class SDL2Backend : IDisposable
{
    private readonly Display _display;
    private readonly Keyboard _keyboard;
    private readonly Mouse _mouse;
    
    public bool IsRunning { get; private set; }
    public double Scale { get; set; } = 1.0;
    public bool AllowResize { get; set; } = false;
    public bool UseLinearFiltering { get; set; } = true;
    
    public SDL2Backend(Display display, Keyboard keyboard, Mouse mouse)
    {
        _display = display ?? throw new ArgumentNullException(nameof(display));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _mouse = mouse ?? throw new ArgumentNullException(nameof(mouse));
    }
    
    public void Initialize()
    {
        throw new NotImplementedException(
            "SDL2 backend is not available. " +
            "To enable SDL2 support, install SDL2-CS and native SDL2 library, " +
            "then define ENABLE_SDL2 in SDL2Backend.cs");
    }
    
    public void ProcessEvents() { }
    public void SetWindowTitle(string title) { }
    public void Beep(int halfWavelengthMicros, int durationMicros) { }
    public void Dispose() { }
}
#else
// Full SDL2 implementation when ENABLE_SDL2 is defined
// Original implementation with SDL2-CS bindings...

/// <summary>
/// SDL2 backend for video rendering and input handling
/// </summary>
public class SDL2Backend : IDisposable
{
    // SDL window and renderer
    private IntPtr _window;
    private IntPtr _renderer;
    private IntPtr _texture;
    
    // References to emulator components
    private readonly Display _display;
    private readonly Keyboard _keyboard;
    private readonly Mouse _mouse;
    
    // Display scaling
    public double Scale { get; set; } = 1.0;
    public bool AllowResize { get; set; } = false;
    public bool UseLinearFiltering { get; set; } = true;
    
    // Window state
    public bool IsRunning { get; private set; }
    private string _windowTitle = "USIM - Lisp Machine Emulator";
    
    // Audio for beep
    private uint _audioDeviceId;
    private BeepState _beepState;
    
    public SDL2Backend(Display display, Keyboard keyboard, Mouse mouse)
    {
        _display = display ?? throw new ArgumentNullException(nameof(display));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _mouse = mouse ?? throw new ArgumentNullException(nameof(mouse));
        _beepState = new BeepState();
    }
    
    /// <summary>
    /// Initialize SDL2 subsystems and create window
    /// </summary>
    public void Initialize()
    {
        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Info, "Initializing SDL2 backend");
        
        // Initialize SDL
        if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_AUDIO | SDL_INIT_EVENTS) < 0)
        {
            throw new InvalidOperationException($"SDL_Init failed: {SDL_GetError()}");
        }
        
        // Create window and renderer
        SDL_WindowFlags windowFlags = SDL_WindowFlags.SDL_WINDOW_OPENGL | SDL_WindowFlags.SDL_WINDOW_ALLOW_HIGHDPI;
        if (AllowResize)
            windowFlags |= SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
        
        int width = (int)(Display.WIDTH * Scale);
        int height = (int)(Display.HEIGHT * Scale);
        
        if (SDL_CreateWindowAndRenderer(width, height, windowFlags, out _window, out _renderer) < 0)
        {
            throw new InvalidOperationException($"SDL_CreateWindowAndRenderer failed: {SDL_GetError()}");
        }
        
        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Info, 
            $"SDL2 video driver: {SDL_GetCurrentVideoDriver()}");
        
        // Get renderer info
        SDL_RendererInfo rendererInfo;
        if (SDL_GetRendererInfo(_renderer, out rendererInfo) == 0)
        {
            TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Info,
                $"SDL2 renderer: {Marshal.PtrToStringAnsi(rendererInfo.name)}");
        }
        
        // Hide cursor (we'll draw our own)
        SDL_ShowCursor(SDL_DISABLE);
        
        // Enable screen saver
        SDL_EnableScreenSaver();
        
        // Set window title
        SDL_SetWindowTitle(_window, _windowTitle);
        
        // Set render quality
        string filter = UseLinearFiltering ? "linear" : "nearest";
        SDL_SetHint(SDL_HINT_RENDER_SCALE_QUALITY, filter);
        SDL_RenderSetLogicalSize(_renderer, Display.WIDTH, Display.HEIGHT);
        
        // Create texture for display
        _texture = SDL_CreateTexture(
            _renderer,
            SDL_PIXELFORMAT_ARGB8888,
            (int)SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING,
            Display.WIDTH,
            Display.HEIGHT);
        
        if (_texture == IntPtr.Zero)
        {
            throw new InvalidOperationException($"SDL_CreateTexture failed: {SDL_GetError()}");
        }
        
        // Initialize audio for beep
        InitializeAudio();
        
        IsRunning = true;
        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Info, "SDL2 backend initialized successfully");
    }
    
    /// <summary>
    /// Initialize audio subsystem for beep support
    /// </summary>
    private void InitializeAudio()
    {
        SDL_AudioSpec want = new SDL_AudioSpec
        {
            freq = 44100,
            format = AUDIO_S16SYS,
            channels = 1,
            samples = 2048,
            callback = AudioCallback,
            userdata = IntPtr.Zero
        };
        
        _audioDeviceId = SDL_OpenAudioDevice(
            null,
            0,
            ref want,
            out SDL_AudioSpec have,
            0);
        
        if (_audioDeviceId == 0)
        {
            TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Warning,
                $"Failed to open audio: {SDL_GetError()}");
        }
        else
        {
            SDL_PauseAudioDevice(_audioDeviceId, 1); // Start paused
        }
    }
    
    /// <summary>
    /// Audio callback for beep generation
    /// </summary>
    private void AudioCallback(IntPtr userdata, IntPtr stream, int len)
    {
        if (_beepState.Duration <= 0)
            return;
        
        unsafe
        {
            short* buffer = (short*)stream;
            int samples = len / 2; // 2 bytes per sample
            
            for (int i = 0; i < samples; i++)
            {
                double time = _beepState.SampleNumber / 44100.0;
                buffer[i] = (short)(28000 * Math.Sin(2.0 * Math.PI * _beepState.Frequency * time));
                _beepState.SampleNumber++;
            }
        }
    }
    
    /// <summary>
    /// Generate a beep sound
    /// </summary>
    public void Beep(int halfWavelengthMicros, int durationMicros)
    {
        if (_audioDeviceId == 0)
            return;
        
        _beepState.Frequency = 1000000.0 / (halfWavelengthMicros * 2);
        _beepState.Duration = durationMicros;
        _beepState.SampleNumber = 0;
        
        SDL_PauseAudioDevice(_audioDeviceId, 0); // Resume
        SDL_Delay((uint)(durationMicros / 1000));
        SDL_PauseAudioDevice(_audioDeviceId, 1); // Pause
    }
    
    /// <summary>
    /// Process SDL events (keyboard, mouse, window)
    /// </summary>
    public void ProcessEvents()
    {
        // Update display
        UpdateDisplay();
        
        // Process keyboard queue
        _keyboard.ProcessEvents();
        
        // Poll SDL events
        while (SDL_PollEvent(out SDL_Event ev) != 0)
        {
            switch (ev.type)
            {
                case SDL_EventType.SDL_WINDOWEVENT:
                    HandleWindowEvent(ref ev.window);
                    break;
                
                case SDL_EventType.SDL_KEYDOWN:
                    HandleKeyEvent(ref ev.key, true);
                    break;
                
                case SDL_EventType.SDL_KEYUP:
                    HandleKeyEvent(ref ev.key, false);
                    break;
                
                case SDL_EventType.SDL_MOUSEMOTION:
                    HandleMouseMotion(ref ev.motion);
                    break;
                
                case SDL_EventType.SDL_MOUSEBUTTONDOWN:
                case SDL_EventType.SDL_MOUSEBUTTONUP:
                    HandleMouseButton(ref ev.button);
                    break;
                
                case SDL_EventType.SDL_QUIT:
                    IsRunning = false;
                    break;
            }
        }
    }
    
    /// <summary>
    /// Update display texture and render
    /// </summary>
    private void UpdateDisplay()
    {
        // Get frame buffer from display
        byte[] frameBuffer = _display.FrameBuffer;
        
        unsafe
        {
            fixed (byte* ptr = frameBuffer)
            {
                // Update texture with frame buffer
                SDL_UpdateTexture(_texture, IntPtr.Zero, (IntPtr)ptr, Display.WIDTH * 4);
            }
        }
        
        // Render
        SDL_RenderClear(_renderer);
        SDL_RenderCopy(_renderer, _texture, IntPtr.Zero, IntPtr.Zero);
        SDL_RenderPresent(_renderer);
    }
    
    /// <summary>
    /// Handle window events
    /// </summary>
    private void HandleWindowEvent(ref SDL_WindowEvent windowEvent)
    {
        if (windowEvent.windowEvent == SDL_WindowEventID.SDL_WINDOWEVENT_RESIZED)
        {
            int width = windowEvent.data1;
            int height = windowEvent.data2;
            
            // Maintain aspect ratio
            float tvAspect = (float)Display.WIDTH / Display.HEIGHT;
            float aspect = (float)width / height;
            
            if (tvAspect < aspect)
                width = (int)(height * tvAspect);
            else
                height = (int)(width / tvAspect);
            
            SDL_SetWindowSize(_window, width, height);
        }
        
        // Refresh display on expose
        if (windowEvent.windowEvent == SDL_WindowEventID.SDL_WINDOWEVENT_EXPOSED)
        {
            UpdateDisplay();
        }
    }
    
    /// <summary>
    /// Handle keyboard events
    /// </summary>
    private void HandleKeyEvent(ref SDL_KeyboardEvent keyEvent, bool keyDown)
    {
        // Translate SDL keycode to X11 keysym
        int keysym = TranslateKeycode(keyEvent);
        
        if (keysym == 0) // XK_VoidSymbol equivalent
        {
            TraceLog.Instance.Trace(TraceCategory.Keyboard, TraceLevel.Debug,
                $"Unable to translate SDL keycode: {keyEvent.keysym.sym}");
            return;
        }
        
        // Get modifier state
        int modifiers = GetModifierState(keyEvent);
        
        // Send to keyboard handler
        if (keyDown)
            _keyboard.KeyDown((uint)keysym, modifiers);
        else
            _keyboard.KeyUp((uint)keysym, modifiers);
    }
    
    /// <summary>
    /// Translate SDL keycode to X11 keysym
    /// </summary>
    private int TranslateKeycode(SDL_KeyboardEvent e)
    {
        bool shift = (e.keysym.mod & SDL_Keymod.KMOD_SHIFT) != 0;
        
        // Function keys
        switch (e.keysym.sym)
        {
            case SDL_Keycode.SDLK_ESCAPE: return 0xFF1B; // XK_Escape
            case SDL_Keycode.SDLK_F1: return 0xFFBE; // XK_F1
            case SDL_Keycode.SDLK_F2: return 0xFFBF;
            case SDL_Keycode.SDLK_F3: return 0xFFC0;
            case SDL_Keycode.SDLK_F4: return 0xFFC1;
            case SDL_Keycode.SDLK_F5: return 0xFFC2;
            case SDL_Keycode.SDLK_F6: return 0xFFC3;
            case SDL_Keycode.SDLK_F7: return 0xFFC4;
            case SDL_Keycode.SDLK_F8: return 0xFFC5;
            case SDL_Keycode.SDLK_F9: return 0xFFC6;
            case SDL_Keycode.SDLK_F10: return 0xFFC7;
            case SDL_Keycode.SDLK_F11: return 0xFFC8;
            case SDL_Keycode.SDLK_F12: return 0xFFC9;
            
            // Navigation keys
            case SDL_Keycode.SDLK_PAGEUP: return 0xFF55; // XK_Page_Up
            case SDL_Keycode.SDLK_PAGEDOWN: return 0xFF56;
            case SDL_Keycode.SDLK_HOME: return 0xFF50;
            case SDL_Keycode.SDLK_END: return 0xFF57;
            case SDL_Keycode.SDLK_LEFT: return 0xFF51;
            case SDL_Keycode.SDLK_RIGHT: return 0xFF53;
            case SDL_Keycode.SDLK_UP: return 0xFF52;
            case SDL_Keycode.SDLK_DOWN: return 0xFF54;
            
            // Special keys
            case SDL_Keycode.SDLK_INSERT: return 0xFF63;
            case SDL_Keycode.SDLK_DELETE: return 0xFFFF;
            case SDL_Keycode.SDLK_BACKSPACE: return 0xFF08;
            case SDL_Keycode.SDLK_TAB: return 0xFF09;
            case SDL_Keycode.SDLK_RETURN: return 0xFF0D;
            case SDL_Keycode.SDLK_SPACE: return 0x0020;
            
            // Modifier keys
            case SDL_Keycode.SDLK_LSHIFT: return 0xFFE1;
            case SDL_Keycode.SDLK_RSHIFT: return 0xFFE2;
            case SDL_Keycode.SDLK_LCTRL: return 0xFFE3;
            case SDL_Keycode.SDLK_RCTRL: return 0xFFE4;
            case SDL_Keycode.SDLK_LALT: return 0xFFE9;
            case SDL_Keycode.SDLK_RALT: return 0xFFEA;
            case SDL_Keycode.SDLK_LGUI: return 0xFFE7; // XK_Meta_L
            case SDL_Keycode.SDLK_RGUI: return 0xFFE8;
            case SDL_Keycode.SDLK_CAPSLOCK: return 0xFFE5;
            
            // Number row with shift
            case SDL_Keycode.SDLK_BACKQUOTE: return shift ? 0x007E : 0x0060; // ~ : `
            case SDL_Keycode.SDLK_1: return shift ? 0x0021 : 0x0031; // ! : 1
            case SDL_Keycode.SDLK_2: return shift ? 0x0040 : 0x0032; // @ : 2
            case SDL_Keycode.SDLK_3: return shift ? 0x0023 : 0x0033; // # : 3
            case SDL_Keycode.SDLK_4: return shift ? 0x0024 : 0x0034; // $ : 4
            case SDL_Keycode.SDLK_5: return shift ? 0x0025 : 0x0035; // % : 5
            case SDL_Keycode.SDLK_6: return shift ? 0x005E : 0x0036; // ^ : 6
            case SDL_Keycode.SDLK_7: return shift ? 0x0026 : 0x0037; // & : 7
            case SDL_Keycode.SDLK_8: return shift ? 0x002A : 0x0038; // * : 8
            case SDL_Keycode.SDLK_9: return shift ? 0x0028 : 0x0039; // ( : 9
            case SDL_Keycode.SDLK_0: return shift ? 0x0029 : 0x0030; // ) : 0
            case SDL_Keycode.SDLK_MINUS: return shift ? 0x005F : 0x002D; // _ : -
            case SDL_Keycode.SDLK_EQUALS: return shift ? 0x002B : 0x003D; // + : =
            
            // Letters (A-Z)
            case SDL_Keycode.SDLK_a: return shift ? 0x0041 : 0x0061;
            case SDL_Keycode.SDLK_b: return shift ? 0x0042 : 0x0062;
            case SDL_Keycode.SDLK_c: return shift ? 0x0043 : 0x0063;
            case SDL_Keycode.SDLK_d: return shift ? 0x0044 : 0x0064;
            case SDL_Keycode.SDLK_e: return shift ? 0x0045 : 0x0065;
            case SDL_Keycode.SDLK_f: return shift ? 0x0046 : 0x0066;
            case SDL_Keycode.SDLK_g: return shift ? 0x0047 : 0x0067;
            case SDL_Keycode.SDLK_h: return shift ? 0x0048 : 0x0068;
            case SDL_Keycode.SDLK_i: return shift ? 0x0049 : 0x0069;
            case SDL_Keycode.SDLK_j: return shift ? 0x004A : 0x006A;
            case SDL_Keycode.SDLK_k: return shift ? 0x004B : 0x006B;
            case SDL_Keycode.SDLK_l: return shift ? 0x004C : 0x006C;
            case SDL_Keycode.SDLK_m: return shift ? 0x004D : 0x006D;
            case SDL_Keycode.SDLK_n: return shift ? 0x004E : 0x006E;
            case SDL_Keycode.SDLK_o: return shift ? 0x004F : 0x006F;
            case SDL_Keycode.SDLK_p: return shift ? 0x0050 : 0x0070;
            case SDL_Keycode.SDLK_q: return shift ? 0x0051 : 0x0071;
            case SDL_Keycode.SDLK_r: return shift ? 0x0052 : 0x0072;
            case SDL_Keycode.SDLK_s: return shift ? 0x0053 : 0x0073;
            case SDL_Keycode.SDLK_t: return shift ? 0x0054 : 0x0074;
            case SDL_Keycode.SDLK_u: return shift ? 0x0055 : 0x0075;
            case SDL_Keycode.SDLK_v: return shift ? 0x0056 : 0x0076;
            case SDL_Keycode.SDLK_w: return shift ? 0x0057 : 0x0077;
            case SDL_Keycode.SDLK_x: return shift ? 0x0058 : 0x0078;
            case SDL_Keycode.SDLK_y: return shift ? 0x0059 : 0x0079;
            case SDL_Keycode.SDLK_z: return shift ? 0x005A : 0x007A;
            
            // Punctuation
            case SDL_Keycode.SDLK_LEFTBRACKET: return shift ? 0x007B : 0x005B; // { : [
            case SDL_Keycode.SDLK_RIGHTBRACKET: return shift ? 0x007D : 0x005D; // } : ]
            case SDL_Keycode.SDLK_BACKSLASH: return shift ? 0x007C : 0x005C; // | : \
            case SDL_Keycode.SDLK_SEMICOLON: return shift ? 0x003A : 0x003B; // : : ;
            case SDL_Keycode.SDLK_QUOTE: return shift ? 0x0022 : 0x0027; // " : '
            case SDL_Keycode.SDLK_COMMA: return shift ? 0x003C : 0x002C; // < : ,
            case SDL_Keycode.SDLK_PERIOD: return shift ? 0x003E : 0x002E; // > : .
            case SDL_Keycode.SDLK_SLASH: return shift ? 0x003F : 0x002F; // ? : /
            
            default: return 0; // XK_VoidSymbol
        }
    }
    
    /// <summary>
    /// Get modifier key state
    /// </summary>
    private int GetModifierState(SDL_KeyboardEvent e)
    {
        int modifiers = 0;
        
        if ((e.keysym.mod & SDL_Keymod.KMOD_SHIFT) != 0)
            modifiers |= 1 << 0; // ShiftMapIndex
        
        if ((e.keysym.mod & SDL_Keymod.KMOD_CTRL) != 0)
            modifiers |= 1 << 2; // ControlMapIndex
        
        if ((e.keysym.mod & SDL_Keymod.KMOD_ALT) != 0)
            modifiers |= 1 << 3; // Mod1MapIndex
        
        if ((e.keysym.mod & SDL_Keymod.KMOD_GUI) != 0)
            modifiers |= 1 << 6; // Mod4MapIndex
        
        if ((e.keysym.mod & SDL_Keymod.KMOD_CAPS) != 0)
            modifiers |= 1 << 1; // LockMapIndex
        
        return modifiers;
    }
    
    /// <summary>
    /// Handle mouse motion events
    /// </summary>
    private void HandleMouseMotion(ref SDL_MouseMotionEvent motionEvent)
    {
        _mouse.UpdatePosition(motionEvent.x, motionEvent.y);
    }
    
    /// <summary>
    /// Handle mouse button events
    /// </summary>
    private void HandleMouseButton(ref SDL_MouseButtonEvent buttonEvent)
    {
        MouseButtons button = MouseButtons.None;
        
        switch (buttonEvent.button)
        {
            case (byte)SDL_BUTTON_LEFT:
                button = MouseButtons.Left;
                break;
            case (byte)SDL_BUTTON_MIDDLE:
                button = MouseButtons.Middle;
                break;
            case (byte)SDL_BUTTON_RIGHT:
                button = MouseButtons.Right;
                break;
        }
        
        if (buttonEvent.state == SDL_PRESSED)
            _mouse.ButtonDown(button);
        else
            _mouse.ButtonUp(button);
        
        // Also update position
        _mouse.UpdatePosition(buttonEvent.x, buttonEvent.y);
    }
    
    /// <summary>
    /// Set window title
    /// </summary>
    public void SetWindowTitle(string title)
    {
        _windowTitle = title ?? "USIM";
        if (_window != IntPtr.Zero)
        {
            SDL_SetWindowTitle(_window, _windowTitle);
        }
    }
    
    /// <summary>
    /// Cleanup SDL resources
    /// </summary>
    public void Dispose()
    {
        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Info, "Shutting down SDL2 backend");
        
        if (_audioDeviceId != 0)
        {
            SDL_CloseAudioDevice(_audioDeviceId);
            _audioDeviceId = 0;
        }
        
        if (_texture != IntPtr.Zero)
        {
            SDL_DestroyTexture(_texture);
            _texture = IntPtr.Zero;
        }
        
        if (_renderer != IntPtr.Zero)
        {
            SDL_DestroyRenderer(_renderer);
            _renderer = IntPtr.Zero;
        }
        
        if (_window != IntPtr.Zero)
        {
            SDL_DestroyWindow(_window);
            _window = IntPtr.Zero;
        }
        
        SDL_Quit();
        IsRunning = false;
    }
    
    /// <summary>
    /// Audio state for beep generation
    /// </summary>
    private class BeepState
    {
        public double Frequency { get; set; }
        public int Duration { get; set; }
        public int SampleNumber { get; set; }
    }
}
#endif
