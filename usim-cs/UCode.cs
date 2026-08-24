// UCode.cs - Microcode execution engine
// Converted from ucode.h and ucode.c
//
// This file contains the microcode execution engine for the CADR Lisp Machine simulator.
// The microcode implements the low-level operations including:
// - ALU operations (arithmetic, logic, shifts, rotations)
// - Memory operations (A, M, D memory access)
// - Control flow (jumps, dispatch, etc.)
// - Stack operations (PDL, SPC)
//
// Bit manipulation operations (rol32, ror32, etc.) are available in MiscUtils.cs

using System;
using System.IO;

namespace Usim;

/// <summary>
/// Microcode execution engine for CADR simulator
/// </summary>
public class UCode
{
    #region Constants

    // Memory sizes
    public const int PROM_SIZE = 512;
    public const int IMEM_SIZE = 16 * 1024;
    public const int AMEM_SIZE = 1024;
    public const int MMEM_SIZE = 32;
    public const int DMEM_SIZE = 2048;
    public const int PDL_SIZE = 1024;
    public const int SPC_SIZE = 32;

    #endregion
    
    // Machine cycles counter
    public ulong MachineCycles { get; set; }

    // Interrupt status
    public int InterruptStatusReg { get; private set; }
    public bool InterruptPendingFlag { get; set; }

    // Microcode memory
    public bool PromEnabledFlag { get; set; }
    public bool PromDisabled { get; set; }
    public ulong[] Prom { get; } = new ulong[PROM_SIZE];
    public ulong[] IMem { get; } = new ulong[IMEM_SIZE];

    // A, M, and D memories
    public uint[] AMem { get; } = new uint[AMEM_SIZE];
    public uint[] MMem { get; } = new uint[MMEM_SIZE];
    public uint[] DMem { get; } = new uint[DMEM_SIZE];

    // Push-down list (stack)
    public uint[] Pdl { get; } = new uint[PDL_SIZE];

    // Stack pointer cache
    public uint[] Spc { get; } = new uint[SPC_SIZE];
    public uint SpcPtr { get; set; }

    // Named registers
    public uint DispatchConstant { get; set; }
    public uint PdlPointer { get; set; }
    public uint PdlIndex { get; set; }
    public uint VmaReg { get; set; }
    public uint MdReg { get; set; }
    public uint Lc { get; set; }
    public uint OaRegHigh { get; set; }
    public uint OaRegLow { get; set; }
    public uint Opc { get; set; }
    public uint Q { get; set; }
    public uint OldQ { get; set; }
    public uint InterruptControl { get; set; }

    // Pipeline registers
    public ulong P0 { get; set; }
    public uint P0Pc { get; set; }
    public bool P0Imem { get; set; }

    public ulong P1 { get; set; }
    public uint P1Pc { get; set; }
    public bool P1Imem { get; set; }

    public ulong Iwr { get; set; }

    public uint Npc { get; set; }

    // Latched operands for the current cycle
    public uint AAddr { get; set; }
    public int AData { get; set; }
    public uint MAddr { get; set; }
    public int MData { get; set; }

    public uint Out { get; set; }

    public bool Inhibit { get; set; }
    public bool UExecHasRunOnce { get; set; }

    // Decoded common fields for the current cycle
    public uint Op { get; set; }
    public bool Popj { get; set; }

    // Pending delayed memory read
    public uint NewMd { get; set; }
    public uint NewMdDelay { get; set; }

    // ALU result/carry for the current cycle (real hardware has no
    // persistent flags register — these replace the old Carry/Overflow/
    // Negative/Zero flags, which were an invented artifact)
    public uint AluCarry { get; set; }
    public uint AluOut { get; set; }

    // OA-register pending-merge flags
    public bool Oal { get; set; }
    public bool Oah { get; set; }

    /// <summary>
    /// Read len bits of P0 starting at bit pos (mirrors uexec.c's ir()).
    /// </summary>
    private ulong Ir(int pos, int len)
    {
        return (P0 >> pos) & ((1UL << len) - 1);
    }

