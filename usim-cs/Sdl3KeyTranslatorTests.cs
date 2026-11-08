// Sdl3KeyTranslatorTests.cs - Tests for the real Cadet-scancode table in
// Sdl3KeyTranslator.TranslateKey. Ports WpfKeyTranslatorTests.cs's
// assertions onto the SDL3-CS scancode/keymod equivalents of the same
// physical keys. Spot-checks a representative sample rather than
// exhaustively re-verifying all entries -- the table itself was
// independently computed via Python, not hand-transcribed.

using System;
using SDL3;

namespace Usim;

public static class Sdl3KeyTranslatorTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== Sdl3KeyTranslator Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestBootCriticalKeys()) passed++; else failed++;
        if (TestShiftDoesNotChangeTheReturnedScancode()) passed++; else failed++;
        if (TestUnmappedKeyReturnsZero()) passed++; else failed++;
        if (TestModifierKeysMapToRealScancodes()) passed++; else failed++;
        if (TestGetModifierStateBitmask()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestBootCriticalKeys()
    {
        Console.WriteLine("Test: boot-critical keys map to their real Cadet scancodes");
        try
        {
            Assert(Sdl3KeyTranslator.TranslateKey(SDL.Scancode.Backspace) == 0x13, "Backspace maps to rubout (0x13)");
            Assert(Sdl3KeyTranslator.TranslateKey(SDL.Scancode.Return) == 0x5E, "Return maps to its real scancode (0x5E)");
            Assert(Sdl3KeyTranslator.TranslateKey(SDL.Scancode.Escape) == 0x63, "Escape maps to its real scancode (0x63)");
            Assert(Sdl3KeyTranslator.TranslateKey(SDL.Scancode.F1) == 0x61, "F1 maps to its real scancode (0x61)");
            Assert(Sdl3KeyTranslator.TranslateKey(SDL.Scancode.Alpha1) == 0x51, "'1' maps to its real scancode (0x51)");
            Assert(Sdl3KeyTranslator.TranslateKey(SDL.Scancode.Space) == 0x5C, "Space maps to its real scancode (0x5C)");
            Assert(Sdl3KeyTranslator.TranslateKey(SDL.Scancode.Backslash) == 0x1F, "Backslash maps to its real scancode (0x1F)");
            Assert(Sdl3KeyTranslator.TranslateKey(SDL.Scancode.Comma) == 0x1C, "Comma maps to its real scancode (0x1C)");

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
        Console.WriteLine("Test: TranslateKey takes no shift parameter -- real hardware communicates shift separately via GetModifierState");
        try
        {
            int scancode = Sdl3KeyTranslator.TranslateKey(SDL.Scancode.A);
            Assert(scancode == 0x53, $"'A' returns its real scancode (0x53) regardless of shift state, got 0x{scancode:X}");

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
        Console.WriteLine("Test: keys with no real Cadet mapping (e.g. F12, Scroll Lock) return 0");
        try
        {
            Assert(Sdl3KeyTranslator.TranslateKey(SDL.Scancode.F12) == 0, "F12 has no default Cadet mapping, returns 0");
            Assert(Sdl3KeyTranslator.TranslateKey(SDL.Scancode.Scrolllock) == 0, "Scroll Lock has no default Cadet mapping, returns 0");

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
            Assert(Sdl3KeyTranslator.TranslateKey(SDL.Scancode.LShift) == 0x14, "LShift maps to 0x14");
            Assert(Sdl3KeyTranslator.TranslateKey(SDL.Scancode.RShift) == 0x14, "RShift shares the same scancode as LShift");
            Assert(Sdl3KeyTranslator.TranslateKey(SDL.Scancode.LCtrl) == 0x10, "LCtrl maps to 0x10");
            Assert(Sdl3KeyTranslator.TranslateKey(SDL.Scancode.LAlt) == 0x25, "LAlt (meta) maps to 0x25");

            Console.WriteLine("  Modifier-keys-map-to-real-scancodes test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Modifier-keys-map-to-real-scancodes test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestGetModifierStateBitmask()
    {
        Console.WriteLine("Test: GetModifierState builds the same bitmask as WpfKeyTranslator.GetModifierState (bit 0 shift, bit 1 capslock, bit 2 control, bit 3 alt/meta, bit 6 windows/super)");
        try
        {
            Assert(Sdl3KeyTranslator.GetModifierState(SDL.Keymod.Shift, false) == (1 << 0), "Shift sets bit 0");
            Assert(Sdl3KeyTranslator.GetModifierState(SDL.Keymod.None, true) == (1 << 1), "capsLock=true sets bit 1");
            Assert(Sdl3KeyTranslator.GetModifierState(SDL.Keymod.Ctrl, false) == (1 << 2), "Ctrl sets bit 2");
            Assert(Sdl3KeyTranslator.GetModifierState(SDL.Keymod.Alt, false) == (1 << 3), "Alt sets bit 3");
            Assert(Sdl3KeyTranslator.GetModifierState(SDL.Keymod.GUI, false) == (1 << 6), "GUI (Windows/super) sets bit 6");
            Assert(Sdl3KeyTranslator.GetModifierState(SDL.Keymod.Shift | SDL.Keymod.Ctrl, false) == ((1 << 0) | (1 << 2)), "Shift+Ctrl combines bits 0 and 2");
            Assert(Sdl3KeyTranslator.GetModifierState(SDL.Keymod.None, false) == 0, "No modifiers and capsLock=false yields 0");

            Console.WriteLine("  GetModifierState-bitmask test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  GetModifierState-bitmask test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
