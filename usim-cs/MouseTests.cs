// MouseTests.cs - Tests for Mouse's real toggle-based wire protocol
// (usim/mouse.c's mouse_event tail/middle/head XOR semantics, delivered
// through usim/iob.c's iob_set_mouse_ready path).

using System;

namespace Usim;

public static class MouseTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== Mouse Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestMoveToTriggersMouseReady()) passed++; else failed++;
        if (TestLeftButtonTogglesTailBitEachTransition()) passed++; else failed++;
        if (TestMiddleButtonTogglesMiddleBit()) passed++; else failed++;
        if (TestRightButtonTogglesHeadBit()) passed++; else failed++;
        if (TestButtonsFlagsStillTrackLevelState()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static (Mouse mouse, Iob iob) MakeWiredMouse()
    {
        var iob = new Iob(new UCode(new MainMemory()));
        var mouse = new Mouse();
        mouse.WireIob(iob);
        return (mouse, iob);
    }

    private static bool TestMoveToTriggersMouseReady()
    {
        Console.WriteLine("Test: MoveTo reports the new position to Iob and sets the mouse-ready bit");
        try
        {
            var (mouse, iob) = MakeWiredMouse();

            mouse.MoveTo(100, 200);

            Assert((iob.Read(0x4A) & 0x10) == 0x10, "mouse-ready bit is set after MoveTo");

            Console.WriteLine("  MoveTo-triggers-mouse-ready test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MoveTo-triggers-mouse-ready test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestLeftButtonTogglesTailBitEachTransition()
    {
        Console.WriteLine("Test: each Left-button transition (down or up) XORs the tail bit (bit 12 of mouse Y) -- edge-triggered, not level-tracked");
        try
        {
            var (mouse, iob) = MakeWiredMouse();
            mouse.MoveTo(0, 0); // establish a known, all-clear masked Y baseline

            mouse.ButtonDown(MouseButtons.Left);
            Assert((iob.Read(0x44) & 0x1000) == 0x1000, "tail bit sets on the first Left-button transition (down)");

            mouse.ButtonUp(MouseButtons.Left);
            Assert((iob.Read(0x44) & 0x1000) == 0, "tail bit toggles back to 0 on the next transition (up) -- a real toggle, not a held-state flag");

            Console.WriteLine("  Left-button-tail-toggle test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Left-button-tail-toggle test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMiddleButtonTogglesMiddleBit()
    {
        Console.WriteLine("Test: Middle-button transitions XOR the middle bit (bit 13 of mouse Y)");
        try
        {
            var (mouse, iob) = MakeWiredMouse();
            mouse.MoveTo(0, 0);

            mouse.ButtonDown(MouseButtons.Middle);
            Assert((iob.Read(0x44) & 0x2000) == 0x2000, "middle bit sets on Middle-button-down");

            Console.WriteLine("  Middle-button-toggle test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Middle-button-toggle test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestRightButtonTogglesHeadBit()
    {
        Console.WriteLine("Test: Right-button transitions XOR the head bit (bit 14 of mouse Y)");
        try
        {
            var (mouse, iob) = MakeWiredMouse();
            mouse.MoveTo(0, 0);

            mouse.ButtonDown(MouseButtons.Right);
            Assert((iob.Read(0x44) & 0x4000) == 0x4000, "head bit sets on Right-button-down");

            Console.WriteLine("  Right-button-toggle test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Right-button-toggle test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestButtonsFlagsStillTrackLevelState()
    {
        Console.WriteLine("Test: the WPF-facing Buttons flags still track ordinary held/released state, independent of the hardware toggle bits");
        try
        {
            var (mouse, _) = MakeWiredMouse();

            mouse.ButtonDown(MouseButtons.Left);
            Assert(mouse.IsButtonPressed(MouseButtons.Left), "Left reads as pressed after ButtonDown");

            mouse.ButtonUp(MouseButtons.Left);
            Assert(!mouse.IsButtonPressed(MouseButtons.Left), "Left reads as released after ButtonUp");

            Console.WriteLine("  Buttons-flags-level-state test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Buttons-flags-level-state test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
