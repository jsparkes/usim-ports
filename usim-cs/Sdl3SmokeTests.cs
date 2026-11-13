// Sdl3SmokeTests.cs - Smoke test proving the SDL3-CS package (and its
// native runtime companion) resolves and runs under this project's TFM.

using System;

namespace Usim;

public static class Sdl3SmokeTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== SDL3 Smoke Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestGetVersion()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestGetVersion()
    {
        Console.WriteLine("Test: SDL3-CS GetVersion");
        try
        {
            Assert(Sdl3Smoke.GetVersion() > 0, "SDL3-CS reports a positive SDL version number");

            Console.WriteLine("  SDL3-CS GetVersion tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  SDL3-CS GetVersion tests failed: {ex.Message}\n");
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
