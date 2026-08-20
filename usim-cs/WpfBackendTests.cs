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
        if (TestBeepWavBuilder()) passed++; else failed++;
        if (TestDisplayByteOrder()) passed++; else failed++;
        if (TestMouseDefaults()) passed++; else failed++;

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
            Assert(WpfKeyTranslator.TranslateKey(Key.OemPipe, shift: false) == 0x005C, "OemPipe (no shift) -> '\\'");
            Assert(WpfKeyTranslator.TranslateKey(Key.OemPipe, shift: true) == 0x007C, "OemPipe (shift) -> '|'");
            Assert(WpfKeyTranslator.TranslateKey(Key.OemComma, shift: false) == 0x002C, "OemComma (no shift) -> ','");
            Assert(WpfKeyTranslator.TranslateKey(Key.OemComma, shift: true) == 0x003C, "OemComma (shift) -> '<'");

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
            Assert(WpfKeyTranslator.GetModifierState(ModifierKeys.Shift | ModifierKeys.Control, capsLock: false) == ((1 << 0) | (1 << 2)), "Shift+Control combined bits");

            Console.WriteLine("  Modifier State tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Modifier State tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBeepWavBuilder()
    {
        Console.WriteLine("Test: Beep WAV Builder");
        try
        {
            // half-wavelength 500us -> 1000 Hz; 100ms duration -> 4410 samples
            byte[] wav = BeepWavBuilder.BuildWav(halfWavelengthMicros: 500, durationMicros: 100_000);

            int expectedSamples = (int)(BeepWavBuilder.SampleRate * 0.1);
            int expectedLength = 44 + expectedSamples * 2;

            Assert(wav.Length == expectedLength, $"WAV length is {expectedLength} bytes");
            Assert(Encoding.ASCII.GetString(wav, 0, 4) == "RIFF", "RIFF header");
            Assert(Encoding.ASCII.GetString(wav, 8, 4) == "WAVE", "WAVE header");
            Assert(Encoding.ASCII.GetString(wav, 36, 4) == "data", "data chunk header");

            Console.WriteLine("  Beep WAV Builder tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Beep WAV Builder tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDisplayByteOrder()
    {
        Console.WriteLine("Test: Display Frame Buffer Byte Order");
        try
        {
            var display = new Display { Mode = DisplayMode.Color };
            // Pixel (0,0) = color index 1 (Blue: R=0, G=0, B=170) in the
            // top 4 bits of the first video memory word.
            display.WriteVideoMemory(display.VideoMemoryBase, 0x10000000u);
            display.Update();

            byte b = display.FrameBuffer[0];
            byte g = display.FrameBuffer[1];
            byte r = display.FrameBuffer[2];
            byte a = display.FrameBuffer[3];

            Assert(b == 170 && g == 0 && r == 0 && a == 255,
                $"Frame buffer is B,G,R,A order (got B={b},G={g},R={r},A={a})");

            Console.WriteLine("  Display Frame Buffer Byte Order tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Display Frame Buffer Byte Order tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMouseDefaults()
    {
        Console.WriteLine("Test: Mouse Default Bounds");
        try
        {
            var mouse = new Mouse();
            Assert(mouse.MaxX == Display.WIDTH, "MaxX defaults to Display.WIDTH");
            Assert(mouse.MaxY == Display.HEIGHT, "MaxY defaults to Display.HEIGHT");

            Console.WriteLine("  Mouse Default Bounds tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Mouse Default Bounds tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
