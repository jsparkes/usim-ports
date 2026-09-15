# Color TV Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a faithful port of `usim/colortv.c` (a genuinely separate device from monochrome TV) and wire it into `BusAdaptor`, `MachineControl`, and `WpfBackend` as a second, independent WPF window.

**Architecture:** Two tasks. Task 1 builds `ColorTv.cs` fully standalone (mirrors `Tv.cs`'s shape) with its own test suite — no wiring into the rest of the app yet. Task 2 wires it in: `BusAdaptor` gains dispatch gated on `UsimState.ColorTvEnabled` (a flag that already exists and is already settable via `--colortv`/config, just never consumed), `MachineControl` always constructs a `ColorTv` (cheap, matching `Tv`'s unconditional construction), and `WpfBackend` creates a second, independent window only when the flag is set.

**Tech Stack:** C#, .NET 8.0, WPF. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-09-14-color-tv-design.md`

## Global Constraints

- No new NuGet dependencies.
- Every octal literal in this plan's code was independently verified — see the spec's Octal Literals table.
- `ScreenWrite` does NOT unpack into `FrameBuffer` — that only happens in `Tick()`, once per frame, matching the real C's lazy per-frame render (unlike monochrome `Tv.ScreenWrite`, which unpacks eagerly). Getting this backwards would silently reintroduce eager rendering that doesn't match `usim/colortv.c`.
- Mode-read bits 5 (VSYNC) and 6 (HSYNC) toggle once per `Tick()` call — this is required, not optional. Real `sys/window/color.lisp`'s `WRITE-COLOR-MAP` (SYNCHRONIZE=T) waits for a VSYNC transition, and its unconditional `%XBUS-WRITE-SYNC` microcode waits for a full HSYNC clear-then-set transition on every call; an always-0 design (the original approach here) makes both hang forever rather than preventing a hang (see spec's Decisions for the corrected rationale). Bit 7 (sync-PROM-enabled) is not a timing signal and must instead reflect the real `_syncPromEnabled` field.
- The hardware-undefined color-map channel encoding (`channel == 3`) is a warning + no-op, not an exception — a deliberate deviation from the real C's `errx()`, matching the same judgment call already made for `tv_init()`'s practically-unreachable fatal path in the monochrome TV port.
- `ColorTv.Reset()` is a deliberate, literal no-op (matching `colortv_bus_reset()`'s genuinely empty body in the real C).
- `ColorTv` is always constructed regardless of `UsimState.ColorTvEnabled` — the enable gate lives only in `BusAdaptor`'s dispatch and `WpfBackend`'s window creation.

---

### Task 1: `ColorTv.cs` — the class itself, fully standalone

**Files:**
- Create: `usim-cs/ColorTv.cs`
- Test: `usim-cs/ColorTvTests.cs` (new)
- Modify: `usim-cs/Program.cs` (CLI wiring for the new test suite)

**Interfaces:**
- Consumes: `UCode.AssertXbusInterrupt()`, `UCode.BusInterface.SetXbusNxm()` (both already real, from earlier phases), `UsimState.ColorTvEnabled` (already exists as `public static bool ColorTvEnabled { get; set; }` in `UsimConstants.cs`).
- Produces (for Task 2): `public ColorTv(UCode ucode)`, `public uint Width { get; }` (576), `public uint Height { get; }` (454), `public byte[] FrameBuffer { get; }`, `public void Reset()`, `public uint ScreenRead(uint offset)`, `public void ScreenWrite(uint offset, uint v)`, `public uint ControlRead(uint offset)`, `public void ControlWrite(uint offset, uint v)`, `public void Tick()`.

This task does not touch `BusAdaptor.cs`, `MachineControl.cs`, or `WpfBackend.cs` — fully testable standalone.

- [ ] **Step 1: Write `usim-cs/ColorTv.cs`**

```csharp
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
```

- [ ] **Step 2: Write `usim-cs/ColorTvTests.cs`**

```csharp
// ColorTvTests.cs - Tests for the faithful ColorTv port (usim/colortv.c).

using System;

namespace Usim;

public static class ColorTvTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== ColorTv Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestScreenWriteDoesNotUnpackEagerly()) passed++; else failed++;
        if (TestTickUnpacksLsbNibbleFirst()) passed++; else failed++;
        if (TestModeWriteMaskAndReadStatusBits()) passed++; else failed++;
        if (TestSyncPromEnabledGating()) passed++; else failed++;
        if (TestColorMapWritePreservesOtherChannels()) passed++; else failed++;
        if (TestColorMapInvalidChannelIsNoOp()) passed++; else failed++;
        if (TestTickAssertsInterruptOnlyWhenEnabled()) passed++; else failed++;
        if (TestTickDoesNothingWhenDisabled()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static ColorTv MakeColorTv() => new ColorTv(new UCode(new MainMemory()));

    private static bool TestScreenWriteDoesNotUnpackEagerly()
    {
        Console.WriteLine("Test: ScreenWrite stores the raw word but does not touch FrameBuffer (unlike Tv.ScreenWrite)");
        try
        {
            var colorTv = MakeColorTv();

            colorTv.ScreenWrite(0, 0xFFFFFFFF);

            Assert(colorTv.ScreenRead(0) == 0xFFFFFFFF, "the raw word round-trips through ScreenRead");
            foreach (byte b in colorTv.FrameBuffer)
            {
                Assert(b == 0, "FrameBuffer is untouched by ScreenWrite alone (no Tick() called)");
                break; // only need to see the array is still all-zero; checking byte 0 is representative
            }

            Console.WriteLine("  ScreenWrite-does-not-unpack test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ScreenWrite-does-not-unpack test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestTickUnpacksLsbNibbleFirst()
    {
        Console.WriteLine("Test: Tick() unpacks the screen buffer into FrameBuffer, LSB-nibble-first, using the color map");
        var saved = UsimState.ColorTvEnabled;
        try
        {
            UsimState.ColorTvEnabled = true;
            var colorTv = MakeColorTv();

            // Color map location 1 -> pure red (R=255,G=0,B=0). Write it via
            // ControlWrite offset 4: location=1, channel 0 (R) with
            // colorChannelValue=255 means (v>>8)&0xFF must be 0 (255-0=255).
            uint channelBits = 0u; // R
            uint locationBits = 1u;
            uint writeValue = (0u << 8) | (channelBits << 6) | locationBits;
            colorTv.ControlWrite(4, writeValue);

            // Screen word: nibble 0 (pixel 0) = location 1 (bits 0-3 = 0001).
            // Nibble 1 (pixel 1) = location 0 (bits 4-7 = 0000) -- a color
            // map entry NEVER written via ControlWrite offset 4, so it's
            // still the array's raw zero-initialized value, alpha included
            // (only an actual write forces alpha to 0xFF -- see ColorTv.cs's
            // ControlWrite case 4). Independently verified via a Python
            // model of the exact channel-write arithmetic before writing
            // this test: an unwritten entry is 0x00000000, fully transparent.
            colorTv.ScreenWrite(0, 0x00000001);

            colorTv.Tick();

            // Pixel 0 should be pure red: B=0x00, G=0x00, R=0xFF, A=0xFF
            Assert(colorTv.FrameBuffer[0] == 0x00 && colorTv.FrameBuffer[1] == 0x00 &&
                   colorTv.FrameBuffer[2] == 0xFF && colorTv.FrameBuffer[3] == 0xFF,
                $"pixel 0 (nibble 0, location 1) is red, got B={colorTv.FrameBuffer[0]:X2} G={colorTv.FrameBuffer[1]:X2} R={colorTv.FrameBuffer[2]:X2} A={colorTv.FrameBuffer[3]:X2}");

            // Pixel 1 should be fully transparent black (location 0, never written): B=G=R=A=0x00
            Assert(colorTv.FrameBuffer[4] == 0x00 && colorTv.FrameBuffer[5] == 0x00 &&
                   colorTv.FrameBuffer[6] == 0x00 && colorTv.FrameBuffer[7] == 0x00,
                $"pixel 1 (nibble 1, location 0) is fully transparent (never written), got B={colorTv.FrameBuffer[4]:X2} G={colorTv.FrameBuffer[5]:X2} R={colorTv.FrameBuffer[6]:X2} A={colorTv.FrameBuffer[7]:X2}");

            Console.WriteLine("  LSB-nibble-first unpacking test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  LSB-nibble-first unpacking test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            UsimState.ColorTvEnabled = saved;
        }
    }

    private static bool TestModeWriteMaskAndReadStatusBits()
    {
        Console.WriteLine("Test: mode write masks to 5 bits, mode read never sets the always-0 status bits");
        try
        {
            var colorTv = MakeColorTv();

            colorTv.ControlWrite(0, 0xFF);
            Assert(colorTv.ControlRead(0) == 0x1F,
                $"mode write masks to bits 0-4 (0x1F), and read never ORs in bits 5-7, got 0x{colorTv.ControlRead(0):X}");

            Console.WriteLine("  Mode-write-mask/read-status-bits test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Mode-write-mask/read-status-bits test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestSyncPromEnabledGating()
    {
        Console.WriteLine("Test: sync-RAM read/write only takes effect when sync PROM is disabled (vert-spacing bit 7 set)");
        try
        {
            var colorTv = MakeColorTv();

            colorTv.ControlWrite(3, 0x80); // sync PROM DISABLED (bit 7 set)
            colorTv.ControlWrite(2, 0x05); // sync pointer = 5
            colorTv.ControlWrite(1, 0x42); // write sync data

            Assert(colorTv.ControlRead(1) == 0x42, $"sync RAM round-trips when sync PROM is disabled, got 0x{colorTv.ControlRead(1):X}");

            colorTv.ControlWrite(3, 0x00); // sync PROM ENABLED (bit 7 clear)
            Assert(colorTv.ControlRead(1) == 0, "sync data always reads 0 while sync PROM is enabled, even with prior data stored");

            Console.WriteLine("  Sync-PROM-enabled gating test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Sync-PROM-enabled gating test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestColorMapWritePreservesOtherChannels()
    {
        Console.WriteLine("Test: writing one color-map channel preserves the other two, and forces alpha opaque");
        var saved = UsimState.ColorTvEnabled;
        try
        {
            UsimState.ColorTvEnabled = true;
            var colorTv = MakeColorTv();
            uint location = 7;

            // Write R=255 (colorChannelValue 255 -> (v>>8)&0xFF = 0), channel 0.
            colorTv.ControlWrite(4, (0u << 8) | (0u << 6) | location);
            // Write G=128 (colorChannelValue 128 -> (v>>8)&0xFF = 255-128=127), channel 1.
            colorTv.ControlWrite(4, (127u << 8) | (1u << 6) | location);
            // Write B=0 (colorChannelValue 0 -> (v>>8)&0xFF = 255), channel 2.
            colorTv.ControlWrite(4, (255u << 8) | (2u << 6) | location);

            // Confirm via rendering: write a screen word selecting `location`
            // at pixel 0 and check the unpacked color.
            colorTv.ScreenWrite(0, location);
            colorTv.Tick();

            Assert(colorTv.FrameBuffer[0] == 0 && colorTv.FrameBuffer[1] == 128 &&
                   colorTv.FrameBuffer[2] == 255 && colorTv.FrameBuffer[3] == 0xFF,
                $"pixel 0 is B=0,G=128,R=255,A=0xFF after three independent channel writes, got B={colorTv.FrameBuffer[0]} G={colorTv.FrameBuffer[1]} R={colorTv.FrameBuffer[2]} A={colorTv.FrameBuffer[3]}");

            Console.WriteLine("  Color-map-write-preserves-channels test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Color-map-write-preserves-channels test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            UsimState.ColorTvEnabled = saved;
        }
    }

    private static bool TestColorMapInvalidChannelIsNoOp()
    {
        Console.WriteLine("Test: a hardware-undefined color-map channel (3) is a no-op, not an exception, and leaves the entry untouched");
        var saved = UsimState.ColorTvEnabled;
        try
        {
            UsimState.ColorTvEnabled = true;
            var colorTv = MakeColorTv();
            uint location = 9;

            // Establish a known value at this location via a legitimate channel write.
            colorTv.ControlWrite(4, (0u << 8) | (0u << 6) | location); // R=255

            // Now issue the invalid channel-3 write -- must not throw.
            colorTv.ControlWrite(4, (200u << 8) | (3u << 6) | location);

            // Confirm the entry is unchanged: render it and check it's still pure red.
            colorTv.ScreenWrite(0, location);
            colorTv.Tick();

            Assert(colorTv.FrameBuffer[0] == 0 && colorTv.FrameBuffer[1] == 0 &&
                   colorTv.FrameBuffer[2] == 255 && colorTv.FrameBuffer[3] == 0xFF,
                $"color-map entry {location} is untouched by the invalid channel-3 write, got B={colorTv.FrameBuffer[0]} G={colorTv.FrameBuffer[1]} R={colorTv.FrameBuffer[2]} A={colorTv.FrameBuffer[3]}");

            Console.WriteLine("  Invalid-color-channel-no-op test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Invalid-color-channel-no-op test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            UsimState.ColorTvEnabled = saved;
        }
    }

    private static bool TestTickAssertsInterruptOnlyWhenEnabled()
    {
        Console.WriteLine("Test: Tick() asserts the real Xbus interrupt only when interrupt-enable is set (and ColorTvEnabled is true)");
        var saved = UsimState.ColorTvEnabled;
        try
        {
            UsimState.ColorTvEnabled = true;
            var ucode = new UCode(new MainMemory());
            var colorTv = new ColorTv(ucode);

            colorTv.Tick();
            Assert(!ucode.InterruptPendingFlag, "no interrupt asserted while interrupt-enable is clear");

            colorTv.ControlWrite(0, 0x8); // interrupt-enable bit set
            colorTv.Tick();
            Assert(ucode.InterruptPendingFlag, "interrupt asserted once interrupt-enable is set");

            Console.WriteLine("  Tick-interrupt-gating test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Tick-interrupt-gating test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            UsimState.ColorTvEnabled = saved;
        }
    }

    private static bool TestTickDoesNothingWhenDisabled()
    {
        Console.WriteLine("Test: Tick() does not unpack FrameBuffer or assert an interrupt when UsimState.ColorTvEnabled is false");
        var saved = UsimState.ColorTvEnabled;
        try
        {
            UsimState.ColorTvEnabled = false;
            var ucode = new UCode(new MainMemory());
            var colorTv = new ColorTv(ucode);

            // Set up state that WOULD produce a visible, non-zero pixel and
            // a fired interrupt if Tick() ran its normal body.
            colorTv.ControlWrite(4, (0u << 8) | (0u << 6) | 1u); // location 1 = red
            colorTv.ScreenWrite(0, 1);
            colorTv.ControlWrite(0, 0x8); // interrupt-enable set

            colorTv.Tick();

            Assert(colorTv.FrameBuffer[0] == 0 && colorTv.FrameBuffer[1] == 0 &&
                   colorTv.FrameBuffer[2] == 0 && colorTv.FrameBuffer[3] == 0,
                "FrameBuffer is untouched (still all-zero) when ColorTvEnabled is false");
            Assert(!ucode.InterruptPendingFlag, "no interrupt fires when ColorTvEnabled is false, even with interrupt-enable set");

            Console.WriteLine("  Tick-does-nothing-when-disabled test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Tick-does-nothing-when-disabled test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            UsimState.ColorTvEnabled = saved;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
```

- [ ] **Step 3: Wire `ColorTvTests` into `Program.cs`**

Add a CLI case, next to the existing `case "--test-tv": TvTests.RunAllTests(); break;` (around line 190):

```csharp
                case "--test-colortv":
                    ColorTvTests.RunAllTests();
                    break;
```

Add a usage-help line, next to the existing `Console.WriteLine("  --test-tv               Run TV tests only");` (around line 295):

```csharp
        Console.WriteLine("  --test-colortv          Run color TV tests only");
```

Add a call into the `--test-all` aggregator, next to the existing `TvTests.RunAllTests();` block (around line 605):

```csharp
        // Run color TV tests
        Console.WriteLine("Running Color TV Tests...\n");
        ColorTvTests.RunAllTests();
        Console.WriteLine();
```

- [ ] **Step 4: Build and test**

Run: `dotnet build usim-cs` then `dotnet run --project usim-cs -- --test-colortv`
Expected: builds clean, `Passed: 8`, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/ColorTv.cs usim-cs/ColorTvTests.cs usim-cs/Program.cs
git commit -m "Add faithful ColorTv port (usim/colortv.c)

Standalone class + tests only -- not yet wired into BusAdaptor/
MachineControl/WpfBackend (a later task).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: Wire `ColorTv` into `BusAdaptor`/`MachineControl`/`WpfBackend`

**Files:**
- Modify: `usim-cs/BusAdaptor.cs`
- Modify: `usim-cs/MachineControl.cs`
- Modify: `usim-cs/WpfBackend.cs`
- Modify: `usim-cs/BusAdaptorTests.cs`

**Interfaces:**
- Consumes: `ColorTv` (Task 1), `UsimState.ColorTvEnabled` (already exists).

- [ ] **Step 1: Wire `ColorTv` into `BusAdaptor`**

In `usim-cs/BusAdaptor.cs`, add a field and a settable-once wiring method next to the existing `_tv`/`WireTv` (around line 24/39-42):

```csharp
    private ColorTv? _colorTv;
```

```csharp
    public void WireColorTv(ColorTv colorTv)
    {
        _colorTv = colorTv;
    }
```

In `ReadXbusIo` (around line 108-147), add new branches for the color-TV screen and control ranges, gated on `UsimState.ColorTvEnabled` in addition to the address check, placed before the `KnownBenignOverrunPaddr`/generic-fallback check (same position the `TvScreenLo`/`TvControlLo` branches already occupy, just added alongside them):

```csharp
        if (UsimState.ColorTvEnabled && paddr >= ColorTvScreenLo && paddr <= ColorTvScreenHi)
        {
            uint offset = paddr - ColorTvScreenLo;
            if (_colorTv == null)
            {
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"BusAdaptor: color TV screen read at offset {offset} with no ColorTv wired");
                return 0;
            }
            return _colorTv.ScreenRead(offset);
        }
        if (UsimState.ColorTvEnabled && paddr >= ColorTvControlLo && paddr <= ColorTvControlHi)
        {
            uint offset = paddr - ColorTvControlLo;
            if (_colorTv == null)
            {
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"BusAdaptor: color TV control read at offset {offset} with no ColorTv wired");
                return 0;
            }
            return _colorTv.ControlRead(offset);
        }
```

(Placed before the existing `TvScreenLo`/`TvControlLo` branches or after -- order between the two devices' branches doesn't matter since their address ranges don't overlap; just keep both inside `ReadXbusIo` before the `KnownBenignOverrunPaddr` check.)

Add the mirror-image branches to `WriteXbusIo` (around line 149-190), same gating and null-guard convention, calling `_colorTv.ScreenWrite(offset, v)` / `_colorTv.ControlWrite(offset, v)` and `return;` instead of `return 0;`:

```csharp
        if (UsimState.ColorTvEnabled && paddr >= ColorTvScreenLo && paddr <= ColorTvScreenHi)
        {
            uint offset = paddr - ColorTvScreenLo;
            if (_colorTv == null)
            {
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"BusAdaptor: color TV screen write at offset {offset} with no ColorTv wired");
                return;
            }
            _colorTv.ScreenWrite(offset, v);
            return;
        }
        if (UsimState.ColorTvEnabled && paddr >= ColorTvControlLo && paddr <= ColorTvControlHi)
        {
            uint offset = paddr - ColorTvControlLo;
            if (_colorTv == null)
            {
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"BusAdaptor: color TV control write at offset {offset} with no ColorTv wired");
                return;
            }
            _colorTv.ControlWrite(offset, v);
            return;
        }
```

When `UsimState.ColorTvEnabled` is false, both ranges fall through exactly as they do today (to the `KnownBenignOverrunPaddr` check, then the generic placeholder via `DescribeXbusIo`) -- this is what makes the real hardware probe (write 1, read back, expect 0 when absent) work correctly. Leave `DescribeXbusIo`'s existing `"color TV screen"`/`"color TV control"` lines exactly as they are -- still correct and reachable when disabled.

- [ ] **Step 2: Wire construction and call sites in `MachineControl.cs`**

Change the constructor (around line 74-90) from:

```csharp
    public MachineControl()
    {
        Memory = new MainMemory();
        UCode = new UCode(Memory);
        DiskController = new DiskController(Memory, UCode);
        UCode.BusAdaptor.WireDiskController(DiskController);
        Keyboard = new Keyboard();
        Mouse = new Mouse();
        Tv = new Tv(UCode, UsimState.TvWidth, UsimState.TvHeight);
        UCode.BusAdaptor.WireTv(Tv);
        Mouse.MaxX = (int)Tv.Width;
        Mouse.MaxY = (int)Tv.Height;
        IOBus = new IOBus();

        State = PowerState.Off;
        IsStopped = true;
    }
```

to:

```csharp
    public MachineControl()
    {
        Memory = new MainMemory();
        UCode = new UCode(Memory);
        DiskController = new DiskController(Memory, UCode);
        UCode.BusAdaptor.WireDiskController(DiskController);
        Keyboard = new Keyboard();
        Mouse = new Mouse();
        Tv = new Tv(UCode, UsimState.TvWidth, UsimState.TvHeight);
        UCode.BusAdaptor.WireTv(Tv);
        Mouse.MaxX = (int)Tv.Width;
        Mouse.MaxY = (int)Tv.Height;
        ColorTv = new ColorTv(UCode);
        UCode.BusAdaptor.WireColorTv(ColorTv);
        IOBus = new IOBus();

        State = PowerState.Off;
        IsStopped = true;
    }
```

Add a new property next to `public Tv Tv { get; private set; }` (around line 55):

```csharp
    public ColorTv ColorTv { get; private set; }
```

Change both `Reset()` (around line 217-229) and `InitializeComponents()` (around line 234-248) to call `ColorTv.Reset()` right after the existing `Tv.Reset()` call:

```csharp
        Tv.Reset();
        ColorTv.Reset();
```

(This is a no-op per Task 1's `ColorTv.Reset()`, kept purely for wiring symmetry -- see the spec's Decisions.)

Change `InitializeDisplay` (around line 95-112) to pass `ColorTv` as a new `WpfBackend` constructor argument:

```csharp
        DisplayBackend = new WpfBackend(Tv, ColorTv, Keyboard, Mouse, onTick: RunMicrocodeBatch)
        {
            AllowResize = allowResize,
            Scale = scale,
            UseLinearFiltering = true
        };
```

In the headless branch of `Run()` (around line 344-350), add `ColorTv.Tick();` right after the existing `Tv.Tick();` call:

```csharp
            while (State == PowerState.Running && !_stopRequested)
            {
                RunMicrocodeBatch();
                if (State != PowerState.Running) break; // halted mid-batch, via Halt()
                Tv.Tick();
                ColorTv.Tick();
                System.Threading.Thread.Sleep(16);
            }
```

- [ ] **Step 3: Add the second window to `WpfBackend.cs`**

Change the constructor and fields (around line 23-48) from:

```csharp
public class WpfBackend : IDisposable
{
    private readonly Tv _tv;
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

    public WpfBackend(Tv tv, Keyboard keyboard, Mouse mouse, Action onTick)
    {
        _tv = tv ?? throw new ArgumentNullException(nameof(tv));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _mouse = mouse ?? throw new ArgumentNullException(nameof(mouse));
        _onTick = onTick ?? throw new ArgumentNullException(nameof(onTick));
    }
```

to:

```csharp
public class WpfBackend : IDisposable
{
    private readonly Tv _tv;
    private readonly ColorTv _colorTv;
    private readonly Keyboard _keyboard;
    private readonly Mouse _mouse;
    private readonly Action _onTick;

    private Application? _application;
    private Window? _window;
    private Image? _image;
    private WriteableBitmap? _bitmap;
    private Window? _colorWindow;
    private Image? _colorImage;
    private WriteableBitmap? _colorBitmap;
    private DispatcherTimer? _timer;
    private string _windowTitle = "USIM - Lisp Machine Emulator";

    public double Scale { get; set; } = 1.0;
    public bool AllowResize { get; set; } = false;
    public bool UseLinearFiltering { get; set; } = true;
    public bool IsRunning { get; private set; }

    public WpfBackend(Tv tv, ColorTv colorTv, Keyboard keyboard, Mouse mouse, Action onTick)
    {
        _tv = tv ?? throw new ArgumentNullException(nameof(tv));
        _colorTv = colorTv ?? throw new ArgumentNullException(nameof(colorTv));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _mouse = mouse ?? throw new ArgumentNullException(nameof(mouse));
        _onTick = onTick ?? throw new ArgumentNullException(nameof(onTick));
    }
```

In `Initialize()` (around line 53-120), add the second window's setup right after the existing `_window.Show(); IsRunning = true;` lines and before the closing `TraceLog.Instance.Trace(TraceCategory.Display, TraceLevel.Info, "WPF backend initialized successfully");` line, gated on `UsimState.ColorTvEnabled`:

```csharp
        if (UsimState.ColorTvEnabled)
        {
            _colorBitmap = new WriteableBitmap((int)_colorTv.Width, (int)_colorTv.Height, 96, 96, PixelFormats.Pbgra32, null);

            _colorImage = new Image
            {
                Source = _colorBitmap,
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = true,
                Width = _colorTv.Width,
                Height = _colorTv.Height
            };
            RenderOptions.SetBitmapScalingMode(_colorImage,
                UseLinearFiltering ? BitmapScalingMode.Linear : BitmapScalingMode.NearestNeighbor);

            var colorViewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = _colorImage
            };

            colorViewbox.Width = _colorTv.Width * Scale;
            colorViewbox.Height = _colorTv.Height * Scale;

            _colorWindow = new Window
            {
                Title = "USIM - Color TV",
                Content = colorViewbox,
                Background = Brushes.Black,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = AllowResize ? ResizeMode.CanResize : ResizeMode.CanMinimize
            };

            var colorWindow = _colorWindow;
            colorWindow.Loaded += (_, _) =>
            {
                colorWindow.SizeToContent = SizeToContent.Manual;
                colorViewbox.Width = double.NaN;
                colorViewbox.Height = double.NaN;
            };

            _colorWindow.Show();
        }
```

(This window has no `KeyDown`/`KeyUp`/`MouseMove`/`MouseDown`/`MouseUp` handlers and no `Cursor = Cursors.None` -- it is display-only, matching real hardware's single shared keyboard/mouse routed entirely through the main window, per the spec's Wiring section.)

Change `Tick()` (around line 145-150) from:

```csharp
    private void Tick()
    {
        _onTick();
        _tv.Tick();
        UpdateBitmap();
    }
```

to:

```csharp
    private void Tick()
    {
        _onTick();
        _tv.Tick();
        _colorTv.Tick();
        UpdateBitmap();
        UpdateColorBitmap();
    }
```

Add a new method right after the existing `UpdateBitmap()` (around line 152-159):

```csharp
    private void UpdateColorBitmap()
    {
        if (_colorBitmap == null)
            return;

        var rect = new Int32Rect(0, 0, (int)_colorTv.Width, (int)_colorTv.Height);
        _colorBitmap.WritePixels(rect, _colorTv.FrameBuffer, (int)(_colorTv.Width * 4), 0);
    }
```

Change `Dispose()` (around line 231-244) from:

```csharp
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
```

to:

```csharp
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
        _colorWindow?.Close();
        IsRunning = false;
    }
```

- [ ] **Step 4: Add a real-dispatch-confirmation test to `BusAdaptorTests.cs`**

Add a test confirming the color-TV screen/control ranges dispatch to a real, wired `ColorTv` only when `UsimState.ColorTvEnabled` is true, and fall through to the generic placeholder when it's false -- mirroring `TestTvRangeDispatchesToRealTv`'s exact shape (around line 510-544):

```csharp
    private static bool TestColorTvRangeDispatchesToRealColorTvOnlyWhenEnabled()
    {
        Console.WriteLine("Test: color TV screen/control ranges dispatch to a real, wired ColorTv only when UsimState.ColorTvEnabled is true");
        var saved = UsimState.ColorTvEnabled;
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = ucode.BusAdaptor;
            var colorTv = new ColorTv(ucode);
            busAdaptor.WireColorTv(colorTv);

            bool promDisabled = false;

            // ColorTvControlLo = 0x3DFFE8 (offset 0, mode register).
            UsimState.ColorTvEnabled = false;
            busAdaptor.Write(0x3DFFE8, 0x1F, ref promDisabled);
            uint modeWhenDisabled = busAdaptor.Read(0x3DFFE8);
            Assert(modeWhenDisabled == 0,
                $"disabled: falls through to the generic placeholder (always reads 0), got 0x{modeWhenDisabled:X}");

            UsimState.ColorTvEnabled = true;
            busAdaptor.Write(0x3DFFE8, 0x1F, ref promDisabled);
            uint modeWhenEnabled = busAdaptor.Read(0x3DFFE8);
            Assert(modeWhenEnabled == 0x1F,
                $"enabled: round-trips through the real ColorTv, got 0x{modeWhenEnabled:X}");
            Assert(promDisabled == false, "color TV control writes never touch promDisabled");

            Console.WriteLine("  Color TV real-dispatch (enabled-gated) test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Color TV real-dispatch (enabled-gated) test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            UsimState.ColorTvEnabled = saved;
        }
    }
```

Register the test in `RunAllTests()`, next to the existing `if (TestTvRangeDispatchesToRealTv()) passed++; else failed++;` line (around line 33):

```csharp
        if (TestColorTvRangeDispatchesToRealColorTvOnlyWhenEnabled()) passed++; else failed++;
```

- [ ] **Step 5: Build and run the full suite**

Run: `dotnet build usim-cs`
Expected: builds clean.

Run: `dotnet run --project usim-cs -- --test-all`
Expected: every suite passes, including `BusAdaptorTests` (now 16 tests) and `ColorTvTests` (8 tests, from Task 1).

- [ ] **Step 6: Commit**

```bash
git add usim-cs/BusAdaptor.cs usim-cs/MachineControl.cs usim-cs/WpfBackend.cs usim-cs/BusAdaptorTests.cs
git commit -m "Wire real ColorTv into BusAdaptor/MachineControl/WpfBackend

BusAdaptor's color-TV screen/control ranges now dispatch to a real,
faithful ColorTv when UsimState.ColorTvEnabled is set -- otherwise
they fall through exactly as before, preserving the real hardware
probe-reads-0-when-absent mechanism. WpfBackend gains a second,
independent window for color TV, matching the real C's separate SDL
window.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```