    /// <summary>
    /// Initialize the microcode system
    /// </summary>
    public void Init()
    {
        MachineCycles = 0;
        InterruptStatusReg = 0;
        InterruptPendingFlag = false;
        PromEnabledFlag = false;
        PromDisabled = false;

        Array.Clear(Prom);
        Array.Clear(IMem);
        Array.Clear(AMem);
        Array.Clear(MMem);
        Array.Clear(DMem);
        Array.Clear(Pdl);
        Array.Clear(Spc);

        SpcPtr = 0;
        DispatchConstant = 0;
        PdlPointer = 0;
        PdlIndex = 0;
        VmaReg = 0;
        MdReg = 0;
        Lc = 0;
        OaRegHigh = 0;
        OaRegLow = 0;
        Opc = 0;
        Q = 0;
        OldQ = 0;
        InterruptControl = 0;

        P0 = 0;
        P0Pc = 0;
        P0Imem = false;

        P1 = 0;
        P1Pc = 0;
        P1Imem = false;

        Iwr = 0;
        Npc = 0;

        AAddr = 0;
        AData = 0;
        MAddr = 0;
        MData = 0;

        Out = 0;
        Inhibit = false;
        UExecHasRunOnce = false;

        Op = 0;
        Popj = false;
        NewMd = 0;
        NewMdDelay = 0;
        AluCarry = 0;
        AluOut = 0;
        Oal = false;
        Oah = false;
    }

    #region Fetch, Decode, and Dispatch

    /// <summary>
    /// Advance the pipeline: P1 becomes P0, then prefetch the next word into P1.
    /// </summary>
    private void IncNpc()
    {
        P0 = P1; P0Pc = P1Pc; P0Imem = P1Imem;

        P1Imem = PromDisabled;
        P1 = P1Imem ? IMem[Npc] : Prom[Npc];
        P1Pc = Npc;

        if (Npc == 0x3FFF) Npc = 0; else Npc++;

        Opc = P0Pc;
    }

    /// <summary>
    /// Execute one microcode cycle (faithful port of uexec_step()).
    /// </summary>
    public void Step()
    {
        UExecHasRunOnce = true;

        IncNpc();

        if (NewMdDelay != 0)
        {
            NewMdDelay--;
            if (NewMdDelay == 0) MdReg = NewMd;
        }

        if (Inhibit)
        {
            Inhibit = false;
            MachineCycles++;
            return;
        }

        if (Oal) { Oal = false; P0 |= OaRegLow & 0x03FFFFFF; }
        if (Oah) { Oah = false; P0 |= (ulong)(OaRegHigh & 0x003FFFFF) << 26; }

        Op = (uint)Ir(43, 2);
        Popj = Ir(42, 1) == 1;
        AAddr = (uint)Ir(32, 10);
        ulong msource = Ir(31, 1);
        MAddr = (uint)Ir(26, 5);

        MData = msource == 0 ? (int)MMem[MAddr] : MfRead(MAddr);
        AData = (int)AMem[AAddr];

        Iwr = ((ulong)(uint)(AData & 0xFFFF) << 32) | (uint)MData;

        switch (Op)
        {
            case 0: Alu(); break;
            case 1: Jmp(); break;
            case 2: Dsp(); break;
            case 3: Byt(); break;
        }

        if (Popj)
        {
            uint target = PopSpc();
            if ((target >> 14 & 1) != 0) target = AdvanceLc(target);
            Npc = target & 0x3FFF;
        }

        MachineCycles++;
    }

    private int LcByteMode()
    {
        if ((InterruptControl & (1 << 29)) != 0)
        {
            int ir4 = (int)(P0 >> 4) & 1, ir3 = (int)(P0 >> 3) & 1;
            int lc1 = (int)(Lc >> 1) & 1, lc0 = (int)Lc & 1;
            int pos = (int)(P0 & 7);
            pos |= ((ir4 ^ (lc1 ^ lc0)) << 4) | ((ir3 ^ lc0) << 3);
            return pos;
        }
        else
        {
            int ir4 = (int)(P0 >> 4) & 1, lc1 = (int)(Lc >> 1) & 1;
            int pos = (int)(P0 & 0xF);
            pos |= ((ir4 ^ lc1) == 0 ? 1 : 0) << 4;
            return pos;
        }
    }

