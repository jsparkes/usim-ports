// DiskController.cs - Faithful port of usim/disk-controller.c. Synchronous
// (blocking-mode) transfers only -- see docs/superpowers/specs/2026-09-08-disk-subsystem-design.md
// for why (this codebase has no other real threading; the real C's
// non-blocking pthread mode is a separate, unported mode).

using System;

namespace Usim;

public class DiskController
{
    public const int NUMBER_OF_DISK_UNITS = 8;

    private readonly MainMemory _mainMemory;
    private readonly UCode _ucode;
    private readonly DiskUnit[] _units;

    // --- registers (usim/disk-controller.c:70-78) ---
    private uint _cmd;
    private uint _clp;
    private uint _da;
    private bool _resetCondition;

    // --- status (usim/disk-controller.c:103-113) ---
    private bool _readCompareDifference;
    private bool _ccwCycle;
    private bool _nonexistentMemoryError;
    private bool _interruptRequest;
    private bool _notActive;

    private bool _doneInterruptEnable;
    private bool _attentionInterruptEnable;

    public DiskController(MainMemory mainMemory, UCode ucode)
    {
        _mainMemory = mainMemory;
        _ucode = ucode;
        _units = new DiskUnit[NUMBER_OF_DISK_UNITS];
        for (uint i = 0; i < NUMBER_OF_DISK_UNITS; i++) _units[i] = new DiskUnit(i);
        Reset();
    }

    /// <summary>Config-driven mount -- mirrors disk_unit_init's role (called
    /// once per configured unit at power-on), not disk_controller_init's
    /// (which only logs, and in the excluded non-blocking mode starts the
    /// thread).</summary>
    public void ConfigureUnit(uint unit, string typeName, string filename)
    {
        _units[unit].Configure(typeName, filename);
    }

    /// <summary>
    /// Test-only accessor for a unit's internals (SeekError/HasFault/
    /// Attention/ReadOnly/LastMemoryAddress etc.) that the real register
    /// interface -- and so this class's public API -- has no way to expose.
    /// Mirrors the internal test-hook pattern already used in UCode.cs
    /// (CallJmp/CallDsp/CallByt/...) for reaching otherwise-private state
    /// from same-assembly tests. Not part of the faithful port surface.
    /// </summary>
    internal DiskUnit GetUnitForTest(uint unit) => _units[unit];

    private DiskUnit SelectedUnit() => _units[(_da >> 28) & 0x7];

    // usim/disk-controller.c:115-128
    private void AssertInterrupt()
    {
        _interruptRequest = true;
        _ucode.AssertXbusInterrupt();
    }

    private void DeassertInterrupt() => _ucode.DeassertXbusInterrupt();

    // usim/disk-controller.c:130-150
    private void SetStatusNotActive()
    {
        _notActive = true;
        if (_doneInterruptEnable) AssertInterrupt();
    }

    private void SetStatusActive() => _notActive = false;

    // usim/disk-controller.c:152-171
    private void ResetStatus()
    {
        _readCompareDifference = false;
        _ccwCycle = false;
        _nonexistentMemoryError = false;
        _interruptRequest = false;
        SetStatusNotActive();
    }

    private void Reset()
    {
        _doneInterruptEnable = false;
        _attentionInterruptEnable = false;
        ResetStatus();
        _cmd = 0;
        _clp = 0;
        _da = 0;
    }

    /// <summary>Faithful port of encode_status (usim/disk-controller.c:173-220).
    /// Masks are all (1&lt;&lt;N) forms in the real C -- no octal-literal risk.
    /// "Selected unit" is recomputed from the CURRENT da on every call, never
    /// cached (matches the real C's SELECTED_UNIT_PTR() macro).</summary>
    private uint EncodeStatus()
    {
        uint v = 0;
        if (_readCompareDifference) v |= 1u << 22;
        if (_ccwCycle) v |= 1u << 21;
        if (_nonexistentMemoryError) v |= 1u << 20;

        DiskUnit p = SelectedUnit();
        if (p.SeekError) v |= 1u << 10;
        if (!p.Online) v |= 1u << 9;
        if (p.ReadOnly) v |= 1u << 7;
        if (p.HasFault) v |= 1u << 6;
        if (p.Attention) v |= 1u << 2;
        if (_interruptRequest) v |= 1u << 3;

        for (int i = 0; i < NUMBER_OF_DISK_UNITS; i++)
        {
            if (_units[i].Attention) { v |= 1u << 1; break; }
        }

        if (_notActive) v |= 1u << 0;
        return v;
    }

