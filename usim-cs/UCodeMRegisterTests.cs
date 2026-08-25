// UCodeMRegisterTests.cs - Tests for UCode's special M-register access
// (Phase 4 of the microcode engine port). Covers MfRead()'s 15 implemented
// register codes plus its Phase-5-deferred MEMORY-MAP-DATA stub and fatal
// default, and (Task 2) MfWrite()'s register codes plus its Phase-5/
// bus-interface-deferred placeholders.

using System;

namespace Usim;

public static class UCodeMRegisterTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== UCode M-Register Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestMfReadSimpleRegisters()) passed++; else failed++;
        if (TestMfReadSpcPeekAndPop()) passed++; else failed++;
        if (TestMfReadPdlPopAndPeek()) passed++; else failed++;
        if (TestMfReadLcByteModeGate()) passed++; else failed++;
        if (TestMfReadPlaceholdersAndMemoryMapData()) passed++; else failed++;
        if (TestMfReadDefaultThrows()) passed++; else failed++;
        if (TestMfWriteLc()) passed++; else failed++;
        if (TestMfWriteInterruptControl()) passed++; else failed++;
        if (TestMfWritePdlAndSpcRegisters()) passed++; else failed++;
        if (TestMfWriteOaRegisters()) passed++; else failed++;
        if (TestMfWriteVmaAndMdRegisters()) passed++; else failed++;
        if (TestMfWriteNoOpAndDefault()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestMfReadSimpleRegisters()
    {
        Console.WriteLine("Test: MfRead simple pass-through registers (codes 0,2,3,6,7,8,10)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            ucode.DispatchConstant = 0x12345;
            Assert(ucode.MfRead(0) == 0x12345, $"code0: DispatchConstant, got 0x{ucode.MfRead(0):X}");

            ucode.PdlPointer = 0x3FF;
            Assert(ucode.MfRead(2) == 0x3FF, $"code2: PdlPointer & 0x3FF, got 0x{ucode.MfRead(2):X}");
            ucode.PdlPointer = 0x7FF; // exercise the mask
            Assert(ucode.MfRead(2) == 0x3FF, $"code2: PdlPointer masked to 0x3FF, got 0x{ucode.MfRead(2):X}");

            ucode.PdlIndex = 0x2AA;
            Assert(ucode.MfRead(3) == 0x2AA, $"code3: PdlIndex & 0x3FF, got 0x{ucode.MfRead(3):X}");

            ucode.Opc = 0xABCD;
            Assert(ucode.MfRead(6) == 0xABCD, $"code6: Opc, got 0x{ucode.MfRead(6):X}");

            ucode.Q = 0xDEADBEEF;
            Assert(ucode.MfRead(7) == unchecked((int)0xDEADBEEF), $"code7: Q, got 0x{ucode.MfRead(7):X}");

            ucode.VmaReg = 0x0FF00FF0;
            Assert(ucode.MfRead(8) == 0x0FF00FF0, $"code8 (010 octal): VmaReg, got 0x{ucode.MfRead(8):X}");

            ucode.MdReg = 0x55555555;
            Assert(ucode.MfRead(10) == 0x55555555, $"code10 (012 octal): MdReg, got 0x{ucode.MfRead(10):X}");

            Console.WriteLine("  MfRead simple-register tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfRead simple-register tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfReadSpcPeekAndPop()
    {
        Console.WriteLine("Test: MfRead SPC peek (code 1) and pop (code 12)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 1: (SpcPtr<<24) | (Spc[SpcPtr] & 0x7FFFF) -- NOT 0x1FFFFF, see the
            // Global Constraints correction. Use a value with bits above 19 set to
            // prove the mask is really 0x7FFFF (19 bits), not the spec's original
            // (wrong) 0x1FFFFF (21 bits): 0x1F0000 has bit 20 set (part of the 21-bit
            // mask but NOT the 19-bit one), so a correct 19-bit mask must clear it.
            ucode.SpcPtr = 5;
            ucode.Spc[5] = 0x1FFFFF; // if masked with 0x7FFFF -> 0x7FFFF; if (wrongly) with 0x1FFFFF -> 0x1FFFFF
            int expected1 = (int)((5u << 24) | (0x1FFFFFu & 0x7FFFFu));
            Assert(ucode.MfRead(1) == expected1, $"code1: (SpcPtr<<24)|(Spc[SpcPtr]&0x7FFFF), got 0x{ucode.MfRead(1):X}, expected 0x{expected1:X}");
            Assert(ucode.SpcPtr == 5, "code1 (peek) does not decrement SpcPtr");

            // Code 12 (014 octal): same read+mask as code 1, but decrements SpcPtr afterward.
            ucode.SpcPtr = 5;
            ucode.Spc[5] = 0x1FFFFF;
            int expected12 = (int)((5u << 24) | (0x1FFFFFu & 0x7FFFFu));
            int res12 = ucode.MfRead(12);
            Assert(res12 == expected12, $"code12: same value as code1 before decrement, got 0x{res12:X}");
            Assert(ucode.SpcPtr == 4, $"code12 (pop) decrements SpcPtr by 1 (mod 0x20), got {ucode.SpcPtr}");

            // Code 12 wraparound: SpcPtr=0 decrements to 0x1F (5-bit wraparound).
            ucode.SpcPtr = 0;
            ucode.Spc[0] = 0;
            ucode.MfRead(12);
            Assert(ucode.SpcPtr == 0x1F, $"code12 decrement wraps 0 -> 0x1F, got 0x{ucode.SpcPtr:X}");

            Console.WriteLine("  MfRead SPC peek/pop tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfRead SPC peek/pop tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfReadPdlPopAndPeek()
    {
        Console.WriteLine("Test: MfRead PDL pop (code 20) and peek (code 21) and code 5");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 5: Pdl[PdlIndex], no mutation.
            ucode.PdlIndex = 0x10;
            ucode.Pdl[0x10] = 0x77777777;
            Assert(ucode.MfRead(5) == unchecked((int)0x77777777), $"code5: Pdl[PdlIndex], got 0x{ucode.MfRead(5):X}");
            Assert(ucode.PdlIndex == 0x10, "code5 does not mutate PdlIndex");

            // Code 20 (024 octal): Pdl[PdlPointer], THEN decrement PdlPointer (mod 0x400).
            ucode.PdlPointer = 0x20;
            ucode.Pdl[0x20] = 0x88888888;
            int res20 = ucode.MfRead(20);
            Assert(res20 == unchecked((int)0x88888888), $"code20: Pdl[PdlPointer] before decrement, got 0x{res20:X}");
            Assert(ucode.PdlPointer == 0x1F, $"code20 decrements PdlPointer by 1, got 0x{ucode.PdlPointer:X}");

            // Code 20 wraparound: PdlPointer=0 decrements to 0x3FF (10-bit wraparound).
            ucode.PdlPointer = 0;
            ucode.Pdl[0] = 0;
            ucode.MfRead(20);
            Assert(ucode.PdlPointer == 0x3FF, $"code20 decrement wraps 0 -> 0x3FF, got 0x{ucode.PdlPointer:X}");

            // Code 21 (025 octal): Pdl[PdlPointer], no mutation.
            ucode.PdlPointer = 0x30;
            ucode.Pdl[0x30] = 0x99999999;
            Assert(ucode.MfRead(21) == unchecked((int)0x99999999), $"code21: Pdl[PdlPointer], got 0x{ucode.MfRead(21):X}");
            Assert(ucode.PdlPointer == 0x30, "code21 does not mutate PdlPointer");

            Console.WriteLine("  MfRead PDL pop/peek tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfRead PDL pop/peek tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfReadLcByteModeGate()
    {
        Console.WriteLine("Test: MfRead code 11 (LC, byte-mode-gated bit-0 clear)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 11 (013 octal): byte mode (InterruptControl bit 29 set) -> Lc verbatim.
            ucode.InterruptControl = 1u << 29;
            ucode.Lc = 0x0ABCDEF1; // odd, bit0 set
            Assert(ucode.MfRead(11) == 0x0ABCDEF1, $"code11 byte mode: Lc verbatim, got 0x{ucode.MfRead(11):X}");

            // Not byte mode -> Lc with bit0 cleared.
            ucode.InterruptControl = 0;
            ucode.Lc = 0x0ABCDEF1;
            Assert(ucode.MfRead(11) == 0x0ABCDEF0, $"code11 not byte mode: Lc & ~1, got 0x{ucode.MfRead(11):X}");

            Console.WriteLine("  MfRead LC byte-mode-gate tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfRead LC byte-mode-gate tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfReadPlaceholdersAndMemoryMapData()
    {
        Console.WriteLine("Test: MfRead placeholder codes 13,22 and MEMORY-MAP-DATA code 9");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            Assert(ucode.MfRead(13) == 0, "code13 (015 octal): placeholder, matches C's '???' returning 0");
            Assert(ucode.MfRead(22) == 0, "code22 (026 octal): placeholder, matches C's '???' returning 0");

            // Code 9 (011 octal, MEMORY-MAP-DATA) is implemented as of Phase 5 -- map
            // MdReg's L1/L2 entries first, then confirm the bit-packed result.
            uint mdReg = 0x00246000;
            uint md = mdReg; // md=mdReg keeps L1 and L2 indices consistent with what MfRead(9) uses (Vtop(MdReg))
            uint l2Data = (1u << 23) | (1u << 22) | 0x00ABCDu; // access+write permission, l2 low bits 0xABCD
            uint vma = (1u << 26) | (1u << 25) | (0x0Bu << 27) | l2Data; // L1 data = 0x0B
            ucode.Uvmem.WriteMap(vma, md);
            ucode.MdReg = mdReg;

            int result9 = ucode.MfRead(9);
            uint expected9 = (0u << 31) | (0u << 30) | (1u << 29) | ((0x0Bu & 0x1F) << 24) | (l2Data & 0x00FFFFFF);
            Assert(unchecked((uint)result9) == expected9, $"code9: bit-packed MEMORY-MAP-DATA, got 0x{result9:X}, expected 0x{expected9:X}");

            Console.WriteLine("  MfRead placeholder/MEMORY-MAP-DATA tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfRead placeholder/MEMORY-MAP-DATA tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfReadDefaultThrows()
    {
        Console.WriteLine("Test: MfRead default case throws, matching C's fatal err()");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            bool threw = false;
            try { ucode.MfRead(4); } // octal 4 is not a valid code
            catch (InvalidOperationException) { threw = true; }
            Assert(threw, "code4 is not a valid MfRead register; must throw");

            Console.WriteLine("  MfRead default-throws tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfRead default-throws tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfWriteLc()
    {
        Console.WriteLine("Test: MfWrite code 1 (LC)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Not byte mode (InterruptControl bit29 clear): low bit cleared, bit31 (NEED-FETCH) set.
            ucode.InterruptControl = 0;
            ucode.Lc = 0xFFFFFFFF; // pre-existing garbage in the untouched high bits, to prove the mask
            ucode.MfWrite(1 << 5, unchecked((int)0x07FFFFFF)); // data with bit26 set, above the 26-bit mask
            uint expected = (0xFFFFFFFFu & ~0x03FFFFFFu) | (0x07FFFFFFu & 0x03FFFFFFu);
            expected &= ~1u;          // not byte mode -> low bit cleared
            expected |= (1u << 31);   // NEED-FETCH always set
            Assert(ucode.Lc == expected, $"code1 not byte mode, got 0x{ucode.Lc:X}, expected 0x{expected:X}");

            // Byte mode (bit29 set): low bit is NOT forced clear.
            ucode.InterruptControl = 1u << 29;
            ucode.Lc = 0;
            ucode.MfWrite(1 << 5, 0x00000003); // odd value, bit0 set
            Assert((ucode.Lc & 1) == 1, "code1 byte mode: low bit is NOT cleared");
            Assert((ucode.Lc & (1u << 31)) != 0, "code1 byte mode: NEED-FETCH still set unconditionally");

            Console.WriteLine("  MfWrite LC tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfWrite LC tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfWriteInterruptControl()
    {
        Console.WriteLine("Test: MfWrite code 2 (INTERRUPT-CONTROL)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            ucode.Lc = 0;
            ucode.MfWrite(2 << 5, unchecked((int)(0xFu << 26))); // set all 4 preserved-flag bits
            Assert(ucode.InterruptControl == (0xFu << 26), $"code2: InterruptControl set verbatim, got 0x{ucode.InterruptControl:X}");
            Assert(ucode.Lc == (0xFu << 26), $"code2: Lc bits 26-29 mirror InterruptControl, got 0x{ucode.Lc:X}");

            // Bit 28 (bus reset) does not throw -- it's a deferred, different-subsystem no-op.
            ucode.MfWrite(2 << 5, unchecked((int)(1u << 28)));
            Assert(true, "code2 bit28 (bus reset) does not throw");

            Console.WriteLine("  MfWrite INTERRUPT-CONTROL tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfWrite INTERRUPT-CONTROL tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfWritePdlAndSpcRegisters()
    {
        Console.WriteLine("Test: MfWrite PDL/SPC registers (codes 8,9,10,11,12,13)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 8 (010 octal): Pdl[PdlPointer] = data (no pointer mutation).
            ucode.PdlPointer = 0x15;
            ucode.MfWrite(8 << 5, unchecked((int)0xAAAAAAAA));
            Assert(ucode.Pdl[0x15] == 0xAAAAAAAA, $"code8: Pdl[PdlPointer] written, got 0x{ucode.Pdl[0x15]:X}");
            Assert(ucode.PdlPointer == 0x15, "code8 does not mutate PdlPointer");

            // Code 9 (011 octal): PdlPointer++ (mod 0x400) THEN write.
            ucode.PdlPointer = 0x15;
            ucode.MfWrite(9 << 5, unchecked((int)0xBBBBBBBB));
            Assert(ucode.PdlPointer == 0x16, $"code9: PdlPointer incremented first, got 0x{ucode.PdlPointer:X}");
            Assert(ucode.Pdl[0x16] == 0xBBBBBBBB, $"code9: written at the NEW pointer, got 0x{ucode.Pdl[0x16]:X}");

            // Code 9 wraparound: PdlPointer=0x3FF increments to 0 (10-bit wraparound).
            ucode.PdlPointer = 0x3FF;
            ucode.MfWrite(9 << 5, 1);
            Assert(ucode.PdlPointer == 0, $"code9 increment wraps 0x3FF -> 0, got 0x{ucode.PdlPointer:X}");

            // Code 10 (012 octal): Pdl[PdlIndex] = data.
            ucode.PdlIndex = 0x20;
            ucode.MfWrite(10 << 5, unchecked((int)0xCCCCCCCC));
            Assert(ucode.Pdl[0x20] == 0xCCCCCCCC, $"code10: Pdl[PdlIndex] written, got 0x{ucode.Pdl[0x20]:X}");

            // Code 11 (013 octal): PdlIndex = data & 0x3FF.
            ucode.MfWrite(11 << 5, 0x7FF);
            Assert(ucode.PdlIndex == 0x3FF, $"code11: PdlIndex masked to 0x3FF, got 0x{ucode.PdlIndex:X}");

            // Code 12 (014 octal): PdlPointer = data & 0x3FF.
            ucode.MfWrite(12 << 5, 0x7FF);
            Assert(ucode.PdlPointer == 0x3FF, $"code12: PdlPointer masked to 0x3FF, got 0x{ucode.PdlPointer:X}");

            // Code 13 (015 octal): PushSpc(data).
            ucode.SpcPtr = 0;
            ucode.MfWrite(13 << 5, unchecked((int)0x12345678));
            Assert(ucode.SpcPtr == 1, $"code13: PushSpc advanced SpcPtr, got {ucode.SpcPtr}");
            Assert(ucode.Spc[1] == 0x12345678, $"code13: pushed value, got 0x{ucode.Spc[1]:X}");

            Console.WriteLine("  MfWrite PDL/SPC tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfWrite PDL/SPC tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfWriteOaRegisters()
    {
        Console.WriteLine("Test: MfWrite OA-REG-LO/HI (codes 14,15)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 14 (016 octal): OaRegLow = data & 0x03FFFFFF (26 bits); Oal = true.
            ucode.MfWrite(14 << 5, unchecked((int)0xFFFFFFFF));
            Assert(ucode.OaRegLow == 0x03FFFFFF, $"code14: 26-bit mask, got 0x{ucode.OaRegLow:X}");
            Assert(ucode.Oal == true, "code14 sets Oal");

            // Code 15 (017 octal): OaRegHigh = data & 0x7FFFFF (23 bits); Oah = true.
            ucode.MfWrite(15 << 5, unchecked((int)0xFFFFFFFF));
            Assert(ucode.OaRegHigh == 0x7FFFFF, $"code15: 23-bit mask, got 0x{ucode.OaRegHigh:X}");
            Assert(ucode.Oah == true, "code15 sets Oah");

            Console.WriteLine("  MfWrite OA-register tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfWrite OA-register tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfWriteVmaAndMdRegisters()
    {
        Console.WriteLine("Test: MfWrite VMA/MD registers (codes 16,17,18,19,24,25,26,27)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 16 (020 octal): VmaReg = data.
            ucode.MfWrite(16 << 5, unchecked((int)0x11111111));
            Assert(ucode.VmaReg == 0x11111111, $"code16: VmaReg set, got 0x{ucode.VmaReg:X}");

            // Code 17 (021 octal): VmaReg = data; VmRead(VmaReg, out NewMd); NewMdDelay = 2.
            ucode.NewMdDelay = 0;
            ucode.MfWrite(17 << 5, unchecked((int)0x22222222));
            Assert(ucode.VmaReg == 0x22222222, $"code17: VmaReg set, got 0x{ucode.VmaReg:X}");
            Assert(ucode.NewMdDelay == 2, $"code17: NewMdDelay set to 2, got {ucode.NewMdDelay}");

            // Code 18 (022 octal): VmaReg = data; VmWrite(VmaReg, MdReg) -- no-op placeholder, must not throw.
            ucode.MfWrite(18 << 5, unchecked((int)0x33333333));
            Assert(ucode.VmaReg == 0x33333333, $"code18: VmaReg set, got 0x{ucode.VmaReg:X}");

            // Code 19 (023 octal): VmaReg = data; Uvmem.WriteMap(VmaReg, MdReg) for real (as
            // of this phase). This unchanged assertion from Phase 4 (0x44444444 happens to
            // have bit26 set, so it does trigger a real L1 write at whatever l1Index MdReg
            // held at this point) only checks VmaReg, not that write's side effect -- the
            // explicit, controlled check right below is what actually proves the wiring.
            ucode.MfWrite(19 << 5, unchecked((int)0x44444444));
            Assert(ucode.VmaReg == 0x44444444, $"code19: VmaReg set, got 0x{ucode.VmaReg:X}");

            // Now prove the wiring is real (not still the old no-op) with an enable bit set:
            // Uvmem.WriteMap(vma=data, md=MdReg) should write L1[l1Index] for real.
            ucode.MdReg = 0; // l1Index = (0>>13)&0x7FF = 0
            uint l1DataToWrite19 = 0x0Au;
            ucode.MfWrite(19 << 5, unchecked((int)((1u << 26) | (l1DataToWrite19 << 27))));
            _ = ucode.Uvmem.Vtop(0, out uint l1Check19, out _, out _, out _, out _);
            Assert(l1Check19 == l1DataToWrite19, $"code19 reaches the REAL Uvmem.WriteMap (not the old no-op), got L1=0x{l1Check19:X}");

            // Code 24 (030 octal): MdReg = data.
            ucode.MfWrite(24 << 5, unchecked((int)0x55555555));
            Assert(ucode.MdReg == 0x55555555, $"code24: MdReg set, got 0x{ucode.MdReg:X}");

            // Code 25 (031 octal): MdReg = data; VmRead(VmaReg, out NewMd); NewMdDelay = 2.
            // Note: reads from VmaReg, not the just-written MdReg -- matches the real C exactly.
            // VmaReg is left at whatever code 19 set it to just above (0x44444444); irrelevant
            // to this assertion since VmRead is currently a no-op regardless of its argument.
            ucode.NewMdDelay = 0;
            ucode.MfWrite(25 << 5, unchecked((int)0x66666666));
            Assert(ucode.MdReg == 0x66666666, $"code25: MdReg set, got 0x{ucode.MdReg:X}");
            Assert(ucode.NewMdDelay == 2, $"code25: NewMdDelay set to 2, got {ucode.NewMdDelay}");

            // Code 26 (032 octal): MdReg = data; VmWrite(VmaReg, MdReg) -- no-op, must not throw.
            ucode.MfWrite(26 << 5, unchecked((int)0x77777777));
            Assert(ucode.MdReg == 0x77777777, $"code26: MdReg set, got 0x{ucode.MdReg:X}");

            // Code 27 (033 octal): MdReg = data; Uvmem.WriteMap(VmaReg, MdReg) for real.
            // Note: MdReg == 0x88888888u (bare uint literal), not a cast int -- comparing a
            // uint field against a negative int constant expression doesn't compile in C#
            // (no implicit conversion for a negative value into uint), unlike the method
            // argument above, which legitimately needs the int cast since MfWrite's data
            // parameter is int.
            ucode.MfWrite(27 << 5, unchecked((int)0x88888888));
            Assert(ucode.MdReg == 0x88888888u, $"code27: MdReg set, got 0x{ucode.MdReg:X}");

            // Prove the wiring is real: VmaReg supplies WriteMap's L1 enable bit + L1 data;
            // MdReg (just set above, 0x88888888) supplies the l1Index WriteMap computes from.
            ucode.VmaReg = (1u << 26) | (0x15u << 27);
            ucode.MfWrite(27 << 5, unchecked((int)0x88888888)); // re-set MdReg=0x88888888, matching VmaReg's target l1Index
            _ = ucode.Uvmem.Vtop(0x88888888u, out uint l1Check27, out _, out _, out _, out _);
            Assert(l1Check27 == 0x15u, $"code27 reaches the REAL Uvmem.WriteMap (not the old no-op), got L1=0x{l1Check27:X}");

            Console.WriteLine("  MfWrite VMA/MD tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfWrite VMA/MD tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestMfWriteNoOpAndDefault()
    {
        Console.WriteLine("Test: MfWrite code 0 (no-op) and default (non-fatal warning)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Code 0: no-op, must not throw or mutate anything observable.
            ucode.MfWrite(0 << 5, unchecked((int)0xFFFFFFFF));
            Assert(true, "code0: no-op does not throw");

            // Default (e.g. dest>>5 == 3, unassigned): matches C's non-fatal warn(), does not throw.
            ucode.MfWrite(3 << 5, 0);
            Assert(true, "default case does not throw (non-fatal warning, matching C's warn())");

            Console.WriteLine("  MfWrite no-op/default tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfWrite no-op/default tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
