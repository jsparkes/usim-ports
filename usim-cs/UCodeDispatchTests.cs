// UCodeDispatchTests.cs - Tests for UCode's Dispatch instruction class
// (Phase 6 of the microcode engine port). Covers Dsp()'s DMEM-write special
// case, the mask/rotate dispatch-address construction, the L2-map-bit
// tweak, byte-mode pos override, n_plus1/enable_ish/inhibit, and the
// push/pop machinery it shares with Jmp().

using System;

namespace Usim;

public static class UCodeDispatchTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== UCode Dispatch Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestDmemWritePath()) passed++; else failed++;
        if (TestBasicDispatchNoMap()) passed++; else failed++;
        if (TestMapBits()) passed++; else failed++;
        if (TestByteModePosOverride()) passed++; else failed++;
        if (TestNPlus1EnableIshInhibitEarlyReturn()) passed++; else failed++;
        if (TestPushOnly()) passed++; else failed++;
        if (TestPopOnly()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestDmemWritePath()
    {
        Console.WriteLine("Test: Dsp() Ir(10,2)==2 writes DMem[dispAddr]=AData and returns immediately");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // dispAddr = Ir(12,11) = 0x123; Ir(10,2) = 2 (bits 10-11 = 10 binary = 2).
            ucode.P0 = ((ulong)0x123 << 12) | (2UL << 10);
            ucode.AData = unchecked((int)0xCAFEBABE);
            ucode.Npc = 0x0042; // must be untouched -- this path returns before touching Npc
            ucode.CallDsp();

            Assert(ucode.DMem[0x123] == 0xCAFEBABEu, $"DMem[dispAddr] = AData, got 0x{ucode.DMem[0x123]:X}");
            Assert(ucode.Npc == 0x0042, "DMEM-write path returns before touching Npc");
            Assert(ucode.Inhibit == false, "DMEM-write path returns before touching Inhibit");

            Console.WriteLine("  Dsp DMEM-write-path tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Dsp DMEM-write-path tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBasicDispatchNoMap()
    {
        Console.WriteLine("Test: Dsp() basic dispatch, map=0, len=0 (no MData contribution), n=p=r=0 fallthrough");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // dispAddr(raw) = Ir(12,11) = 5; len = Ir(5,3) = 0 (mask=0); map = Ir(8,2) = 0;
            // Ir(10,2) = 0 (not the DMEM-write or byte-mode case); dispConst = Ir(32,10) = 0x2AA.
            ucode.P0 = ((ulong)5 << 12) | ((ulong)0x2AA << 32);
            ucode.DMem[5] = 0x1234; // n=0,p=0,r=0 (bits 14-16 clear), target=0x1234
            ucode.Npc = 0x0099; // must be overwritten by the fallthrough
            ucode.CallDsp();

            Assert(ucode.Npc == 0x1234, $"fallthrough: Npc = target, got 0x{ucode.Npc:X}");
            Assert(ucode.Popj == false, "fallthrough sets Popj = false");
            Assert(ucode.Inhibit == false, "n=0 -> Inhibit untouched");
            Assert(ucode.DispatchConstant == 0x2AA, $"DispatchConstant = Ir(32,10), got 0x{ucode.DispatchConstant:X}");

            Console.WriteLine("  Dsp basic-dispatch tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Dsp basic-dispatch tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMapBits()
    {
        Console.WriteLine("Test: Dsp() map=1/2/3 tweak dispAddr with Uvmem's L2 bit18/bit19");
        try
        {
            // map=1 selects bit18. l2MapBits has ONLY bit18 set (bit19 clear) -- if the
            // implementation read bit19 instead by mistake, dispAddr would resolve to 0
            // (DMem[0]'s default, untouched) instead of 1, giving a different Npc.
            {
                var ucode = new UCode();
                ucode.Init();
                ucode.MdReg = 0x1000;
                ucode.Uvmem.WriteMap((1u << 25) | (1u << 18), 0x1000); // L2-only write, l1Data stays 0

                ucode.P0 = (1UL << 8); // map = Ir(8,2) = 1; dispAddr(raw)=0, len=0
                ucode.DMem[1] = 0x0055; // n=p=r=0
                ucode.CallDsp();
                Assert(ucode.Npc == 0x0055, $"map=1 uses bit18 (set) -> dispAddr=1 -> DMem[1], got Npc=0x{ucode.Npc:X}");
            }

            // map=2 selects bit19. l2MapBits has ONLY bit19 set (bit18 clear) -- same
            // discrimination logic as above, mirrored.
            {
                var ucode = new UCode();
                ucode.Init();
                ucode.MdReg = 0x1000;
                ucode.Uvmem.WriteMap((1u << 25) | (1u << 19), 0x1000);

                ucode.P0 = (2UL << 8); // map = 2
                ucode.DMem[1] = 0x0066;
                ucode.CallDsp();
                Assert(ucode.Npc == 0x0066, $"map=2 uses bit19 (set) -> dispAddr=1 -> DMem[1], got Npc=0x{ucode.Npc:X}");
            }

            // map=3 ORs both bits together. Using bit19 set / bit18 CLEAR proves the OR
            // combines them (not just "happens to work when both are set").
            {
                var ucode = new UCode();
                ucode.Init();
                ucode.MdReg = 0x1000;
                ucode.Uvmem.WriteMap((1u << 25) | (1u << 19), 0x1000);

                ucode.P0 = (3UL << 8); // map = 3
                ucode.DMem[1] = 0x0077;
                ucode.CallDsp();
                Assert(ucode.Npc == 0x0077, $"map=3 ORs bit18|bit19 -> dispAddr=1 even with only bit19 set, got Npc=0x{ucode.Npc:X}");
            }

            Console.WriteLine("  Dsp map-bits tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Dsp map-bits tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestByteModePosOverride()
    {
        Console.WriteLine("Test: Dsp() Ir(10,2)==3 uses LcByteMode()'s pos, not the raw Ir(0,5) bits");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // P0 bits 0-3 = 3, bit4 = 0 (both the raw Ir(0,5) value AND LcByteMode's
            // low-nibble input); Ir(10,2) = 3 (bits 10-11 = 11 binary = 3); raw
            // dispAddr(bits 12-22) = 7; len(bits 5-7) = 0 (mask=0, so MData's rotated
            // value never affects dispAddr -- isolates the pos-selection check to MData
            // alone). InterruptControl=0 (Init() default, not byte mode) and Lc=0
            // (Init() default) -> LcByteMode()'s "not byte mode" branch: ir4=(P0>>4)&1=0,
            // lc1=(Lc>>1)&1=0, pos=(P0&0xF)|(((0^0)==0?1:0)<<4)=3|16=19. Raw Ir(0,5)
            // would have been P0&0x1F=3 -- 19 != 3, so this genuinely discriminates
            // which pos value was actually used for the rotation.
            ucode.P0 = ((ulong)7 << 12) | (3UL << 10) | 3UL;
            ucode.MData = 1;
            ucode.DMem[7] = 0x0099; // n=p=r=0
            ucode.CallDsp();

            Assert(ucode.MData == (1 << 19), $"MData rotated by LcByteMode()'s pos=19 (not raw pos=3), got 0x{ucode.MData:X}");
            Assert(ucode.Npc == 0x0099, $"dispAddr unaffected (len=0) -> DMem[7], got Npc=0x{ucode.Npc:X}");

            Console.WriteLine("  Dsp byte-mode-pos-override tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Dsp byte-mode-pos-override tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestNPlus1EnableIshInhibitEarlyReturn()
    {
        Console.WriteLine("Test: Dsp() n_plus1 (Npc--), enable_ish (AdvanceLc), Inhibit, and p&&r's early return");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // n_plus1 = Ir(25,1) = 1; enable_ish = Ir(24,1) = 1; raw dispAddr = 9;
            // len=0, map=0. DMem[9] encodes n=p=r=1 (bits 14,15,16 all set) --
            // p&&r triggers the early return, which is REQUIRED to observe Npc--'s
            // effect (see this plan's Global Constraints note: the fallthrough's
            // unconditional "Npc = target" would otherwise silently overwrite it).
            ucode.P0 = ((ulong)9 << 12) | (1UL << 25) | (1UL << 24);
            ucode.DMem[9] = (1u << 16) | (1u << 15) | (1u << 14); // r|p|n, target=0 (unused, early return)
            ucode.Npc = 5;
            ucode.Popj = true; // must survive unchanged -- the early return skips "Popj = false"
            ucode.CallDsp();

            Assert(ucode.Npc == 4, $"n_plus1 && n -> Npc--, and the value SURVIVES because p&&r returns before the fallthrough overwrite, got {ucode.Npc}");
            Assert(ucode.Inhibit == true, "n=1 -> Inhibit = true (set before the early return)");
            Assert(ucode.Lc == 2, $"enable_ish -> AdvanceLc(0) called (Lc 0->2, not-byte-mode +2 path, no NEED-FETCH), got 0x{ucode.Lc:X}");
            Assert(ucode.Popj == true, "p&&r early return skips 'Popj = false' -- Popj stays whatever it was before the call");

            Console.WriteLine("  Dsp n_plus1/enable_ish/inhibit/early-return tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Dsp n_plus1/enable_ish/inhibit/early-return tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestPushOnly()
    {
        Console.WriteLine("Test: Dsp() p=1,r=0,n=0 -> PushSpc(Npc), then falls through to Npc=target");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            ucode.P0 = (ulong)3 << 12; // raw dispAddr=3, len=0, map=0
            ucode.DMem[3] = (1u << 15) | 0x0033; // p only (bit15), target=0x0033
            ucode.Npc = 10;
            ucode.CallDsp();

            Assert(ucode.SpcPtr == 1, $"PushSpc advanced SpcPtr from 0, got {ucode.SpcPtr}");
            Assert(ucode.Spc[1] == 10, $"PushSpc(Npc) pushed the PRE-call Npc (10), got 0x{ucode.Spc[1]:X}");
            Assert(ucode.Npc == 0x0033, $"fallthrough still sets Npc=target (p alone, no r, doesn't return early), got 0x{ucode.Npc:X}");

            Console.WriteLine("  Dsp push-only tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Dsp push-only tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestPopOnly()
    {
        Console.WriteLine("Test: Dsp() r=1,p=0,n=0 -> PopSpc(), with and without the bit-14 AdvanceLc trigger");
        try
        {
            // No bit 14 in the popped value -> no AdvanceLc call, target used as-is.
            {
                var ucode = new UCode();
                ucode.Init();
                ucode.SpcPtr = 0;
                ucode.Spc[0] = 0x0044;

                ucode.P0 = (ulong)2 << 12; // raw dispAddr=2, len=0, map=0
                ucode.DMem[2] = 1u << 16; // r only (bit16); target field unused, will be overwritten by the pop
                ucode.CallDsp();

                Assert(ucode.Npc == 0x0044, $"PopSpc() without bit14 uses the popped value directly, got 0x{ucode.Npc:X}");
                Assert(ucode.SpcPtr == 0x1F, $"PopSpc decrements SpcPtr, wrapping 0->0x1F, got 0x{ucode.SpcPtr:X}");
            }

            // Bit 14 set in the popped value -> AdvanceLc(poppedTarget) is called, and its
            // return value (not the raw popped value) becomes target. Hand-derived: with
            // Lc=0/InterruptControl=0 (Init() defaults), AdvanceLc(0x4055) computes
            // oldLc=0, Lc+=2 (Lc=2, not byte mode), bit31 of Lc clear -> ppc|=2 ->
            // returns 0x4055|2=0x4057; lastByteInWord check leaves Lc at 2 (not 0x80000002).
            // target = 0x4057 & 0x3FFF = 0x0057.
            {
                var ucode = new UCode();
                ucode.Init();
                ucode.SpcPtr = 0;
                ucode.Spc[0] = 0x4055; // bit14 set

                ucode.P0 = (ulong)2 << 12;
                ucode.DMem[2] = 1u << 16; // r only
                ucode.CallDsp();

                Assert(ucode.Npc == 0x0057, $"PopSpc() with bit14 routes through AdvanceLc, got 0x{ucode.Npc:X}");
                Assert(ucode.Lc == 2, $"AdvanceLc's own Lc update (0->2), got 0x{ucode.Lc:X}");
            }

            Console.WriteLine("  Dsp pop-only tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Dsp pop-only tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
