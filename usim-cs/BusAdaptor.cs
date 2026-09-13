// BusAdaptor.cs - Faithful (but deliberately scoped) port of usim/bus-adaptor.c's
// XBus-I/O and Unibus device routing. UCode.Vm() already handles the "xbus main
// memory" range (physical page number <= 0x3BFB) for real; this class handles
// everything above that -- the boot PROM's disk-control and diagnostic-register
// accesses, the real Unibus Map DMA-translation path (to main memory, Xbus I/O,
// or the diagnostic MD-register backdoor) and its unibus-mapping register file,
// plus non-fatal placeholders for every other device bus-adaptor.c would route
// to (TV, color TV, tape, IOB). The bus-interface range (0766040-0766136 octal)
// routes to the real BusInterface (Phase 4's own faithful port), not a
// placeholder. NOT a full device-emulation port -- see the Phase 5B spec
// section for what's deliberately out of scope and why.

using System;

namespace Usim;

public class BusAdaptor
{
    private readonly BusInterface _busInterface;
    private readonly MainMemory _mainMemory;
    private readonly UCode _ucode;
    private readonly UnibusMapping _unibusMapping;
    private DiskController? _diskController;
    private Tv? _tv;

    public BusAdaptor(BusInterface busInterface, MainMemory mainMemory, UCode ucode)
    {
        _busInterface = busInterface;
        _mainMemory = mainMemory;
        _ucode = ucode;
        _unibusMapping = new UnibusMapping(busInterface);
    }

    public void WireDiskController(DiskController diskController)
    {
        _diskController = diskController;
    }