    private uint AdvanceLc(uint ppc)
    {
        uint oldLc = Lc & 0x03FFFFFF; // 26-bit mask (LC is 26 bits; the real C mask is octal 0377777777 = 0x03FFFFFF, not 0x0FFFFFFF)
        if ((InterruptControl & (1 << 29)) != 0) Lc++; else Lc += 2;

        if ((Lc & (1u << 31)) != 0)
        {
            Lc &= ~(1u << 31);
            VmaReg = oldLc >> 2;
            VmRead(oldLc >> 2, out uint newMd);
            NewMd = newMd;
            NewMdDelay = 2;
        }
        else
        {
            ppc |= 2;
        }

        uint lc0b = ((InterruptControl & (1 << 29)) != 0 ? 1u : 0u) & (Lc & 1);
        uint lc1 = (Lc & 2) != 0 ? 1u : 0u;
        bool lastByteInWord = (~lc0b & ~lc1 & 1) != 0;
        if (lastByteInWord) Lc |= (1u << 31);

        return ppc;
    }

    private void PushSpc(uint pc)
    {
        SpcPtr = (SpcPtr + 1) & 0x1F;
        Spc[SpcPtr] = pc;
    }

    private uint PopSpc()
    {
        uint v = Spc[SpcPtr];
        SpcPtr = (SpcPtr - 1) & 0x1F;
        return v;
    }

    private int MfRead(uint addr)
    {
        throw new NotImplementedException("MfRead is implemented in Phase 4 (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md)");
    }

    private void Alu()
    {
        throw new NotImplementedException("Alu is implemented in Phase 2 (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md)");
    }

    private void Jmp()
    {
        throw new NotImplementedException("Jmp is implemented in Phase 3 (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md)");
    }

    private void Dsp()
    {
        throw new NotImplementedException("Dsp is implemented in Phase 6 (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md)");
    }

    private void Byt()
    {
        throw new NotImplementedException("Byt is implemented in Phase 7 (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md)");
    }

    /// <summary>
    /// VMA-ok state for the current cycle (real page-fault detection lands in Phase 5;
    /// defaults to true — "no page fault" — until then).
    /// </summary>
    public bool VmaOk { get; set; } = true;

    private void VmRead(uint vaddr, out uint v)
    {
        // Real virtual-memory read lands in Phase 5. Until then, treat every
        // read as a page fault-free no-op returning 0, matching "VmaOk = true"
        // above (Phase 5 replaces this with the real Vm()/Uvmem-backed path).
        v = 0;
    }

    #endregion

    /// <summary>
    /// Load PROM from a .mcr file (faithful port of ucode.c's
    /// ucode_load_prom_from_file()). The file starts with a 12-byte section
    /// header — code/start/size, each a 32-bit "PDP-endian" (middle-endian,
    /// byte order 1-0-3-2) value — followed by `size` 64-bit microcode words,
    /// each stored as four little-endian 16-bit halves assembled MSB-first
    /// (w1&lt;&lt;48 | w2&lt;&lt;32 | w3&lt;&lt;16 | w4). This does NOT match a
    /// straight little-endian 8-byte read, and the header must be skipped —
    /// both were bugs in the previous version of this method.
    /// </summary>
    public void LoadPromFromFile(string filename)
    {
        using var stream = File.OpenRead(filename);

        uint ReadU16Le()
        {
            int b0 = stream.ReadByte();
            int b1 = stream.ReadByte();
            return (uint)(((b1 & 0xFF) << 8) | (b0 & 0xFF));
        }

        uint ReadU32Pdp()
        {
            int b0 = stream.ReadByte();
            int b1 = stream.ReadByte();
            int b2 = stream.ReadByte();
            int b3 = stream.ReadByte();
            return (uint)(((b1 & 0xFF) << 24) | ((b0 & 0xFF) << 16) | ((b3 & 0xFF) << 8) | (b2 & 0xFF));
        }

        // Section header: code (section type, unused here — the boot PROM
        // is always the file's first, I-memory, section), start (base PROM
        // location), size (word count).
        ReadU32Pdp(); // code
        uint start = ReadU32Pdp();
        uint size = ReadU32Pdp();

        for (uint i = 0; i < size; i++)
        {
            if (stream.Position + 8 > stream.Length)
                break;

            ulong w1 = ReadU16Le();
            ulong w2 = ReadU16Le();
            ulong w3 = ReadU16Le();
            ulong w4 = ReadU16Le();
            ulong word = (w1 << 48) | (w2 << 32) | (w3 << 16) | w4;

            uint loc = start + i;
            if (loc < (uint)Prom.Length)
                Prom[loc] = word;
        }

        PromEnabledFlag = true;
    }

