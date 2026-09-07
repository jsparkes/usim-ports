// BusInterface.cs - Faithful port of usim/bus-interface.c's bus-error-status
// and interrupt-register block. Deliberately excludes the "lashup" remote-
// debugger protocol (a serial/socket link to a SECOND, PHYSICAL CADR machine
// acting as a debuggee) -- a software-only emulator has no second machine to
// talk to, so every lashup-only register becomes a logged no-op (or, for
// 0766104, a faithfully-always-0 read: with no debuggee ever attached, 0 IS
// the real C's correct answer, not a compromise). See
// docs/superpowers/specs/2026-09-07-bus-interface-design.md for the full
// register-by-register rationale.

using System;

namespace Usim;

public class BusInterface
{
    private readonly UCode _ucode;

    // Mirrors the real C's file-scope statics (usim/bus-interface.c:26-45).
    // modifier_reset and debuggee_bus_error_status are lashup-only state --
    // omitted, since every call site that would read or write them is
    // already a no-op in this port.
    private ushort _busErrorStatus;
    private bool _nxmInhibited;
    private bool _addr17;
    private ushort _addr;

    public BusInterface(UCode ucode)
    {
        _ucode = ucode;
    }

    // usim/bus-interface.c:53-57
    public ushort GetBusErrorStatus() => _busErrorStatus;

    // usim/bus-interface.c:59-75. Masks independently re-verified via
    // script: Xbus NXM = octal 01 = 0x1, Unibus NXM = octal 010 = 0x8,
    // Unibus Map Error = octal 040 = 0x20.
    public bool IsXbusNxm() => (_busErrorStatus & 0x1) != 0;
    public bool IsUnibusNxm() => (_busErrorStatus & 0x8) != 0;
    public bool IsUnibusMapError() => (_busErrorStatus & 0x20) != 0;

    // usim/bus-interface.c:77-81
    public void ResetBusErrorStatus() => _busErrorStatus = 0;

    // usim/bus-interface.c:89-93
    public void SetNxmInhibit(bool inhibit) => _nxmInhibited = inhibit;

    // usim/bus-interface.c:95-103. Gated by nxm_inhibited.
    public void SetXbusNxm()
    {
        if (_nxmInhibited) return;
        _busErrorStatus |= 0x1;
    }

    // usim/bus-interface.c:106-114. Gated by nxm_inhibited.
    public void SetUnibusNxm()
    {
        if (_nxmInhibited) return;
        _busErrorStatus |= 0x8;
    }

    // usim/bus-interface.c:116-123. NOT gated by nxm_inhibited -- this
    // asymmetry with SetXbusNxm/SetUnibusNxm is in the real C as written.
    public void SetUnibusMapError()
    {
        _busErrorStatus |= 0x20;
    }

