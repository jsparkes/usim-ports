// DiagnosticInterface.cs - Faithful port of usim/diagnostic-interface.c (the
// CADR "spy" diagnostic interface, Unibus 0766000-0766036 octal). Ports the
// NDEBUG (release) build: the usim-specific debug-only main-memory dump
// commands at 0766020-0766026 are compiled out of the real release binary,
// so those writes fall through to "invalid uaddr" there, and here.

using System;

namespace Usim;

public class DiagnosticInterface
{
    // Unibus addresses, octal -> hex independently computed (Python):
    // 0766000=0x3EC00 ... 0766036=0x3EC1E, step 2.
    private const uint IrLow = 0x3EC00;           // 0766000 IR<15-0>
    private const uint IrMid = 0x3EC02;           // 0766002 IR<31-16>
    private const uint IrHigh = 0x3EC04;          // 0766004 IR<47-32>
    private const uint ClockControl = 0x3EC06;    // 0766006
    private const uint Opc = 0x3EC08;             // 0766010
    private const uint ModeOrPc = 0x3EC0A;        // 0766012 (read: PC, write: mode register)
    private const uint ObLow = 0x3EC0C;           // 0766014
    private const uint ObHigh = 0x3EC0E;          // 0766016
    private const uint Flag1 = 0x3EC10;           // 0766020
    private const uint Flag2 = 0x3EC12;           // 0766022
    private const uint MLow = 0x3EC14;            // 0766024
    private const uint MHigh = 0x3EC16;           // 0766026
    private const uint ALow = 0x3EC18;            // 0766030
    private const uint AHigh = 0x3EC1A;           // 0766032
    private const uint StatCounterLow = 0x3EC1C;  // 0766034
    private const uint StatCounterHigh = 0x3EC1E; // 0766036

    private readonly UCode _ucode;
    private readonly BusInterface _busInterface;

    /// <summary>Last value written through the DEBUG-IR registers (never read back by the real C either).</summary>
    public ulong DebugIr { get; private set; }

    public DiagnosticInterface(UCode ucode, BusInterface busInterface)
    {
        _ucode = ucode;
        _busInterface = busInterface;
    }

    // Names with _ are active-low in the C; usim cannot produce any of these
    // error conditions, so the register is effectively constant.
    private static ushort GetFlagRegister1()
    {
        const bool _wait = true;     // waiting for memory, never happens
        const bool _v1pe = true;     // level-2 map parity error, never happens
        const bool _v0pe = true;     // level-1 map parity error, never happens
        const bool highok = true;
        const bool stathalt = false; // no statistics counter
        const bool err = false;
        const bool ssdone = false;   // no single stepping
        const bool srun = false;
        const bool _higherr = true;
        const bool _mempe = true;
        const bool _ipe = true;
        const bool _dpe = true;
        const bool _spe = true;
        const bool _pdlpe = true;
        const bool _mpe = true;
        const bool _ape = true;

        ushort reg = 0;
        if (_wait) reg |= 0x8000;
        if (_v1pe) reg |= 0x4000;
        if (_v0pe) reg |= 0x2000;
        if (highok) reg |= 0x1000;
        if (!stathalt) reg |= 0x0800;
        if (err) reg |= 0x0400;
        if (ssdone) reg |= 0x0200;
        if (srun) reg |= 0x0100;
        if (_higherr) reg |= 0x0080;
        if (_mempe) reg |= 0x0040;
        if (_ipe) reg |= 0x0020;
        if (_dpe) reg |= 0x0010;
        if (_spe) reg |= 0x0008;
        if (_pdlpe) reg |= 0x0004;
        if (_mpe) reg |= 0x0002;
        if (_ape) reg |= 0x0001;
        return reg;
    }

    // These wires (map-write, dest-space, I-write/mod, PDL-write/push, the
    // IR parity check, nop and jump-condition detect) all come from
    // microcode-sequencer hardware this port doesn't implement, so they are
    // hardcoded false here, matching the real C (diagnostic-interface.c's
    // get_flag_register_2), which never sets them either.
    private ushort GetFlagRegister2()
    {
        bool wmapd = false;
        bool destspcd = false;
        bool iwrited = false;
        bool imodd = false;
        bool pdlwrited = false;
        bool spushd = false;
        const bool ir_parity = false;
        bool nop = false;
        bool vmaok = _ucode.VmaOk;
        bool jcond = false;
        const uint pcs = 0;

        ushort reg = 0;
        if (wmapd) reg |= 0x2000;
        if (destspcd) reg |= 0x1000;
        if (iwrited) reg |= 0x0800;
        if (imodd) reg |= 0x0400;
        if (pdlwrited) reg |= 0x0200;
        if (spushd) reg |= 0x0100;
        if (ir_parity) reg |= 0x0020;
        if (nop) reg |= 0x0010;
        if (vmaok) reg |= 0x0008;
        if (jcond) reg |= 0x0004;
        reg |= (ushort)pcs;
        return reg;
    }

    private static string Oct(ulong v) => Convert.ToString((long)v, 8);

    private static void Spy(string message) =>
        TraceLog.Instance.Trace(TraceCategory.Spy, TraceLevel.Info, "spy: " + message);

