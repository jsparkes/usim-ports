// Sdl3Video.cs - SDL3-CS port of sdl3-video.c's window/renderer/texture/
// present path.
//
// Faithful port of sdl3_video_init/sdl3_video_present_main/
// sdl3_video_present_color/sdl3_video_set_scalemode/
// sdl3_video_update_window_title/sdl3_video_quit -- EXCEPT (documented
// divergence #2): this port does not set the "opengl" SDL_HINT_RENDER_DRIVER
// hint, does not pass SDL_WINDOW_OPENGL when creating either window, and
// never calls SDL_GL_CreateContext/SDL_GL_MakeCurrent. All real drawing in
// sdl3-video.c goes through the 2D SDL_Renderer/texture API (SDL_RenderClear,
// SDL_RenderTexture, SDL_RenderPresent), which manages its own rendering
// context internally -- the explicit OpenGL window/context calls in the C
// are vestigial for that renderer and are never required to make the
// textures appear on screen. Omitting them also avoids depending on a real
// GL-capable driver, which the "dummy" video driver (used by this class's
// own tests, and by any headless/CI run) does not provide.
//
// usim-cs's Tv/ColorTv already unpack their framebuffers into byte[] arrays
// in BGRA32 byte order (see Tv.cs/ColorTv.cs), which is a byte-order alias
// for SDL_PIXELFORMAT_ARGB8888 on little-endian hosts (the only hosts this
// project targets) -- so this port creates its textures with
// SDL_PIXELFORMAT_ARGB8888, exactly as real C does, and simply hands the
// already-unpacked frame buffer to SDL_UpdateTexture instead of re-deriving
// pixels bit-by-bit from a raw screen word buffer the way sdl3_video_
// present_main/present_color do in C (that bit-unpacking is Tv's/ColorTv's
// job in this port, done once when the framebuffer is written -- see
// Tv.cs/ColorTv.cs).

using System;
using SDL3;

namespace Usim;

public class Sdl3Video : IDisposable
{
    private IntPtr _mainWindow = IntPtr.Zero;
    private IntPtr _mainRenderer = IntPtr.Zero;
    private IntPtr _mainTexture = IntPtr.Zero;

    private IntPtr _colorWindow = IntPtr.Zero;
    private IntPtr _colorRenderer = IntPtr.Zero;
    private IntPtr _colorTexture = IntPtr.Zero;

    private uint _tvWidth;
    private uint _tvHeight;
    private uint _colorTvWidth;
    private uint _colorTvHeight;
    private bool _colorTvEnabled;

    /// <summary>
    /// The real SDL main-window pointer/handle, for Sdl3Backend's mouse-warp
    /// call (SDL_WarpMouseInWindow-equivalent). Zero until Init succeeds.
    /// </summary>
    public nint MainWindowHandle => _mainWindow;

    /// <summary>
    /// True once the color window/renderer/texture were actually created
    /// (i.e. Init was called with colorTvEnabled: true and succeeded).
    /// Exposed for tests; mirrors real C's color_window != NULL checks.
    /// </summary>
    public bool ColorWindowCreated => _colorWindow != IntPtr.Zero;

    /// <summary>
    /// Port of sdl3_video_init. Creates the main window/renderer/texture and,
    /// when colorTvEnabled, the color-TV window/renderer/texture, positions
    /// them per usim.ini's [Display] settings (see DisplayConfig), and
    /// applies the initial texture scale mode. Returns false (without
    /// throwing) on any SDL failure, matching real C's bool-returning
    /// control flow exactly.
    /// </summary>
    public bool Init(
        uint tvWidth, uint tvHeight,
        uint colorTvWidth, uint colorTvHeight,
        bool colorTvEnabled,
        double scale,
        bool allowResize,
        int windowDisplay,
        int windowPositionX, int windowPositionY,
        bool windowAlwaysOnTop,
        string windowTitle)
    {
        _tvWidth = tvWidth;
        _tvHeight = tvHeight;
        _colorTvWidth = colorTvWidth;
        _colorTvHeight = colorTvHeight;
        _colorTvEnabled = colorTvEnabled;

        // Real C initializes SDL_INIT_VIDEO (along with AUDIO/EVENTS) once in
        // usim.c:551 before calling sdl3_video_init -- this port has no such
        // single entry point yet, so Init initializes its own subsystem here,
        // the same way Sdl3Audio.Init() does for SDL_INIT_AUDIO.
        if (!SDL.InitSubSystem(SDL.InitFlags.Video))
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: failed to init video subsystem: {SDL.GetError()}");
            return false;
        }

