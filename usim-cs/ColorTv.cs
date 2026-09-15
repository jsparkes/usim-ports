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
    // (576*454/8 = 32,688 words) is already just under MaxWords, so there
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
    private bool _hsync;
    private bool _vsync;

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
    public uint ScreenRead(uint offset)
    {
        uint v = _screenBuffer[offset];
        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
            $"colortv: screen read: offset:{offset} v:0x{_screenBuffer[offset]:X}");
        return v;
    }

    /// <summary>
    /// Faithful port of colortv_screen_write (usim/colortv.c:74-79). Unlike
    /// Tv.ScreenWrite, this does NOT unpack into FrameBuffer -- real color
    /// TV rendering happens once per frame in Tick(), not per write.
    /// </summary>
    public void ScreenWrite(uint offset, uint v)
    {
        _screenBuffer[offset] = v;
        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
            $"colortv: screen write: offset:{offset} v:0x{v:X}");
    }

    /// <summary>Faithful port of colortv_control_read (usim/colortv.c:82-113).</summary>
    public uint ControlRead(uint offset)
    {
        switch (offset)
        {
            case 0:
                {
                    // Bit 7 (sync-PROM-enabled) reflects the real, deterministic
                    // _syncPromEnabled field (set on offset-3 writes) -- it is
                    // NOT a hardware timing signal, unlike bits 5/6. Bits 5/6
                    // (VSYNC/HSYNC) reflect _vsync/_hsync, which Tick() toggles
                    // once per call -- see Tick()'s comment for why this
                    // (not literally always 0) is required for correctness.
                    uint result = _mode
                        | (_syncPromEnabled ? 0x80u : 0u)
                        | (_hsync ? 0x40u : 0u)
                        | (_vsync ? 0x20u : 0u);
                    TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                        $"colortv: read mode: 0x{result:X}");
                    return result;
                }

            case 1:
                {
                    uint v = _syncPromEnabled ? 0u : _syncRam[_syncPtr];
                    if (!_syncPromEnabled)
                    {
                        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                            $"colortv: read sync_ram[0x{_syncPtr:X}] = 0x{v:X}");
                    }
                    TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                        $"colortv: read sync data: 0x{v:X}");
                    return v;
                }

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
                TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                    $"colortv: write mode: 0x{v:X}");
                _mode = v & 0x1F;
                break;

            case 1:
                TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                    $"colortv: write sync data: 0x{v:X}");
                if (!_syncPromEnabled)
                {
                    _syncRam[_syncPtr] = (byte)(v & 0xFF);
                    TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                        $"colortv: write sync_ram[0x{_syncPtr:X}] = 0x{_syncRam[_syncPtr]:X}");
                }
                break;

            case 2:
                TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                    $"colortv: write sync pointer: 0x{v:X}");
                _syncPtr = v & 0x0FFF;
                break;

            case 3:
                TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                    $"colortv: write vert spacing: 0x{v:X}");
                _syncPromEnabled = (v & 0x80) == 0;
                _vertSpacing = v & 0x7F;
                break;

            case 4:
                {
                    TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                        $"colortv: write color map: 0x{v:X}");
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
                    TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                        $"colortv: write loc:{location} channel:{colorChannel} value:{colorChannelValue} final:0x{_colorMap[location]:X8}");
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

        // Real hardware toggles VSYNC/HSYNC continuously at video timing
        // rates; this emulator only updates color-TV state once per Tick()
        // (~16ms), so toggling both here is the finest faithful
        // approximation achievable -- but it is NOT optional. Real Lisp
        // (sys/window/color.lisp's WRITE-COLOR-MAP) both explicitly waits
        // for a VSYNC transition (when SYNCHRONIZE=T) and, unconditionally
        // on every call, relies on microcode (%XBUS-WRITE-SYNC) that waits
        // for a full HSYNC clear-then-set transition before writing the
        // color map. Never toggling these bits makes both waits hang
        // forever -- this was a real, confirmed bug in the original design.
        _vsync = !_vsync;
        _hsync = !_hsync;

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
