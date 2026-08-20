// Display.cs - Display/TV emulation
// Converted from tv.h and tv.c

using System;

namespace Usim;

/// <summary>
/// Display modes
/// </summary>
public enum DisplayMode
{
    BlackAndWhite,
    Color
}

/// <summary>
/// Display system for CADR
/// Emulates the TV raster display
/// </summary>
public class Display
{
    // Display dimensions
    public const int WIDTH = 768;
    public const int HEIGHT = 896;
    
    // Video memory
    private readonly uint[] _videoMemory;
    private const int VIDEO_MEMORY_SIZE = WIDTH * HEIGHT / 32; // 32 pixels per word
    
    // Display mode
    public DisplayMode Mode { get; set; }
    
    // Refresh rate
    public int RefreshRate { get; set; } = 60;
    
    // Video memory base address
    public uint VideoMemoryBase { get; set; }
    
    // Vertical blank interrupt
    public bool VBlankEnabled { get; set; }
    public event Action? VBlankInterrupt;
    
    // Statistics
    public ulong FrameCount { get; private set; }
    public DateTime LastFrameTime { get; private set; }
    public double CurrentFPS { get; private set; }
    
    // Frame buffer for rendering
    public byte[] FrameBuffer { get; private set; }
    
    public Display()
    {
        _videoMemory = new uint[VIDEO_MEMORY_SIZE];
        FrameBuffer = new byte[WIDTH * HEIGHT * 4]; // B,G,R,A format for WPF's WriteableBitmap (Pbgra32)
        LastFrameTime = DateTime.Now;
        Initialize();
    }
    
    /// <summary>
    /// Initialize display
    /// </summary>
    public void Initialize()
    {
        Array.Clear(_videoMemory);
        Array.Clear(FrameBuffer);
        FrameCount = 0;
        Mode = DisplayMode.BlackAndWhite;
        VideoMemoryBase = 0;
        VBlankEnabled = false;
    }
    
    /// <summary>
    /// Write to video memory
    /// </summary>
    public void WriteVideoMemory(uint address, uint value)
    {
        uint offset = address - VideoMemoryBase;
        if (offset < VIDEO_MEMORY_SIZE)
        {
            _videoMemory[offset] = value;
        }
    }
    
    /// <summary>
    /// Read from video memory
    /// </summary>
    public uint ReadVideoMemory(uint address)
    {
        uint offset = address - VideoMemoryBase;
        if (offset < VIDEO_MEMORY_SIZE)
        {
            return _videoMemory[offset];
        }
        return 0;
    }
    
    /// <summary>
    /// Update display - render video memory to frame buffer
    /// </summary>
    public void Update()
    {
        // Calculate FPS
        var now = DateTime.Now;
        var elapsed = (now - LastFrameTime).TotalSeconds;
        if (elapsed > 0)
        {
            CurrentFPS = 1.0 / elapsed;
        }
        LastFrameTime = now;
        
        // Render video memory to frame buffer
        RenderFrame();
        
        FrameCount++;
        
        // Trigger VBlank interrupt
        if (VBlankEnabled)
        {
            VBlankInterrupt?.Invoke();
        }
    }
    
    /// <summary>
    /// Render video memory to RGBA frame buffer
    /// </summary>
    private void RenderFrame()
    {
        if (Mode == DisplayMode.BlackAndWhite)
        {
            RenderBlackAndWhite();
        }
        else
        {
            RenderColor();
        }
    }
    
    /// <summary>
    /// Render in black and white mode
    /// Each bit represents one pixel
    /// WPF's WriteableBitmap expects Pbgra32 (B,G,R,A byte order)
    /// </summary>
    private void RenderBlackAndWhite()
    {
        int bufferIndex = 0;
        
        for (int y = 0; y < HEIGHT; y++)
        {
            for (int x = 0; x < WIDTH; x += 32)
            {
                int wordIndex = (y * WIDTH + x) / 32;
                if (wordIndex >= _videoMemory.Length)
                    break;
                
                uint word = _videoMemory[wordIndex];
                
                // Each bit is one pixel
                for (int bit = 0; bit < 32 && x + bit < WIDTH; bit++)
                {
                    bool pixelOn = ((word >> (31 - bit)) & 1) != 0;
                    byte value = pixelOn ? (byte)255 : (byte)0;

                    FrameBuffer[bufferIndex++] = value; // B
                    FrameBuffer[bufferIndex++] = value; // G
                    FrameBuffer[bufferIndex++] = value; // R
                    FrameBuffer[bufferIndex++] = 255;   // A
                }
            }
        }
    }
    
