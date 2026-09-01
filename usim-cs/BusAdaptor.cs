// BusAdaptor.cs - Faithful (but deliberately scoped) port of usim/bus-adaptor.c's
// XBus-I/O and Unibus device routing. UCode.Vm() already handles the "xbus main
// memory" range (physical page number <= 0x3BFB) for real; this class handles
// everything above that -- the boot PROM's disk-control and diagnostic-register
// accesses, plus non-fatal placeholders for every other device bus-adaptor.c
// would route to (TV, color TV, tape, Unibus Map DMA, IOB, unibus-mapping,
// bus-interface). NOT a full device-emulation port -- see the Phase 5B spec
// section for what's deliberately out of scope and why.

using System;

namespace Usim;

public class BusAdaptor
{
    // XBus I/O absolute physical-address range for disk control (usim/bus-adaptor.c's
    // bus_adaptor_xbusio_rw). 017377774-017377777 octal = 0x3DFFFC-0x3DFFFF.
    private const uint DiskControlLo = 0x3DFFFC;
    private const uint DiskControlHi = 0x3DFFFF;

    // Unibus 16-bit-word address range for the diagnostic-interface "spy" registers
    // (usim/bus-adaptor.c's bus_adaptor_unibus_rw). 0766000-0766036 octal =
    // 0x3EC00-0x3EC1E; the mode register specifically is 0766012 octal = 0x3EC0A.
    private const uint DiagnosticLo = 0x3EC00;
    private const uint DiagnosticHi = 0x3EC1E;
    private const uint DiagnosticModeRegister = 0x3EC0A;

    /// <summary>
    /// Faithful port of bus_adaptor_read (usim/bus-adaptor.c), for the XBus-I/O and
    /// Unibus ranges only -- UCode.Vm() already handles "xbus main memory" for real
    /// before ever calling this. paddr's page number (paddr>>8 &amp; 0x3FFF) must
    /// already be &gt; 0x3BFB, matching Vm()'s own dispatch split.
    /// </summary>
    public uint Read(uint paddr)
    {
        uint dispatchPn = (paddr >> 8) & 0x3FFF;
        if (dispatchPn <= 0x3DFF) return ReadXbusIo(paddr);
        uint uaddr = (((dispatchPn - 0x3E00) << 8) | (paddr & 0xFF)) << 1;
        return ReadUnibus(uaddr);
    }

    /// <summary>
    /// Faithful port of bus_adaptor_write (usim/bus-adaptor.c), same scope note as
    /// Read. promDisabled is UCode's own PromDisabled field, threaded through
    /// because the one real register this class implements (the diagnostic-
    /// interface mode register) sets it directly, matching the real C's
    /// machine_state.promdisabled = (v &amp; (1&lt;&lt;5)) != 0.
    /// </summary>
    public void Write(uint paddr, uint v, ref bool promDisabled)
    {
        uint dispatchPn = (paddr >> 8) & 0x3FFF;
        if (dispatchPn <= 0x3DFF) { WriteXbusIo(paddr, v); return; }
        uint uaddr = (((dispatchPn - 0x3E00) << 8) | (paddr & 0xFF)) << 1;
        WriteUnibus(uaddr, v, ref promDisabled);
    }

    private uint ReadXbusIo(uint paddr)
    {
        if (paddr >= DiskControlLo && paddr <= DiskControlHi)
        {
            uint offset = paddr - DiskControlLo;
            // Faithful port of encode_status() (usim/disk-controller.c:174-219) for
            // the two bits the boot PROM's DISK-RECALIBRATE polls: bit0=not_active
            // (ready/idle)=1, bit9=!online=0 (i.e. online). Every other status bit
            // (seek_error, read_only, has_fault, attention, interrupt_request, any
            // real error condition) defaults to 0 -- this is not a real disk, just
            // "no errors, ready, online". Offsets 1 (memory address), 2 (disk
            // address), 3 (ECC, "no ECC errors in usim, so this always returns 0")
            // have no real disk state to report either.
            return offset == 0 ? 1u : 0u;
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: read un-ported XBus-I/O paddr 0x{paddr:X} (TV/color-TV -- not implemented, Phase 5B scope)");
        return 0;
    }

    private void WriteXbusIo(uint paddr, uint v)
    {
        if (paddr >= DiskControlLo && paddr <= DiskControlHi)
        {
            // Command/CLP/DA writes: a no-op. This is NOT a working disk -- no real
            // transfer happens. Tracked as a deliberate, out-of-scope gap in the
            // Phase 5B spec section, not silently implied to work.
            return;
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: write un-ported XBus-I/O paddr 0x{paddr:X} v=0x{v:X} (TV/color-TV -- not implemented, Phase 5B scope)");
    }

    private uint ReadUnibus(uint uaddr)
    {
        if (uaddr >= DiagnosticLo && uaddr <= DiagnosticHi)
        {
            // No spy register is meaningfully readable back without real debug-IR/
            // clock state, which this phase doesn't implement -- 0 is a safe
            // default; the boot PROM's early boot never reads these back.
            return 0;
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: read un-ported Unibus uaddr 0x{uaddr:X} (Unibus Map/IOB/tape/unibus-mapping/bus-interface -- not implemented, Phase 5B scope)");
        return 0;
    }

    private void WriteUnibus(uint uaddr, uint v, ref bool promDisabled)
    {
        if (uaddr == DiagnosticModeRegister)
        {
            promDisabled = (v & (1 << 5)) != 0;
            return;
        }
        if (uaddr >= DiagnosticLo && uaddr <= DiagnosticHi)
        {
            // Other spy registers (DEBUG-IR, clock control, OPC control) are fatal
            // misuse guards in the real C (errx() -- real microcode is never
            // expected to trigger them) -- a no-op here, not a faithful throw. See
            // Global Constraints in the Phase 5B plan for why.
            return;
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: write un-ported Unibus uaddr 0x{uaddr:X} v=0x{v:X} (Unibus Map/IOB/tape/unibus-mapping/bus-interface -- not implemented, Phase 5B scope)");
    }
}
