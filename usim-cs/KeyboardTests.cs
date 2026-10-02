// KeyboardTests.cs - Tests for Keyboard's real scancode wire protocol
// (usim/iob.c's iob_set_keyboard_ready path).

using System;

namespace Usim;

public static class KeyboardTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== Keyboard Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestKeyDownDeliversScancodeWithBit8Clear()) passed++; else failed++;
        if (TestKeyUpDeliversScancodeWithBit8Set()) passed++; else failed++;
        if (TestZeroKeysymIsANoOp()) passed++; else failed++;
        if (TestKeysPressedIncrementsOnKeyDownOnly()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static (Keyboard keyboard, Iob iob) MakeWiredKeyboard()
    {
        var iob = new Iob(new UCode(new MainMemory()));
        var keyboard = new Keyboard();
        keyboard.WireIob(iob);
        return (keyboard, iob);
    }

    private static bool TestKeyDownDeliversScancodeWithBit8Clear()
    {
        Console.WriteLine("Test: KeyDown delivers the scancode to a wired Iob with bit 8 clear (key-down encoding)");
        try
        {
            var (keyboard, iob) = MakeWiredKeyboard();

            keyboard.KeyDown(0x53, modifiers: 0); // 'A' scancode (Task 2's real Cadet table)

            Assert((iob.Read(0x4A) & 0x20) == 0x20, "keyboard-ready bit is set after KeyDown");
            ushort scancode = iob.Read(0x40);
            Assert(scancode == 0x53, $"kbd low reflects the scancode with bit 8 clear, got 0x{scancode:X}");

            Console.WriteLine("  KeyDown-bit8-clear test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  KeyDown-bit8-clear test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestKeyUpDeliversScancodeWithBit8Set()
    {
        Console.WriteLine("Test: KeyUp delivers the scancode to a wired Iob with bit 8 set (key-up encoding)");
        try
        {
            var (keyboard, iob) = MakeWiredKeyboard();

            keyboard.KeyUp(0x53, modifiers: 0);

            ushort scancode = iob.Read(0x40);
            Assert(scancode == 0x153, $"kbd low is 0x153 (bit 8 set, low byte 0x53) for a key-up event, got 0x{scancode:X}");

            Console.WriteLine("  KeyUp-bit8-set test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  KeyUp-bit8-set test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestZeroKeysymIsANoOp()
    {
        Console.WriteLine("Test: KeyDown/KeyUp with keysym=0 (no real Cadet mapping) never reaches Iob");
        try
        {
            var (keyboard, iob) = MakeWiredKeyboard();

            keyboard.KeyDown(0, modifiers: 0);
            Assert((iob.Read(0x4A) & 0x20) == 0, "keyboard-ready bit stays clear for an unmapped key (keysym=0)");

            Console.WriteLine("  Zero-keysym-is-a-no-op test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Zero-keysym-is-a-no-op test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestKeysPressedIncrementsOnKeyDownOnly()
    {
        Console.WriteLine("Test: KeysPressed increments on KeyDown but not KeyUp");
        try
        {
            var (keyboard, _) = MakeWiredKeyboard();

            keyboard.KeyDown(0x53, modifiers: 0);
            Assert(keyboard.KeysPressed == 1, $"KeysPressed is 1 after one KeyDown, got {keyboard.KeysPressed}");

            keyboard.KeyUp(0x53, modifiers: 0);
            Assert(keyboard.KeysPressed == 1, $"KeysPressed is still 1 after a KeyUp, got {keyboard.KeysPressed}");

            Console.WriteLine("  KeysPressed-increments-on-keydown-only test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  KeysPressed-increments-on-keydown-only test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
