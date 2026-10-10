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
        if (TestKeysymIsMaskedTo7Bits()) passed++; else failed++;
        if (TestKeysPressedIncrementsOnKeyDownOnly()) passed++; else failed++;
        if (TestSecondKeyWaitsUntilFirstIsRead()) passed++; else failed++;
        if (TestQueuePreservesOrder()) passed++; else failed++;
        if (TestFullQueueDropsNewKeys()) passed++; else failed++;
        if (TestBusyTimeoutDropsHeadKey()) passed++; else failed++;

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
            ushort high = iob.Read(0x42);
            Assert(high == 0x00F9, $"kbd high carries the NKB header 0x00F9 (0xF90000 >> 16), got 0x{high:X}");

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
            ushort high = iob.Read(0x42);
            Assert(high == 0x00F9, $"kbd high carries the NKB header 0x00F9 for a key-up too, got 0x{high:X}");

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

    private static bool TestKeysymIsMaskedTo7Bits()
    {
        Console.WriteLine("Test: a keysym above 0x7F is masked to 7 bits (real code ORs cadet_scancode & 0x7F)");
        try
        {
            var (keyboard, iob) = MakeWiredKeyboard();

            keyboard.KeyDown(0x1D3, modifiers: 0); // low 7 bits = 0x53
            Assert(iob.Read(0x40) == 0x0053, "KeyDown low word is masked to 7 bits (0x1D3 -> 0x53)");
            Assert(iob.Read(0x42) == 0x00F9, "KeyDown high word is still the NKB header");

            keyboard.KeyUp(0x1D3, modifiers: 0);
            Assert(iob.Read(0x40) == 0x0153, "KeyUp low word is bit 8 | (0x1D3 & 0x7F)");
            Assert(iob.Read(0x42) == 0x00F9, "KeyUp high word is still the NKB header");

            Console.WriteLine("  Keysym-masked-to-7-bits test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Keysym-masked-to-7-bits test failed: {ex.Message}\n");
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

    private static bool TestSecondKeyWaitsUntilFirstIsRead()
    {
        Console.WriteLine("Test: a second key stays queued (the first is not overwritten) until Lisp reads the first, then Tick delivers it");
        try
        {
            var (keyboard, iob) = MakeWiredKeyboard();

            keyboard.KeyDown(0x53, modifiers: 0);
            keyboard.KeyDown(0x54, modifiers: 0);

            Assert(keyboard.QueuedCount == 1, $"second key is queued, got {keyboard.QueuedCount}");
            keyboard.Tick();
            Assert(keyboard.QueuedCount == 1, "Tick does not deliver while the IOB keyboard is busy");
            Assert(iob.Read(0x40) == 0x53, "first scancode is intact");

            keyboard.Tick();
            Assert(keyboard.QueuedCount == 0, "Tick delivers the queued key once the first was read");
            Assert(iob.IsKeyboardReadySet, "delivery sets keyboard-ready again");
            Assert(iob.Read(0x40) == 0x54, "second scancode delivered");

            Console.WriteLine("  Second-key-waits test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Second-key-waits test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestQueuePreservesOrder()
    {
        Console.WriteLine("Test: a burst of key events is delivered in order, one per read (a Shift release cannot be lost)");
        try
        {
            var (keyboard, iob) = MakeWiredKeyboard();

            keyboard.KeyDown(0x10, 0);  // e.g. shift down
            keyboard.KeyDown(0x53, 0);
            keyboard.KeyUp(0x53, 0);
            keyboard.KeyUp(0x10, 0);

            ushort[] expected = { 0x10, 0x53, 0x153, 0x110 };
            foreach (ushort want in expected)
            {
                ushort got = iob.Read(0x40);
                Assert(got == want, $"expected 0x{want:X}, got 0x{got:X}");
                keyboard.Tick();
            }
            Assert(keyboard.QueuedCount == 0 && !iob.IsKeyboardReadySet, "queue drained, nothing left pending");

            Console.WriteLine("  Queue-preserves-order test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Queue-preserves-order test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestFullQueueDropsNewKeys()
    {
        Console.WriteLine("Test: the queue holds 100 scancodes; further keys are dropped");
        try
        {
            var (keyboard, iob) = MakeWiredKeyboard();

            // 1 delivered immediately + 100 queued; the rest are dropped.
            for (uint i = 1; i <= 105; i++)
                keyboard.KeyDown(i, 0);

            Assert(keyboard.QueuedCount == 100, $"queue is capped at 100, got {keyboard.QueuedCount}");

            // Accepted keys are 1..101 in order; 102..105 were dropped.
            for (uint want = 1; want <= 101; want++)
            {
                ushort got = iob.Read(0x40);
                Assert(got == want, $"expected {want}, got {got}");
                keyboard.Tick();
            }
            Assert(!iob.IsKeyboardReadySet, "nothing beyond the 101 accepted keys is delivered");

            Console.WriteLine("  Full-queue-drops test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Full-queue-drops test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBusyTimeoutDropsHeadKey()
    {
        Console.WriteLine("Test: a key blocked for 510 ms of IOB-busy is dropped, then the next one gets its own wait");
        try
        {
            long now = 1000;
            var iob = new Iob(new UCode(new MainMemory()));
            var keyboard = new Keyboard(() => now);
            keyboard.WireIob(iob);

            keyboard.KeyDown(0x01, 0);   // delivered, IOB now busy
            keyboard.KeyDown(0x02, 0);   // queued; busy timer starts
            keyboard.KeyDown(0x03, 0);   // queued behind it

            now += 509;
            keyboard.Tick();
            Assert(keyboard.QueuedCount == 2, "still waiting at 509 ms");

            now += 1;
            keyboard.Tick();
            Assert(keyboard.QueuedCount == 1, "head key (0x02) dropped at 510 ms");

            now += 509;
            keyboard.Tick();
            Assert(keyboard.QueuedCount == 1, "the next key gets its own fresh 510 ms wait");

            Assert(iob.Read(0x40) == 0x01, "the delivered key was never overwritten");
            keyboard.Tick();
            Assert(iob.Read(0x40) == 0x03, "once Lisp reads, the surviving key is delivered");

            Console.WriteLine("  Busy-timeout test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Busy-timeout test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