    /// <summary>
    /// Set interrupt status register
    /// </summary>
    public void SetInterruptStatusReg(int newValue)
    {
        InterruptStatusReg = newValue;
        InterruptPendingFlag = (newValue != 0);
    }

    /// <summary>
    /// Assert Unibus interrupt
    /// </summary>
    public void AssertUnibusInterrupt(int level)
    {
        InterruptStatusReg |= (1 << level);
        InterruptPendingFlag = true;
    }

    /// <summary>
    /// Deassert Unibus interrupt
    /// </summary>
    public void DeassertUnibusInterrupt()
    {
        InterruptStatusReg = 0;
        InterruptPendingFlag = false;
    }

    /// <summary>
    /// Assert Xbus interrupt
    /// </summary>
    public void AssertXbusInterrupt()
    {
        InterruptPendingFlag = true;
    }

    /// <summary>
    /// Deassert Xbus interrupt
    /// </summary>
    public void DeassertXbusInterrupt()
    {
        InterruptPendingFlag = false;
    }

    /// <summary>
    /// 32-bit add with carry-in/carry-out, matching the C macro add32().
    /// NOTE: this macro's carry-out polarity is inverted from the naive
    /// "1 = unsigned overflow occurred" intuition (and from m32.h's own
    /// doc comment) — ported literally from the real m32.h macro text,
    /// which is what actually runs. Verified by hand against the C source;
    /// do not "fix" this to match intuition.
    /// NOTE: the carry line's comparison (`b >= ~a` / `b > ~a`) is a plain
    /// SIGNED int comparison in the real macro — there are no casts anywhere
    /// in that line of m32.h, and every real call site passes plain
    /// `int`-typed arguments (mdata/adata, abs32(...), or (int32_t)alu_out).
    /// Do not cast to unsigned here.
    /// </summary>
    internal static (uint Out, uint Carry) Add32(int a, int b, bool ci)
    {
        uint outv = unchecked((uint)a + (uint)b + (ci ? 1u : 0u));
        int notA = ~a;
        uint co = ci ? (b >= notA ? 0u : 1u) : (b > notA ? 0u : 1u);
        return (outv, co);
    }

    /// <summary>
    /// 32-bit subtract with carry-in/carry-out, matching the C macro sub32().
    /// NOTE: this macro's carry-out polarity is inverted from the naive
    /// "1 = borrow occurred" intuition — ported literally from the real
    /// m32.h macro text, which is what actually runs. Verified by hand
    /// against the C source; do not "fix" this to match intuition.
    /// </summary>
    internal static (uint Out, uint Carry) Sub32(int a, int b, bool ci)
    {
        uint outv = unchecked((uint)a - (uint)b - (ci ? 0u : 1u));
        uint co = outv < (uint)a ? 1u : 0u;
        return (outv, co);
    }

    /// <summary>
    /// Two's-complement absolute value, matching the C macro abs32().
    /// </summary>
    internal static int Abs32(int a) => a < 0 ? ~a + 1 : a;

    /// <summary>
    /// 32-bit rotate-left, matching the C function rol32() in m32.c.
    /// </summary>
    internal static uint Rol32(uint value, int bits)
    {
        if (bits == 0) return value;
        int mask = unchecked((int)0x80000000) >> bits;
        uint tmp = (uint)(((ulong)(value & (uint)mask)) >> (32 - bits));
        return (value << bits) | tmp;
    }

    /// <summary>
    /// Logical ALU operations, codes 0-15 (octal 000-017).
    /// </summary>
    internal void LogiOps(uint op)
    {
        switch (op)
        {
            case 0: AluOut = 0; break;                                       // SETZ
            case 1: AluOut = (uint)(MData & AData); break;                   // AND
            case 2: AluOut = (uint)(MData & ~AData); break;                  // ANDCA
            case 3: AluOut = (uint)MData; break;                             // SETM
            case 4: AluOut = (uint)(~MData & AData); break;                  // ANDCM
            case 5: AluOut = (uint)AData; break;                             // SETA
            case 6: AluOut = (uint)(MData ^ AData); break;                   // XOR
            case 7: AluOut = (uint)(MData | AData); break;                   // IOR
            case 8: AluOut = (uint)(~AData & ~MData); break;                 // NOR
            case 9: AluOut = (AData == MData) ? 1u : 0u; break;              // EQV (boolean test)
            case 10: AluOut = (uint)~AData; break;                           // SETCA
            case 11: AluOut = (uint)(MData | ~AData); break;                // ORCA
            case 12: AluOut = (uint)~MData; break;                          // SETCM
            case 13: AluOut = (uint)(~MData | AData); break;                 // ORCM
            case 14: AluOut = (uint)(~MData | ~AData); break;                // ORCB
            case 15: AluOut = 0xFFFFFFFFu; break;                           // SETO
        }
    }

