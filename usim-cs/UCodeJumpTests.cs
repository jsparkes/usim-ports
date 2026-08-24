// UCodeJumpTests.cs - Tests for UCode's Jump instruction class (Phase 3 of
// the microcode engine port). Covers CheckJumpCondition()'s bit-test and
// fixed-condition modes, and Jmp()'s field decode, SPC push/pop, ILLOP/
// MISC-3 handling, and the P&R micro-code-write special case.

using System;

namespace Usim;

public static class UCodeJumpTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== UCode Jump Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestCheckJumpConditionBitTest()) passed++; else failed++;
        if (TestCheckJumpConditionFixedCodes()) passed++; else failed++;
        if (TestJmpUnconditional()) passed++; else failed++;
        if (TestJmpInhibitNoPush()) passed++; else failed++;
        if (TestJmpInhibitWithPush()) passed++; else failed++;
        if (TestJmpConditionalNotTaken()) passed++; else failed++;
        if (TestJmpPushPop()) passed++; else failed++;
        if (TestJmpInvertSense()) passed++; else failed++;
        if (TestJmpMicrocodeWrite()) passed++; else failed++;
        if (TestJmpIllop()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestCheckJumpConditionBitTest()
    {
        Console.WriteLine("Test: CheckJumpCondition bit-test mode (Ir(5,1)==0)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // rot = Ir(0,5) = 1 (bits 0-4), Ir(5,1) = 0 (bit 5 clear -> bit-test mode).
            ucode.P0 = 1; // rot=1, bit5=0
            ucode.MData = 0x00000001; // rotate left by 1 -> 0x00000002, bit0 clear
            bool cond = ucode.CheckJumpCondition();
            Assert(ucode.MData == 0x00000002, $"MData mutated by Rol32 (side effect), got 0x{ucode.MData:X}");
            Assert(cond == false, $"bit0 of rotated MData is 0 -> false, got {cond}");

            // rot = 0 -> no rotation; bit0 already set -> true.
            ucode.P0 = 0; // rot=0, bit5=0
            ucode.MData = 0x00000001;
            cond = ucode.CheckJumpCondition();
            Assert(ucode.MData == 0x00000001, "rot=0 is a no-op rotation");
            Assert(cond == true, $"bit0 of MData(rot=0) is 1 -> true, got {cond}");

            Console.WriteLine("  CheckJumpCondition bit-test tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  CheckJumpCondition bit-test tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestCheckJumpConditionFixedCodes()
    {
        Console.WriteLine("Test: CheckJumpCondition fixed condition codes (Ir(5,1)!=0)");
        try
        {
            var ucode = new UCode();
            ucode.Init();
            ucode.P0 = 1UL << 5; // bit5 = 1 -> fixed-condition mode; Ir(0,4) set per case below

            // Code 1: MData < AData (signed).
            ucode.P0 = (1UL << 5) | 1;
            ucode.MData = -5; ucode.AData = 3;
            Assert(ucode.CheckJumpCondition() == true, "code1: -5 < 3");
            ucode.MData = 5; ucode.AData = 3;
            Assert(ucode.CheckJumpCondition() == false, "code1: 5 < 3 is false");

            // Code 2: MData <= AData.
            ucode.P0 = (1UL << 5) | 2;
            ucode.MData = 3; ucode.AData = 3;
            Assert(ucode.CheckJumpCondition() == true, "code2: 3 <= 3");

            // Code 3: MData == AData.
            ucode.P0 = (1UL << 5) | 3;
            ucode.MData = 7; ucode.AData = 7;
            Assert(ucode.CheckJumpCondition() == true, "code3: 7 == 7");
            ucode.MData = 7; ucode.AData = 8;
            Assert(ucode.CheckJumpCondition() == false, "code3: 7 == 8 is false");

            // Code 4: !VmaOk.
            ucode.P0 = (1UL << 5) | 4;
            ucode.VmaOk = true;
            Assert(ucode.CheckJumpCondition() == false, "code4: VmaOk=true -> false");
            ucode.VmaOk = false;
            Assert(ucode.CheckJumpCondition() == true, "code4: VmaOk=false -> true");
            ucode.VmaOk = true; // restore default for later tests

            // Code 5: !VmaOk || (bit27 set && InterruptPendingFlag).
            ucode.P0 = (1UL << 5) | 5;
            ucode.VmaOk = true; ucode.InterruptControl = 0; ucode.InterruptPendingFlag = true;
            Assert(ucode.CheckJumpCondition() == false, "code5: bit27 clear -> InterruptPendingFlag ignored");
            ucode.InterruptControl = 1u << 27; ucode.InterruptPendingFlag = true;
            Assert(ucode.CheckJumpCondition() == true, "code5: bit27 set & pending -> true");
            ucode.InterruptControl = 1u << 27; ucode.InterruptPendingFlag = false;
            Assert(ucode.CheckJumpCondition() == false, "code5: bit27 set & not pending -> false");
            ucode.InterruptControl = 0;

            // Code 6: !VmaOk || (bit27 set && InterruptPendingFlag) || (bit26 set).
            ucode.P0 = (1UL << 5) | 6;
            ucode.VmaOk = true; ucode.InterruptControl = 1u << 26; ucode.InterruptPendingFlag = false;
            Assert(ucode.CheckJumpCondition() == true, "code6: bit26 set alone -> true");
            ucode.InterruptControl = 0;
            Assert(ucode.CheckJumpCondition() == false, "code6: nothing set -> false");

            // Code 7: unconditional true.
            ucode.P0 = (1UL << 5) | 7;
            Assert(ucode.CheckJumpCondition() == true, "code7: always true");

            // Code 0: matches the C's fall-through-to-err() fatal path.
            ucode.P0 = (1UL << 5) | 0;
            bool threw = false;
            try { ucode.CheckJumpCondition(); }
            catch (InvalidOperationException) { threw = true; }
            Assert(threw, "code0 throws InvalidOperationException, matching C's err()");

            Console.WriteLine("  CheckJumpCondition fixed-code tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  CheckJumpCondition fixed-code tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestJmpUnconditional()
    {
        Console.WriteLine("Test: Jmp unconditional (code 7), no P/R/N");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // target = Ir(12,14) = 0x1234; r=p=n=invertSense=0; condition code 7 (always true).
            ucode.P0 = ((ulong)0x1234 << 12) | (1UL << 5) | 7;
            ucode.Npc = 0x0100;
            ucode.CallJmp();

            Assert(ucode.Npc == 0x1234, $"cond=true, no P/R -> Npc=target, got 0x{ucode.Npc:X}");
            Assert(ucode.Popj == false, "Popj forced false when cond is true");
            Assert(ucode.Inhibit == false, "n=0 -> Inhibit untouched");

            Console.WriteLine("  Jmp unconditional tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Jmp unconditional tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestJmpInhibitNoPush()
    {
        Console.WriteLine("Test: Jmp n=1, p=0 (inhibit next Step's dispatch, no SPC push)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // target = Ir(12,14) = 0x1500 (within the 14-bit field); n=1 (bit7), p=0, r=0,
            // invertSense=0, condition code 7 (always true). This is the dominant real-world
            // jump shape per the boot-microcode survey (n=1 without p): plain inhibit-next-
            // dispatch, target taken directly, no PushSpc involved.
            ucode.P0 = ((ulong)0x1500 << 12) | (1UL << 7) | (1UL << 5) | 7;
            ucode.Npc = 0x0200;
            ucode.CallJmp();

            Assert(ucode.Inhibit == true, $"n=1 -> Inhibit=true, got {ucode.Inhibit}");
            Assert(ucode.Npc == 0x1500, $"cond=true, p=0 -> Npc=target, got 0x{ucode.Npc:X}");
            Assert(ucode.Popj == false, "Popj forced false when cond is true");

            Console.WriteLine("  Jmp n=1,p=0 tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Jmp n=1,p=0 tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestJmpInhibitWithPush()
    {
        Console.WriteLine("Test: Jmp n=1, p=1 (PushSpc(Npc-1) variant, differs from n=0,p=1's PushSpc(Npc))");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // target = Ir(12,14) = 0x2600; p=1 (bit8), n=1 (bit7), r=0, invertSense=0,
            // condition code 7 (always true). Per Jmp()'s p&&cond block: since n is set,
            // it pushes Npc-1 (not Npc) onto the SPC stack. This n=1&&p=1 combination is
            // the single most common real jump shape in ucadr.mcr (per the boot-microcode
            // survey), so it needs its own coverage distinct from the n=0,p=1 push case.
            uint npcBefore = 0x0050;
            ucode.P0 = ((ulong)0x2600 << 12) | (1UL << 8) | (1UL << 7) | (1UL << 5) | 7;
            ucode.Npc = npcBefore;
            ucode.CallJmp();

            Assert(ucode.Spc[ucode.SpcPtr] == npcBefore - 1,
                $"n=1,p=1 -> PushSpc(Npc-1), got 0x{ucode.Spc[ucode.SpcPtr]:X}, expected 0x{npcBefore - 1:X}");
            Assert(ucode.Inhibit == true, $"n=1 -> Inhibit=true, got {ucode.Inhibit}");
            Assert(ucode.Npc == 0x2600, $"cond=true -> Npc=target, got 0x{ucode.Npc:X}");

            Console.WriteLine("  Jmp n=1,p=1 tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Jmp n=1,p=1 tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestJmpConditionalNotTaken()
    {
        Console.WriteLine("Test: Jmp condition false leaves Npc/Popj untouched");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // condition code 3 (MData==AData), made false; target must NOT be taken.
            ucode.P0 = ((ulong)0x2000 << 12) | (1UL << 5) | 3;
            ucode.MData = 1; ucode.AData = 2;
            ucode.Npc = 0x0055;
            ucode.Popj = true; // pre-set, must survive since cond is false
            ucode.CallJmp();

            Assert(ucode.Npc == 0x0055, $"cond=false -> Npc untouched, got 0x{ucode.Npc:X}");
            Assert(ucode.Popj == true, "cond=false -> Popj untouched");

            Console.WriteLine("  Jmp conditional-not-taken tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Jmp conditional-not-taken tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestJmpPushPop()
    {
        Console.WriteLine("Test: Jmp P (push SPC) and R (pop SPC) flags");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // P=1 (bit8), N=0 (bit7), cond true (code7) -> pushSpc(Npc).
            ucode.P0 = ((ulong)0x3000 << 12) | (1UL << 8) | (1UL << 5) | 7;
            ucode.Npc = 0x0042;
            ucode.CallJmp();
            Assert(ucode.Npc == 0x3000, "P alone still takes the jump target");

            // Now R=1 (bit9), P=0, cond true -> target = popSpc() (the 0x0042 just pushed),
            // masked with 037777 = 0x3FFF. Bit 14 of that popped value is 0, so no AdvanceLc.
            ucode = new UCode();
            ucode.Init();
            ucode.P0 = ((ulong)0x3000 << 12) | (1UL << 8) | (1UL << 5) | 7; // push 0x99 via P
            ucode.Npc = 0x0099;
            ucode.CallJmp();
            ucode.P0 = ((ulong)0x1111 << 12) | (1UL << 9) | (1UL << 5) | 7; // R=1, target ignored (popped instead)
            ucode.CallJmp();
            Assert(ucode.Npc == 0x0099, $"R=1 -> Npc = popped SPC value, got 0x{ucode.Npc:X}");

            Console.WriteLine("  Jmp push/pop tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Jmp push/pop tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestJmpInvertSense()
    {
        Console.WriteLine("Test: Jmp invertSense flips the condition");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // condition code 3 (MData==AData) is FALSE, but invertSense (bit6) flips it to true.
            // target must stay within Ir(12,14)'s 14-bit field (max 0x3FFF) or it gets masked
            // away by Ir() itself before Jmp() ever sees it.
            ucode.P0 = ((ulong)0x2400 << 12) | (1UL << 6) | (1UL << 5) | 3;
            ucode.MData = 1; ucode.AData = 2;
            ucode.Npc = 0x0000;
            ucode.CallJmp();

            Assert(ucode.Npc == 0x2400, $"invertSense flips false cond to true -> jump taken, got 0x{ucode.Npc:X}");

            Console.WriteLine("  Jmp invertSense tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Jmp invertSense tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestJmpMicrocodeWrite()
    {
        Console.WriteLine("Test: Jmp P&R micro-code-write special case (bypasses condition entirely)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // P=1 (bit8) & R=1 (bit9) -> IMem[target] = Iwr; return immediately (no condition
            // evaluated, so an otherwise-fatal code0 condition must NOT throw here).
            ucode.P0 = ((ulong)0x0777 << 12) | (1UL << 9) | (1UL << 8) | (1UL << 5) | 0;
            ucode.Iwr = 0xDEADBEEFCAFEUL;
            ucode.Npc = 0x1234; // must be untouched, since this path returns before touching Npc
            ucode.CallJmp();

            Assert(ucode.IMem[0x0777] == 0xDEADBEEFCAFEUL, $"P&R writes Iwr to IMem[target], got 0x{ucode.IMem[0x0777]:X}");
            Assert(ucode.Npc == 0x1234, "P&R path returns before touching Npc");

            Console.WriteLine("  Jmp micro-code-write tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Jmp micro-code-write tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestJmpIllop()
    {
        Console.WriteLine("Test: Jmp ILLOP sets Halted but continues executing (does not return early)");
        try
        {
            var ucode = new UCode();
            ucode.Init();
            Assert(ucode.Halted == false, "Halted starts false after Init()");

            // Ir(10,2) == 1 -> ILLOP. Also code 7 (always true) so the jump target is STILL
            // taken afterward, proving execution continued past the ILLOP check. target must
            // stay within Ir(12,14)'s 14-bit field (max 0x3FFF) or it gets masked away by
            // Ir() itself before Jmp() ever sees it.
            ucode.P0 = ((ulong)0x2500 << 12) | (1UL << 10) | (1UL << 5) | 7;
            ucode.Npc = 0x0000;
            ucode.CallJmp();

            Assert(ucode.Halted == true, "ILLOP sets Halted = true");
            Assert(ucode.Npc == 0x2500, "ILLOP does not return early -> jump condition still evaluated and taken");

            Console.WriteLine("  Jmp ILLOP tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Jmp ILLOP tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
