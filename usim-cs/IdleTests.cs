// IdleTests.cs - Tests for the idle port (usim/idle.c, SDL3 path).

using System;
using System.Collections.Generic;

namespace Usim;

public static class IdleTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== Idle Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestDefaultsMatchUcfgDefs()) passed++; else failed++;
        if (TestBusyUntilCyclesExceeded()) passed++; else failed++;
        if (TestSleepsOnlyOnQuantumBoundary()) passed++; else failed++;
        if (TestActivityResetsTheIdleTimer()) passed++; else failed++;
        if (TestDiskStartCountsAsActivity()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestDefaultsMatchUcfgDefs()
    {
        Console.WriteLine("Test: defaults are ucfg.defs' 655350/65535/1000, and idle is disabled (the real C never enables it)");
        try
        {
            var idle = new Idle(new UCode(new MainMemory()), _ => { });
            Assert(idle.Cycles == 655350, "cycles");
            Assert(idle.Quantum == 65535, "quantum");
            Assert(idle.TimeoutMicros == 1000, "timeout");
            Assert(!idle.Enabled, "disabled by default");
            Assert(!idle.IsIdle && idle.IdleCount == 0, "starts running with a zero count");

            Console.WriteLine("  Defaults test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Defaults test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBusyUntilCyclesExceeded()
    {
        Console.WriteLine("Test: the machine is idle only once more than Cycles cycles have passed since the last activity");
        try
        {
            var sleeps = new List<int>();
            var idle = new Idle(new UCode(new MainMemory()), sleeps.Add) { Cycles = 100, Quantum = 0xFFFF };

            idle.Check(100);
            Assert(!idle.IsIdle, "exactly Cycles cycles is not yet idle (strictly greater is required)");
            idle.Check(101);
            Assert(idle.IsIdle, "Cycles+1 cycles is idle");

            Console.WriteLine("  Cycles-threshold test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Cycles-threshold test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestSleepsOnlyOnQuantumBoundary()
    {
        Console.WriteLine("Test: while idle it sleeps TimeoutMicros (rounded up to ms) only when (cycles & Quantum) == 0, counting each sleep");
        try
        {
            var sleeps = new List<int>();
            var idle = new Idle(new UCode(new MainMemory()), sleeps.Add)
            {
                Cycles = 10, Quantum = 0xFF, TimeoutMicros = 1500
            };

            idle.Check(0x1FF);
            Assert(sleeps.Count == 0 && idle.IdleCount == 0, "0x1FF & 0xFF != 0: no sleep");
            idle.Check(0x200);
            Assert(sleeps.Count == 1 && sleeps[0] == 2 && idle.IdleCount == 1, "0x200 & 0xFF == 0: sleeps 1500us -> 2 ms");

            Console.WriteLine("  Quantum-boundary test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Quantum-boundary test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestActivityResetsTheIdleTimer()
    {
        Console.WriteLine("Test: Activity() restarts the idle window at the current machine cycle");
        try
        {
            var ucode = new UCode(new MainMemory());
            var idle = new Idle(ucode, _ => { }) { Cycles = 100, Quantum = 0xFFFF };

            idle.Check(500);
            Assert(idle.IsIdle, "idle at cycle 500 with no activity");

            ucode.MachineCycles = 500;
            idle.Activity();
            idle.Check(550);
            Assert(!idle.IsIdle, "activity at cycle 500 makes cycle 550 busy again");
            idle.Check(601);
            Assert(idle.IsIdle, "and idle again 101 cycles after it");

            idle.Init();
            Assert(!idle.IsIdle && idle.IdleCount == 0, "Init resets state");

            Console.WriteLine("  Activity test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Activity test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDiskStartCountsAsActivity()
    {
        Console.WriteLine("Test: starting a disk command records activity (idle_disk_activity in the C start())");
        try
        {
            var ucode = new UCode(new MainMemory());
            var dc = new DiskController(new MainMemory(), ucode);
            var idle = new Idle(ucode, _ => { }) { Cycles = 100, Quantum = 0xFFFF };
            dc.WireIdle(idle);

            ucode.MachineCycles = 1000;
            dc.Write(3, 0); // register 3 = start (unit not online, but the activity hook comes first)

            idle.Check(1050);
            Assert(!idle.IsIdle, "disk start at cycle 1000 means 1050 is not idle");

            Console.WriteLine("  Disk-activity test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Disk-activity test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
