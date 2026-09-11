// UnibusMapping.cs - Faithful port of usim/unibus-mapping.c's 16 Unibus <-> Xbus
// mapping registers (the storage half of "Unibus Map DMA" -- the real DMA
// translation logic lives in BusAdaptor.cs; see
// docs/superpowers/specs/2026-09-10-unibus-map-dma-design.md).

using System;

namespace Usim;

public class UnibusMapping
{
    private readonly ushort[] _registers = new ushort[16];
    private readonly ushort[] _buffers = new ushort[16];
    private readonly BusInterface _busInterface;

    public UnibusMapping(BusInterface busInterface)
    {
        _busInterface = busInterface;
    }

    public ushort GetRegister(uint pageNo) => _registers[pageNo];
    public ushort GetBuffer(uint pageNo) => _buffers[pageNo];
    public void SetBuffer(uint pageNo, ushort value) => _buffers[pageNo] = value;

    /// <summary>
    /// Faithful port of unibus_mapping_read (usim/unibus-mapping.c:85-89, via
    /// unibus_mapping_rw at :19-83). uaddr is the Unibus word address --
    /// BusAdaptor already range-gates to BusAdaptor.UnibusMappingLo..
    /// UnibusMappingHi before calling this, and every address in that exact
    /// range is one of the 16 registers, so the real C's default case (odd
    /// uaddr, or genuinely out of range) is not reachable via that call path
    /// -- ported faithfully anyway, matching this project's established
    /// practice for real-but-practically-unreachable fallback cases.
    /// </summary>
    public uint Read(uint uaddr)
    {
        if (uaddr >= BusAdaptor.UnibusMappingLo && uaddr <= BusAdaptor.UnibusMappingHi && (uaddr & 1) == 0)
        {
            uint pageNo = (uaddr - BusAdaptor.UnibusMappingLo) / 2;
            return _registers[pageNo];
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"unibus-mapping: read invalid uaddr:0x{uaddr:X}");
        _busInterface.SetUnibusNxm();
        return 0;
    }

    /// <summary>Faithful port of unibus_mapping_write (usim/unibus-mapping.c:91-95).</summary>
    public void Write(uint uaddr, uint v)
    {
        if (uaddr >= BusAdaptor.UnibusMappingLo && uaddr <= BusAdaptor.UnibusMappingHi && (uaddr & 1) == 0)
        {
            uint pageNo = (uaddr - BusAdaptor.UnibusMappingLo) / 2;
            _registers[pageNo] = (ushort)v;
            return;
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"unibus-mapping: write invalid uaddr:0x{uaddr:X}");
        _busInterface.SetUnibusNxm();
    }
}