    /// <summary>
    /// Arithmetic ALU operations, codes 16-31 (octal 020-037).
    /// </summary>
    internal void ArithOps(uint op)
    {
        bool cin = Ir(2, 1) != 0;
        long lv;

        switch (op)
        {
            case 16: AluOut = cin ? 0u : uint.MaxValue; AluCarry = 0; return;
            case 17:
                lv = (long)(MData & AData) - (cin ? 0 : 1);
                break;
            case 18:
                lv = (long)(MData & ~AData) - (cin ? 0 : 1);
                break;
            case 19:
                lv = (long)MData - (cin ? 0 : 1);
                break;
            case 20:
                lv = (long)(MData | ~AData) + (cin ? 1 : 0);
                break;
            case 21:
                lv = (long)(MData | ~AData) + (MData & AData) + (cin ? 1 : 0);
                break;
            case 22:
                (AluOut, AluCarry) = Sub32(MData, AData, cin);
                return;
            case 23:
                lv = (long)(MData | ~AData) + MData + (cin ? 1 : 0);
                break;
            case 24:
                lv = (long)(MData | AData) + (cin ? 1 : 0);
                break;
            case 25:
                (AluOut, AluCarry) = Add32(MData, AData, cin);
                return;
            case 26:
                lv = (long)(MData | AData) + (MData & ~AData) + (cin ? 1 : 0);
                break;
            case 27:
                lv = (long)(MData | AData) + MData + (cin ? 1 : 0);
                break;
            case 28:
                AluOut = (uint)(MData + (cin ? 1 : 0));
                AluCarry = 0;
                if (MData == -1 && cin) AluCarry = 1;
                return;
            case 29:
                lv = (long)MData + (MData & AData) + (cin ? 1 : 0);
                break;
            case 30:
                lv = (long)MData + (MData | ~AData) + (cin ? 1 : 0);
                break;
            case 31:
                (AluOut, AluCarry) = Add32(MData, MData, cin);
                return;
            default:
                return;
        }

        AluOut = (uint)lv;
        AluCarry = (lv >> 32) != 0 ? 1u : 0u;
    }

    /// <summary>
    /// Multiply/divide-step ALU operations, codes 32, 33, 37, 41
    /// (octal 040, 041, 045, 051).
    /// </summary>
    internal void DivOps(uint op)
    {
        bool cin = Ir(2, 1) != 0;

        switch (op)
        {
            case 32: // multiply step
                if ((Q & 1) != 0)
                {
                    (AluOut, AluCarry) = Add32(AData, MData, cin);
                }
                else
                {
                    AluOut = (uint)MData;
                    AluCarry = (AluOut & 0x80000000) != 0 ? 1u : 0u;
                }
                break;
            case 33: // divide step
                if ((Q & 1) != 0)
                    (AluOut, AluCarry) = Sub32(MData, Abs32(AData), !cin);
                else
                    (AluOut, AluCarry) = Add32(MData, Abs32(AData), cin);
                break;
            case 37: // remainder correction
                // The real C call is add32((int32_t)alu_out, abs32(adata), cin,
                // alu_out, alu_carry) — 'out' and 'a' are the SAME variable
                // (alu_out) at this call site. Textually expanding the macro,
                // line 1 reassigns alu_out first, then line 2's carry
                // computation re-reads ~(a), which is now the just-written
                // NEW alu_out, not the value alu_out held on entry. A plain
                // function call (Add32((int)AluOut, ...)) evaluates its
                // argument once before the call and would use the OLD value
                // instead, diverging from the reference emulator. This must
                // be expanded inline to replicate that self-aliasing quirk.
                if ((Q & 1) != 0)
                {
                    AluCarry = 0;
                }
                else
                {
                    int aArg = (int)AluOut;
                    int bArg = Abs32(AData);
                    uint newOut = unchecked((uint)aArg + (uint)bArg + (cin ? 1u : 0u));
                    AluOut = newOut;
                    int aAfterReassign = (int)AluOut;
                    AluCarry = cin
                        ? (bArg >= ~aAfterReassign ? 0u : 1u)
                        : (bArg > ~aAfterReassign ? 0u : 1u);
                }
                break;
            case 41: // initial divide step (unconditional)
                (AluOut, AluCarry) = Sub32(MData, Abs32(AData), !cin);
                break;
        }
    }

