// DiagnosticInterfaceTests.cs - Tests for the diagnostic ("spy") interface port
// (usim/diagnostic-interface.c, release build).

using System;

namespace Usim;

public static class DiagnosticInterfaceTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== Diagnostic Interface Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestIrAndPcRegistersReflectMachineState()) passed++; else failed++;
        if (TestOutMAndARegistersAreSplitIn16BitHalves()) passed++; else failed++;
        if (TestFlagRegisters()) passed++; else failed++;
        if (TestModeRegisterSetsPromDisabled()) passed++; else failed++;
        if (TestDebugIrWritesAssembleThe48BitValue()) passed++; else failed++;
        if (TestClockControlAcceptsOnlyRun()) passed++; else failed++;
        if (TestOpcControlAndStatisticsCounterAreFatal()) passed++; else failed++;
        if (TestUnusedAndInvalidAddresses()) passed++; else failed++;
        if (TestBusAdaptorRoutesTheDiagnosticRange()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static (DiagnosticInterface di, UCode ucode) Make()
    {
        var ucode = new UCode(new MainMemory());
        return (new DiagnosticInterface(ucode, ucode.BusInterface), ucode);
    }

    // Octal -> hex (Python): 0766000=0x3EC00 0766002=0x3EC02 0766004=0x3EC04
    // 0766006=0x3EC06 0766010=0x3EC08 0766012=0x3EC0A 0766014=0x3EC0C
    // 0766016=0x3EC0E 0766020=0x3EC10 0766022=0x3EC12 0766024=0x3EC14
    // 0766026=0x3EC16 0766030=0x3EC18 0766032=0x3EC1A 0766034=0x3EC1C 0766036=0x3EC1E.

    private static bool TestIrAndPcRegistersReflectMachineState()
    {
        Console.WriteLine("Test: IR<15-0>/<31-16>/<47-32>, OPC and PC reads reflect P0, Opc and Npc");
        try
        {
            var (di, ucode) = Make();
            ucode.P0 = 0x0000_1234_5678_9ABCUL;
            ucode.Opc = 0x1234;
            ucode.Npc = 0x2345;

            Assert(di.Read(0x3EC00) == 0x9ABC, "IR<15-0>");
            Assert(di.Read(0x3EC02) == 0x5678, "IR<31-16>");
            Assert(di.Read(0x3EC04) == 0x1234, "IR<47-32>");
            Assert(di.Read(0x3EC08) == 0x1234, "OPC");
            Assert(di.Read(0x3EC0A) == 0x2345, "PC reads Npc (the next PC)");

            Console.WriteLine("  IR/OPC/PC test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  IR/OPC/PC test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestOutMAndARegistersAreSplitIn16BitHalves()
    {
        Console.WriteLine("Test: OB, M and A registers are read as low/high 16-bit halves");
        try
        {
            var (di, ucode) = Make();
            ucode.Out = 0xAABBCCDD;
            ucode.MData = unchecked((int)0x11223344);
            ucode.AData = unchecked((int)0x99887766);

            Assert(di.Read(0x3EC0C) == 0xCCDD && di.Read(0x3EC0E) == 0xAABB, "OB");
            Assert(di.Read(0x3EC14) == 0x3344 && di.Read(0x3EC16) == 0x1122, "M");
            Assert(di.Read(0x3EC18) == 0x7766 && di.Read(0x3EC1A) == 0x9988, "A");

            Console.WriteLine("  OB/M/A test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  OB/M/A test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestFlagRegisters()
    {
        Console.WriteLine("Test: flag register 1 is the constant 0xF8FF; flag register 2 is 8 when VMA is ok, else 0");
        try
        {
            var (di, ucode) = Make();

            // Sum of the always-true bits: 0x8000+0x4000+0x2000+0x1000+0x0800 (+ !stathalt) + 0x00FF (the eight low active-low bits).
            Assert(di.Read(0x3EC10) == 0xF8FF, $"flag register 1, got 0x{di.Read(0x3EC10):X}");

            ucode.VmaOk = true;
            Assert(di.Read(0x3EC12) == 0x0008, "flag register 2 with vmaok");
            ucode.VmaOk = false;
            Assert(di.Read(0x3EC12) == 0x0000, "flag register 2 without vmaok");

            Console.WriteLine("  Flag registers test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Flag registers test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestModeRegisterSetsPromDisabled()
    {
        Console.WriteLine("Test: writing the mode register (0766012) sets PromDisabled from bit 5");
        try
        {
            var (di, _) = Make();
            bool promDisabled = false;

            di.Write(0x3EC0A, 1 << 5, ref promDisabled);
            Assert(promDisabled, "bit 5 set -> prom disabled");
            di.Write(0x3EC0A, 0, ref promDisabled);
            Assert(!promDisabled, "bit 5 clear -> prom enabled");
            di.Write(0x3EC0A, 0xFFDF, ref promDisabled);
            Assert(!promDisabled, "every bit but 5 set -> still enabled");

            Console.WriteLine("  Mode-register test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Mode-register test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDebugIrWritesAssembleThe48BitValue()
    {
        Console.WriteLine("Test: DEBUG-IR writes at 0766000/2/4 assemble a 48-bit value without disturbing other fields");
        try
        {
            var (di, _) = Make();
            bool p = false;

            di.Write(0x3EC04, 0x1234, ref p);
            di.Write(0x3EC02, 0x5678, ref p);
            di.Write(0x3EC00, 0x9ABC, ref p);
            Assert(di.DebugIr == 0x0000_1234_5678_9ABCUL, $"got 0x{di.DebugIr:X}");

            di.Write(0x3EC02, 0xFFFF, ref p);
            Assert(di.DebugIr == 0x0000_1234_FFFF_9ABCUL, "rewriting the middle word only changes bits 31-16");

            Console.WriteLine("  DEBUG-IR test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  DEBUG-IR test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestClockControlAcceptsOnlyRun()
    {
        Console.WriteLine("Test: clock control (0766006) accepts 1 (RUN) and throws otherwise, like the real C's errx");
        try
        {
            var (di, _) = Make();
            bool p = false;

            di.Write(0x3EC06, 1, ref p);

            bool threw = false;
            try { di.Write(0x3EC06, 0x99, ref p); } catch (InvalidOperationException) { threw = true; }
            Assert(threw, "clock control != 1 throws");

            Console.WriteLine("  Clock-control test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Clock-control test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestOpcControlAndStatisticsCounterAreFatal()
    {
        Console.WriteLine("Test: OPC control write and statistics-counter reads throw (the real C errx()s)");
        try
        {
            var (di, _) = Make();
            bool p = false;

            Assert(Throws(() => di.Write(0x3EC08, 0, ref p)), "OPC control write throws");
            Assert(Throws(() => di.Read(0x3EC1C)), "statistics counter low read throws");
            Assert(Throws(() => di.Read(0x3EC1E)), "statistics counter high read throws");

            Console.WriteLine("  Fatal-register test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Fatal-register test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUnusedAndInvalidAddresses()
    {
        Console.WriteLine("Test: unused registers are silent; a non-register address sets Unibus NXM (release build has no 0766020-0766026 writes)");
        try
        {
            var (di, ucode) = Make();
            bool p = false;

            di.Write(0x3EC0C, 0x1234, ref p);   // 0766014 not used
            di.Write(0x3EC0E, 0x1234, ref p);   // 0766016 not used
            Assert(di.Read(0x3EC06) == 0, "clock control reads 0 (not used)");
            Assert(!ucode.BusInterface.IsUnibusNxm(), "no NXM so far");

            di.Write(0x3EC10, 1, ref p);        // 0766020 debug command: compiled out in release
            Assert(ucode.BusInterface.IsUnibusNxm(), "write to 0766020 sets Unibus NXM");

            ucode.BusInterface.ResetBusErrorStatus();
            Assert(di.Read(0x3EC01) == 0 && ucode.BusInterface.IsUnibusNxm(), "an odd address read sets NXM and reads 0");

            Console.WriteLine("  Unused/invalid-address test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Unused/invalid-address test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBusAdaptorRoutesTheDiagnosticRange()
    {
        Console.WriteLine("Test: reads/writes through BusAdaptor reach the diagnostic interface (PC read, mode-register write)");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = new BusAdaptor(ucode.BusInterface, mainMemory, ucode);
            ucode.Npc = 0x0123;
            bool promDisabled = false;

            Assert(busAdaptor.Read(UaddrToPaddr(0x3EC0A)) == 0x0123, "PC read through the adaptor");
            busAdaptor.Write(UaddrToPaddr(0x3EC0A), 1 << 5, ref promDisabled);
            Assert(promDisabled, "mode-register write through the adaptor");

            Console.WriteLine("  BusAdaptor-routing test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  BusAdaptor-routing test failed: {ex.Message}\n");
            return false;
        }
    }

    private static uint UaddrToPaddr(uint uaddr)
    {
        uint halfWordIndex = uaddr >> 1;
        uint dispatchPn = (halfWordIndex >> 8) + 0x3E00;
        return (dispatchPn << 8) | (halfWordIndex & 0xFF);
    }

    private static bool Throws(Action a)
    {
        try { a(); return false; }
        catch (InvalidOperationException) { return true; }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
