// MachineControlTests.cs - Tests for MachineControl's UCode/MainMemory
// wiring and Halted-driven run loop (Phase 8 of the microcode engine port).

using System;

namespace Usim;

public static class MachineControlTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== MachineControl Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestUCodeAndMemorySharedInstance()) passed++; else failed++;
        if (TestRunLoopStopsOnHalted()) passed++; else failed++;
        if (TestSingleStepTransitionsToHaltedState()) passed++; else failed++;
        if (TestPowerOnCallsBusReset()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestUCodeAndMemorySharedInstance()
    {
        Console.WriteLine("Test: MachineControl's UCode and Memory share the SAME MainMemory instance");
        try
        {
            var mc = new MachineControl();

            // Write directly to mc.Memory (physical), then read it back
            // through mc.UCode.Vm() (via CallVm(), Phase 5's test-access
            // wrapper) -- this only succeeds if UCode's own MainMemory
            // reference is the SAME object as mc.Memory, not a separately
            // constructed one (the pre-existing bug this task fixes).
            // Mirrors Phase 5's UCodeVirtualMemoryTests.
            // TestUCodeExplicitConstructorSharesMainMemory pattern exactly.
            mc.Memory.WritePhysical(42, 0xABCDEF01);

            uint vaddr = 42;
            uint md = vaddr; // md=vaddr keeps L1 and L2 indices consistent with Vtop(vaddr) inside Vm()
            uint l2Data = (1u << 23) | 0u; // access permission, pn=0
            uint vma = (1u << 26) | (1u << 25) | (0x00u << 27) | l2Data;
            mc.UCode.Uvmem.WriteMap(vma, md);

            uint readVal = 0;
            mc.UCode.CallVm(false, vaddr, ref readVal);
            Assert(readVal == 0xABCDEF01, $"MachineControl's UCode reads through the SAME mc.Memory instance, got 0x{readVal:X}");

            Console.WriteLine("  UCode/Memory shared-instance test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  UCode/Memory shared-instance test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestRunLoopStopsOnHalted()
    {
        Console.WriteLine("Test: MachineControl.Run()'s headless loop stops and fires Halted when UCode.Halted becomes true");
        try
        {
            var mc = new MachineControl();
            mc.PowerOn(BootMode.Cold);

            bool haltedEventFired = false;
            mc.Halted += () => haltedEventFired = true;

            // Force an immediate halt without needing real microcode to
            // reach an ILLOP: UCode.Halted is a plain settable property.
            mc.UCode.Halted = true;

            mc.Run(); // headless branch (no DisplayBackend) -- must return promptly

            Assert(haltedEventFired, "MachineControl.Halted event fired when UCode.Halted was true");
            Assert(mc.State == PowerState.Halted, $"MachineControl.State transitioned to Halted (got {mc.State})");
            Assert(mc.StopTime > DateTime.MinValue, "Halt() set StopTime (proves routing through Halt(), not a hand-duplicated inline transition)");

            Console.WriteLine("  Run-loop-stops-on-Halted test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Run-loop-stops-on-Halted test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestSingleStepTransitionsToHaltedState()
    {
        Console.WriteLine("Test: MachineControl.Step() transitions State to Halted when UCode.Step() leaves UCode.Halted true");
        try
        {
            var mc = new MachineControl();
            mc.PowerOn(BootMode.Cold);

            // Prom is all-zero by default (no file loaded in a headless
            // test run) -- an all-zero P0 decodes as Op=0/ALU, which never
            // sets Halted. So directly force Halted=true, then confirm the
            // NEXT Step() call (which still executes UCode.Step() once)
            // observes it afterward and transitions State.
            mc.UCode.Halted = true;
            mc.Step();

            Assert(mc.State == PowerState.Halted, $"MachineControl.State transitioned to Halted after Step() (got {mc.State})");
            Assert(mc.StopTime > DateTime.MinValue, "Halt() set StopTime (proves routing through Halt(), not a hand-duplicated inline transition)");

            Console.WriteLine("  Single-step Halted-state-transition test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Single-step Halted-state-transition test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestPowerOnCallsBusReset()
    {
        Console.WriteLine("Test: PowerOn() resets the bus interface's error status");
        try
        {
            var mc = new MachineControl();
            mc.UCode.BusInterface.SetXbusNxm(); // dirty the state before PowerOn

            mc.PowerOn(BootMode.Cold);

            Assert(mc.UCode.BusInterface.GetBusErrorStatus() == 0,
                "bus_error_status is 0 after PowerOn (BusReset was called)");

            Console.WriteLine("  PowerOn-calls-BusReset test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  PowerOn-calls-BusReset test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