    /// <summary>Faithful port of decode_da (usim/disk-controller.c:222-229).
    /// Masks independently verified: octal 07=0x7, 07777=0xFFF, 0377=0xFF.</summary>
    private static void DecodeDa(uint da, out uint unit, out uint cylinder, out uint head, out uint block)
    {
        unit = (da >> 28) & 0x7;
        cylinder = (da >> 16) & 0xFFF;
        head = (da >> 8) & 0xFF;
        block = da & 0xFF;
    }

    /// <summary>Faithful port of perform_xfer (usim/disk-controller.c:254-383) --
    /// the DMA-style CCW-chain transfer, minus threading.</summary>
    private bool PerformXfer(DiskUnit p, bool read, bool compare, uint clp)
    {
        _readCompareDifference = false;
        _ccwCycle = false;
        _nonexistentMemoryError = false;

        var buffer = new uint[256];
        var bufferCompare = new uint[256];

        ushort clpOffset = 0;
        while (true)
        {
            uint currentClp = clp + clpOffset;
            p.LastMemoryAddress = currentClp;
            _ccwCycle = true;
            if (!_mainMemory.TryReadWord(currentClp, out uint ccw))
            {
                _nonexistentMemoryError = true;
                return false;
            }
            _ccwCycle = false;

            uint paddr = ccw & 0x00FFFF00u;

            if (read)
            {
                if (p.Read(buffer))
                {
                    if (compare)
                    {
                        p.LastMemoryAddress = paddr;
                        if (_mainMemory.ReadPage(paddr, bufferCompare))
                        {
                            p.LastMemoryAddress = paddr + 255;
                            if (!BuffersEqual(buffer, bufferCompare))
                            {
                                // "This error does not stop the transfer."
                                _readCompareDifference = true;
                            }
                        }
                        else
                        {
                            _nonexistentMemoryError = true;
                            return false;
                        }
                    }
                    else
                    {
                        p.LastMemoryAddress = paddr;
                        if (_mainMemory.WritePage(paddr, buffer))
                        {
                            p.LastMemoryAddress = paddr + 255;
                        }
                        else
                        {
                            _nonexistentMemoryError = true;
                            return false;
                        }
                    }
                }
                else
                {
                    p.HasFault = true;
                    return false;
                }
            }
            else
            {
                p.LastMemoryAddress = paddr;
                if (_mainMemory.ReadPage(paddr, buffer))
                {
                    p.LastMemoryAddress = paddr + 255;
                    if (!p.Write(buffer))
                    {
                        p.HasFault = true;
                        return false;
                    }
                }
                else
                {
                    _nonexistentMemoryError = true;
                    return false;
                }
            }

            // is it the last ccw?
            if ((ccw & 1) == 0) break;

            if (!p.SeekNextLba()) return false;
            clpOffset++;
        }

        return true;
    }

