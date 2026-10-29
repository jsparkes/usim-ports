// Sdl3VideoTests.cs - Tests for Sdl3Video, the SDL3-CS port of
// sdl3-video.c's window/renderer/texture/present path.

using System;

namespace Usim;

public static class Sdl3VideoTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== Sdl3Video Test Suite ===\n");

        // Run headlessly on any machine, including CI with no real display --
        // "dummy" is a real SDL video driver that discards all drawing.
        Environment.SetEnvironmentVariable("SDL_VIDEODRIVER", "dummy");

        int passed = 0;
        int failed = 0;

        if (TestInitAndPresentWithColorTv()) passed++; else failed++;
        if (TestPresentColorIsNoOpWhenColorTvDisabled()) passed++; else failed++;
        if (TestSetScaleModeDoesNotThrow()) passed++; else failed++;
        if (TestSetWindowTitleDoesNotThrow()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestInitAndPresentWithColorTv()
    {
        Console.WriteLine("Test: Init succeeds under SDL_VIDEODRIVER=dummy, with color TV enabled");
        try
        {
            using var video = new Sdl3Video();
            Assert(video.Init(
                tvWidth: 768, tvHeight: 963,
                colorTvWidth: 576, colorTvHeight: 454,
                colorTvEnabled: true,
                scale: 1.0,
                allowResize: false,
                windowDisplay: -1,
                windowPositionX: -1,
                windowPositionY: -1,
                windowAlwaysOnTop: false,
                windowTitle: "USIM - Lisp Machine Emulator"),
                "Init succeeds under SDL_VIDEODRIVER=dummy");

            Assert(video.MainWindowHandle != 0, "main window handle is non-zero after Init");

            var mainFrame = new byte[768 * 963 * 4];
            video.PresentMain(mainFrame); // must not throw

            var colorFrame = new byte[576 * 454 * 4];
            video.PresentColor(colorFrame); // must not throw, color TV was enabled

            Console.WriteLine("  Init/present test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Init/present test failed: {ex}\n");
            return false;
        }
    }

    private static bool TestPresentColorIsNoOpWhenColorTvDisabled()
    {
        Console.WriteLine("Test: PresentColor is a safe no-op when colorTvEnabled was false at Init");
        try
        {
            using var video = new Sdl3Video();
            Assert(video.Init(
                tvWidth: 768, tvHeight: 963,
                colorTvWidth: 576, colorTvHeight: 454,
                colorTvEnabled: false,
                scale: 1.0,
                allowResize: false,
                windowDisplay: -1,
                windowPositionX: -1,
                windowPositionY: -1,
                windowAlwaysOnTop: false,
                windowTitle: "USIM - Lisp Machine Emulator"),
                "Init succeeds with colorTvEnabled: false");

            Assert(!video.ColorWindowCreated, "no color window was created when colorTvEnabled was false");

            // must not throw even though there is no color window/texture
            video.PresentColor(new byte[576 * 454 * 4]);

            Console.WriteLine("  PresentColor no-op test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  PresentColor no-op test failed: {ex}\n");
            return false;
        }
    }

    private static bool TestSetScaleModeDoesNotThrow()
    {
        Console.WriteLine("Test: SetScaleMode(true/false) does not throw after Init");
        try
        {
            using var video = new Sdl3Video();
            Assert(video.Init(
                tvWidth: 768, tvHeight: 963,
                colorTvWidth: 576, colorTvHeight: 454,
                colorTvEnabled: true,
                scale: 1.0,
                allowResize: false,
                windowDisplay: -1,
                windowPositionX: -1,
                windowPositionY: -1,
                windowAlwaysOnTop: false,
                windowTitle: "USIM - Lisp Machine Emulator"),
                "Init succeeds");

            video.SetScaleMode(true);  // linear
            video.SetScaleMode(false); // nearest

            Console.WriteLine("  SetScaleMode test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  SetScaleMode test failed: {ex}\n");
            return false;
        }
    }

    private static bool TestSetWindowTitleDoesNotThrow()
    {
        Console.WriteLine("Test: SetWindowTitle does not throw after Init");
        try
        {
            using var video = new Sdl3Video();
            Assert(video.Init(
                tvWidth: 768, tvHeight: 963,
                colorTvWidth: 576, colorTvHeight: 454,
                colorTvEnabled: false,
                scale: 1.0,
                allowResize: false,
                windowDisplay: -1,
                windowPositionX: -1,
                windowPositionY: -1,
                windowAlwaysOnTop: false,
                windowTitle: "USIM - Lisp Machine Emulator"),
                "Init succeeds");

            video.SetWindowTitle("a new title");

            Console.WriteLine("  SetWindowTitle test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  SetWindowTitle test failed: {ex}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception($"Assertion failed: {message}");
        }
    }
}
