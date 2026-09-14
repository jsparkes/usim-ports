// ColorTv.cs - Faithful port of usim/colortv.c (color TV display). A
// genuinely separate device from Tv (monochrome), not a mode of it. See
// docs/superpowers/specs/2026-09-14-color-tv-design.md.

using System;

namespace Usim;

public class ColorTv
{
    private const int MaxWords = 0x8000; // colortv_screen_buffer's real allocated size (32K words)

    public uint Width { get; } = 576;
    public uint Height { get; } = 454;

    // Sized to the exact visible geometry -- unlike Tv's deliberately
    // oversized FrameBuffer, color TV's addressable screen-buffer range
    // (576*454/8 = 32,751 words) is already just under MaxWords, so there
    // is no writable-but-off-screen gap to guard against here.
    public byte[] FrameBuffer { get; } = new byte[576 * 454 * 4];

    private readonly UCode _ucode;
    private readonly uint[] _screenBuffer = new uint[MaxWords];
    private readonly uint[] _colorMap = new uint[64];
    private readonly byte[] _syncRam = new byte[4096];
    private uint _mode;
    private uint _syncPtr;
    private uint _vertSpacing;
    private bool _syncPromEnabled;

    public ColorTv(UCode ucode)
    {
        _ucode = ucode;
    }

    /// <summary>
    /// Faithful port of colortv_bus_reset (usim/colortv.c:205-208), which
    /// has a genuinely empty body -- color TV state is never cleared by
    /// any reset in the real C, unlike Tv.Reset(). Kept as an explicit
    /// method (rather than omitted) purely for wiring symmetry with Tv.
    /// </summary>
    public void Reset()
    {
    }

    /// <summary>Faithful port of colortv_screen_read (usim/colortv.c:68-72).</summary>
    public uint ScreenRead(uint offset) => _screenBuffer[offset];

    /// <summary>
    /// Faithful port of colortv_screen_write (usim/colortv.c:74-79). Unlike
    /// Tv.ScreenWrite, this does NOT unpack into FrameBuffer -- real color
    /// TV rendering happens once per frame in Tick(), not per write.
    /// </summary>
    public void ScreenWrite(uint offset, uint v)
    {
        _screenBuffer[offset] = v;
    }

    /// <summary>Faithful port of colortv_control_read (usim/colortv.c:82-113).</summary>
    public uint ControlRead(uint offset)
    {
        switch (offset)
        {
            case 0:
                // Bits 5/6/7 (VSYNC/HSYNC/sync-PROM-enabled) are always 0
                // in this port -- see the design's Decisions section.
                return _mode;

            case 1:
                return _syncPromEnabled ? 0u : _syncRam[_syncPtr];

            default:
                TraceLog.Instance.Warning(TraceCategory.Display,
                    $"colortv: read invalid offset:{offset}");
                _ucode.BusInterface.SetXbusNxm();
                return 0;
        }
    }

    /// <summary>Faithful port of colortv_control_write (usim/colortv.c:115-197).</summary>
    public void ControlWrite(uint offset, uint v)
    {
        switch (offset)
        {
            case 0:
                _mode = v & 0x1F;
                break;

            case 1:
                if (!_syncPromEnabled)
                {
                    _syncRam[_syncPtr] = (byte)(v & 0xFF);
                }
                break;

            case 2:
                _syncPtr = v & 0x0FFF;
                break;

            case 3:
                _syncPromEnabled = (v & 0x80) == 0;
                _vertSpacing = v & 0x7F;
                break;

            case 4:
                {
                    uint colorChannelValue = 255 - ((v >> 8) & 0xFF);
                    uint colorChannel = (v >> 6) & 0x3;
                    uint location = v & 0x3F;
                    uint currentValue = _colorMap[location];
                    uint newValue;
                    switch (colorChannel)
                    {
                        case 0:
                            newValue = (currentValue & 0x0000FFFF) | (colorChannelValue << 16);
                            break;
                        case 1:
                            newValue = (currentValue & 0x00FF00FF) | (colorChannelValue << 8);
                            break;
                        case 2:
                            newValue = (currentValue & 0x00FFFF00) | colorChannelValue;
                            break;
                        default:
                            TraceLog.Instance.Warning(TraceCategory.Display,
                                $"colortv: write invalid color channel:{colorChannel} (hardware-undefined 2-bit encoding 3)");
                            return;
                    }
                    _colorMap[location] = 0xFF000000 | newValue;
                }
                break;

            default:
                TraceLog.Instance.Warning(TraceCategory.Display,
                    $"colortv: write invalid offset:{offset}");
                _ucode.BusInterface.SetXbusNxm();
                break;
        }
    }

    private bool IsInterruptEnabled() => (_mode & 0x8) != 0;

    /// <summary>
    /// Faithful port of colortv_assert_interrupt (usim/colortv.c:49-58),
    /// combined with the two real enable-gates that wrap rendering/
    /// interrupt assertion in usim.c/sdl3-video.c into one early-return.
    /// </summary>
    public void Tick()
    {
        if (!UsimState.ColorTvEnabled)
        {
            return;
        }

        UnpackFrameBuffer();

        if (IsInterruptEnabled())
        {
            _mode |= 1u << 4;
            _ucode.AssertXbusInterrupt();
        }
    }

    /// <summary>
    /// Faithful port of sdl3_video_present_color's unpacking loop
    /// (usim/sdl3-video.c:544-577), minus the hsync/vsync toggling and
    /// per-scanline timing (out of scope -- see the design). LSB-nibble-
    /// first: bits 0-3 of a word is the first pixel in its run of 8.
    /// </summary>
    private void UnpackFrameBuffer()
    {
        uint wordIndex = 0;
        uint bitsInWord = 0;
        uint word = _screenBuffer[0];

        for (uint y = 0; y < Height; y++)
        {
            for (uint x = 0; x < Width; x++)
            {
                uint pixel = word & 0xF;
                uint color = _colorMap[pixel];
                uint byteIndex = (y * Width + x) * 4;
                FrameBuffer[byteIndex + 0] = (byte)(color & 0xFF);         // B
                FrameBuffer[byteIndex + 1] = (byte)((color >> 8) & 0xFF); // G
                FrameBuffer[byteIndex + 2] = (byte)((color >> 16) & 0xFF); // R
                FrameBuffer[byteIndex + 3] = (byte)((color >> 24) & 0xFF); // A

                bitsInWord += 4;
                if (bitsInWord == 32)
                {
                    bitsInWord = 0;
                    wordIndex++;
                    word = _screenBuffer[wordIndex];
                }
                else
                {
                    word >>= 4;
                }
            }
        }
    }
}
