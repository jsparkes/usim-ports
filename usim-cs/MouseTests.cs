// MouseTests.cs - Tests for Mouse: level-based buttons through the IOB and
// SDL3-style A-memory position injection (usim/sdl3-mouse.c).

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
        if (TestMoveToSendsZeroPositionToIob()) passed++; else failed++;
        if (TestLeftButtonIsLevelBased()) passed++; else failed++;
        if (TestMiddleButtonSetsMiddleBit()) passed++; else failed++;
        if (TestRightButtonSetsHeadBit()) passed++; else failed++;
        if (TestButtonsFlagsStillTrackLevelState()) passed++; else failed++;
        if (TestMoveToInjectsPositionIntoAMem()) passed++; else failed++;
        if (TestMoveToWithoutSymbolsIsSilentNoOp()) passed++; else failed++;
        if (TestMoveToWithMissingSymbolNameIsSilentNoOp()) passed++; else failed++;

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
        Console.WriteLine("Test: MoveTo reports to Iob and sets the mouse-ready bit");
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

    private static bool TestMoveToSendsZeroPositionToIob()
    {
        Console.WriteLine("Test: MoveTo leaves the IOB X/Y registers at 0 (position goes via A-memory, not the registers)");
        try
        {
            var (mouse, iob) = MakeWiredMouse();

            mouse.MoveTo(100, 200);

            Assert(iob.Read(0x46) == 0, $"mouse X register is 0, got 0x{iob.Read(0x46):X}");
            Assert((iob.Read(0x44) & 0x7777) == 0, $"mouse Y register position bits are 0, got 0x{iob.Read(0x44):X}");
            Assert((iob.Read(0x44) & 0x7000) == 0, "no buttons held, so Y has no button bits either");

            Console.WriteLine("  MoveTo-sends-zero-position test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MoveTo-sends-zero-position test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestLeftButtonIsLevelBased()
    {
        Console.WriteLine("Test: Left button sets the tail bit (bit 12 of mouse Y) while held, clears on release; a redundant press does not toggle it back");
        try
        {
            var (mouse, iob) = MakeWiredMouse();
            mouse.MoveTo(0, 0);

            mouse.ButtonDown(MouseButtons.Left);
            Assert((iob.Read(0x44) & 0x1000) == 0x1000, "tail bit set on Left down");

            mouse.ButtonDown(MouseButtons.Left);
            Assert((iob.Read(0x44) & 0x1000) == 0x1000, "tail bit still set after a redundant second Left down");

            mouse.ButtonUp(MouseButtons.Left);
            Assert((iob.Read(0x44) & 0x1000) == 0, "tail bit clear after Left up");

            mouse.ButtonUp(MouseButtons.Left);
            Assert((iob.Read(0x44) & 0x1000) == 0, "tail bit still clear after a redundant second Left up");

            Console.WriteLine("  Left-button-level test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Left-button-level test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMiddleButtonSetsMiddleBit()
    {
        Console.WriteLine("Test: Middle button sets the middle bit (bit 13 of mouse Y) while held");
        try
        {
            var (mouse, iob) = MakeWiredMouse();
            mouse.MoveTo(0, 0);

            mouse.ButtonDown(MouseButtons.Middle);
            Assert((iob.Read(0x44) & 0x2000) == 0x2000, "middle bit set on Middle down");
            Assert((iob.Read(0x44) & 0x5000) == 0, "other button bits stay clear");

            mouse.ButtonUp(MouseButtons.Middle);
            Assert((iob.Read(0x44) & 0x2000) == 0, "middle bit clear on Middle up");

            Console.WriteLine("  Middle-button-level test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Middle-button-level test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestRightButtonSetsHeadBit()
    {
        Console.WriteLine("Test: Right button sets the head bit (bit 14 of mouse Y) while held");
        try
        {
            var (mouse, iob) = MakeWiredMouse();
            mouse.MoveTo(0, 0);

            mouse.ButtonDown(MouseButtons.Right);
            Assert((iob.Read(0x44) & 0x4000) == 0x4000, "head bit set on Right down");
            Assert((iob.Read(0x44) & 0x3000) == 0, "other button bits stay clear");

            mouse.ButtonUp(MouseButtons.Right);
            Assert((iob.Read(0x44) & 0x4000) == 0, "head bit clear on Right up");

            Console.WriteLine("  Right-button-level test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Right-button-level test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestButtonsFlagsStillTrackLevelState()
    {
        Console.WriteLine("Test: the WPF-facing Buttons flags track held/released state");
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

    // Runs body with Disassembler.Symbols temporarily replaced by a table
    // built from the given symbol lines (null = no symbols at all). The first
    // line goes on the "-2" marker line, the rest follow one per line.
    private static void WithSymbols(string[]? symbolLines, Action body)
    {
        var saved = Disassembler.Symbols;
        string? tempPath = null;
        try
        {
            if (symbolLines == null)
            {
                Disassembler.Symbols = null;
            }
            else
            {
                tempPath = System.IO.Path.GetTempFileName();
                System.IO.File.WriteAllText(tempPath,
                    "\n-4 \n\n-2 " + symbolLines[0] + " (junk)\n" +
                    string.Join("\n", symbolLines, 1, symbolLines.Length - 1) + "\n-1 \n");
                var table = new SymbolTable();
                table.LoadFromFile(tempPath);
                Disassembler.Symbols = table;
            }
            body();
        }
        finally
        {
            Disassembler.Symbols = saved;
            if (tempPath != null) System.IO.File.Delete(tempPath);
        }
    }

    // Octal values: 0100 = 0x40, 0101 = 0x41, 0102 = 0x42, 0103 = 0x43.
    private static readonly string[] FourMouseSymbols =
    {
        "A-MOUSE-X A-MEM 100",
        "A-MOUSE-Y A-MEM 101",
        "A-MOUSE-WAKEUP A-MEM 102",
        "A-V-TRUE A-MEM 103",
    };

    private static bool TestMoveToInjectsPositionIntoAMem()
    {
        Console.WriteLine("Test: MoveTo writes DTP_FIX x/y into A-MOUSE-X/Y and A-V-TRUE's value into A-MOUSE-WAKEUP (sdl3-mouse.c)");
        try
        {
            WithSymbols(FourMouseSymbols, () =>
            {
                var ucode = new UCode(new MainMemory());
                var iob = new Iob(ucode);
                var mouse = new Mouse();
                mouse.WireIob(iob);
                mouse.WireMicrocode(ucode);

                const uint vTrue = 0x0A123456;
                ucode.AMem[0x43] = vTrue;

                mouse.MoveTo(100, 200);

                uint x = ucode.AMem[0x40], y = ucode.AMem[0x41], wake = ucode.AMem[0x42];
                Assert(x == ((100u & 0xFFFFFF) | (5u << 25)), $"A-MOUSE-X is DTP_FIX(100), got 0x{x:X}");
                Assert(y == ((200u & 0xFFFFFF) | (5u << 25)), $"A-MOUSE-Y is DTP_FIX(200), got 0x{y:X}");
                Assert(wake == vTrue, $"A-MOUSE-WAKEUP holds A-V-TRUE's value, got 0x{wake:X}");
                Assert(iob.Read(0x46) == 0 && (iob.Read(0x44) & 0x7777) == 0, "IOB X/Y registers still 0");

                mouse.MoveTo(5, 7);
                Assert(ucode.AMem[0x40] == (5u | (5u << 25)) && ucode.AMem[0x41] == (7u | (5u << 25)),
                    "a second move updates both slots (resolved addresses are cached)");
            });

            Console.WriteLine("  MoveTo-injects-position-into-AMem test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MoveTo-injects-position-into-AMem test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMoveToWithoutSymbolsIsSilentNoOp()
    {
        Console.WriteLine("Test: with no microcode symbols loaded, MoveTo does not throw and writes nothing to A-memory");
        try
        {
            WithSymbols(null, () =>
            {
                var ucode = new UCode(new MainMemory());
                var iob = new Iob(ucode);
                var mouse = new Mouse();
                mouse.WireIob(iob);
                mouse.WireMicrocode(ucode);

                mouse.MoveTo(100, 200);
                mouse.ButtonDown(MouseButtons.Left);

                foreach (uint w in ucode.AMem)
                {
                    Assert(w == 0, "A-memory is untouched");
                }
                Assert((iob.Read(0x4A) & 0x10) == 0x10, "the IOB mouse-ready report still happens");
            });

            Console.WriteLine("  MoveTo-without-symbols test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MoveTo-without-symbols test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMoveToWithMissingSymbolNameIsSilentNoOp()
    {
        Console.WriteLine("Test: a symbol table lacking one of the four names makes injection a silent no-op");
        try
        {
            WithSymbols(new[] { "A-MOUSE-X A-MEM 100", "A-MOUSE-Y A-MEM 101", "A-MOUSE-WAKEUP A-MEM 102" }, () =>
            {
                var ucode = new UCode(new MainMemory());
                var mouse = new Mouse();
                mouse.WireIob(new Iob(ucode));
                mouse.WireMicrocode(ucode);

                mouse.MoveTo(100, 200);

                foreach (uint w in ucode.AMem)
                {
                    Assert(w == 0, "A-memory is untouched when A-V-TRUE is missing");
                }
            });

            Console.WriteLine("  MoveTo-with-missing-symbol test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MoveTo-with-missing-symbol test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