    /// <summary>
    /// Port of diagnostic_interface_read. An address in the range that is not
    /// a real register sets Unibus NXM and reads 0. The statistics counter is
    /// fatal in the real C (errx), so it throws here.
    /// </summary>
    public ushort Read(uint uaddr)
    {
        ulong p0 = _ucode.P0;
        uint mdata = (uint)_ucode.MData;
        uint adata = (uint)_ucode.AData;
        ushort v;
        switch (uaddr)
        {
            case IrLow:
                v = (ushort)(p0 & 0xFFFF);
                Spy($"read IR<15-0>: {Oct(v)} IR={Oct(p0)}");
                return v;
            case IrMid:
                v = (ushort)((p0 >> 16) & 0xFFFF);
                Spy($"read IR<31-16>: {Oct(v)} IR={Oct(p0)}");
                return v;
            case IrHigh:
                v = (ushort)((p0 >> 32) & 0xFFFF);
                Spy($"read IR<47-32>: {Oct(v)} IR={Oct(p0)}");
                return v;
            case ClockControl: // not used
                // The real C leaves *pv uninitialized here (undefined
                // behavior); returning a defined 0 is a deliberate choice
                // this port makes instead.
                return 0;
            case Opc:
                v = (ushort)_ucode.Opc;
                Spy($"read OPC: {Oct(v)}");
                return v;
            case ModeOrPc:
                v = (ushort)_ucode.Npc;
                Spy($"read PC: {Oct(v)}");
                return v;
            case ObLow:
                v = (ushort)(_ucode.Out & 0xFFFF);
                Spy($"read OB<15-0>: {Oct(v)} OB={Oct(_ucode.Out)}");
                return v;
            case ObHigh:
                v = (ushort)((_ucode.Out >> 16) & 0xFFFF);
                Spy($"read OB<31-16>: {Oct(v)} OB={Oct(_ucode.Out)}");
                return v;
            case Flag1:
                v = GetFlagRegister1();
                Spy($"read flag register 1: 0x{v:x4}");
                return v;
            case Flag2:
                v = GetFlagRegister2();
                Spy($"read flag register 2: 0x{v:x4}");
                return v;
            case MLow:
                v = (ushort)(mdata & 0xFFFF);
                Spy($"read M<15-0>: {Oct(v)} M={Oct(mdata)}");
                return v;
            case MHigh:
                v = (ushort)((mdata >> 16) & 0xFFFF);
                Spy($"read M<31-16>: {Oct(v)} M={Oct(mdata)}");
                return v;
            case ALow:
                v = (ushort)(adata & 0xFFFF);
                Spy($"read A<15-0>: {Oct(v)} A={Oct(adata)}");
                return v;
            case AHigh:
                v = (ushort)((adata >> 16) & 0xFFFF);
                Spy($"read A<31-16>: {Oct(v)} A={Oct(adata)}");
                return v;
            case StatCounterLow:
            case StatCounterHigh:
                throw new InvalidOperationException("Statistics counter is not implemented");
            default:
                // As above: the real C leaves *pv uninitialized for an
                // invalid address; 0 here is a deliberate, defined choice.
                TraceLog.Instance.Warning(TraceCategory.Spy, $"spy: read invalid uaddr:{Oct(uaddr)}");
                _busInterface.SetUnibusNxm();
                return 0;
        }
    }

    /// <summary>
    /// Port of diagnostic_interface_write. promDisabled is UCode's own
    /// PromDisabled (threaded through BusAdaptor.Write), which the mode
    /// register sets (machine_state.promdisabled in the C). The real C errx()s
    /// on a clock-control write other than 1 and on any OPC-control write;
    /// those throw here.
    /// </summary>
    public void Write(uint uaddr, ushort v, ref bool promDisabled)
    {
        switch (uaddr)
        {
            case IrLow:
                Spy($"write DEBUG-IR<15-0>: {Oct(v)}");
                DebugIr = (DebugIr & 0xFFFFFFFFFFFF0000UL) | v;
                break;
            case IrMid:
                Spy($"write DEBUG-IR<31-16>: {Oct(v)}");
                DebugIr = (DebugIr & 0xFFFFFFFF0000FFFFUL) | ((ulong)v << 16);
                break;
            case IrHigh:
                Spy($"write DEBUG-IR<47-32>: {Oct(v)}");
                DebugIr = (DebugIr & 0xFFFF0000FFFFFFFFUL) | ((ulong)v << 32);
                Spy($"DEBUG-IR<47-0>: {Oct(DebugIr)} {Disassembler.DisassembleInst(DebugIr)}");
                break;
            case ClockControl:
                Spy($"write clock control register: 0{Oct(v)}");
                if (v != 1)
                {
                    throw new InvalidOperationException("Clock control register can only be loaded with 1 (RUN)");
                }
                break;
            case Opc:
                Spy($"write OPC control register: 0{Oct(v)}");
                throw new InvalidOperationException("OPC control register should not be touched");
            case ModeOrPc:
                Spy($"write mode register: 0{Oct(v)}");
                promDisabled = (v & (1 << 5)) != 0;
                break;
            case ObLow: // not used
            case ObHigh: // not used
                break;
            default:
                TraceLog.Instance.Warning(TraceCategory.Spy, $"spy: write invalid uaddr:{Oct(uaddr)}");
                _busInterface.SetUnibusNxm();
                break;
        }
    }
}