    /// <summary>
    /// Q-register shift/load control, dispatched on Ir(0,2).
    /// </summary>
    internal void QControl()
    {
        OldQ = Q;
        switch (Ir(0, 2))
        {
            case 1:
                Q <<= 1;
                if ((AluOut & 0x80000000) == 0) Q |= 1;
                break;
            case 2:
                Q >>= 1;
                if ((AluOut & 1) != 0) Q |= 0x80000000;
                break;
            case 3:
                Q = AluOut;
                break;
        }
    }

    /// <summary>
    /// ALU-output routing/shift control, dispatched on bits 12-13 of P0 (raw,
    /// not via Ir() — matches the C source's direct bit extraction).
    /// </summary>
    internal void OutControl()
    {
        switch ((P0 >> 12) & 3)
        {
            case 0:
                TraceLog.Instance.Warning(TraceCategory.MicroCode, "OutControl: out == 0!");
                Out = Rol32((uint)MData, (int)(P0 & 0x1F));
                break;
            case 1:
                Out = AluOut;
                break;
            case 2:
                Out = (AluOut >> 1) | (AluCarry != 0 ? 0x80000000u : 0);
                break;
            case 3:
                Out = (AluOut << 1) | ((OldQ & 0x80000000) != 0 ? 1u : 0);
                break;
        }
    }

    #region ALU Operations
    #endregion

    #region Memory Access
    
    /// <summary>
    /// Read from A memory
    /// </summary>
    public uint ReadAMem(uint address)
    {
        address &= 0x3FF; // 10-bit address
        return AMem[address];
    }

    /// <summary>
    /// Write to A memory
    /// </summary>
    public void WriteAMem(uint address, uint value)
    {
        address &= 0x3FF;
        AMem[address] = value;
    }

    /// <summary>
    /// Read from M memory
    /// </summary>
    public uint ReadMMem(uint address)
    {
        address &= 0x1F; // 5-bit address
        return MMem[address];
    }

    /// <summary>
    /// Write to M memory
    /// </summary>
    public void WriteMMem(uint address, uint value)
    {
        address &= 0x1F;
        MMem[address] = value;
    }

    /// <summary>
    /// Read from D memory
    /// </summary>
    public uint ReadDMem(uint address)
    {
        address &= 0x7FF; // 11-bit address
        return DMem[address];
    }

    /// <summary>
    /// Write to D memory
    /// </summary>
    public void WriteDMem(uint address, uint value)
    {
        address &= 0x7FF;
        DMem[address] = value;
    }

    /// <summary>
    /// Push value onto PDL stack
    /// </summary>
    public void PushPdl(uint value)
    {
        PdlPointer = (PdlPointer + 1) & 0x3FF;
        Pdl[PdlPointer] = value;
    }

    /// <summary>
    /// Pop value from PDL stack
    /// </summary>
    public uint PopPdl()
    {
        uint value = Pdl[PdlPointer];
        PdlPointer = (PdlPointer - 1) & 0x3FF;
        return value;
    }

    /// <summary>
    /// Read PDL at offset from pointer (0 = top of stack, 1 = second from top, etc.)
    /// </summary>
    public uint ReadPdl(uint offset)
    {
        // In CADR, offset 0 means top of stack (current PdlPointer)
        // offset 1 means one back, offset 2 means two back, etc.
        uint address = (PdlPointer - offset) & 0x3FF;
        return Pdl[address];
    }
    
    #endregion
    
    #region Dispatch ROM
    
    // Dispatch ROM for opcode dispatch
    public ushort[] DispatchRom { get; } = new ushort[2048];
    public bool DispatchRomLoadedFlag { get; set; }

