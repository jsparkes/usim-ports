// UnibusMappingTests.cs - Tests for the faithful Unibus-mapping register port
// (usim/unibus-mapping.c).

using System;

namespace Usim;

public static class UnibusMappingTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== UnibusMapping Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestRegisterReadWriteRoundTrip()) passed++; else failed++;
        if (TestAllSixteenRegistersIndependent()) passed++; else failed++;
        if (TestBufferAccessors()) passed++; else failed++;
        if (TestInvalidUaddrSetsNxm()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestRegisterReadWriteRoundTrip()
    {
        Console.WriteLine("Test: register read/write round-trip at 0x3EC60 (register 0)");
        try
        {
            var busInterface = new BusInterface(new UCode(new MainMemory()));
            var mapping = new UnibusMapping(busInterface);

            mapping.Write(0x3EC60, 0xABCD);
            Assert(mapping.Read(0x3EC60) == 0xABCD, $"register 0 round-trips, got 0x{mapping.Read(0x3EC60):X}");
            Assert(mapping.GetRegister(0) == 0xABCD, $"GetRegister(0) matches, got 0x{mapping.GetRegister(0):X}");

            Console.WriteLine("  Register round-trip test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Register round-trip test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestAllSixteenRegistersIndependent()
    {
        Console.WriteLine("Test: all 16 registers (0x3EC60-0x3EC7E) are independently addressable");
        try
        {
            var busInterface = new BusInterface(new UCode(new MainMemory()));
            var mapping = new UnibusMapping(busInterface);

            for (uint i = 0; i < 16; i++)
            {
                uint uaddr = 0x3EC60 + (i * 2);
                mapping.Write(uaddr, 0x1000 + i);
            }
            for (uint i = 0; i < 16; i++)
            {
                uint uaddr = 0x3EC60 + (i * 2);
                Assert(mapping.Read(uaddr) == 0x1000 + i, $"register {i} independent, got 0x{mapping.Read(uaddr):X}");
                Assert(mapping.GetRegister(i) == 0x1000 + i, $"GetRegister({i}) matches");
            }

            Console.WriteLine("  All-16-registers test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  All-16-registers test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBufferAccessors()
    {
        Console.WriteLine("Test: word-buffer accessors round-trip, independent of registers");
        try
        {
            var busInterface = new BusInterface(new UCode(new MainMemory()));
            var mapping = new UnibusMapping(busInterface);

            mapping.SetBuffer(3, 0x5678);
            Assert(mapping.GetBuffer(3) == 0x5678, $"buffer 3 round-trips, got 0x{mapping.GetBuffer(3):X}");
            Assert(mapping.GetBuffer(4) == 0, "buffer 4 is untouched (independent)");

            Console.WriteLine("  Buffer-accessors test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Buffer-accessors test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestInvalidUaddrSetsNxm()
    {
        Console.WriteLine("Test: an invalid uaddr (outside 0x3EC60-0x3EC7E, or odd) sets Unibus NXM, doesn't throw");
        try
        {
            var busInterface = new BusInterface(new UCode(new MainMemory()));
            var mapping = new UnibusMapping(busInterface);

            Assert(mapping.Read(0x3EC80) == 0, "out-of-range read returns 0");
            Assert(busInterface.IsUnibusNxm(), "out-of-range read sets Unibus NXM");

            busInterface.ResetBusErrorStatus();
            mapping.Write(0x3EC80, 0x1234);
            Assert(busInterface.IsUnibusNxm(), "out-of-range write sets Unibus NXM");

            busInterface.ResetBusErrorStatus();
            Assert(mapping.Read(0x3EC61) == 0, "odd uaddr within range returns 0"); // defensive, real-C-faithful case
            Assert(busInterface.IsUnibusNxm(), "odd uaddr within range sets Unibus NXM");

            Console.WriteLine("  Invalid-uaddr test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Invalid-uaddr test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
