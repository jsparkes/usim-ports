// UCodeByteTests.cs - Tests for UCode's Byt() byte-instruction handler
// (Phase 7 of the microcode engine port). Phase 9 folds this into the
// full microcode test suite.

using System;

namespace Usim;

public static class UCodeByteTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== UCode Byte Instruction Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestMrSrBitsZero()) passed++; else failed++;
        if (TestLdbMaskAtPositionZero()) passed++; else failed++;
        if (TestSelDepMaskAtPosNoRotation()) passed++; else failed++;
        if (TestDpbMaskAtPosWithRotation()) passed++; else failed++;
        if (TestByteModePosOverride()) passed++; else failed++;
        if (TestByteModeWithLdbRotation()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestMrSrBitsZero()
    {
        Console.WriteLine("Test: Byt() mrSrBits==0 sets Out=0 regardless of MData/AData, then WriteDest");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            ucode.AMem[0] = 0xDEADBEEF; // sentinel: overwritten by WriteDest(0x800) if it runs

            // P0 = 0x180002000000: Op(Ir 43,2)=3 (Byte), mrSrBits(Ir 12,2)=0,
            // dest(Ir 14,12)=0x800 (bit11 set -> WriteDest routes to
            // AMem[dest&0x3FF]=AMem[0]), pos/widthm1 both 0 (irrelevant here).
            ucode.P0 = 0x180002000000UL;
            ucode.MData = 0x1234;
            ucode.AData = 0x5678;

            ucode.CallByt();

            Assert(ucode.Out == 0, "mrSrBits==0 forces Out=0, ignoring MData/AData entirely");
            Assert(ucode.AMem[0] == 0, "WriteDest(0x800) overwrote the AMem[0] sentinel with Out(0)");

            Console.WriteLine("  mrSrBits==0 test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  mrSrBits==0 test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestLdbMaskAtPositionZero()
    {
        Console.WriteLine("Test: Byt() mrSrBits==1 (LDB) rotates MData by pos but builds the mask at position 0, not pos");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // P0 = 0x18000200107c: Op=3, mrSrBits(Ir 12,2)=1, dest=0x800,
            // widthm1(Ir 5,5)=3 (width 4), pos(Ir 0,5)=28. mask is built at
            // position 0 (LDB), not at pos=28: leftMaskIndex=(0+3)&0x1F=3,
            // mask=0b1111=0xF.
            ucode.P0 = 0x18000200107cUL;

            // MData=0xF0 (bits4-7 set). Rotating LEFT by 28 == rotating
            // RIGHT by 4, so bits4-7 land at bits0-3: rotated MData=0xF,
            // exactly matching the position-0 mask (0xF). AData=0, so the
            // merge's AData contribution is 0 either way -- isolates the
            // rotation+mask-position check cleanly.
            ucode.MData = 0xF0;
            ucode.AData = 0;

            ucode.CallByt();

            // If the rotation didn't happen, MData stays 0xF0 and
            // (0xF0 & 0xF) == 0, not 0xF. If the mask were wrongly built at
            // pos=28 instead of 0, mask would be 0xF0000000 and
            // (0xF & 0xF0000000) == 0, not 0xF. Either bug is caught by this
            // single assertion.
            Assert(ucode.Out == 0xF, "LDB: rotated MData (0xF0->0xF) merged through a position-0 mask (0xF)");
            Assert(ucode.AMem[0] == 0xF, "WriteDest(0x800) wrote Out(0xF) to AMem[0]");

            Console.WriteLine("  LDB mask-at-position-zero test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  LDB mask-at-position-zero test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestSelDepMaskAtPosNoRotation()
    {
        Console.WriteLine("Test: Byt() mrSrBits==2 (SEL-DEP) builds the mask at pos and never rotates MData");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // P0 = 0x180002002064: Op=3, mrSrBits=2, dest=0x800,
            // widthm1=3 (width 4), pos=4. mask is built at pos=4 (SEL-DEP):
            // leftMaskIndex=(4+3)&0x1F=7, mask=0xFF & (0xFFFFFFFF<<4)=0xF0.
            ucode.P0 = 0x180002002064UL;

            // MData=0xF (bits0-3 set) -- entirely OUTSIDE the mask window
            // (bits4-7) if MData is used unrotated, as SEL-DEP requires.
            // AData=0xFFFFFFFF proves the merge's AData contribution
            // outside the mask window survives.
            ucode.MData = 0xF;
            ucode.AData = unchecked((int)0xFFFFFFFF);

            ucode.CallByt();

            // Correct: (0xF & 0xF0)=0 | (0xFFFFFFFF & ~0xF0=0xFFFFFF0F) =
            // 0xFFFFFF0F. If SEL-DEP wrongly rotated MData by pos=4 first
            // (Rol32(0xF,4)=0xF0, landing squarely inside the mask), the
            // result would instead saturate to 0xFFFFFFFF -- a clean,
            // visible discriminator for an erroneous rotation.
            Assert(ucode.Out == 0xFFFFFF0F, "SEL-DEP: unrotated MData merged through a pos=4 mask (0xF0), AData preserved outside it");
            Assert(ucode.AMem[0] == 0xFFFFFF0F, "WriteDest(0x800) wrote Out to AMem[0]");

            Console.WriteLine("  SEL-DEP mask-at-pos-no-rotation test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  SEL-DEP mask-at-pos-no-rotation test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDpbMaskAtPosWithRotation()
    {
        Console.WriteLine("Test: Byt() mrSrBits==3 (DPB) both rotates MData by pos AND builds the mask at pos");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // P0 = 0x18000200307c: Op=3, mrSrBits=3, dest=0x800,
            // widthm1=3 (width 4), pos=28. mask is built at pos=28 (DPB):
            // leftMaskIndex=(28+3)&0x1F=31, mask=0xFFFFFFFF & (0xFFFFFFFF<<28)
            // = 0xF0000000.
            ucode.P0 = 0x18000200307cUL;

            // MData=0xF (bits0-3 set). Rotating LEFT by 28 moves bits0-3 to
            // bits28-31, landing exactly inside the pos=28 mask window:
            // rotated MData=0xF0000000. AData=0.
            ucode.MData = 0xF;
            ucode.AData = 0;

            ucode.CallByt();

            // If the rotation didn't happen, MData stays 0xF (bits0-3),
            // which doesn't overlap the bits28-31 mask window at all:
            // (0xF & 0xF0000000)==0, not 0xF0000000. If the mask were
            // wrongly built at position 0 instead of pos=28 (treating DPB
            // like LDB), mask would be 0xF and the rotated MData
            // (0xF0000000) wouldn't overlap it either: also 0, not
            // 0xF0000000. Either bug is caught by this single assertion.
            Assert(ucode.Out == 0xF0000000, "DPB: rotated MData (0xF->0xF0000000) merged through a pos=28 mask (0xF0000000)");
            Assert(ucode.AMem[0] == 0xF0000000, "WriteDest(0x800) wrote Out(0xF0000000) to AMem[0]");

            Console.WriteLine("  DPB mask-at-pos-with-rotation test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  DPB mask-at-pos-with-rotation test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestByteModePosOverride()
    {
        Console.WriteLine("Test: Byt() Ir(10,2)==3 uses LcByteMode()'s pos, not the raw Ir(0,5) bits, for mask position");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // P0 = 0x180002002c63: Op=3, mrSrBits(Ir 12,2)=2 (SEL-DEP, so pos
            // affects only the mask position, isolating this check from
            // rotation entirely), dest=0x800, selector(Ir 10,2)=3 (triggers
            // LcByteMode()), raw pos bits(Ir 0,5)=3, widthm1(Ir 5,5)=3
            // (width 4). With InterruptControl=0/Lc=0 (Init() defaults),
            // LcByteMode()'s else-branch gives pos=19 (ir4=(P0>>4)&1=0,
            // lc1=(Lc>>1)&1=0, (ir4^lc1)==0 -> bit4 of pos set -> pos =
            // (P0&0xF=3) | (1<<4) = 19) -- deliberately different from the
            // raw Ir(0,5) value of 3, so this genuinely discriminates
            // whether Byt() delegates to LcByteMode() or uses the raw bits.
            ucode.P0 = 0x180002002c63UL;
            ucode.MData = unchecked((int)0xFFFFFFFF);
            ucode.AData = 0;

            ucode.CallByt();

            // Correct (pos=19): leftMaskIndex=(19+3)&0x1F=22,
            // mask=0x7FFFFF & (0xFFFFFFFF<<19)=0x780000.
            // (0xFFFFFFFF & 0x780000)|(0 & ~0x780000) = 0x780000.
            // If Byt() wrongly used the raw pos=3 instead: mask would be
            // 0x78 (bits3-6), giving Out=0x78 -- a clean, visible
            // discriminator.
            Assert(ucode.Out == 0x780000, "byte-mode pos (19, via LcByteMode()) used for the mask, not raw Ir(0,5)=3");
            Assert(ucode.AMem[0] == 0x780000, "WriteDest(0x800) wrote Out(0x780000) to AMem[0]");

            Console.WriteLine("  byte-mode pos-override test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  byte-mode pos-override test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestByteModeWithLdbRotation()
    {
        Console.WriteLine("Test: Byt() byte-mode pos override reaches Rol32() for LDB (mrSrBits=1), the combination real microcode actually uses (35x in sys/ubin/ucadr.mcr, vs. zero uses of the SEL-DEP+byte-mode combination the other byte-mode test covers)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // P0 = 0x180002001d00: Op(Ir 43,2)=3 (Byte), mrSrBits(Ir 12,2)=1
            // (LDB), dest(Ir 14,12)=0x800, selector(Ir 10,2)=3 (triggers
            // LcByteMode()), widthm1(Ir 5,5)=8 (width 9), raw pos(Ir 0,5)=0.
            // With InterruptControl=0/Lc=0 (Init() defaults), LcByteMode()'s
            // else-branch gives pos=16 (ir4=(P0>>4)&1=0, lc1=(Lc>>1)&1=0,
            // (ir4^lc1)==0 -> bit4 of pos set -> pos=(P0&0xF=0)|(1<<4)=16).
            // Since mrSrBits=1 (LDB), the mask is built at position 0
            // regardless: leftMaskIndex=(0+8)&0x1F=8, mask=0x1FF.
            ucode.P0 = 0x180002001d00UL;

            // MData=0xABCD0000: rotating LEFT by pos=16 swaps the two
            // 16-bit halves, giving rotated MData=0x0000ABCD. Masked with
            // 0x1FF: 0xABCD & 0x1FF = 0x1CD. AData=0xFFFFFFFF proves the
            // merge's AData contribution outside the mask window survives:
            // Out = 0x1CD | (0xFFFFFFFF & ~0x1FF=0xFFFFFE00) = 0xFFFFFFCD.
            ucode.MData = unchecked((int)0xABCD0000);
            ucode.AData = unchecked((int)0xFFFFFFFF);

            ucode.CallByt();

            // If the byte-mode override were ignored (pos stayed at the raw
            // Ir(0,5) value of 0), MData would never rotate, giving
            // Out = (0xABCD0000 & 0x1FF=0)|(0xFFFFFFFF & ~0x1FF=0xFFFFFE00)
            // = 0xFFFFFE00 -- a clean, visible discriminator from the
            // correct 0xFFFFFFCD.
            Assert(ucode.Out == 0xFFFFFFCD, "byte-mode pos (16, via LcByteMode()) reached Rol32() for LDB, not just Msk()");
            Assert(ucode.AMem[0] == 0xFFFFFFCD, "WriteDest(0x800) wrote Out(0xFFFFFFCD) to AMem[0]");

            Console.WriteLine("  byte-mode LDB rotation test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  byte-mode LDB rotation test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