    /// <summary>
    /// Load dispatch ROM from file
    /// </summary>
    public void LoadDispatchRomFromFile(string filename)
    {
        using var stream = File.OpenRead(filename);
        for (int i = 0; i < DispatchRom.Length; i++)
        {
            if (stream.Position >= stream.Length)
                break;
                
            // Read 16-bit dispatch entry
            byte[] buffer = new byte[2];
            stream.Read(buffer, 0, 2);
            
            DispatchRom[i] = BitConverter.ToUInt16(buffer, 0);
        }
        
        DispatchRomLoadedFlag = true;
    }
    
    /// <summary>
    /// Perform dispatch operation
    /// </summary>
    public uint Dispatch(uint dispatchAddress, uint instruction)
    {
        // Combine dispatch address with instruction bits to form ROM address
        uint romAddress = (dispatchAddress & 0x7FF) | ((instruction & 0x0F) << 11);
        
        if (romAddress < DispatchRom.Length && DispatchRomLoadedFlag)
        {
            return DispatchRom[romAddress];
        }
        
        return 0;
    }
    
    #endregion
    
    #region Debug and Trace
    
    /// <summary>
    /// Enable/disable instruction tracing
    /// </summary>
    public bool InstructionTraceEnabled { get; set; }

    /// <summary>
    /// Enable/disable microcode tracing
    /// </summary>
    public bool MicrocodeTraceEnabled { get; set; }

    /// <summary>
    /// Maximum number of trace lines to keep
    /// </summary>
    public int MaxTraceLines { get; set; } = 10000;

    /// <summary>
    /// Trace buffer for instruction history
    /// </summary>
    private readonly System.Collections.Generic.Queue<string> _traceBuffer = new();

    /// <summary>
    /// Trace an instruction execution
    /// </summary>
    private void TraceInstruction(uint pc, bool useImem, ulong instruction, uint result)
    {
        if (!InstructionTraceEnabled && !MicrocodeTraceEnabled)
            return;
        
        string disasm = Disassembler.DisassembleInst2(instruction, useImem);
        string memType = useImem ? "IMEM" : (PromEnabledFlag ? "PROM" : "IMEM");
        
        string traceLine = $"[{MachineCycles:D10}] PC={pc:X4}({memType}) {disasm} => OUT=0x{result:X8} " +
                          $"FLAGS={FormatFlags()} " +
                          $"M={MData:X} A={AData:X}";
        
        // Output to console if enabled
        if (InstructionTraceEnabled)
        {
            Console.WriteLine(traceLine);
        }
        
        // Add to trace buffer
        if (MicrocodeTraceEnabled)
        {
            _traceBuffer.Enqueue(traceLine);
            
            // Limit buffer size
            while (_traceBuffer.Count > MaxTraceLines)
            {
                _traceBuffer.Dequeue();
            }
        }
        
        // Also log to TraceLog if available
        TraceLog.Instance?.Trace(TraceCategory.MicroCode, TraceLevel.Verbose, traceLine);
    }
    
    /// <summary>
    /// Format processor flags as string
    /// </summary>
    private string FormatFlags()
    {
        return $"CARRY={AluCarry}";
    }

    /// <summary>
    /// Convert bool to single character
    /// </summary>
    private char BoolToChar(bool value)
    {
        return value ? '1' : '0';
    }

    /// <summary>
    /// Get trace buffer contents
    /// </summary>
    public string[] GetTraceBuffer()
    {
        return _traceBuffer.ToArray();
    }

    /// <summary>
    /// Clear trace buffer
    /// </summary>
    public void ClearTraceBuffer()
    {
        _traceBuffer.Clear();
    }

    /// <summary>
    /// Dump trace buffer to console
    /// </summary>
    public void DumpTraceBuffer()
    {
        Console.WriteLine("=== Microcode Trace Buffer ===");
        Console.WriteLine($"Lines: {_traceBuffer.Count}");
        Console.WriteLine();

        foreach (var line in _traceBuffer)
        {
            Console.WriteLine(line);
        }
    }