    /// <summary>
    /// Faithful port of bus_interface_read (usim/bus-interface.c:125-171),
    /// minus lashup. uaddr is the Unibus word address, matching BusAdaptor's
    /// ReadUnibus/WriteUnibus convention.
    /// </summary>
    public uint Read(uint uaddr)
    {
        switch (uaddr)
        {
            case 0x3EC20: // 0766040 octal
                return (uint)_ucode.InterruptStatusReg;

            // 0766042 is write-only in the real C -- falls to default below.

            case 0x3EC24: // 0766044 octal
                TraceLog.Instance.Debug(TraceCategory.Memory,
                    $"bus-interface: read bus status: {Convert.ToString(_busErrorStatus, 8)}");
                return _busErrorStatus;

            case 0x3EC40: // 0766100 octal
                // Real C attempts a lashup read of the debuggee's bus; no
                // debuggee exists here.
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    "bus-interface: read data -- no debuggee attached (lashup not ported)");
                return 0;

            case 0x3EC44: // 0766104 octal
                // Debuggee's mirrored bus-error status (lashup-only). Always
                // 0 here -- the real, correct answer with no debuggee ever
                // attached, not a placeholder.
                return 0;

            default:
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"bus-interface: read invalid uaddr:{Convert.ToString((int)uaddr, 8)}");
                SetUnibusNxm();
                return 0;
        }
    }

    /// <summary>
    /// Faithful port of bus_interface_write (usim/bus-interface.c:173-292),
    /// minus lashup.
    /// </summary>
    public void Write(uint uaddr, uint v)
    {
        switch (uaddr)
        {
            case 0x3EC20: // 0766040 octal
                // "Writing this location writes into bits 0 and 10-13 (mask
                // 36001)." Octal 036001 = 0x3C01 -- independently verified
                // via script; a naive hand-conversion gives the wrong
                // 0xF001 (bits 0, 12-15).
                _ucode.SetInterruptStatusReg((_ucode.InterruptStatusReg & ~0x3C01) | ((int)v & 0x3C01));
                break;

            case 0x3EC22: // 0766042 octal
                // "Writing this location writes into bits 2-9 and 15 (mask
                // 101774)." Octal 0101774 = 0x83FC.
                _ucode.SetInterruptStatusReg((_ucode.InterruptStatusReg & ~0x83FC) | ((int)v & 0x83FC));
                break;

            case 0x3EC24: // 0766044 octal
                TraceLog.Instance.Debug(TraceCategory.Memory,
                    "bus-interface: write (clear) bus status");
                _busErrorStatus = 0;
                break;

            case 0x3EC40: // 0766100 octal
                // Real C writes to the debuggee's bus over lashup; no-op here.
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"bus-interface: write data 0x{v:X} -- no debuggee attached (lashup not ported)");
                break;

            case 0x3EC42: // 0766102 octal
                // Remote usim command over lashup; no-op here.
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"bus-interface: remote usim command 0x{v:X} -- no debuggee attached (lashup not ported)");
                break;

            case 0x3EC48: // 0766110 octal
                // Modifier bits. Only addr17 (bit 0) has any meaning without
                // lashup (it's part of the debuggee-target-address
                // computation, itself only consumed by the 0766100 lashup
                // path) -- captured anyway as harmless bookkeeping. Every
                // other bit (reset, timeout-inhibit, debugger/debuggee mark,
                // ping) drives a lashup_debugger_* call in the real C; all
                // become logged no-ops here.
                _addr17 = (v & 0x1) != 0;
                TraceLog.Instance.Debug(TraceCategory.Memory,
                    $"bus-interface: write modifier bits 0x{v:X} (addr17={_addr17}) -- reset/timeout-inhibit/mark/ping are lashup-only, no-op");
                break;

            case 0x3EC4A: // 0766112 octal
                // Local usim command -- already a log-only no-op in the real
                // C itself (its cmd/param params are marked unused there).
                TraceLog.Instance.Debug(TraceCategory.Memory,
                    $"bus-interface: local usim command 0x{v:X}");
                break;

            case 0x3EC4C: // 0766114 octal
                _addr = (ushort)v;
                TraceLog.Instance.Debug(TraceCategory.Memory,
                    $"bus-interface: write address: 0x{v:X}");
                break;

            default:
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"bus-interface: write invalid uaddr:{Convert.ToString((int)uaddr, 8)}");
                SetUnibusNxm();
                break;
        }
    }

    /// <summary>
    /// Faithful port of bus_interface_bus_reset (usim/bus-interface.c:294-312),
    /// minus lashup state. The real C's fan-out to iob_bus_reset()/
    /// main_memory_bus_reset()/disk_controller_bus_reset() is a genuine no-op
    /// even there (all three are empty function bodies) -- nothing to call.
    /// tape_controller_bus_reset()/tv_bus_reset() would do something (clear a
    /// status struct; the latter is empty too) but neither TapeController nor
    /// TV exist in C# yet -- logged as a placeholder note, not silently
    /// dropped.
    /// </summary>
    public void BusReset()
    {
        _busErrorStatus = 0;
        _nxmInhibited = false;
        _addr17 = false;
        _addr = 0;

        TraceLog.Instance.Info(TraceCategory.Memory,
            "bus-interface: bus reset (tape-controller/tv bus_reset not yet ported -- no-op)");
    }
}
