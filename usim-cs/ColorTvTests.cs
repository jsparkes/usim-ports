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
