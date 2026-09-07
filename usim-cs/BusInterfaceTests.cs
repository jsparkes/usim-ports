// BusInterfaceTests.cs - Tests for the faithful bus-interface port
// (usim/bus-interface.c), minus the permanently-out-of-scope lashup
// remote-debugger protocol.

using System;

namespace Usim;

public static class BusInterfaceTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== BusInterface Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestBusErrorStatusRoundtrip()) passed++; else failed++;
        if (TestNxmInhibitGating()) passed++; else failed++;
        if (TestUnibusMapErrorNotGated()) passed++; else failed++;
        if (TestInterruptStatusRegWriteMasks()) passed++; else failed++;
        if (TestBusStatusReadWriteRegister()) passed++; else failed++;
        if (TestDebuggeeStatusAlwaysZero()) passed++; else failed++;
        if (TestLashupOnlyRegistersNoOp()) passed++; else failed++;
        if (TestBusResetClearsState()) passed++; else failed++;
        if (TestDefaultCaseSetsUnibusNxm()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestBusErrorStatusRoundtrip()
    {
        Console.WriteLine("Test: bus_error_status get/set/reset roundtrip");
        try
        {
            var bi = new BusInterface(new UCode(new MainMemory()));

            Assert(bi.GetBusErrorStatus() == 0, "starts at 0");
            bi.SetXbusNxm();
            Assert(bi.IsXbusNxm(), "xbus nxm set");
            Assert(bi.GetBusErrorStatus() == 0x1, "bus_error_status == 0x1 after xbus nxm");
            bi.ResetBusErrorStatus();
            Assert(bi.GetBusErrorStatus() == 0, "reset clears status");

            Console.WriteLine("  Roundtrip test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Roundtrip test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestNxmInhibitGating()
    {
        Console.WriteLine("Test: SetXbusNxm/SetUnibusNxm respect nxm_inhibited");
        try
        {
            var bi = new BusInterface(new UCode(new MainMemory()));

            bi.SetNxmInhibit(true);
            bi.SetXbusNxm();
            Assert(!bi.IsXbusNxm(), "xbus nxm suppressed while inhibited");
            bi.SetUnibusNxm();
            Assert(!bi.IsUnibusNxm(), "unibus nxm suppressed while inhibited");

            bi.SetNxmInhibit(false);
            bi.SetXbusNxm();
            Assert(bi.IsXbusNxm(), "xbus nxm sets once inhibit lifted");

            Console.WriteLine("  Inhibit-gating test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Inhibit-gating test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUnibusMapErrorNotGated()
    {
        Console.WriteLine("Test: SetUnibusMapError is NOT gated by nxm_inhibited (real C asymmetry)");
        try
        {
            var bi = new BusInterface(new UCode(new MainMemory()));

            bi.SetNxmInhibit(true);
            bi.SetUnibusMapError();
            Assert(bi.IsUnibusMapError(), "unibus map error sets even while nxm-inhibited");

            Console.WriteLine("  Unibus-map-error asymmetry test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Unibus-map-error asymmetry test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestInterruptStatusRegWriteMasks()
    {
        Console.WriteLine("Test: 0766040/0766042 writes only touch their real bitmasks");
        try
        {
            var ucode = new UCode(new MainMemory());
            var bi = new BusInterface(ucode);

            // 0766040 mask is 0x3C01 (bits 0, 10-13). Write all-1s and
            // confirm only masked bits land.
            bi.Write(0x3EC20, 0xFFFFFFFF);
            Assert(ucode.InterruptStatusReg == 0x3C01,
                $"0766040 write masks to 0x3C01, got 0x{ucode.InterruptStatusReg:X}");

            ucode.SetInterruptStatusReg(0);

            // 0766042 mask is 0x83FC (bits 2-9, 15).
            bi.Write(0x3EC22, 0xFFFFFFFF);
            Assert(ucode.InterruptStatusReg == 0x83FC,
                $"0766042 write masks to 0x83FC, got 0x{ucode.InterruptStatusReg:X}");

            // Confirm the two masks are disjoint (no accidental overlap
            // corrupting the other register's bits) and their union is
            // exactly what a combined write would produce.
            ucode.SetInterruptStatusReg(0);
            bi.Write(0x3EC20, 0xFFFFFFFF);
            bi.Write(0x3EC22, 0xFFFFFFFF);
            Assert(ucode.InterruptStatusReg == (0x3C01 | 0x83FC),
                $"combined writes == 0x{(0x3C01 | 0x83FC):X}, got 0x{ucode.InterruptStatusReg:X}");

            Console.WriteLine("  Interrupt-status-reg write-mask tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Interrupt-status-reg write-mask tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBusStatusReadWriteRegister()
    {
        Console.WriteLine("Test: 0766044 read returns bus_error_status; write clears it");
        try
        {
            var bi = new BusInterface(new UCode(new MainMemory()));

            bi.SetXbusNxm();
            bi.SetUnibusMapError();
            Assert(bi.Read(0x3EC24) == (0x1 | 0x20),
                $"0766044 read == 0x{(0x1 | 0x20):X}, got 0x{bi.Read(0x3EC24):X}");

            bi.Write(0x3EC24, 0); // value written is ignored; write always clears
            Assert(bi.GetBusErrorStatus() == 0, "0766044 write clears bus_error_status");

            Console.WriteLine("  Bus-status register test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Bus-status register test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDebuggeeStatusAlwaysZero()
    {
        Console.WriteLine("Test: 0766104 always reads 0 (no debuggee ever attached)");
        try
        {
            var bi = new BusInterface(new UCode(new MainMemory()));
            Assert(bi.Read(0x3EC44) == 0, "0766104 reads 0");

            Console.WriteLine("  Debuggee-status test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Debuggee-status test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestLashupOnlyRegistersNoOp()
    {
        Console.WriteLine("Test: lashup-only registers (0766100/102/110/112/114) are non-fatal no-ops");
        try
        {
            var bi = new BusInterface(new UCode(new MainMemory()));

            Assert(bi.Read(0x3EC40) == 0, "0766100 read is a safe default (0)");
            bi.Write(0x3EC40, 0x1234);
            bi.Write(0x3EC42, 0x1234);
            bi.Write(0x3EC48, 0x1234);
            bi.Write(0x3EC4A, 0x1234);
            bi.Write(0x3EC4C, 0x1234);

            Console.WriteLine("  Lashup-only-registers test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Lashup-only-registers test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBusResetClearsState()
    {
        Console.WriteLine("Test: BusReset clears all local state");
        try
        {
            var bi = new BusInterface(new UCode(new MainMemory()));

            bi.SetNxmInhibit(true);
            bi.SetUnibusMapError(); // not gated by inhibit, so this sets bus_error_status
            bi.Write(0x3EC48, 0x1); // addr17 = true
            bi.Write(0x3EC4C, 0x42); // addr = 0x42

            bi.BusReset();

            Assert(bi.GetBusErrorStatus() == 0, "bus_error_status cleared");
            bi.SetXbusNxm();
            Assert(bi.IsXbusNxm(), "nxm_inhibited cleared (xbus nxm now takes effect)");

            Console.WriteLine("  BusReset test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  BusReset test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDefaultCaseSetsUnibusNxm()
    {
        Console.WriteLine("Test: an unrecognized uaddr sets Unibus NXM on both read and write, without throwing");
        try
        {
            var bi = new BusInterface(new UCode(new MainMemory()));

            Assert(bi.Read(0x3EC80) == 0, "unrecognized read returns 0");
            Assert(bi.IsUnibusNxm(), "unrecognized read sets unibus nxm");

            bi.ResetBusErrorStatus();
            bi.Write(0x3EC80, 0x1234);
            Assert(bi.IsUnibusNxm(), "unrecognized write sets unibus nxm");

            Console.WriteLine("  Default-case test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Default-case test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
