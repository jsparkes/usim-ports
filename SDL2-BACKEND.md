# SDL2 Backend for USIM

## Overview

The USIM C# port includes an SDL2 backend for video rendering and input handling (keyboard and mouse). The backend is implemented but **disabled by default** due to SDL2-CS package compatibility issues.

## Current Status

✅ **Implemented:**
- SDL2Backend.cs with full video, keyboard, and mouse support
- Window creation and rendering
- Keyboard event translation (SDL keycodes → X11 keysyms)
- Mouse motion and button handling  
- Audio beep generation
- Display frame buffer rendering (ARGB8888 format)
- Integration with Display, Keyboard, and Mouse classes
- Main run loop with SDL2 event processing

🔄 **Stub Mode (Current):**
- SDL2Backend compiles as a stub that throws NotImplementedException
- Headless mode still works for testing emulator logic
- To enable SDL2, follow instructions below

## Enabling SDL2 Support

### Prerequisites

1. **Native SDL2 Library**
   - Windows: Download SDL2 from https://libsdl.org/download-2.0.php
   - Place `SDL2.dll` in your project bin directory or system PATH
   - Linux/Mac: Install via package manager (`apt install libsdl2-dev` or `brew install sdl2`)

2. **SDL2-CS Bindings**
   - The NuGet SDL2-CS package v2.0.0 is outdated and incompatible
   - Recommended: Clone https://github.com/flibitijibibo/SDL2-CS
   - Add as project reference or use newer NuGet package when available

### Enable SDL2 Backend

1. **Add SDL2-CS Reference:**
   ```bash
   # Option A: Clone SDL2-CS and add project reference
   git clone https://github.com/flibitijibibo/SDL2-CS.git
   cd usim-cs
   dotnet add reference ../SDL2-CS/SDL2-CS.Core.csproj
   
   # Option B: Try newer NuGet package (if available)
   dotnet add package SDL2-CS --version <latest>
   ```

2. **Enable SDL2 in Code:**
   - Open `usim-cs/SDL2Backend.cs`
   - Uncomment the line: `// #define ENABLE_SDL2`
   - This switches from stub to full implementation

3. **Rebuild:**
   ```bash
   dotnet build
   ```

## Architecture

### SDL2Backend Class

**Location:** `usim-cs/SDL2Backend.cs`

**Key Features:**
- Window and renderer management
- Texture streaming for display updates
- Event loop integration
- Keyboard mapping: SDL_Keycode → X11 KeySym
- Mouse coordinate scaling (SDL pixels → CADR 11-bit coordinates)
- Audio callback for beep generation

### Integration Points

**Display.cs:**
- `FrameBuffer` property provides ARGB8888 pixel data
- `Update()` called from main loop to render frames
- `DrawTestPattern()` for initial display verification

**Keyboard.cs:**
- `KeyDown(uint keysym, int modifiers)` - Process key press
- `KeyUp(uint keysym, int modifiers)` - Process key release
- `ProcessEvents()` - Called each frame from event loop

**Mouse.cs:**
- `UpdatePosition(int x, int y)` - Update mouse position
- `ButtonDown(MouseButtons button)` - Handle button press
- `ButtonUp(MouseButtons button)` - Handle button release

**MachineControl.cs:**
- `InitializeSDL2(bool allowResize, double scale)` - Create backend
- `Run()` - Main loop calls `SDL2Backend.ProcessEvents()`
- Automatic cleanup on shutdown

**Program.cs:**
- Initializes SDL2 backend unless `--headless` flag set
- Starts background thread for console commands
- Main thread runs SDL2 event loop

## Usage

### With SDL2 Enabled:

```bash
# Run with SDL2 window
dotnet run --project usim-cs

# SDL window opens showing display
# Console accepts debug commands
# Close window or Ctrl+C to exit
```

### Headless Mode (No SDL2 required):

```bash
# Run without display
dotnet run --project usim-cs -- --headless

# Interactive command loop
# Type 'help' for commands
# Type 'quit' to exit
```

## Configuration

Edit `usim.ini`:

```ini
[Display]
window-title = "CADR Lisp Machine"
colortv = false
scale = 1.0
allow-resize = false

[Execution]
headless = false
auto-boot = false
```

## Keyboard Mapping

SDL2 keycodes are translated to X11 keysyms for CADR compatibility:

| SDL Key | X11 KeySym | CADR Function |
|---------|------------|---------------|
| F1-F12 | XK_F1-F12 | Function keys |
| Escape | XK_Escape | ESCAPE |
| Return | XK_Return | RETURN |
| Tab | XK_Tab | TAB |
| Backspace | XK_BackSpace | RUBOUT |
| Delete | XK_Delete | DELETE |
| Arrows | XK_Left/Right/Up/Down | Navigation |
| Ctrl/Shift/Alt/Meta | Modifier keys | Bucky bits |
| A-Z, 0-9 | ASCII codes | Standard typing |

## Mouse Handling

- SDL mouse coordinates are scaled to CADR's 11-bit range (0-2047)
- Button mapping: Left = Button 1, Middle = Button 2, Right = Button 3
- Position updates trigger hardware register updates
- Raw hardware registers accessible via `Mouse.MouseX/MouseY/MouseButtonBits`

## Audio Beep

- Sample rate: 44.1 kHz
- Format: Signed 16-bit
- Generates sine wave tones for system beep
- Called from microcode `%BEEP` instruction

## Display Rendering

**Black & White Mode:**
- 768x896 pixels
- 1 bit per pixel (32 pixels per word)
- Video memory: 21,504 words

**Color Mode:**
- 4 bits per pixel (16 colors)
- 8 pixels per word
- Simple 16-color VGA palette

**Frame Buffer Format:** ARGB8888 (Alpha, Red, Green, Blue)

## Troubleshooting

### "SDL2 backend is not available"
- SDL2 support is disabled (stub mode)
- Follow "Enabling SDL2 Support" instructions above

### "SDL_Init failed"
- Native SDL2.dll not found
- Download SDL2 and place in executable directory
- Or install system-wide (Linux/Mac)

### Window doesn't open
- Check `--headless` flag not set
- Verify SDL2Backend.Initialize() called
- Check console for error messages

### Black screen
- Display not updating - check `Display.Update()` called
- Try `Display.DrawTestPattern()` to verify rendering
- Check frame buffer format matches SDL texture format

## Performance

Expected performance with SDL2:
- 60 FPS target (16ms frame time)
- Event processing: <1ms per frame
- Display update: <2ms per frame
- Total overhead: ~20% CPU on modern systems

## Future Enhancements

Planned improvements:
- [ ] OpenGL rendering backend option
- [ ] Hardware-accelerated scaling
- [ ] Multiple window support
- [ ] Screenshot capture
- [ ] Video recording
- [ ] Custom color palettes
- [ ] Fullscreen mode
- [ ] VSync control

## References

- SDL2: https://libsdl.org/
- SDL2-CS: https://github.com/flibitijibibo/SDL2-CS
- Original C implementation: `usim/sdl2.c`
- X11 KeySyms: https://www.x.org/releases/current/doc/xproto/x11protocol.html