    private static bool BuffersEqual(uint[] a, uint[] b)
    {
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>Faithful port of submit_xfer (usim/disk-controller.c:422-440),
    /// collapsed with do_xfer since this port is synchronous-only.</summary>
    private void SubmitXfer(bool read, bool compare)
    {
        SetStatusActive();

        DecodeDa(_da, out uint unit, out uint cylinder, out uint head, out uint block);
        DiskUnit p = _units[unit];

        if (p.Seek(cylinder, head, block))
        {
            PerformXfer(p, read, compare, _clp);
            _da = p.Da();
        }
        else
        {
            TraceLog.Instance.Warning(TraceCategory.Disk, $"disk-unit {p.Unit}: seek error");
        }

        SetStatusNotActive();
    }

    private void StartRead() => SubmitXfer(true, false);
    private void StartReadCompare() => SubmitXfer(true, true);

    private void StartWrite()
    {
        // "Writing while the disk is read-only causes a fault."
        DiskUnit p = SelectedUnit();
        if (p.ReadOnly)
        {
            p.HasFault = true;
        }
        else
        {
            SubmitXfer(false, false);
        }
    }

    private void StartSeek()
    {
        SetStatusActive();
        DecodeDa(_da, out uint unit, out uint cylinder, out uint head, out uint block);
        DiskUnit p = _units[unit];
        p.Seek(cylinder, head, block);
        p.RaiseAttention();
        OnDiskUnitAttention(p);
        SetStatusNotActive();
    }

    private void StartRecalibrate()
    {
        SetStatusActive();
        DiskUnit p = SelectedUnit();
        p.Seek(0, 0, 0);
        p.HasFault = false;
        p.SeekError = false;
        p.RaiseAttention();
        OnDiskUnitAttention(p);
        SetStatusNotActive();
    }

    private void StartFaultClear()
    {
        SetStatusActive();
        SelectedUnit().HasFault = false;
        SetStatusNotActive();
    }

    private void StartAtEase()
    {
        SetStatusActive();
        SelectedUnit().Attention = false;
        SetStatusNotActive();
    }

    private void StartOffsetClear()
    {
        // "there is no concept of servo offset in usim; offset_clear is
        // simply a nop" -- usim/disk-controller.c:537-538.
        SetStatusActive();
        SetStatusNotActive();
    }

    /// <summary>Faithful port of start (usim/disk-controller.c:543-629).
    /// Command values independently verified: octal 000=0x0 read, 010=0x8
    /// read-compare, 011=0x9 write, 002=0x2 read-all (fatal, unimplemented
    /// in the real C too), 013=0xB write-all (same), 004=0x4 seek, 005=0x5
    /// at-ease (+01000=0x200 recalibrate, +00400=0x100 fault-clear), 006=0x6
    /// offset-clear. cmd mask 017=0xF.</summary>
    private void Start()
    {
        DiskUnit p = SelectedUnit();
        if (!p.Online)
        {
            TraceLog.Instance.Info(TraceCategory.Disk, $"disk controller: start, but disk unit {p.Unit} not online");
            return;
        }

        switch (_cmd & 0xF)
        {
            case 0x0: StartRead(); break;
            case 0x8: StartReadCompare(); break;
            case 0x9: StartWrite(); break;
            case 0x2: throw new InvalidOperationException("disk-controller: read all not implemented");
            case 0xB: throw new InvalidOperationException("disk-controller: write all not implemented");
            case 0x4: StartSeek(); break;
            case 0x5:
                StartAtEase();
                // "1405_This probably does both a Recalibrate and a Fault Clear."
                if ((_cmd & 0x200) != 0) StartRecalibrate();
                if ((_cmd & 0x100) != 0) StartFaultClear();
                break;
            case 0x6: StartOffsetClear(); break;
            default:
                throw new InvalidOperationException($"disk-controller: start, cmd (0x{_cmd:X}) unknown");
        }
    }

    /// <summary>Faithful port of disk_controller_get_attention
    /// (usim/disk-controller.c:633-640) -- "not used at the moment but if a
    /// disk unit is for example changes state while idle, this can be
    /// used".</summary>
    private void OnDiskUnitAttention(DiskUnit p)
    {
        if (_notActive && _attentionInterruptEnable) AssertInterrupt();
    }

    /// <summary>Faithful port of disk_controller_read (usim/disk-controller.c:724-767).
    /// While reset is in effect, EVERY read (all four offsets) returns 0 --
    /// this check happens before the offset switch in the real C, not
    /// per-offset.</summary>
    public uint Read(uint offset)
    {
        if (_resetCondition) return 0;

        switch (offset)
        {
            case 0: return EncodeStatus();
            case 1: return SelectedUnit().LastMemoryAddress;
            case 2: return _da;
            case 3: return 0; // no ECC errors modeled in usim
            default:
                throw new InvalidOperationException($"disk-controller: unknown read {offset}");
        }
    }

    /// <summary>Faithful port of disk_controller_write (usim/disk-controller.c:769-834).
    /// Reset magic value 016 octal = 0xE.</summary>
    public void Write(uint offset, uint v)
    {
        switch (offset)
        {
            case 0:
                // "Reset. ... After storing a Reset command you should store
                // 0 in the command register to turn off the reset condition."
                if (v == 0) { _cmd = 0; _resetCondition = false; break; }
                if (v == 0xE) { Reset(); _resetCondition = true; break; }
                if (_resetCondition) break;
                _cmd = v;
                _doneInterruptEnable = (v & 0x800) != 0;
                _attentionInterruptEnable = (v & 0x400) != 0;
                if (!_doneInterruptEnable && !_attentionInterruptEnable) DeassertInterrupt();
                break;

            case 1:
                if (_resetCondition) break;
                _clp = v;
                break;

            case 2:
                if (_resetCondition) break;
                // "Storing into the Disk Address register momentarily
                // deselects the current unit ..."
                _da = v;
                break;

            case 3:
                if (_resetCondition) break;
                Start();
                break;

            default:
                throw new InvalidOperationException($"disk-controller: unknown write {offset}");
        }
    }

    /// <summary>disk_controller_bus_reset (usim/disk-controller.c:836-839) --
    /// empty in the real C too.</summary>
    public void BusReset() { }
}
