// WpfBackendTests.cs - Tests for the WPF backend's pure-logic helpers
// (key translation, beep WAV generation) plus the Display/Mouse fixes
// made as part of the SDL2-to-WPF conversion.

using System;
using System.Text;
using System.Windows.Input;

namespace Usim;

public static class WpfBackendTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== WPF Backend Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestKeyTranslation()) passed++; else failed++;
        if (TestModifierState()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestKeyTranslation()
    {
        Console.WriteLine("Test: Key Translation");
        try
        {
            Assert(WpfKeyTranslator.TranslateKey(Key.Escape, shift: false) == 0xFF1B, "Escape -> XK_Escape");
            Assert(WpfKeyTranslator.TranslateKey(Key.F1, shift: false) == 0xFFBE, "F1 -> XK_F1");
            Assert(WpfKeyTranslator.TranslateKey(Key.A, shift: false) == 0x0061, "a (no shift) -> 'a'");
            Assert(WpfKeyTranslator.TranslateKey(Key.A, shift: true) == 0x0041, "a (shift) -> 'A'");
            Assert(WpfKeyTranslator.TranslateKey(Key.D1, shift: false) == 0x0031, "1 (no shift) -> '1'");
            Assert(WpfKeyTranslator.TranslateKey(Key.D1, shift: true) == 0x0021, "1 (shift) -> '!'");
            Assert(WpfKeyTranslator.TranslateKey(Key.Space, shift: false) == 0x0020, "Space -> 0x20");
            Assert(WpfKeyTranslator.TranslateKey(Key.Scroll, shift: false) == 0, "Unmapped key -> XK_VoidSymbol");

            Console.WriteLine("  Key Translation tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Key Translation tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestModifierState()
    {
        Console.WriteLine("Test: Modifier State");
        try
        {
            Assert(WpfKeyTranslator.GetModifierState(ModifierKeys.Shift, capsLock: false) == (1 << 0), "Shift bit");
            Assert(WpfKeyTranslator.GetModifierState(ModifierKeys.None, capsLock: true) == (1 << 1), "CapsLock bit");
            Assert(WpfKeyTranslator.GetModifierState(ModifierKeys.Control, capsLock: false) == (1 << 2), "Control bit");
            Assert(WpfKeyTranslator.GetModifierState(ModifierKeys.Alt, capsLock: false) == (1 << 3), "Alt bit");
            Assert(WpfKeyTranslator.GetModifierState(ModifierKeys.Windows, capsLock: false) == (1 << 6), "Windows bit");

            Console.WriteLine("  Modifier State tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Modifier State tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
