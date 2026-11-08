// Sdl3AudioTests.cs - Tests for Sdl3Audio, the SDL3-CS port of
// sdl3-audio.c's beep generation.

using System;
using System.Diagnostics;
using SDL3;

namespace Usim;

public static class Sdl3AudioTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== Sdl3Audio Test Suite ===\n");

        // Run headlessly on any machine, including CI with no real audio
        // device -- "dummy" is a real SDL audio driver that discards audio.
        // SDL.SetHint (not Environment.SetEnvironmentVariable) is required
        // here: the latter only changes .NET's own copy of the environment
        // and never reaches native SDL via setenv, especially on Unix, so
        // on Linux/macOS these tests would otherwise run against whatever
        // real audio driver is present instead of the dummy one.
        SDL.SetHint(SDL.Hints.AudioDriver, "dummy");

        int passed = 0;
        int failed = 0;

        if (TestInitSucceedsWithoutRealDevice()) passed++; else failed++;
        if (TestShortBeepDoesNotBlock()) passed++; else failed++;
        if (TestBeepDoesNotFlushOnSameWavelength()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestInitSucceedsWithoutRealDevice()
    {
        Console.WriteLine("Test: Init succeeds under SDL_AUDIODRIVER=dummy");
        try
        {
            using var audio = new Sdl3Audio();
            Assert(audio.Init(), "Init succeeds even with SDL_AUDIODRIVER=dummy (no real device)");

            Console.WriteLine("  Init test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Init test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestShortBeepDoesNotBlock()
    {
        Console.WriteLine("Test: a short beep does not block for its full duration");
        try
        {
            using var audio = new Sdl3Audio();
            Assert(audio.Init(), "Init succeeds");

            // Beep must return without throwing and without blocking >= 100ms
            // for a short duration.
            var sw = Stopwatch.StartNew();
            audio.Beep(halfWavelengthMicros: 500, durationMicros: 50_000); // 50ms < the 100ms no-wait threshold
            sw.Stop();
            Assert(sw.ElapsedMilliseconds < 100, "a beep shorter than 100ms does not block the caller for its full duration");

            Console.WriteLine("  Short beep test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Short beep test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBeepDoesNotFlushOnSameWavelength()
    {
        Console.WriteLine("Test: consecutive beeps at the same wavelength do not throw, longer ones block");
        try
        {
            using var audio = new Sdl3Audio();
            Assert(audio.Init(), "Init succeeds");

            // First call establishes last_wavelength_in_us and (since
            // duration >= 100ms) blocks for roughly its duration.
            var sw = Stopwatch.StartNew();
            audio.Beep(halfWavelengthMicros: 500, durationMicros: 120_000);
            sw.Stop();
            Assert(sw.ElapsedMilliseconds >= 100, "a beep of >= 100ms blocks for roughly its duration");

            // Second call with the SAME wavelength must not throw (no flush
            // should be triggered this time); keep the duration short so the
            // test stays fast.
            sw.Restart();
            audio.Beep(halfWavelengthMicros: 500, durationMicros: 50_000);
            sw.Stop();
            Assert(sw.ElapsedMilliseconds < 100, "a same-wavelength short beep still skips the blocking wait");

            Console.WriteLine("  Same-wavelength beep test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Same-wavelength beep test failed: {ex.Message}\n");
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
