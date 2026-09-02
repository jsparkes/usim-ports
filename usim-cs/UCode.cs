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

    private readonly MainMemory _mainMemory;
    public Uvmem Uvmem { get; }
    public BusAdaptor BusAdaptor { get; }

    public UCode() : this(new MainMemory()) { }

    public UCode(MainMemory mainMemory)
    {
        _mainMemory = mainMemory;
        Uvmem = new Uvmem();
        BusAdaptor = new BusAdaptor();
    }

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
        Halted = false;
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
            // Same fragment as Jmp()/Dsp() -- see Dsp()'s comment.
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

    /// <summary>
    /// Faithful port of mfread() (usim/uexec.c:233-304). Code 9 (MEMORY-MAP-DATA)
    /// uses Uvmem.Vtop, as of Phase 5. Codes 1/12's mask is 0x7FFFF (19 bits, from the C's literal
    /// octal 01777777) -- NOT 0x1FFFFF (21 bits) as an earlier draft of the spec
    /// mistranslated; re-derived and hand-verified against the literal digits.
    /// </summary>
    internal int MfRead(uint addr)
    {
        switch (addr & 0x1F)
        {
            case 0: return (int)DispatchConstant;
            case 1: return (int)((SpcPtr << 24) | (Spc[SpcPtr] & 0x7FFFF));
            case 2: return (int)(PdlPointer & 0x3FF);
            case 3: return (int)(PdlIndex & 0x3FF);
            case 5: return (int)Pdl[PdlIndex];
            case 6: return (int)Opc;
            case 7: return (int)Q;
            case 8: return (int)VmaReg;
            case 9:
            {
                _ = Uvmem.Vtop(MdReg, out uint l1_9, out uint l2_9, out _, out bool wp9, out bool ap9);
                return (int)((!wp9 ? (1u << 31) : 0) | (!ap9 ? (1u << 30) : 0) | (1u << 29) | ((l1_9 & 0x1F) << 24) | (l2_9 & 0x00FFFFFF));
            }
            case 10: return (int)MdReg;
            case 11: return (int)((InterruptControl & (1 << 29)) != 0 ? Lc : Lc & ~1u);
            case 12:
            {
                int res = (int)((SpcPtr << 24) | (Spc[SpcPtr] & 0x7FFFF));
                SpcPtr = (SpcPtr - 1) & 0x1F;
                return res;
            }
            case 13: return 0; // placeholder, matches the real C's own "???" comment
            case 20:
            {
                int res = (int)Pdl[PdlPointer];
                PdlPointer = (PdlPointer - 1) & 0x3FF;
                return res;
            }
            case 21: return (int)Pdl[PdlPointer];
            case 22: return 0; // placeholder, matches the real C's own "???" comment
            default:
                // .NET has no built-in octal format specifier (unlike the C's %o) --
                // hex is used here purely for a readable diagnostic message; this has
                // no bearing on emulation behavior, only on the exception's text.
                throw new InvalidOperationException($"unknown MF register (0x{addr:X}) read"); // matches the C's fatal err()
        }
    }

    /// <summary>
    /// Faithful port of mfwrite() (usim/uexec.c:306-459). Codes 18/26 call
    /// VmWrite, which as of Phase 5B routes through BusAdaptor for real for
    /// anything outside the "xbus main memory" address range (see Vm()) --
    /// BusAdaptor itself still has its own deliberately-scoped placeholders
    /// for most devices (see its class doc comment), so this is no longer a
    /// blind no-op, just not a full device port. Codes 19/27 call the
    /// now-real Uvmem.WriteMap. Code 2's bit-28 bus-reset
    /// is a no-op + Info log -- the real
    /// bus_interface_bus_reset() lives in a wholly separate, not-yet-ported
    /// subsystem (usim/bus-interface.c). Note: the real C's comment on this
    /// case claims to detect a "1-0 transition", but the actual code just
    /// checks whether bit 28 is set on THIS write -- ported the code, not
    /// the comment, per this project's established practice.
    /// </summary>
    internal void MfWrite(uint dest, int data)
    {
        uint udata = (uint)data;
        switch (dest >> 5)
        {
            case 0:
                return;
            case 1:
                Lc = (Lc & ~0x03FFFFFFu) | (udata & 0x03FFFFFFu);
                if ((InterruptControl & (1 << 29)) == 0)
                {
                    Lc &= ~1u;
                }
                Lc |= (1u << 31);
                return;
            case 2:
                InterruptControl = udata;
                if ((InterruptControl & (1 << 28)) != 0)
                {
                    TraceLog.Instance.Info(TraceCategory.MicroCode, "usim: ic.bus reset");
                }
                Lc = (Lc & ~(0xFu << 26)) | (InterruptControl & (0xFu << 26));
                return;
            case 8:
                Pdl[PdlPointer] = udata;
                return;
            case 9:
                PdlPointer = (PdlPointer + 1) & 0x3FF;
                Pdl[PdlPointer] = udata;
                return;
            case 10:
                Pdl[PdlIndex] = udata;
                return;
            case 11:
                PdlIndex = udata & 0x3FF;
                return;
            case 12:
                PdlPointer = udata & 0x3FF;
                return;
            case 13:
                PushSpc(udata);
                return;
            case 14:
                OaRegLow = udata & 0x03FFFFFF;
                Oal = true;
                return;
            case 15:
                OaRegHigh = udata & 0x7FFFFF;
                Oah = true;
                return;
            case 16:
                VmaReg = udata;
                return;
            case 17:
                VmaReg = udata;
                VmRead(VmaReg, out uint newMd17);
                NewMd = newMd17;
                NewMdDelay = 2;
                return;
            case 18:
                VmaReg = udata;
                VmWrite(VmaReg, MdReg);
                return;
            case 19:
                VmaReg = udata;
                Uvmem.WriteMap(VmaReg, MdReg);
                return;
            case 24:
                MdReg = udata;
                return;
            case 25:
                MdReg = udata;
                VmRead(VmaReg, out uint newMd25);
                NewMd = newMd25;
                NewMdDelay = 2;
                return;
            case 26:
                MdReg = udata;
                VmWrite(VmaReg, MdReg);
                return;
            case 27:
                MdReg = udata;
                Uvmem.WriteMap(VmaReg, MdReg);
                return;
            default:
                // Hex, not octal, for the same reason noted in MfRead's default case --
                // .NET has no built-in octal format specifier; this is diagnostic text only.
                TraceLog.Instance.Warning(TraceCategory.MicroCode, $"unknown MF register (0x{dest:X}) write (0x{data:X})");
                return;
        }
    }

    private void Alu()
    {
        uint dest = (uint)Ir(14, 12);
        uint aluop = (uint)Ir(3, 6);
        AluCarry = 0;

        if (aluop <= 15) LogiOps(aluop);
        else if (aluop >= 16 && aluop <= 31) ArithOps(aluop);
        else if (aluop == 32 || aluop == 33 || aluop == 37 || aluop == 41) DivOps(aluop);

        QControl();
        OutControl();
        WriteDest(dest);
    }

    /// <summary>
    /// Fixed jump-condition codes (Ir(0,4) when Ir(5,1) != 0), or a rotate-
    /// and-test-bit-0 mode when Ir(5,1) == 0. Faithful port of check_jcond()
    /// (usim/uexec.c:858-892) — note the bit-test mode mutates MData as a
    /// side effect (it really does rotate MData in place in the real C too).
    /// </summary>
    internal bool CheckJumpCondition()
    {
        if (Ir(5, 1) == 0)
        {
            int rot = (int)Ir(0, 5);
            MData = (int)Rol32((uint)MData, rot);
            return (MData & 1) != 0;
        }
        return Ir(0, 4) switch
        {
            1 => MData < AData,
            2 => MData <= AData,
            3 => MData == AData,
            4 => !VmaOk,
            5 => !VmaOk || (((InterruptControl & (1 << 27)) != 0) && InterruptPendingFlag),
            6 => !VmaOk || (((InterruptControl & (1 << 27)) != 0) && InterruptPendingFlag) || (InterruptControl & (1 << 26)) != 0,
            7 => true,
            _ => throw new InvalidOperationException($"unknown jump condition {Ir(0, 4)}"), // includes code 0, matching the C's fall-through-to-err()
        };
    }

    /// <summary>
    /// Faithful port of jmp() (usim/uexec.c:894-953). Note ILLOP sets
    /// Halted but does NOT return early — the real C continues on to
    /// evaluate the jump condition and can still branch afterward. Port
    /// this exactly; it is surprising but real reference behavior, not a
    /// bug to "fix".
    /// </summary>
    private void Jmp()
    {
        uint target = (uint)Ir(12, 14);
        bool r = Ir(9, 1) != 0, p = Ir(8, 1) != 0, n = Ir(7, 1) != 0;
        bool invertSense = Ir(6, 1) != 0;

        if (Ir(10, 2) == 1)
        {
            TraceLog.Instance.Info(TraceCategory.MicroCode, "usim: illop, asserting halted");
            Halted = true;
        }
        if (Ir(10, 2) == 3)
        {
            TraceLog.Instance.Warning(TraceCategory.MicroCode, "jump w/misc-3!");
        }

        if (p && r)
        {
            IMem[target] = Iwr;
            return;
        }

        bool cond = CheckJumpCondition();
        if (invertSense) cond = !cond;

        if (p && cond)
        {
            if (!n) PushSpc(Npc); else PushSpc(Npc - 1);
        }
        if (r && cond)
        {
            // Same fragment as Dsp() and Step()'s popj block -- see Dsp()'s comment.
            target = PopSpc();
            if ((target >> 14 & 1) != 0) target = AdvanceLc(target);
            target &= 0x3FFF;
        }
        if (cond)
        {
            if (n) Inhibit = true;
            Npc = target;
            Popj = false;
        }
    }

    /// <summary>
    /// Test-only forwarding wrapper: Jmp() itself stays private (matching
    /// Alu()'s existing visibility — the per-instruction-class dispatcher
    /// is only ever meant to be reached through Step()), but UCodeJumpTests
    /// needs to exercise its many branch combinations directly without
    /// going through Step()'s full fetch/decode pipeline.
    /// </summary>
    internal void CallJmp() => Jmp();

    /// <summary>
    /// Faithful port of dsp() (usim/uexec.c:740-854). Independently re-verified
    /// field-by-field against the real C during Phase 6 planning -- no bugs
    /// found (unlike Phases 2/4/5/5B, which each had at least one). Shares
    /// PushSpc/PopSpc/AdvanceLc/Inhibit/Popj machinery with Jmp() (Phase 3).
    /// </summary>
    private void Dsp()
    {
        uint dispAddr = (uint)Ir(12, 11);
        if (Ir(10, 2) == 2) { DMem[dispAddr] = (uint)AData; return; }

        int pos = (int)Ir(0, 5);
        if (Ir(10, 2) == 3) pos = LcByteMode();

        MData = (int)Rol32((uint)MData, pos);

        int len = (int)Ir(5, 3);
        int leftMaskIndex = (len - 1) & 0x1F;
        int mask = len == 0 ? 0 : unchecked((int)(~0u >> (31 - leftMaskIndex)));
        dispAddr |= (uint)MData & (uint)mask;

        uint map = (uint)Ir(8, 2);
        if (map != 0)
        {
            Uvmem.Vtop(MdReg, out _, out uint l2MapBits, out _, out _, out _);
            uint bit19 = (l2MapBits >> 19) & 1, bit18 = (l2MapBits >> 18) & 1;
            // map==3 never appears in either sys/ubin/promh.mcr or sys/ucadr/promh.mcr's
            // real microcode (only 0/1/2 do) -- this arm is verified against
            // usim/uexec.c's logic, not against real usage.
            dispAddr |= map switch { 1 => bit18, 2 => bit19, 3 => bit18 | bit19, _ => 0 };
        }

        dispAddr &= 0x7FF;
        uint dispWord = DMem[dispAddr];
        DispatchConstant = (uint)Ir(32, 10);

        uint target = dispWord & 0x3FFF;
        bool n = ((dispWord >> 14) & 1) != 0, p = ((dispWord >> 15) & 1) != 0, r = ((dispWord >> 16) & 1) != 0;

        // Unmasked decrement, matching C's raw npc-- on a uint32_t. If Npc is ever 0
        // here (not reachable from real microcode), this underflows to uint.MaxValue
        // and the next Prom/IMem access throws IndexOutOfRangeException where C would
        // silently read out of bounds -- expected/faithful, not a new bug.
        if (Ir(25, 1) != 0 && n) Npc--;
        if (Ir(24, 1) != 0) AdvanceLc(0);
        if (n) Inhibit = true;
        if (p && r) return;

        if (p) { if (!n) PushSpc(Npc); else PushSpc(Npc - 1); }
        if (r)
        {
            // Same pop+AdvanceLc+mask fragment as Jmp() and Step()'s popj block below --
            // intentional, faithful triplication matching usim/uexec.c's own repetition;
            // do not unify (each site's surrounding control flow differs).
            target = PopSpc();
            if ((target >> 14 & 1) != 0) target = AdvanceLc(target);
            target &= 0x3FFF;
        }
        Npc = target;
        Popj = false;
    }

    /// <summary>
    /// Test-only forwarding wrapper: Dsp() stays private (matching Alu()/Jmp()'s
    /// existing visibility), but UCodeDispatchTests needs to exercise its many
    /// branch combinations directly. Matches the CallJmp()/CallVm() precedent
    /// from Phases 3 and 5.
    /// </summary>
    internal void CallDsp() => Dsp();

    private void Byt()
    {
        throw new NotImplementedException("Byt is implemented in Phase 7 (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md)");
    }

    /// <summary>
    /// VMA-ok state for the current cycle. Set for real by Vm() (Phase 5) from
    /// Uvmem's permission bits; defaults to true ("no page fault") before the
    /// first cycle that calls Vm() (e.g. via AdvanceLc or MfWrite).
    /// </summary>
    public bool VmaOk { get; set; } = true;

    /// <summary>
    /// Set by Jmp()'s ILLOP handling (matches the C's machine_state.halted).
    /// This class has no reference to MachineControl's richer power-state
    /// machinery — wiring this flag to an actual run-loop stop condition is
    /// an integration concern outside this phase's scope.
    /// </summary>
    public bool Halted { get; set; }

    /// <summary>
    /// Faithful port of the virtual-memory-resolution part of vm()
    /// (usim/uvmem.c:172-228). Sets VmaOk from Uvmem's permission bits;
    /// on a fault, reads return 0 and writes are discarded (matching the
    /// real C's *pv=0 on read). For an address that resolves within the
    /// "xbus main memory" range (physical page number &lt;= 0x3BFB -- verified
    /// against usim/bus-adaptor.c's bus_adaptor_xbus_rw, whose own pn&lt;=035773
    /// branch is a bare pass-through to real main memory), reads/writes go
    /// through MainMemory's physical-address accessors for real. Anything
    /// else (XBus I/O devices, Unibus) is routed through BusAdaptor (Phase
    /// 5B), which itself implements the boot PROM's disk-control status and
    /// diagnostic-mode-register accesses for real and keeps its own
    /// deliberately-scoped, non-fatal placeholders for every other device.
    /// </summary>
    private void Vm(bool write, uint vaddr, ref uint v)
    {
        vaddr &= 0x00FFFFFF;
        uint paddr = Uvmem.Vtop(vaddr, out _, out _, out uint pn, out bool wp, out bool ap);
        VmaOk = write ? (ap && wp) : ap;
        if (!VmaOk) { v = 0; return; }

        // TV-screen quirk: this line is the WORKAROUND, not the bug -- usim/uvmem.c's
        // vm() comments on a symptom in the plain (pn<<8)|(vaddr&0xFF) formula for
        // this one page ("this is not working for the access below... no idea why"),
        // and this override is what actually produces the correct address (verified:
        // it reproduces the C comment's own worked "actual paddr should be
        // 17'051'765" example exactly). Do not delete this as dead/broken code.
        // 036000 octal = 0x3C00 (NOT 0x1E00) and 017000000 octal = 0x3C0000 (NOT
        // 0x0F00000) -- both corrected from an earlier draft of this spec; re-derived
        // by direct computation, not manual octal-digit counting.
        if (pn == 0x3C00) paddr = 0x3C0000 | (vaddr & 0x7FFF);

        // The real C dispatches through bus_adaptor_read/write, which re-derives its
        // OWN page number from the (possibly quirk-overridden) paddr, not from Vtop's
        // original pn -- mirrored here. The 0x3BFC-0x3BFF range (the real C's
        // assert(false)-guarded dead branch) is folded into the "not main memory"
        // placeholder below, which is a safe superset for it.
        uint dispatchPn = (paddr >> 8) & 0x3FFF;
        if (dispatchPn <= 0x3BFB)
        {
            if (write) _mainMemory.WritePhysical(paddr, v);
            else v = _mainMemory.ReadPhysical(paddr);
        }
        else
        {
            bool promDisabled = PromDisabled;
            if (write) BusAdaptor.Write(paddr, v, ref promDisabled);
            else v = BusAdaptor.Read(paddr);
            PromDisabled = promDisabled;
        }
    }

    private void VmRead(uint vaddr, out uint v) { v = 0; Vm(false, vaddr, ref v); }
    private void VmWrite(uint vaddr, uint data) { uint v = data; Vm(true, vaddr, ref v); }

    /// <summary>
    /// Test-only forwarding wrapper: Vm() stays private (matching Jmp()'s
    /// visibility -- only reachable through VmRead/VmWrite in production),
    /// but UCodeVirtualMemoryTests needs to exercise its branch combinations
    /// directly. Matches the CallJmp() precedent from Phase 3.
    /// </summary>
    internal void CallVm(bool write, uint vaddr, ref uint v) => Vm(write, vaddr, ref v);

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

    #region ALU Operations

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

    /// <summary>
    /// Route the ALU/byte-instruction result to its destination: plain
    /// A-memory (if dest bit 11 is set) or a functional register via
    /// MfWrite, plus the low-5-bit-addressed MMem/AMem shadow copies.
    /// </summary>
    internal void WriteDest(uint dest)
    {
        if ((dest & 0x800) != 0)
        {
            AMem[dest & 0x3FF] = Out;
        }
        else
        {
            MfWrite(dest, (int)Out);
            MMem[dest & 0x1F] = AMem[dest & 0x1F] = Out;
        }
    }

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
