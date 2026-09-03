// UCodeInterruptTests.cs - Tests for UCode's interrupt-status-register
// producers (Phase 8 of the microcode engine port).

using System;

namespace Usim;

public static class UCodeInterruptTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== UCode Interrupt Register Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestSetInterruptStatusRegPendingMask()) passed++; else failed++;
        if (TestAssertUnibusInterruptGatedByEnableBit()) passed++; else failed++;
        if (TestDeassertUnibusInterruptGatedByAcceptedBit()) passed++; else failed++;
        if (TestAssertXbusInterruptUngated()) passed++; else failed++;
        if (TestDeassertXbusInterruptGatedByBit()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestSetInterruptStatusRegPendingMask()
    {
        Console.WriteLine("Test: SetInterruptStatusReg() sets InterruptPendingFlag from mask 0x400 (Unibus-accepted) | 0x4000 (Xbus), not from any nonzero bit");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Real C: interrupt_pending_flag = (interrupt_status_reg & 0140000) ? 1 : 0;
            // 0140000 octal = 0xC000 = bit15(0x8000, Unibus-accepted)|bit14(0x4000, Xbus).
            ucode.SetInterruptStatusReg(0x400); // enable bit only, NOT in the 0xC000 mask
            Assert(ucode.InterruptPendingFlag == false, "0x400 (enable bit) alone does not set pending");

            ucode.SetInterruptStatusReg(0x8000); // Unibus-accepted bit
            Assert(ucode.InterruptPendingFlag == true, "0x8000 (Unibus-accepted) sets pending");

            ucode.SetInterruptStatusReg(0x4000); // Xbus bit
            Assert(ucode.InterruptPendingFlag == true, "0x4000 (Xbus) sets pending");

            ucode.SetInterruptStatusReg(0);
            Assert(ucode.InterruptPendingFlag == false, "0 clears pending");
            Assert(ucode.InterruptStatusReg == 0, "InterruptStatusReg reflects the raw value set");

            Console.WriteLine("  SetInterruptStatusReg pending-mask test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  SetInterruptStatusReg pending-mask test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestAssertUnibusInterruptGatedByEnableBit()
    {
        Console.WriteLine("Test: AssertUnibusInterrupt() is a no-op unless the 0x400 enable bit is already set, and masks the vector to 0x3FC when it fires");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Disabled case: enable bit (0x400) clear -> no-op. Pre-set
            // InterruptPendingFlag=true via a prior call so we can prove
            // the no-op path leaves it UNCHANGED (not reset to false).
            ucode.SetInterruptStatusReg(0x4000); // Xbus bit -> pending=true, status=0x4000
            ucode.AssertUnibusInterrupt(0x7FF);
            Assert(ucode.InterruptStatusReg == 0x4000, "disabled: AssertUnibusInterrupt left InterruptStatusReg untouched");
            Assert(ucode.InterruptPendingFlag == true, "disabled: AssertUnibusInterrupt left InterruptPendingFlag untouched (still true from before)");

            // Enabled case: enable bit (0x400) set, vector=0x7FF (has bits
            // both inside and outside the 0x3FC vector-mask).
            // Real C: set_interrupt_status_reg((status & ~01774) | 0100000 | (vector & 01774))
            // 01774 octal = 0x3FC, 0100000 octal = 0x8000.
            // status=0x400 (enable only) -> (0x400 & ~0x3FC)=0x400 (no overlap)
            // | 0x8000 | (0x7FF & 0x3FC = 0x3FC) = 0x87FC.
            var ucode2 = new UCode();
            ucode2.Init();
            ucode2.SetInterruptStatusReg(0x400);
            ucode2.AssertUnibusInterrupt(0x7FF);
            Assert(ucode2.InterruptStatusReg == 0x87FC, $"enabled: status became 0x87FC (was 0x{ucode2.InterruptStatusReg:X})");
            Assert(ucode2.InterruptPendingFlag == true, "enabled: 0x87FC has bit 0x8000 set, so pending is true");

            Console.WriteLine("  AssertUnibusInterrupt gating test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  AssertUnibusInterrupt gating test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDeassertUnibusInterruptGatedByAcceptedBit()
    {
        Console.WriteLine("Test: DeassertUnibusInterrupt() is a no-op unless the 0x8000 accepted bit is set, and clears only the vector+accepted bits when it fires");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // No-op case: accepted bit (0x8000) clear. Use 0x4400 (enable
            // bit 0x400 | Xbus bit 0x4000) rather than bare 0x400 so
            // InterruptPendingFlag is pre-set true (0x4000 is in the
            // 0xC000 pending mask) while the accepted bit (0x8000) stays
            // clear -- proving the no-op path leaves InterruptPendingFlag
            // UNCHANGED, not reset to false, matching the pattern already
            // used in TestAssertUnibusInterruptGatedByEnableBit's no-op case.
            ucode.SetInterruptStatusReg(0x4400);
            ucode.DeassertUnibusInterrupt();
            Assert(ucode.InterruptStatusReg == 0x4400, "no-op: status untouched when 0x8000 clear");
            Assert(ucode.InterruptPendingFlag == true, "no-op: InterruptPendingFlag untouched (still true from before)");

            // Fires case: status has accepted(0x8000) + vector(0x3FC) + enable(0x400).
            // Real C: set_interrupt_status_reg(status & ~(01774 | 0100000))
            // 0x8000|0x3FC|0x400 with (0x3FC|0x8000) cleared -> 0x400 remains.
            var ucode2 = new UCode();
            ucode2.Init();
            ucode2.SetInterruptStatusReg(0x8000 | 0x3FC | 0x400);
            ucode2.DeassertUnibusInterrupt();
            Assert(ucode2.InterruptStatusReg == 0x400, $"fires: vector+accepted cleared, enable bit survives (got 0x{ucode2.InterruptStatusReg:X})");
            Assert(ucode2.InterruptPendingFlag == false, "fires: 0x400 alone is not in the 0xC000 pending mask");

            Console.WriteLine("  DeassertUnibusInterrupt gating test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  DeassertUnibusInterrupt gating test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestAssertXbusInterruptUngated()
    {
        Console.WriteLine("Test: AssertXbusInterrupt() unconditionally ORs in bit 0x4000, no gating check (unlike the Unibus variants)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            ucode.SetInterruptStatusReg(0x400); // arbitrary base state, no enable check applies here
            ucode.AssertXbusInterrupt();
            Assert(ucode.InterruptStatusReg == 0x4400, $"0x400 | 0x4000 = 0x4400 (got 0x{ucode.InterruptStatusReg:X})");
            Assert(ucode.InterruptPendingFlag == true, "0x4400 has bit 0x4000 set, so pending is true");

            Console.WriteLine("  AssertXbusInterrupt ungated test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  AssertXbusInterrupt ungated test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDeassertXbusInterruptGatedByBit()
    {
        Console.WriteLine("Test: DeassertXbusInterrupt() is a no-op unless bit 0x4000 is set, and clears only that bit when it fires");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // No-op case: bit 0x4000 clear. Use 0x8400 (enable bit 0x400 |
            // Unibus-accepted bit 0x8000) rather than bare 0x400 so
            // InterruptPendingFlag is pre-set true (0x8000 is in the
            // 0xC000 pending mask) while bit 0x4000 stays clear -- proving
            // the no-op path leaves InterruptPendingFlag UNCHANGED, not
            // reset to false, matching the pattern already used in
            // TestAssertUnibusInterruptGatedByEnableBit's no-op case.
            ucode.SetInterruptStatusReg(0x8400);
            ucode.DeassertXbusInterrupt();
            Assert(ucode.InterruptStatusReg == 0x8400, "no-op: status untouched when 0x4000 clear");
            Assert(ucode.InterruptPendingFlag == true, "no-op: InterruptPendingFlag untouched (still true from before)");

            // Fires case: bit 0x4000 set alongside 0x400.
            var ucode2 = new UCode();
            ucode2.Init();
            ucode2.SetInterruptStatusReg(0x4000 | 0x400);
            ucode2.DeassertXbusInterrupt();
            Assert(ucode2.InterruptStatusReg == 0x400, $"fires: only 0x4000 cleared, 0x400 survives (got 0x{ucode2.InterruptStatusReg:X})");
            Assert(ucode2.InterruptPendingFlag == false, "fires: 0x400 alone is not in the 0xC000 pending mask");

            Console.WriteLine("  DeassertXbusInterrupt gating test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  DeassertXbusInterrupt gating test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