    public void WireTv(Tv tv)
    {
        _tv = tv;
    }

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
    private const uint BusInterfaceLo = 0x3EC20;    // 0766040-0766136 octal -- routed to
    private const uint BusInterfaceHi = 0x3EC5E;    // BusInterface (Phase 4's own faithful port)
    internal const uint UnibusMappingLo = 0x3EC60;  // 0766140-0766176 octal
    internal const uint UnibusMappingHi = 0x3EC7E;
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
            if (_diskController == null)
            {
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"BusAdaptor: disk-control read at offset {offset} with no DiskController wired");
                return 0;
            }
            return _diskController.Read(offset);
        }
        if (paddr >= TvScreenLo && paddr <= TvScreenHi)
        {
            uint offset = paddr - TvScreenLo;
            if (_tv == null)
            {
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"BusAdaptor: TV screen read at offset {offset} with no Tv wired");
                return 0;
            }
            return _tv.ScreenRead(offset);
        }
        if (paddr >= TvControlLo && paddr <= TvControlHi)
        {
            uint offset = paddr - TvControlLo;
            if (_tv == null)
            {
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"BusAdaptor: TV control read at offset {offset} with no Tv wired");
                return 0;
            }
            return _tv.ControlRead(offset);
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
            uint offset = paddr - DiskControlLo;
            if (_diskController == null)
            {
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"BusAdaptor: disk-control write at offset {offset} with no DiskController wired");
                return;
            }
            _diskController.Write(offset, v);
            return;
        }
        if (paddr >= TvScreenLo && paddr <= TvScreenHi)
        {
            uint offset = paddr - TvScreenLo;
            if (_tv == null)
            {
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"BusAdaptor: TV screen write at offset {offset} with no Tv wired");
                return;
            }
            _tv.ScreenWrite(offset, v);
            return;
        }
        if (paddr >= TvControlLo && paddr <= TvControlHi)
        {
            uint offset = paddr - TvControlLo;
            if (_tv == null)
            {
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"BusAdaptor: TV control write at offset {offset} with no Tv wired");
                return;
            }
            _tv.ControlWrite(offset, v);
            return;
        }
        if (paddr == KnownBenignOverrunPaddr) return; // see the constant's comment
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: write un-ported XBus-I/O paddr 0x{paddr:X} v=0x{v:X} ({DescribeXbusIo(paddr)} -- not implemented, Phase 5B scope)");
    }

    private static string DescribeXbusIo(uint paddr)
    {
        if (paddr >= ColorTvScreenLo && paddr <= ColorTvScreenHi) return "color TV screen";
        if (paddr >= ColorTvControlLo && paddr <= ColorTvControlHi) return "color TV control";
        return "unmapped XBus I/O";
    }

    /// <summary>
    /// Faithful port of the Unibus-Map-handling branch of bus_adaptor_unibus_rw
    /// (usim/bus-adaptor.c:210-367), read side. "Selected mapping register" is
    /// recomputed from pageNo on every call, matching the real C.
    /// </summary>
    private uint UnibusMapDmaRead(uint uaddr)
    {
        uint pageNo = (uaddr - UnibusMapLo) / 0x400;
        ushort mappingRegister = _unibusMapping.GetRegister(pageNo);
        bool mapValid = (mappingRegister & 0x8000) != 0;
        uint xbusPageNumber = (uint)(mappingRegister & 0x3FFF);
        uint paddr = (xbusPageNumber << 8) | ((uaddr >> 2) & 0xFF);

        if (!mapValid)
        {
            _busInterface.SetUnibusMapError();
            return 0;
        }

        bool hiword = ((uaddr >> 1) & 1) != 0;

        // "An additional feature is that writing an Xbus address of 17400000
        // or higher through the Unibus map writes into CADR's MD register."
        if (xbusPageNumber >= 0x3E00)
        {
            TraceLog.Instance.Debug(TraceCategory.Memory,
                $"BusAdaptor: unibus-map DMA read via MD-register backdoor, page {pageNo} reg=0x{mappingRegister:X} (xbus page 0x{xbusPageNumber:X}, {(hiword ? "hi" : "lo")} half), MdReg=0x{_ucode.MdReg:X}");
            return hiword ? (_ucode.MdReg >> 16) & 0xFFFF : _ucode.MdReg & 0xFFFF;
        }

        if (hiword)
        {
            // High half returns the value buffered by the low-half read below --
            // no new Xbus transfer.
            return _unibusMapping.GetBuffer(pageNo);
        }

        uint v32 = XbusRead(paddr);
        _unibusMapping.SetBuffer(pageNo, (ushort)((v32 >> 16) & 0xFFFF));
        TraceLog.Instance.Debug(TraceCategory.Memory,
            $"BusAdaptor: unibus-map DMA read, page {pageNo} reg=0x{mappingRegister:X} -> xbus paddr 0x{paddr:X}, value 0x{v32:X}");
        return v32 & 0xFFFF;
    }

    /// <summary>Write side of UnibusMapDmaRead's port.</summary>
    private void UnibusMapDmaWrite(uint uaddr, uint v)
    {
        uint pageNo = (uaddr - UnibusMapLo) / 0x400;
        ushort mappingRegister = _unibusMapping.GetRegister(pageNo);
        bool mapValid = (mappingRegister & 0x8000) != 0;
        bool writePermit = (mappingRegister & 0x4000) != 0;
        uint xbusPageNumber = (uint)(mappingRegister & 0x3FFF);
        uint paddr = (xbusPageNumber << 8) | ((uaddr >> 2) & 0xFF);

        if (!mapValid)
        {
            _busInterface.SetUnibusMapError();
            return;
        }
        if (!writePermit)
        {
            _busInterface.SetUnibusMapError();
            return;
        }

        bool hiword = ((uaddr >> 1) & 1) != 0;

        if (xbusPageNumber >= 0x3E00)
        {
            uint v32;
            if (hiword)
            {
                v32 = _ucode.MdReg & 0x0000FFFFu;
                v32 |= (v << 16) & 0xFFFF0000u;
            }
            else
            {
                v32 = _ucode.MdReg & 0xFFFF0000u;
                v32 |= v & 0x0000FFFFu;
            }
            _ucode.MdReg = v32;
            TraceLog.Instance.Debug(TraceCategory.Memory,
                $"BusAdaptor: unibus-map DMA write via MD-register backdoor, page {pageNo} reg=0x{mappingRegister:X} (xbus page 0x{xbusPageNumber:X}, {(hiword ? "hi" : "lo")} half) -> MdReg=0x{_ucode.MdReg:X}");
            return;
        }

        if (hiword)
        {
            // High half completes the transfer, combining the cached low half
            // (from the write below, on a prior call) with this high half.
            ushort cachedLo = _unibusMapping.GetBuffer(pageNo);
            uint v32 = ((v << 16) & 0xFFFF0000u) | cachedLo;
            TraceLog.Instance.Debug(TraceCategory.Memory,
                $"BusAdaptor: unibus-map DMA write, page {pageNo} reg=0x{mappingRegister:X} -> xbus paddr 0x{paddr:X}, value 0x{v32:X}");
            XbusWrite(paddr, v32);
        }
        else
        {
            // Low half: cache it, no transfer yet.
            _unibusMapping.SetBuffer(pageNo, (ushort)v);
        }
    }

    /// <summary>
    /// Faithful port of bus_adaptor_xbus_rw (usim/bus-adaptor.c:164-193), read
    /// side, split into a Read/Write pair per this file's existing
    /// ReadXbusIo/WriteXbusIo and ReadUnibus/WriteUnibus convention (the real
    /// C uses one bool-flagged function; this codebase doesn't).
    /// </summary>
    private uint XbusRead(uint paddr)
    {
        uint pn = (paddr >> 8) & 0x3FFF;
        if (pn <= 0x3BFB)
        {
            return _mainMemory.ReadPhysical(paddr);
        }
        if (pn >= 0x3C00 && pn <= 0x3DFF)
        {
            return ReadXbusIo(paddr);
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: xbus read unknown paddr 0x{paddr:X}");
        _busInterface.SetXbusNxm();
        return 0;
    }

    /// <summary>Write side of XbusRead's port.</summary>
    private void XbusWrite(uint paddr, uint v)
    {
        uint pn = (paddr >> 8) & 0x3FFF;
        if (pn <= 0x3BFB)
        {
            _mainMemory.WritePhysical(paddr, v);
            return;
        }
        if (pn >= 0x3C00 && pn <= 0x3DFF)
        {
            WriteXbusIo(paddr, v);
            return;
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: xbus write unknown paddr 0x{paddr:X} v=0x{v:X}");
        _busInterface.SetXbusNxm();
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
        if (uaddr >= BusInterfaceLo && uaddr <= BusInterfaceHi)
        {
            return _busInterface.Read(uaddr);
        }
        if (uaddr >= UnibusMappingLo && uaddr <= UnibusMappingHi)
        {
            return _unibusMapping.Read(uaddr);
        }
        if (uaddr >= UnibusMapLo && uaddr <= UnibusMapHi)
        {
            return UnibusMapDmaRead(uaddr);
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
        if (uaddr >= BusInterfaceLo && uaddr <= BusInterfaceHi)
        {
            _busInterface.Write(uaddr, v);
            return;
        }
        if (uaddr >= UnibusMappingLo && uaddr <= UnibusMappingHi)
        {
            _unibusMapping.Write(uaddr, v);
            return;
        }
        if (uaddr >= UnibusMapLo && uaddr <= UnibusMapHi)
        {
            UnibusMapDmaWrite(uaddr, v);
            return;
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: write un-ported Unibus uaddr 0x{uaddr:X} v=0x{v:X} ({DescribeUnibus(uaddr)} -- not implemented, Phase 5B scope)");
    }

    private static string DescribeUnibus(uint uaddr)
    {
        if (uaddr >= IobLo && uaddr <= IobHi) return "IOB";
        if (uaddr >= TapeControllerLo && uaddr <= TapeControllerHi) return "tape controller";
        return "unmapped Unibus";
    }
}
