// Uvmem.cs - CADR two-level virtual memory page tables.
// Faithful port of usim/uvmem.c's uvmem_vtop() and uvmem_write_map().
//
// L1 (First Level Map): 2048 entries, 5 bits each, addressed by vaddr<23:13>.
// L2 (Second Level Map): 1024 entries, 24 bits each, addressed by
// (L1 data << 5) | vaddr<12:8>. L2's bits are: <23>=access permission,
// <22>=write permission, <13-0>=physical page number.
//
// Uvmem deliberately has no MainMemory dependency: neither Vtop nor
// WriteMap ever touches main memory, matching the real C (uvmem_vtop/
// uvmem_write_map never call into main-memory.c either) -- actual physical
// memory access belongs to UCode.Vm(), which holds its own MainMemory
// reference (Phase 5 Task 2).

using System;

namespace Usim;

public class Uvmem
{
    private readonly uint[] _l1Map = new uint[2048];
    private readonly uint[] _l2Map = new uint[1024];

    /// <summary>
    /// Resolve a 24-bit virtual address to a physical address plus the raw
    /// L1/L2 map entries and permission bits. Faithful port of
    /// uvmem_vtop() (usim/uvmem.c:78-111).
    /// </summary>
    public uint Vtop(uint vaddr, out uint l1Data, out uint l2Data,
                      out uint physicalPageNumber, out bool writePermission, out bool accessPermission)
    {
        vaddr &= 0x00FFFFFF;
        uint l1Index = (vaddr >> 13) & 0x7FF;
        l1Data = _l1Map[l1Index] & 0x1F;
        uint l2Index = (l1Data << 5) | ((vaddr >> 8) & 0x1F);
        l2Data = _l2Map[l2Index];
        physicalPageNumber = l2Data & 0x3FFF;
        writePermission = (l2Data & (1 << 22)) != 0;
        accessPermission = (l2Data & (1 << 23)) != 0;
        return (physicalPageNumber << 8) | (vaddr & 0xFF);
    }

    /// <summary>
    /// Update the L1 and/or L2 map entries. Called from UCode.MfWrite's
    /// VMA-WRITE-MAP/MD-WRITE-MAP cases with vma=VmaReg, md=MdReg. Faithful
    /// port of uvmem_write_map() (usim/uvmem.c:120-157). If both enable
    /// bits are set in one call, the L2 write re-reads _l1Map AFTER the L1
    /// write executes, so it sees the just-written L1 entry -- this is the
    /// real C's actual sequential-statement-order behavior, not an
    /// optimization to "fix".
    /// </summary>
    public void WriteMap(uint vma, uint md)
    {
        bool enableL1 = (vma & (1 << 26)) != 0;
        bool enableL2 = (vma & (1 << 25)) != 0;

        if (enableL1)
        {
            uint l1Index = (md >> 13) & 0x7FF;
            _l1Map[l1Index] = (vma >> 27) & 0x1F;
        }
        if (enableL2)
        {
            uint l1Index = (md >> 13) & 0x7FF;
            uint l1Data = _l1Map[l1Index];
            uint l2Index = (l1Data << 5) | ((md >> 8) & 0x1F);
            _l2Map[l2Index] = vma & 0x00FFFFFF;
        }
    }
}
