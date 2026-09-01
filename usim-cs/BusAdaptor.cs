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
    // XBus I/O absolute physical-address ranges (usim/bus-adaptor.c's
    // bus_adaptor_xbusio_rw). All boundaries re-derived by direct octal-to-hex
    // computation (Python `0o`/`hex()`), not manual digit counting.
    private const uint TvScreenLo = 0x3C0000;      // 017000000-017077777 octal
    private const uint TvScreenHi = 0x3C7FFF;
    private const uint ColorTvScreenLo = 0x3D0000; // 017200000-017277777 octal
    private const uint ColorTvScreenHi = 0x3D7FFF;
    private const uint ColorTvControlLo = 0x3DFFE8; // 017377750-017377757 octal
    private const uint ColorTvControlHi = 0x3DFFEF;
    private const uint TvControlLo = 0x3DFFF0;      // 017377760-017377767 octal
    private const uint TvControlHi = 0x3DFFF7;
    private const uint DiskControlLo = 0x3DFFFC;    // 017377774-017377777 octal
    private const uint DiskControlHi = 0x3DFFFF;
    // The real C special-cases this exact paddr (bus-adaptor.c:125,140) because the
    // boot PROM's own PAGE-0-PARITY-FIX loop reads/writes it every single boot ("This
    // does one extra location, too bad" -- promh.text) -- suppressed here too, rather
    // than warning on a known-benign access the reference emulator's own author
    // deliberately silenced.
    private const uint KnownBenignOverrunPaddr = 0x3DFF00; // 017377400 octal

    // Unibus 16-bit-word address ranges (usim/bus-adaptor.c's bus_adaptor_unibus_rw).
    private const uint UnibusMapLo = 0xC000;        // 0140000-0177777 octal
    private const uint UnibusMapHi = 0xFFFF;
    private const uint IobLo = 0x3E800;             // 0764000-0764176 octal
    private const uint IobHi = 0x3E87E;
    private const uint DiagnosticLo = 0x3EC00;      // 0766000-0766036 octal
    private const uint DiagnosticHi = 0x3EC1E;
    private const uint DiagnosticModeRegister = 0x3EC0A; // 0766012 octal
    private const uint BusInterfaceLo = 0x3EC20;    // 0766040-0766136 octal -- already a
    private const uint BusInterfaceHi = 0x3EC5E;    // separately-deferred subsystem (Phase 4 ruling)
    private const uint UnibusMappingLo = 0x3EC60;   // 0766140-0766176 octal
    private const uint UnibusMappingHi = 0x3EC7E;
    private const uint TapeControllerLo = 0x3F550;  // 0772520-0772532 octal
    private const uint TapeControllerHi = 0x3F55A;

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
        if (paddr == KnownBenignOverrunPaddr) return 0; // see the constant's comment
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: read un-ported XBus-I/O paddr 0x{paddr:X} ({DescribeXbusIo(paddr)} -- not implemented, Phase 5B scope)");
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
        if (paddr == KnownBenignOverrunPaddr) return; // see the constant's comment
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: write un-ported XBus-I/O paddr 0x{paddr:X} v=0x{v:X} ({DescribeXbusIo(paddr)} -- not implemented, Phase 5B scope)");
    }

    private static string DescribeXbusIo(uint paddr)
    {
        if (paddr >= TvScreenLo && paddr <= TvScreenHi) return "main TV screen";
        if (paddr >= ColorTvScreenLo && paddr <= ColorTvScreenHi) return "color TV screen";
        if (paddr >= ColorTvControlLo && paddr <= ColorTvControlHi) return "color TV control";
        if (paddr >= TvControlLo && paddr <= TvControlHi) return "main TV control";
        return "unmapped XBus I/O";
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
            $"BusAdaptor: read un-ported Unibus uaddr 0x{uaddr:X} ({DescribeUnibus(uaddr)} -- not implemented, Phase 5B scope)");
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
            $"BusAdaptor: write un-ported Unibus uaddr 0x{uaddr:X} v=0x{v:X} ({DescribeUnibus(uaddr)} -- not implemented, Phase 5B scope)");
    }

    private static string DescribeUnibus(uint uaddr)
    {
        if (uaddr >= UnibusMapLo && uaddr <= UnibusMapHi) return "Unibus Map DMA";
        if (uaddr >= IobLo && uaddr <= IobHi) return "IOB";
        if (uaddr >= BusInterfaceLo && uaddr <= BusInterfaceHi) return "bus-interface (already separately deferred, Phase 4)";
        if (uaddr >= UnibusMappingLo && uaddr <= UnibusMappingHi) return "unibus-mapping";
        if (uaddr >= TapeControllerLo && uaddr <= TapeControllerHi) return "tape controller";
        return "unmapped Unibus";
    }
}
