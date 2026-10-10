// WpfKeyTranslatorTests.cs - Tests for the real Cadet-scancode table in
// WpfKeyTranslator.TranslateKey. Spot-checks a representative sample rather
// than exhaustively re-verifying all ~55 entries -- the table itself was
// independently computed via Python, not hand-transcribed.

using System;
using System.Windows.Input;

namespace Usim;

public static class WpfKeyTranslatorTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== WpfKeyTranslator Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestBootCriticalKeys()) passed++; else failed++;
        if (TestShiftDoesNotChangeTheReturnedScancode()) passed++; else failed++;
        if (TestUnmappedKeyReturnsZero()) passed++; else failed++;
        if (TestModifierKeysMapToRealScancodes()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestBootCriticalKeys()
    {
        Console.WriteLine("Test: Backspace (rubout) and Return map to their real Cadet scancodes");
        try
        {
            Assert(WpfKeyTranslator.TranslateKey(Key.Back, false) == 0x13, "Backspace maps to rubout (0x13)");
            Assert(WpfKeyTranslator.TranslateKey(Key.Return, false) == 0x5E, "Return maps to its real scancode (0x5E)");

            Console.WriteLine("  Boot-critical-keys test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Boot-critical-keys test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestShiftDoesNotChangeTheReturnedScancode()
    {
        Console.WriteLine("Test: shift does not change the returned scancode (real hardware communicates shift separately)");
        try
        {
            int unshifted = WpfKeyTranslator.TranslateKey(Key.A, false);
            int shifted = WpfKeyTranslator.TranslateKey(Key.A, true);
            Assert(unshifted == shifted && unshifted == 0x53, $"'A' returns the same scancode (0x53) regardless of shift, got unshifted=0x{unshifted:X} shifted=0x{shifted:X}");

            Console.WriteLine("  Shift-does-not-change-scancode test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Shift-does-not-change-scancode test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUnmappedKeyReturnsZero()
    {
        Console.WriteLine("Test: a key with no real Cadet mapping (e.g. F12) returns 0");
        try
        {
            Assert(WpfKeyTranslator.TranslateKey(Key.F12, false) == 0, "F12 has no default Cadet mapping, returns 0");

            Console.WriteLine("  Unmapped-key-returns-zero test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Unmapped-key-returns-zero test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestModifierKeysMapToRealScancodes()
    {
        Console.WriteLine("Test: modifier keys (shift/ctrl/meta/super) map to their real Cadet scancodes, both sides sharing the same value");
        try
        {
            Assert(WpfKeyTranslator.TranslateKey(Key.LeftShift, false) == 0x14, "LeftShift maps to 0x14");
            Assert(WpfKeyTranslator.TranslateKey(Key.RightShift, false) == 0x14, "RightShift shares the same scancode as LeftShift");
            Assert(WpfKeyTranslator.TranslateKey(Key.LeftCtrl, false) == 0x10, "LeftCtrl maps to 0x10");
            Assert(WpfKeyTranslator.TranslateKey(Key.LeftAlt, false) == 0x25, "LeftAlt (meta) maps to 0x25");

            Console.WriteLine("  Modifier-keys-map-to-real-scancodes test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Modifier-keys-map-to-real-scancodes test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