        // scale of 0 means "use native scaling" in usim.ini; real C then
        // forces sdl3_video_scale to 1.0 because it is also used to unscale
        // mouse coordinates (sdl3-video.c:124-127).
        double effectiveScale = scale == 0 ? 1.0 : scale;

        // --- RESOLVE DISPLAY INDEX/POSITION (sdl3-video.c:93-120) ---

        int displayIdx = 0;
        if (windowDisplay > 0) displayIdx = windowDisplay;

        uint[]? displayIds = SDL.GetDisplays(out int ndisplays);
        if (displayIds == null)
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: failed to get displays: {SDL.GetError()}");
            return false;
        }

        if (windowDisplay >= ndisplays)
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: there are {ndisplays} displays (< {windowDisplay} given in usim.ini)");
            return false;
        }

        if (!SDL.GetDisplayBounds(displayIds[displayIdx], out SDL.Rect displayBounds))
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: failed to get display bounds: {SDL.GetError()}");
            return false;
        }

        int displayX = displayBounds.X;
        int displayY = displayBounds.Y;

        // --- SET HINTS (sdl3-video.c:161-170, minus the opengl render-driver hint) ---

        SDL.SetHint(SDL.Hints.RenderVsync, "1");
        SDL.SetHint(SDL.Hints.FramebufferAcceleration, "1");
        SDL.SetHint(SDL.Hints.NoSignalHandlers, "1");

        // --- CREATE MAIN WINDOW AND RENDERER (sdl3-video.c:172-206) ---

        SDL.WindowFlags mainFlags = allowResize ? SDL.WindowFlags.Resizable : 0;

        if (!SDL.CreateWindowAndRenderer(
                "usim",
                (int)(tvWidth * effectiveScale),
                (int)(tvHeight * effectiveScale),
                mainFlags,
                out _mainWindow,
                out _mainRenderer))
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: failed to create main window and renderer: {SDL.GetError()}");
            return false;
        }

        SDL.SetRenderScale(_mainRenderer, (float)effectiveScale, (float)effectiveScale);

        uint mainWindowId = SDL.GetWindowID(_mainWindow);
        if (mainWindowId == 0)
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: failed to get main window ID: {SDL.GetError()}");
            return false;
        }

        // --- CREATE COLOR WINDOW AND RENDERER (sdl3-video.c:208-233) ---

        if (colorTvEnabled)
        {
            if (!SDL.CreateWindowAndRenderer(
                    "usim - Color TV",
                    (int)(colorTvWidth * effectiveScale),
                    (int)(colorTvHeight * effectiveScale),
                    mainFlags,
                    out _colorWindow,
                    out _colorRenderer))
            {
                TraceLog.Instance.Warning(TraceCategory.Display,
                    $"sdl3: failed to create color window and renderer: {SDL.GetError()}");
                return false;
            }

            SDL.SetRenderScale(_colorRenderer, (float)effectiveScale, (float)effectiveScale);
        }

        // --- SET WINDOW POSITION (sdl3-video.c:235-261) ---

        if (SDL.SetWindowPosition(_mainWindow, windowPositionX + displayX, windowPositionY + displayY))
        {
            if (_colorWindow != IntPtr.Zero)
            {
                int colorX = (int)(windowPositionX + displayX + tvWidth * effectiveScale + 64);
                int colorY = (int)(windowPositionY + displayY + (tvHeight * effectiveScale - colorTvHeight) / 2);

                if (!SDL.SetWindowPosition(_colorWindow, colorX, colorY))
                {
                    TraceLog.Instance.Warning(TraceCategory.Display,
                        $"sdl3: failed to set color window position: {SDL.GetError()}");
                }
            }
        }
        else
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: failed to set main window position: {SDL.GetError()}");
        }

        SDL.SetWindowAlwaysOnTop(_mainWindow, windowAlwaysOnTop);
        if (_colorWindow != IntPtr.Zero)
        {
            SDL.SetWindowAlwaysOnTop(_colorWindow, windowAlwaysOnTop);
        }

        // cursor is drawn by CADR, so hide the system cursor
        SDL.HideCursor();

        // SDL disables the screen saver when a window is created; explicitly
        // re-enable it, matching real C.
        SDL.EnableScreenSaver();

        if (!string.IsNullOrEmpty(windowTitle))
        {
            SDL.SetWindowTitle(_mainWindow, windowTitle);
        }

        // --- CHECK IF VSYNC IS REALLY ON (sdl3-video.c:315-328) ---
        // main renderer only -- real C never repeats this check for the
        // color-TV renderer.

        if (SDL.GetRenderVSync(_mainRenderer, out int vsync))
        {
            if (vsync == 1)
            {
                TraceLog.Instance.Info(TraceCategory.Display, "sdl3: vsync is on");
            }
            else
            {
                TraceLog.Instance.Warning(TraceCategory.Display,
                    "sdl3: vsync is off, 60Hz updates will have unknown timings.");
            }
        }
        else
        {
            TraceLog.Instance.Error(TraceCategory.Display,
                $"sdl3: failed to get render vsync: {SDL.GetError()}");
            return false;
        }

        // --- SET RENDER LOGICAL PRESENTATION (sdl3-video.c:330-358) ---

        if (!SDL.SetRenderLogicalPresentation(
                _mainRenderer, (int)tvWidth, (int)tvHeight, SDL.RendererLogicalPresentation.Letterbox))
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: failed to set main renderer scale: {SDL.GetError()}");
            return false;
        }

        if (_colorRenderer != IntPtr.Zero)
        {
            if (!SDL.SetRenderLogicalPresentation(
                    _colorRenderer, (int)colorTvWidth, (int)colorTvHeight, SDL.RendererLogicalPresentation.Letterbox))
            {
                TraceLog.Instance.Warning(TraceCategory.Display,
                    $"sdl3: failed to set color renderer scale: {SDL.GetError()}");
                return false;
            }
        }

        // --- CREATE TEXTURE AND SET SCALE MODE (sdl3-video.c:360-397) ---

        _mainTexture = SDL.CreateTexture(
            _mainRenderer, SDL.PixelFormat.ARGB8888, SDL.TextureAccess.Streaming, (int)tvWidth, (int)tvHeight);

        if (_mainTexture == IntPtr.Zero)
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: failed to create main texture: {SDL.GetError()}");
            return false;
        }

        if (_colorRenderer != IntPtr.Zero)
        {
            _colorTexture = SDL.CreateTexture(
                _colorRenderer, SDL.PixelFormat.ARGB8888, SDL.TextureAccess.Streaming,
                (int)colorTvWidth, (int)colorTvHeight);

            if (_colorTexture == IntPtr.Zero)
            {
                TraceLog.Instance.Warning(TraceCategory.Display,
                    $"sdl3: failed to create color texture: {SDL.GetError()}");
                return false;
            }
        }

        SetScaleMode(UseLinearFiltering: true);

        // otherwise color_window has the focus
        SDL.RaiseWindow(_mainWindow);

        return true;
    }

    /// <summary>
    /// Port of sdl3_video_present_main: copies frameBuffer (already-unpacked
    /// BGRA32 bytes, see Tv.FrameBuffer) into the main texture and presents
    /// it. Does not throw on SDL failures; logs a warning and returns,
    /// matching real C's own ERR-and-return behavior.
    /// </summary>
    public void PresentMain(byte[] frameBuffer)
    {
        if (_mainTexture == IntPtr.Zero || _mainRenderer == IntPtr.Zero) return;

        int pitch = (int)(_tvWidth * 4);

        if (!SDL.UpdateTexture(_mainTexture, IntPtr.Zero, frameBuffer, pitch))
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: failed to update main texture: {SDL.GetError()}");
            return;
        }

        if (!SDL.RenderClear(_mainRenderer))
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: failed to render clear main: {SDL.GetError()}");
            return;
        }

        if (!SDL.RenderTexture(_mainRenderer, _mainTexture, IntPtr.Zero, IntPtr.Zero))
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: failed to render main texture: {SDL.GetError()}");
            return;
        }

        if (!SDL.RenderPresent(_mainRenderer))
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: failed to render present main: {SDL.GetError()}");
        }
    }

    /// <summary>
    /// Port of sdl3_video_present_color. A true no-op (matching real C's
    /// "if (color_renderer == NULL) return true;" at sdl3-video.c:523) when
    /// Init was called with colorTvEnabled: false, so callers do not need to
    /// guard this call themselves.
    /// </summary>
    public void PresentColor(byte[] frameBuffer)
    {
        if (_colorRenderer == IntPtr.Zero || _colorTexture == IntPtr.Zero) return;

        int pitch = (int)(_colorTvWidth * 4);

        if (!SDL.UpdateTexture(_colorTexture, IntPtr.Zero, frameBuffer, pitch))
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: failed to update color texture: {SDL.GetError()}");
            return;
        }

        if (!SDL.RenderClear(_colorRenderer))
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: failed to render clear color: {SDL.GetError()}");
            return;
        }

        if (!SDL.RenderTexture(_colorRenderer, _colorTexture, IntPtr.Zero, IntPtr.Zero))
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: failed to render texture color: {SDL.GetError()}");
            return;
        }

        if (!SDL.RenderPresent(_colorRenderer))
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: failed to render present color: {SDL.GetError()}");
        }
    }

    /// <summary>
    /// Port of sdl3_video_update_window_title.
    /// </summary>
    public void SetWindowTitle(string title)
    {
        if (_mainWindow == IntPtr.Zero) return;
        SDL.SetWindowTitle(_mainWindow, title);
    }

    /// <summary>
    /// Port of sdl3_video_set_scalemode: true maps to SDL_SCALEMODE_LINEAR,
    /// false to SDL_SCALEMODE_NEAREST, applied to both textures (the color
    /// texture only if it exists), matching real C exactly.
    /// </summary>
    public void SetScaleMode(bool UseLinearFiltering)
    {
        SDL.ScaleMode scaleMode = UseLinearFiltering ? SDL.ScaleMode.Linear : SDL.ScaleMode.Nearest;

        if (_mainTexture == IntPtr.Zero) return;

        if (SDL.SetTextureScaleMode(_mainTexture, scaleMode))
        {
            if (_colorTexture != IntPtr.Zero)
            {
                if (!SDL.SetTextureScaleMode(_colorTexture, scaleMode))
                {
                    TraceLog.Instance.Warning(TraceCategory.Display,
                        $"sdl3: failed to set color texture scale mode: {SDL.GetError()}");
                }
            }
        }
        else
        {
            TraceLog.Instance.Warning(TraceCategory.Display,
                $"sdl3: failed to set main texture scale mode: {SDL.GetError()}");
        }
    }

    /// <summary>
    /// Port of sdl3_video_quit: destroys both windows' textures, renderers,
    /// and windows if they were created. Safe to call multiple times and
    /// safe to call when Init was never called or failed partway through.
    /// </summary>
    public void Dispose()
    {
        if (_mainTexture != IntPtr.Zero) { SDL.DestroyTexture(_mainTexture); _mainTexture = IntPtr.Zero; }
        if (_mainRenderer != IntPtr.Zero) { SDL.DestroyRenderer(_mainRenderer); _mainRenderer = IntPtr.Zero; }
        if (_mainWindow != IntPtr.Zero) { SDL.DestroyWindow(_mainWindow); _mainWindow = IntPtr.Zero; }

        if (_colorTexture != IntPtr.Zero) { SDL.DestroyTexture(_colorTexture); _colorTexture = IntPtr.Zero; }
        if (_colorRenderer != IntPtr.Zero) { SDL.DestroyRenderer(_colorRenderer); _colorRenderer = IntPtr.Zero; }
        if (_colorWindow != IntPtr.Zero) { SDL.DestroyWindow(_colorWindow); _colorWindow = IntPtr.Zero; }

        GC.SuppressFinalize(this);
    }
}
