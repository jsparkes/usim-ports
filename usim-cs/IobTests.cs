// IobTests.cs - Tests for the faithful Iob port (usim/iob.c's WITH_SDL3 path).

using System;

namespace Usim;

public static class IobTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== Iob Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestCsrWriteMasksToLow4Bits()) passed++; else failed++;
        if (TestKeyboardReadySetAndClearOnLowWordOnly()) passed++; else failed++;
        if (TestKeyboardInterruptGating()) passed++; else failed++;
        if (TestMouseReadySetAndClearOnYOnly()) passed++; else failed++;
        if (TestMouseInterruptGating()) passed++; else failed++;
        if (TestMouseBitLayoutWithRawAlwaysZero()) passed++; else failed++;
        if (TestUsecClockStartsAtZeroThenIncreases()) passed++; else failed++;
        if (Test60HzClockIncrementsOncePerTick()) passed++; else failed++;
        if (TestIntervalTimerFiresAtComputedTickCount()) passed++; else failed++;
        if (TestBeepCallbackOnReadAndWrite()) passed++; else failed++;
        if (TestGpioSerialUndocumentedStubsDoNotThrow()) passed++; else failed++;
        if (TestChaosStubRegistersDoNotThrow()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static Iob MakeIob(out UCode ucode)
    {
        ucode = new UCode(new MainMemory());
        ucode.SetInterruptStatusReg(0x400);  // Enable Unibus interrupts
        return new Iob(ucode);
    }

    private static bool TestCsrWriteMasksToLow4Bits()
    {
        Console.WriteLine("Test: CSR write only touches the low 4 bits");
        try
        {
            var iob = MakeIob(out _);

            iob.Write(0x4A, 0xFFFF);
            Assert(iob.Read(0x4A) == 0xF, $"CSR write masks to low 4 bits, got 0x{iob.Read(0x4A):X}");

            Console.WriteLine("  CSR-write-mask test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  CSR-write-mask test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestKeyboardReadySetAndClearOnLowWordOnly()
    {
        Console.WriteLine("Test: SetKeyboardReady sets CSR bit 5; reading the low word clears it, reading the high word does not");
        try
        {
            var iob = MakeIob(out _);

            iob.SetKeyboardReady(0x00120034); // arbitrary scancode with distinct low/high halves
            Assert((iob.Read(0x4A) & 0x20) == 0x20, "keyboard-ready bit is set after SetKeyboardReady");

            ushort high = iob.Read(0x42); // high word first -- must not clear the ready bit
            Assert(high == 0x0012, $"kbd high returns the upper 16 bits, got 0x{high:X}");
            Assert((iob.Read(0x4A) & 0x20) == 0x20, "keyboard-ready bit is still set after reading kbd high");

            ushort low = iob.Read(0x40); // low word -- clears the ready bit
            Assert(low == 0x0034, $"kbd low returns the lower 16 bits, got 0x{low:X}");
            Assert((iob.Read(0x4A) & 0x20) == 0, "keyboard-ready bit is cleared after reading kbd low");

            Console.WriteLine("  Keyboard-ready set/clear-on-low-word-only test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Keyboard-ready set/clear-on-low-word-only test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestKeyboardInterruptGating()
    {
        Console.WriteLine("Test: SetKeyboardReady asserts the Unibus interrupt only when CSR bit 2 (keyboard-interrupt-enable) is set");
        try
        {
            var iob = MakeIob(out UCode ucode);

            iob.SetKeyboardReady(0x1);
            Assert(!ucode.InterruptPendingFlag, "no interrupt when keyboard-interrupt-enable is clear");

            iob.Write(0x4A, 0x4); // interrupt-enable bit 2
            iob.SetKeyboardReady(0x2);
            Assert(ucode.InterruptPendingFlag, "interrupt asserted once keyboard-interrupt-enable is set");
            Assert((ucode.InterruptStatusReg & 0x3FC) == 0xB0, $"keyboard interrupt vector is 0xB0, got 0x{ucode.InterruptStatusReg & 0x3FC:X}");

            Console.WriteLine("  Keyboard-interrupt-gating test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Keyboard-interrupt-gating test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMouseReadySetAndClearOnYOnly()
    {
        Console.WriteLine("Test: SetMouseReady sets CSR bit 4; reading mouse Y clears it, reading mouse X does not");
        try
        {
            var iob = MakeIob(out _);

            iob.SetMouseReady(10, 20, false, false, false);
            Assert((iob.Read(0x4A) & 0x10) == 0x10, "mouse-ready bit is set after SetMouseReady");

            iob.Read(0x46); // mouse X -- must not clear the ready bit
            Assert((iob.Read(0x4A) & 0x10) == 0x10, "mouse-ready bit is still set after reading mouse X");

            iob.Read(0x44); // mouse Y -- clears the ready bit
            Assert((iob.Read(0x4A) & 0x10) == 0, "mouse-ready bit is cleared after reading mouse Y");

            Console.WriteLine("  Mouse-ready set/clear-on-Y-only test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Mouse-ready set/clear-on-Y-only test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMouseInterruptGating()
    {
        Console.WriteLine("Test: SetMouseReady asserts the Unibus interrupt only when CSR bit 1 (mouse-interrupt-enable) is set");
        try
        {
            var iob = MakeIob(out UCode ucode);

            iob.SetMouseReady(1, 1, false, false, false);
            Assert(!ucode.InterruptPendingFlag, "no interrupt when mouse-interrupt-enable is clear");

            iob.Write(0x4A, 0x2); // interrupt-enable bit 1
            iob.SetMouseReady(2, 2, false, false, false);
            Assert(ucode.InterruptPendingFlag, "interrupt asserted once mouse-interrupt-enable is set");
            Assert((ucode.InterruptStatusReg & 0x3FC) == 0xB4, $"mouse interrupt vector is 0xB4, got 0x{ucode.InterruptStatusReg & 0x3FC:X}");

            Console.WriteLine("  Mouse-interrupt-gating test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Mouse-interrupt-gating test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMouseBitLayoutWithRawAlwaysZero()
    {
        Console.WriteLine("Test: mouse X/Y register bit layout matches the real 0x7777 mask, with rawX/rawY always 0, and tail/middle/head packed into Y at bits 12/13/14");
        try
        {
            var iob = MakeIob(out _);

            // x=0xFFFF exercises the mask on its own: every bit set, masking
            // must clear every 4th bit, leaving exactly 0x7777.
            int x = 0xFFFF;
            // y=0 is deliberate, not arbitrary: 0x7777's own top nibble is
            // 0111, meaning bits 12/13/14 are ALREADY set by the mask
            // whenever y's own top nibble has any bits set (e.g. y=0xFFFF
            // masks to 0x7777, which already has bits 12 and 14 set --
            // independently verified via Python before writing this test).
            // That would make tailPressed/headPressed's OR'd-in bits
            // indistinguishable from the mask's own contribution -- a
            // vacuous test. y=0 makes the masked contribution to those bits
            // exactly 0, so the toggle bits below are the only source of
            // any 1s there, making this a genuinely discriminating check.
            int y = 0;
            iob.SetMouseReady(x, y, tailPressed: true, middlePressed: false, headPressed: true);

            ushort mx = iob.Read(0x46);
            Assert(mx == 0x7777, $"mouse X is x masked by 0x7777 with rawX/rawY always 0, got 0x{mx:X}");

            ushort my = iob.Read(0x44);
            // bits 12=tail(1), 13=middle(0), 14=head(1) over a masked y of 0 -> 0x5000 exactly.
            ushort expectedY = (ushort)((1 << 12) | (0 << 13) | (1 << 14) | (y & 0x7777));
            Assert(my == expectedY, $"mouse Y packs tail/middle/head at bits 12/13/14 over the masked y, expected 0x{expectedY:X} got 0x{my:X}");

            // Asymmetric cases (y=0): each button alone, so swapped or wrong
            // shifts cannot cancel out.
            iob.SetMouseReady(0, 0, tailPressed: true, middlePressed: false, headPressed: false);
            ushort tailOnly = iob.Read(0x44);
            Assert(tailOnly == 0x1000, $"tail alone sets bit 12 only, expected 0x1000 got 0x{tailOnly:X}");

            iob.SetMouseReady(0, 0, tailPressed: false, middlePressed: true, headPressed: false);
            ushort middleOnly = iob.Read(0x44);
            Assert(middleOnly == 0x2000, $"middle alone sets bit 13 only, expected 0x2000 got 0x{middleOnly:X}");

            iob.SetMouseReady(0, 0, tailPressed: false, middlePressed: false, headPressed: true);
            ushort headOnly = iob.Read(0x44);
            Assert(headOnly == 0x4000, $"head alone sets bit 14 only, expected 0x4000 got 0x{headOnly:X}");

            Console.WriteLine("  Mouse-bit-layout test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Mouse-bit-layout test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUsecClockStartsAtZeroThenIncreases()
    {
        Console.WriteLine("Test: the usec clock returns 0 on its first read, then a monotonically increasing value");
        try
        {
            var iob = MakeIob(out _);

            // A high read before any low read returns 0 (nothing latched yet)
            // and must NOT start the clock.
            Assert(iob.Read(0x52) == 0, "usec high before any low read returns 0");
            System.Threading.Thread.Sleep(5);

            ushort firstLow = iob.Read(0x50);
            ushort firstHigh = iob.Read(0x52);
            Assert(firstLow == 0 && firstHigh == 0, $"first read is 0, got low=0x{firstLow:X} high=0x{firstHigh:X}");

            System.Threading.Thread.Sleep(5);

            ushort secondLow = iob.Read(0x50);
            Assert(secondLow > 0, $"a later read is greater than 0 after real elapsed time, got 0x{secondLow:X}");

            Console.WriteLine("  Usec-clock test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Usec-clock test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool Test60HzClockIncrementsOncePerTick()
    {
        Console.WriteLine("Test: the 60Hz clock increments exactly once per Tick() call");
        try
        {
            var iob = MakeIob(out _);

            Assert(iob.Read(0x54) == 0, "60Hz clock starts at 0");
            iob.Tick();
            Assert(iob.Read(0x54) == 1, $"60Hz clock is 1 after one Tick(), got {iob.Read(0x54)}");
            iob.Tick();
            iob.Tick();
            Assert(iob.Read(0x54) == 3, $"60Hz clock is 3 after three Tick() calls total, got {iob.Read(0x54)}");

            Console.WriteLine("  60Hz-clock-increments-per-tick test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  60Hz-clock-increments-per-tick test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestIntervalTimerFiresAtComputedTickCount()
    {
        Console.WriteLine("Test: writing the interval timer register fires the Unibus interrupt at exactly the computed tick count");
        try
        {
            var iob = MakeIob(out UCode ucode);

            // 16 * v / 16000.0 ticks, ceiling. v=2000 -> 16*2000/16000 = 2.0 -> 2 ticks exactly.
            iob.Write(0x54, 2000);

            iob.Tick();
            Assert(!ucode.InterruptPendingFlag, "no interrupt after the first of two required ticks");

            iob.Tick();
            Assert(ucode.InterruptPendingFlag, "interrupt fires on exactly the second tick");
            Assert((ucode.InterruptStatusReg & 0x3FC) == 0xBC, $"interval-timer interrupt vector is 0xBC, got 0x{ucode.InterruptStatusReg & 0x3FC:X}");

            Console.WriteLine("  Interval-timer-fires-at-computed-tick-count test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Interval-timer-fires-at-computed-tick-count test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBeepCallbackOnReadAndWrite()
    {
        Console.WriteLine("Test: reading the beep register invokes OnBeep with the real fixed default (744); writing invokes it with the written value");
        try
        {
            var iob = MakeIob(out _);
            int? lastBeep = null;
            iob.OnBeep = v => lastBeep = v;

            iob.Read(0x48);
            Assert(lastBeep == 744, $"read-triggered beep uses the real fixed default 744, got {lastBeep}");

            lastBeep = null;
            iob.Write(0x48, 500);
            Assert(lastBeep == 500, $"write-triggered beep passes the written value through, got {lastBeep}");

            Console.WriteLine("  Beep-callback test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Beep-callback test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestGpioSerialUndocumentedStubsDoNotThrow()
    {
        Console.WriteLine("Test: GPIO/serial/undocumented registers return 0 and never throw, on read or write");
        try
        {
            var iob = MakeIob(out _);

            Assert(iob.Read(0x56) == 0, "gpio read returns 0");
            Assert(iob.Read(0x70) == 0, "serial read returns 0");
            Assert(iob.Read(0x78) == 0, "undocumented read returns 0");
            iob.Write(0x70, 0x1234); // must not throw

            Console.WriteLine("  GPIO/serial/undocumented-stubs test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  GPIO/serial/undocumented-stubs test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestChaosStubRegistersDoNotThrow()
    {
        Console.WriteLine("Test: Chaos-interface stub registers return their documented idle values and never throw, on read or write");
        try
        {
            var iob = MakeIob(out _);

            Assert(iob.Read(0x60) == 0, "ch_csr reads 0 (idle)");
            Assert(iob.Read(0x62) == 0x7C20, $"ch_myaddr reads the placeholder address, got 0x{iob.Read(0x62):X}");
            Assert(iob.Read(0x64) == 0, "ch_rbf reads 0");
            Assert(iob.Read(0x66) == 0, "ch_rbc reads 0");
            Assert(iob.Read(0x6A) == 0x7C20, "ch_xmt echoes the placeholder address");
            iob.Write(0x60, 0xFFFF); // must not throw, must not change ch_csr's idle read
            Assert(iob.Read(0x60) == 0, "ch_csr write is accepted and ignored");

            Console.WriteLine("  Chaos-stub-registers test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Chaos-stub-registers test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