    /// <summary>
    /// Save trace buffer to file
    /// </summary>
    public void SaveTraceBuffer(string filename)
    {
        try
        {
            System.IO.File.WriteAllLines(filename, _traceBuffer);
            Console.WriteLine($"Trace buffer saved to {filename}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving trace buffer: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get current instruction as string for debugging
    /// </summary>
    public string GetCurrentInstructionString()
    {
        if (Iwr != 0)
        {
            return Disassembler.DisassembleInst(Iwr);
        }
        return "[No instruction in IWR]";
    }

    /// <summary>
    /// Dump microcode state for debugging
    /// </summary>
    public void DumpState()
    {
        Console.WriteLine("=== Microcode State ===");
        Console.WriteLine($"Cycles: {MachineCycles}");
        Console.WriteLine($"PC: {Npc:X4}  OPC: {Opc:X4}");
        Console.WriteLine($"Out: {Out:X8}  Q: {Q:X8}");
        Console.WriteLine($"VMA: {VmaReg:X8}  MD: {MdReg:X8}");
        Console.WriteLine($"LC: {Lc:X8}");
        Console.WriteLine($"PDL Ptr: {PdlPointer:X3}  SPC Ptr: {SpcPtr:X2}");
        Console.WriteLine($"AluCarry: {AluCarry}");
        Console.WriteLine($"Interrupts: {(InterruptPendingFlag ? "PENDING" : "None")} (SR={InterruptStatusReg:X})");
        Console.WriteLine($"IWR: {GetCurrentInstructionString()}");
    }

    /// <summary>
    /// Dump A memory contents
    /// </summary>
    public void DumpAMem(uint start, uint count)
    {
        Console.WriteLine($"=== A Memory [{start:X3}..{start+count-1:X3}] ===");
        for (uint i = 0; i < count; i++)
        {
            uint addr = (start + i) & 0x3FF;
            if (i % 4 == 0)
                Console.Write($"{addr:X3}: ");
            Console.Write($"{AMem[addr]:X8} ");
            if (i % 4 == 3)
                Console.WriteLine();
        }
        if (count % 4 != 0)
            Console.WriteLine();
    }
    
    /// <summary>
    /// Dump M memory contents
    /// </summary>
    public void DumpMMem()
    {
        Console.WriteLine("=== M Memory ===");
        for (int i = 0; i < MMEM_SIZE; i++)
        {
            if (i % 4 == 0)
                Console.Write($"M[{i:X2}]: ");
            Console.Write($"{MMem[i]:X8} ");
            if (i % 4 == 3)
                Console.WriteLine();
        }
    }
    
    /// <summary>
    /// Dump PDL stack
    /// </summary>
    public void DumpPdlStack(int depth)
    {
        Console.WriteLine($"=== PDL Stack (top {depth}) ===");
        Console.WriteLine($"PDL Pointer: {PdlPointer:X3}");
        for (int i = 0; i < depth && i <= PdlPointer; i++)
        {
            uint addr = (PdlPointer - (uint)i) & 0x3FF;
            Console.WriteLine($"  [{i}] @{addr:X3}: {Pdl[addr]:X8}");
        }
    }
    
    #endregion
    
    #region Performance Statistics
    
    public ulong TotalInstructions { get; set; }
    public ulong TotalAluOps { get; set; }
    public ulong TotalMemoryAccesses { get; set; }
    public ulong TotalJumps { get; set; }
    public ulong TotalInterrupts { get; set; }

    /// <summary>
    /// Reset performance counters
    /// </summary>
    public void ResetStats()
    {
        TotalInstructions = 0;
        TotalAluOps = 0;
        TotalMemoryAccesses = 0;
        TotalJumps = 0;
        TotalInterrupts = 0;
    }
    
    /// <summary>
    /// Print performance statistics
    /// </summary>
    public void PrintStats()
    {
        Console.WriteLine("=== Performance Statistics ===");
        Console.WriteLine($"Total Cycles:          {MachineCycles:N0}");
        Console.WriteLine($"Total Instructions:    {TotalInstructions:N0}");
        Console.WriteLine($"Total ALU Operations:  {TotalAluOps:N0}");
        Console.WriteLine($"Total Memory Accesses: {TotalMemoryAccesses:N0}");
        Console.WriteLine($"Total Jumps:           {TotalJumps:N0}");
        Console.WriteLine($"Total Interrupts:      {TotalInterrupts:N0}");
        
        if (MachineCycles > 0)
        {
            double ipc = (double)TotalInstructions / MachineCycles;
            Console.WriteLine($"Instructions per Cycle: {ipc:F3}");
        }
    }
    
    #endregion
}
