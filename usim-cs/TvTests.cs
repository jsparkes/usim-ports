// TvTests.cs - Tests for the faithful Tv port (usim/tv.c), main
// (black-and-white) TV only. Exercises Tv.cs directly -- the
// through-BusAdaptor register-interface tests live in BusAdaptorTests.cs
// (Task 2), matching this codebase's established split (e.g.
// DiskControllerTests.cs tests DiskController directly; the real-disk
// BusAdaptor-wired tests are elsewhere).

using System;
using System.IO;

namespace Usim;

public static class TvTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== Tv Test Suite ===\n");
        int passed = 0, failed = 0;

        if (TestScreenReadWriteRoundTrip()) passed++; else failed++;
        if (TestScreenBufferLastWordRoundTrip()) passed++; else failed++;
        if (TestReset()) passed++; else failed++;
        if (TestModeRegisterWriteNoMasking()) passed++; else failed++;
        if (TestSyncRamGatedByProm()) passed++; else failed++;
        if (TestInvalidControlOffsetSetsXbusNxm()) passed++; else failed++;
        if (TestSaveScreenshotPbmMatchesIndependentComputation()) passed++; else failed++;
        if (TestSaveScreenshotPbmIgnoresBufferTail()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static Tv MakeTv() => new Tv(new BusInterface(new UCode()));

    private static bool TestScreenReadWriteRoundTrip()
    {
        Console.WriteLine("Test: ScreenWrite then ScreenRead round-trips a word at offset 0");
        try
        {
            var tv = MakeTv();
            tv.ScreenWrite(0, 0xDEADBEEF);
            tv.ScreenRead(0, out uint v);
            Assert(v == 0xDEADBEEF, $"round-trip at offset 0, got 0x{v:X}");
            Console.WriteLine("  Screen-read-write round-trip test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Screen-read-write round-trip test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestScreenBufferLastWordRoundTrip()
    {
        Console.WriteLine("Test: ScreenWrite/ScreenRead round-trips the last valid word, offset 0x7FFF");
        try
        {
            var tv = MakeTv();
            tv.ScreenWrite(0x7FFF, 0x12345678);
            tv.ScreenRead(0x7FFF, out uint v);
            Assert(v == 0x12345678, $"round-trip at offset 0x7FFF, got 0x{v:X}");
            Console.WriteLine("  Last-word round-trip test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Last-word round-trip test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestReset()
    {
        Console.WriteLine("Test: Reset() clears Mode and the screen buffer");
        try
        {
            var tv = MakeTv();
            tv.ScreenWrite(5, 0xFFFFFFFF);
            tv.ControlWrite(0, 0xFF);
            tv.Reset();
            tv.ScreenRead(5, out uint v);
            Assert(v == 0, "screen buffer cleared after Reset()");
            Assert(tv.Mode == 0, "Mode cleared after Reset()");
            Console.WriteLine("  Reset test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Reset test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestModeRegisterWriteNoMasking()
    {
        Console.WriteLine("Test: ControlWrite(0, ...) applies no masking -- a wholesale write sticks in every bit");
        try
        {
            var tv = MakeTv();
            tv.ControlWrite(0, 0xFFFFFFFF);
            tv.ControlRead(0, out uint v);
            Assert(v == 0xFFFFFFFF, $"mode register holds every bit written, got 0x{v:X}");
            Console.WriteLine("  Mode-register-no-masking test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Mode-register-no-masking test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestSyncRamGatedByProm()
    {
        Console.WriteLine("Test: sync-RAM read/write (control offset 1) is gated by sync-prom-enabled (vertSpacing bit 7)");
        try
        {
            var tv = MakeTv();

            // Real C (usim/tv.c:106-108): tv_is_sync_prom_enabled() returns
            // !((tv_vert_spacing & 0200) != 0) -- enabled when bit 7 is CLEAR. Both
            // tv_control_read and tv_control_write (tv.c:262, 311) gate sync-RAM
            // access with "if (!tv_is_sync_prom_enabled())" -- i.e. sync RAM is only
            // accessed when sync-prom-enabled is FALSE (bit 7 SET). So: bit 7 CLEAR
            // (the default, vertSpacing = 0) -> sync-prom-enabled TRUE -> sync RAM
            // DISABLED (reads 0, writes ignored); bit 7 SET -> sync-prom-enabled
            // FALSE -> sync RAM LIVE.

            // Set bit 7 first so sync RAM is live, and write a value through it.
            tv.ControlWrite(3, 0x80); // vertSpacing bit 7 set -> sync-prom-enabled becomes false -> sync RAM live
            tv.ControlWrite(2, 0x010); // sync ptr = 0x10
            tv.ControlWrite(1, 0xAB);  // sync_ram[0x10] = 0xAB
            tv.ControlRead(1, out uint v1);
            Assert(v1 == 0xAB, $"sync RAM live when sync-prom disabled (vertSpacing bit 7 set), got 0x{v1:X}");

            // Clear bit 7 -- back to the default, sync-prom-enabled becomes TRUE,
            // sync RAM disabled: reads must return 0, writes ignored.
            tv.ControlWrite(3, 0x00); // vertSpacing bit 7 clear (default) -> sync-prom-enabled true -> sync RAM disabled
            tv.ControlWrite(1, 0xCD); // must be ignored
            tv.ControlRead(1, out uint v2);
            Assert(v2 == 0, $"sync RAM reads 0 when sync-prom enabled (vertSpacing bit 7 clear, the default), got 0x{v2:X}");

            // Set bit 7 again -- the original 0xAB at ptr 0x10 must still be there (the 0xCD write was ignored).
            tv.ControlWrite(3, 0x80);
            tv.ControlRead(1, out uint v3);
            Assert(v3 == 0xAB, $"ignored write during sync-prom-enabled didn't stick, got 0x{v3:X}");

            Console.WriteLine("  Sync-RAM-gated-by-prom test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Sync-RAM-gated-by-prom test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestInvalidControlOffsetSetsXbusNxm()
    {
        Console.WriteLine("Test: an invalid control-register offset (4) sets Xbus NXM on both read and write, without throwing");
        try
        {
            var ucode = new UCode();
            var busInterface = new BusInterface(ucode);
            var tv = new Tv(busInterface);

            Assert(!busInterface.IsXbusNxm(), "sanity: Xbus NXM starts clear");
            tv.ControlWrite(4, 0x1);
            Assert(busInterface.IsXbusNxm(), "invalid-offset write sets Xbus NXM");

            // Fresh BusInterface/Tv pair for the read path -- reusing the write-path
            // instances would leave NXM already set from the write above, making this
            // assertion trivially true regardless of whether ControlRead's default case
            // actually sets it.
            var readUcode = new UCode();
            var readBusInterface = new BusInterface(readUcode);
            var readTv = new Tv(readBusInterface);
            Assert(!readBusInterface.IsXbusNxm(), "sanity: Xbus NXM starts clear on the fresh read-path pair");
            readTv.ControlRead(5, out uint v);
            Assert(readBusInterface.IsXbusNxm(), "invalid-offset read sets Xbus NXM");
            Assert(v == 0, $"invalid-offset read assigns 0 (documented deviation from the real C's untouched-*pv), got 0x{v:X}");

            Console.WriteLine("  Invalid-control-offset-sets-NXM test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Invalid-control-offset-sets-NXM test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestSaveScreenshotPbmMatchesIndependentComputation()
    {
        Console.WriteLine("Test: SaveScreenshotPbm's output matches an independently-computed PBM for a simple pattern");
        string path = Path.GetTempFileName();
        try
        {
            var tv = MakeTv();
            // Fill the visible range with a row-dependent pattern -- alternate the word
            // value by row (0xAAAAAAAA on even rows, 0x55555555 on odd rows) rather than
            // writing the same value everywhere. Width (768) is a multiple of 32 (24 words
            // per row), so a single repeated word value would make every row identical --
            // unable to catch a row-stride or word-permutation bug, only basic bit order.
            uint wordsPerRow = tv.Width / 32;
            uint visibleWords = tv.Width * tv.Height / 32;
            for (uint i = 0; i < visibleWords; i++)
            {
                uint row = i / wordsPerRow;
                tv.ScreenWrite(i, (row % 2 == 0) ? 0xAAAAAAAAu : 0x55555555u);
            }

            tv.SaveScreenshotPbm(path);

            // Independently compute the expected PBM text -- written fresh here, not by
            // calling into Tv's own bit-extraction code.
            var expected = new System.Text.StringBuilder();
            expected.Append("P1\n").Append(tv.Width).Append(' ').Append(tv.Height).Append('\n');
            for (uint i = 0; i < tv.Width * tv.Height; i++)
            {
                uint wordIndex = i / 32;
                uint row = wordIndex / wordsPerRow;
                int bitpos = (int)(i % 32);
                uint wordValue = (row % 2 == 0) ? 0xAAAAAAAAu : 0x55555555u;
                bool bit = (wordValue & (1u << bitpos)) != 0;
                expected.Append(bit ? '1' : '0');
                if (i % 70 == 0 && i > 0) expected.Append('\n');
            }

            string actual = File.ReadAllText(path);
            Assert(actual == expected.ToString(), "PBM output matches independently-computed expected text exactly");

            Console.WriteLine("  Screenshot-matches-independent-computation test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Screenshot-matches-independent-computation test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestSaveScreenshotPbmIgnoresBufferTail()
    {
        Console.WriteLine("Test: SaveScreenshotPbm reflects only the visible Width*Height bits, not writes to the buffer's unused tail");
        string path = Path.GetTempFileName();
        try
        {
            var tv = MakeTv();
            uint visibleWords = tv.Width * tv.Height / 32; // 21504 -- strictly less than the 32768-word buffer
            Assert(visibleWords < 0x8000, "sanity: the visible range is smaller than the full buffer");

            // Leave the visible range all-zero; fill the UNUSED tail with all-ones.
            for (uint i = visibleWords; i < 0x8000; i++) tv.ScreenWrite(i, 0xFFFFFFFF);

            tv.SaveScreenshotPbm(path);
            string actual = File.ReadAllText(path);
            // Check only the bitmap body: the "P1" magic number itself contains a '1' character.
            string body = actual.Substring(actual.IndexOf('\n', actual.IndexOf('\n') + 1) + 1);
            Assert(!body.Contains('1'), "screenshot contains no set bits -- the all-ones tail must not leak into the visible output");

            Console.WriteLine("  Screenshot-ignores-buffer-tail test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Screenshot-ignores-buffer-tail test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception($"Assertion failed: {message}");
    }
}