    /// <summary>
    /// Render in color mode
    /// Color mode uses multiple bits per pixel
    /// WPF's WriteableBitmap expects Pbgra32 (B,G,R,A byte order)
    /// </summary>
    private void RenderColor()
    {
        // Simplified color rendering
        // Full color TV implementation is more complex
        int bufferIndex = 0;
        
        for (int y = 0; y < HEIGHT; y++)
        {
            for (int x = 0; x < WIDTH; x += 8) // 8 pixels per word in color mode
            {
                int wordIndex = (y * WIDTH + x) / 8;
                if (wordIndex >= _videoMemory.Length)
                    break;
                
                uint word = _videoMemory[wordIndex];
                
                // 4 bits per pixel (16 colors)
                for (int pixel = 0; pixel < 8 && x + pixel < WIDTH; pixel++)
                {
                    int shift = (7 - pixel) * 4;
                    byte colorIndex = (byte)((word >> shift) & 0x0F);

                    // Simple color palette
                    (byte r, byte g, byte b) = GetColor(colorIndex);

                    FrameBuffer[bufferIndex++] = b;
                    FrameBuffer[bufferIndex++] = g;
                    FrameBuffer[bufferIndex++] = r;
                    FrameBuffer[bufferIndex++] = 255; // A
                }
            }
        }
    }
    
    /// <summary>
    /// Get RGB color from palette index
    /// </summary>
    private (byte r, byte g, byte b) GetColor(byte index)
    {
        // Simple 16-color palette
        return index switch
        {
            0 => (0, 0, 0),         // Black
            1 => (0, 0, 170),       // Blue
            2 => (0, 170, 0),       // Green
            3 => (0, 170, 170),     // Cyan
            4 => (170, 0, 0),       // Red
            5 => (170, 0, 170),     // Magenta
            6 => (170, 85, 0),      // Brown
            7 => (170, 170, 170),   // Light Gray
            8 => (85, 85, 85),      // Dark Gray
            9 => (85, 85, 255),     // Light Blue
            10 => (85, 255, 85),    // Light Green
            11 => (85, 255, 255),   // Light Cyan
            12 => (255, 85, 85),    // Light Red
            13 => (255, 85, 255),   // Light Magenta
            14 => (255, 255, 85),   // Yellow
            15 => (255, 255, 255),  // White
            _ => (0, 0, 0)
        };
    }
    
    /// <summary>
    /// Clear display
    /// </summary>
    public void Clear()
    {
        Array.Clear(_videoMemory);
        Array.Clear(FrameBuffer);
    }
    
    /// <summary>
    /// Set pixel (for testing)
    /// </summary>
    public void SetPixel(int x, int y, bool value)
    {
        if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT)
            return;
        
        int wordIndex = (y * WIDTH + x) / 32;
        int bitIndex = 31 - (x % 32);
        
        if (value)
        {
            _videoMemory[wordIndex] |= (uint)(1 << bitIndex);
        }
        else
        {
            _videoMemory[wordIndex] &= ~(uint)(1 << bitIndex);
        }
    }
    
    /// <summary>
    /// Draw test pattern
    /// </summary>
    public void DrawTestPattern()
    {
        // Draw checkerboard
        for (int y = 0; y < HEIGHT; y++)
        {
            for (int x = 0; x < WIDTH; x++)
            {
                bool value = ((x / 32) + (y / 32)) % 2 == 0;
                SetPixel(x, y, value);
            }
        }
    }
}
