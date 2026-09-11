// TvTests.cs - Tests for the faithful Tv port (usim/tv.c).

using System;

namespace Usim;

public static class TvTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== Tv Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestScreenWriteUnpacksLsbFirst()) passed++; else failed++;
        if (TestBowToggleTriggersFullRepaint()) passed++; else failed++;
        if (TestControlRegisterRoundTrips()) passed++; else failed++;
        if (TestSyncPromEnabledGating()) passed++; else failed++;
        if (TestTickAssertsInterruptOnlyWhenEnabled()) passed++; else failed++;
        if (TestDefaultCaseSetsXbusNxm()) passed++; else failed++;
        if (TestWriteBeyondVisibleScreenDoesNotThrow()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static Tv MakeTv() => new Tv(new UCode(new MainMemory()), 768, 896);

    private static bool TestScreenWriteUnpacksLsbFirst()
    {
        Console.WriteLine("Test: ScreenWrite unpacks bits LSB-first (the bug this port fixes)");
        try
        {
            var tv = MakeTv();

            // Bit 0 set, bit 1 clear. Pixel 0 must be foreground (bit 0),
            // pixel 1 must be background (bit 1) -- the OLD invented code
            // would have put bit 0 at pixel 31, not pixel 0.
            tv.ScreenWrite(0, 0x00000001);

            Assert(tv.FrameBuffer[0] == 0xFF && tv.FrameBuffer[1] == 0xFF &&
                   tv.FrameBuffer[2] == 0xFF && tv.FrameBuffer[3] == 0xFF,
                $"pixel 0 (bit 0, set) is foreground/white, got B={tv.FrameBuffer[0]:X2} G={tv.FrameBuffer[1]:X2} R={tv.FrameBuffer[2]:X2} A={tv.FrameBuffer[3]:X2}");

            Assert(tv.FrameBuffer[4] == 0x00 && tv.FrameBuffer[5] == 0x00 &&
                   tv.FrameBuffer[6] == 0x00 && tv.FrameBuffer[7] == 0xFF,
                $"pixel 1 (bit 1, clear) is background/black, got B={tv.FrameBuffer[4]:X2} G={tv.FrameBuffer[5]:X2} R={tv.FrameBuffer[6]:X2} A={tv.FrameBuffer[7]:X2}");

            Console.WriteLine("  LSB-first unpacking test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  LSB-first unpacking test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBowToggleTriggersFullRepaint()
    {
        Console.WriteLine("Test: a BOW-bit transition triggers a full-screen repaint with the new colors");
        try
        {
            var tv = MakeTv();

            tv.ScreenWrite(0, 0x00000001); // bit 0 set -> pixel 0 = white (default, BOW=0)
            Assert(tv.FrameBuffer[0] == 0xFF, "pixel 0 is white before the BOW toggle");

            tv.ControlWrite(0, 0x4); // set BOW bit (mode = 0x4) -- transition 0 -> 1

            // BOW=1 means foreground=Black. The repaint re-applies ScreenWrite
            // for every word, so pixel 0 (bit 0, still set in the stored word)
            // must now be black -- with NO additional ScreenWrite call.
            Assert(tv.FrameBuffer[0] == 0x00,
                $"pixel 0 repainted to black after the BOW toggle, got B={tv.FrameBuffer[0]:X2}");

            Console.WriteLine("  BOW-toggle repaint test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  BOW-toggle repaint test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestControlRegisterRoundTrips()
    {
        Console.WriteLine("Test: control register read/write round-trips (offsets 0-3)");
        try
        {
            var tv = MakeTv();

            tv.ControlWrite(0, 0xC); // BOW + interrupt-enable
            Assert(tv.ControlRead(0) == 0xC, $"mode register round-trips, got 0x{tv.ControlRead(0):X}");

            // Offset 2 (sync pointer) and 3 (vert spacing) are write-only --
            // writing must not throw, and reading them falls to the default
            // (NXM) case, not a dedicated read path.
            tv.ControlWrite(2, 0x123);
            tv.ControlWrite(3, 0x00); // sync PROM enabled (bit 7 clear)

            // Offset 1 (sync data) with sync PROM enabled reads back 0.
            Assert(tv.ControlRead(1) == 0, "sync data reads 0 while sync PROM is enabled");

            Console.WriteLine("  Control-register round-trip test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Control-register round-trip test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestSyncPromEnabledGating()
    {
        Console.WriteLine("Test: sync-RAM read/write only takes effect when sync PROM is disabled (vert-spacing bit 7 set)");
        try
        {
            var tv = MakeTv();

            tv.ControlWrite(3, 0x80); // sync PROM DISABLED (bit 7 set)
            tv.ControlWrite(2, 0x05); // sync pointer = 5
            tv.ControlWrite(1, 0x42); // write sync data

            Assert(tv.ControlRead(1) == 0x42, $"sync RAM round-trips when sync PROM is disabled, got 0x{tv.ControlRead(1):X}");

            tv.ControlWrite(3, 0x00); // sync PROM ENABLED (bit 7 clear)
            Assert(tv.ControlRead(1) == 0, "sync data always reads 0 while sync PROM is enabled, even with prior data stored");

            Console.WriteLine("  Sync-PROM-enabled gating test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Sync-PROM-enabled gating test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestTickAssertsInterruptOnlyWhenEnabled()
    {
        Console.WriteLine("Test: Tick() asserts the real Xbus interrupt only when interrupt-enable is set");
        try
        {
            var ucode = new UCode(new MainMemory());
            var tv = new Tv(ucode, 768, 896);

            tv.Tick();
            Assert(!ucode.InterruptPendingFlag, "no interrupt asserted while interrupt-enable is clear");

            tv.ControlWrite(0, 0x8); // interrupt-enable bit set
            tv.Tick();
            Assert(ucode.InterruptPendingFlag, "interrupt asserted once interrupt-enable is set");

            Console.WriteLine("  Tick-interrupt-gating test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Tick-interrupt-gating test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDefaultCaseSetsXbusNxm()
    {
        Console.WriteLine("Test: an unrecognized control-register offset sets Xbus NXM, doesn't throw");
        try
        {
            var ucode = new UCode(new MainMemory());
            var tv = new Tv(ucode, 768, 896);

            Assert(tv.ControlRead(4) == 0, "unrecognized read returns 0");
            Assert(ucode.BusInterface.IsXbusNxm(), "unrecognized read sets Xbus NXM");

            ucode.BusInterface.ResetBusErrorStatus();
            tv.ControlWrite(4, 0x1234);
            Assert(ucode.BusInterface.IsXbusNxm(), "unrecognized write sets Xbus NXM");

            Console.WriteLine("  Default-case test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Default-case test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestWriteBeyondVisibleScreenDoesNotThrow()
    {
        Console.WriteLine("Test: writing at a screen-buffer offset beyond the visible geometry doesn't throw");
        try
        {
            var tv = MakeTv(); // 768x896 -> visible word count = 768*896/32 = 21504

            // An offset well beyond the visible screen but still within the
            // real 0x8000-word address range -- must not throw, exercising
            // the oversized FrameBuffer this spec's Decisions section
            // requires (a FrameBuffer sized to Width*Height*4 would throw here).
            tv.ScreenWrite(30000, 0xFFFFFFFF);
            Assert(tv.ScreenRead(30000) == 0xFFFFFFFF, "the write is still stored and reads back correctly");

            Console.WriteLine("  Write-beyond-visible-screen test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Write-beyond-visible-screen test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
