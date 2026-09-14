// Tv.cs - Faithful port of usim/tv.c (monochrome TV display). Replaces the
// entirely-invented Display.cs. See
// docs/superpowers/specs/2026-09-11-monochrome-tv-design.md.

using System;

namespace Usim;

public class Tv
{
    private const int MaxWords = 0x8000; // tv_screen_buffer's real allocated size (32K words)
    private const uint Black = 0xFF000000u; // usim/tv.c's BLACK
    private const uint White = 0xFFFFFFFFu; // usim/tv.c's WHITE

    public uint Width { get; }
    public uint Height { get; }

    // BGRA32, sized to the FULL possible screen-buffer address range
    // (MaxWords*32 pixels), not Width*Height -- matches the real C's own
    // oversized tv_bitmap[1024*1024] (== MaxWords*32 exactly). See spec's
    // Decisions for why this must not be sized to Width*Height instead.
    public byte[] FrameBuffer { get; } = new byte[MaxWords * 32 * 4];

    private readonly UCode _ucode;
    private readonly uint[] _screenBuffer = new uint[MaxWords];
    private readonly byte[] _syncRam = new byte[4096];
    private uint _mode;
    private uint _syncPtr;
    private uint _vertSpacing;
    private uint _foreground;
    private uint _background;

    public Tv(UCode ucode, uint width, uint height)
    {
        _ucode = ucode;
        Width = width;
        Height = height;
        Reset();
    }

    /// <summary>
    /// Faithful port of tv_reset (usim/tv.c:201-212), with one deliberate
    /// deviation: uses the full-alpha Black/White constants here too,
    /// instead of the real C's literal zero-alpha 0x000000/0xffffff for
    /// this function specifically -- see this plan's Global Constraints.
    /// </summary>
    public void Reset()
    {
        _mode = 0;
        Array.Clear(_screenBuffer);
        _background = Black;
        _foreground = White;
        Array.Clear(FrameBuffer);
    }

    /// <summary>Faithful port of tv_screen_read (usim/tv.c:214-218).</summary>
    public uint ScreenRead(uint offset) => _screenBuffer[offset];

    /// <summary>
    /// Faithful port of tv_screen_write (usim/tv.c:220-246). Unpacks
    /// LSB-first -- bit 0 of v is pixel offset*32+0, bit 1 is pixel
    /// offset*32+1, etc. This is the exact bit order this port fixes; the
    /// previous invented Display.cs unpacked MSB-first.
    /// </summary>
    public void ScreenWrite(uint offset, uint v)
    {
        _screenBuffer[offset] = v;

        uint pixelBase = offset * 32;
        uint word = v;
        for (int i = 0; i < 32; i++)
        {
            uint color = (word & 1) != 0 ? _foreground : _background;
            int byteIndex = (int)((pixelBase + (uint)i) * 4);
            FrameBuffer[byteIndex + 0] = (byte)(color & 0xFF);         // B
            FrameBuffer[byteIndex + 1] = (byte)((color >> 8) & 0xFF);  // G
            FrameBuffer[byteIndex + 2] = (byte)((color >> 16) & 0xFF); // R
            FrameBuffer[byteIndex + 3] = (byte)((color >> 24) & 0xFF); // A
            word >>= 1;
        }
    }

    /// <summary>
    /// Faithful port of tv_control_read (usim/tv.c:251-281). Offsets 2/3
    /// are write-only in the real C (no case for them in the read switch)
    /// -- they correctly fall to default here too, not a separate check.
    /// </summary>
    public uint ControlRead(uint offset)
    {
        switch (offset)
        {
            case 0:
                {
                    TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                        $"tv: read mode: 0x{_mode:X}");
                    return _mode;
                }

            case 1:
                {
                    uint v = IsSyncPromEnabled() ? 0u : _syncRam[_syncPtr];
                    if (!IsSyncPromEnabled())
                        TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                            $"tv: read sync_ram[0x{_syncPtr:X}] = 0x{v:X}");
                    TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                        $"tv: read sync data: 0x{v:X}");
                    return v;
                }

            default:
                TraceLog.Instance.Warning(TraceCategory.Display,
                    $"tv: read invalid offset:{offset}");
                _ucode.BusInterface.SetXbusNxm();
                return 0;
        }
    }

    /// <summary>Faithful port of tv_control_write (usim/tv.c:283-335).</summary>
    public void ControlWrite(uint offset, uint v)
    {
        switch (offset)
        {
            case 0:
                {
                    TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                        $"tv: write mode: 0x{v:X} [old:0x{_mode:X}]");
                    bool wasBow = IsBlackOnWhite();
                    _mode = v;
                    bool isBow = IsBlackOnWhite();
                    // Recomputed unconditionally on every mode write,
                    // matching the real C exactly -- not gated on the BOW
                    // bit actually changing (see spec's Register Model).
                    _foreground = isBow ? Black : White;
                    _background = isBow ? White : Black;
                    if (wasBow != isBow)
                    {
                        uint wordCount = (Width * Height) / 32;
                        for (uint i = 0; i < wordCount; i++)
                        {
                            ScreenWrite(i, ScreenRead(i));
                        }
                    }
                }
                break;

            case 1:
                TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                    $"tv: write sync data: 0x{v:X}");
                if (!IsSyncPromEnabled())
                {
                    _syncRam[_syncPtr] = (byte)(v & 0xFF);
                    TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                        $"tv: write sync_ram[0x{_syncPtr:X}] = 0x{_syncRam[_syncPtr]:X}");
                }
                break;

            case 2:
                TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                    $"tv: write sync pointer: 0x{v:X}");
                _syncPtr = v & 0x0FFF;
                break;

            case 3:
                TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Debug,
                    $"tv: write vert spacing: 0x{v:X}");
                _vertSpacing = v;
                break;

            default:
                TraceLog.Instance.Warning(TraceCategory.Display,
                    $"tv: write invalid offset:{offset}");
                _ucode.BusInterface.SetXbusNxm();
                break;
        }
    }

    /// <summary>
    /// Faithful port of tv_assert_interrupt (usim/tv.c:158-167). Called
    /// once per ~16ms tick from both WpfBackend.Tick() and
    /// MachineControl.Run()'s headless loop (Task 3) -- matches the real
    /// C's run_at_60hz() cadence. Does NOT re-render anything; screen
    /// writes already keep FrameBuffer current (see ScreenWrite above).
    /// </summary>
    public void Tick()
    {
        if (IsInterruptEnabled())
        {
            _mode |= 1u << 4; // VERT FLAG / interrupt-request bit
            _ucode.AssertXbusInterrupt();
        }
    }

    private bool IsBlackOnWhite() => (_mode & 0x4) != 0;
    private bool IsInterruptEnabled() => (_mode & 0x8) != 0;
    private bool IsSyncPromEnabled() => (_vertSpacing & 0x80) == 0;
}
