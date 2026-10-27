// TapeController.cs - Faithful port of usim/tape-controller.c (Western
// Peripherals Model TC-131 Tape Controller), synchronous-mode only -- see
// docs/superpowers/specs/2026-10-26-tape-subsystem-design.md. The real C runs
// GO processing on a separate pthread signalled by a cond var; this port has
// no such thread (matching this codebase's established disk-controller
// precedent), so tape_controller_update_mtc's go-signal and
// tape_controller_run's thread-loop body are collapsed into one synchronous
// call from Write(Mtc, ...).
//
// DMA (MTCMA-addressed transfers) is a deliberate, confirmed divergence from
// DiskController: the real C's tape-controller.c calls
// bus_adaptor_unibus_read/write(mtcma, ...) -- the SAME Unibus dispatcher
// every other Unibus device goes through -- not main_memory_read/write_page
// directly. This port mirrors that exactly via BusAdaptor.ReadUnibus/
// WriteUnibus (promoted to internal for this purpose). A transfer only
// actually reaches MainMemory when mtcma falls inside BusAdaptor's Unibus Map
// range (0xC000-0xFFFF) with a configured mapping register -- exactly the
// same path BusAdaptorTests.cs's own TestUnibusMapDmaWriteToMainMemory/
// TestUnibusMapDmaReadFromMainMemory already exercise. Any other mtcma value
// either hits a real device's register range or the generic "unmapped
// Unibus" fallback (both non-fatal, matching the real C's own non-fatal
// probing tolerance for most ranges, though the real C's own generic
// fallback does set Unibus NXM where this port's does not -- an existing,
// out-of-scope BusAdaptor simplification, not something this task touches).
//
// TapeDrive.cs (Task 1) doesn't expose tape_drive_space_forward/
// tape_drive_space_reverse/tape_drive_rewind/the "ready" flag -- those are
// re-implemented here directly from the primitives TapeDrive.cs *does*
// expose (SeekAbsolute/SeekRelative/ReadUInt32/WriteUInt32/Pos/Bot/Eot),
// matching tape-drive.c's own logic function-by-function. TapeDrive.Eot is
// recomputed by TapeDrive itself on every Seek call (mirroring tape_drive_
// seek's callback into tape_controller_set/reset_mts_eot), but MTS.EOT is
// still tracked as this class's own field (not read live from the drive)
// because tape_controller_go's reset_mtc_err() must be able to force it back
// to false at the start of every command -- this class syncs its own _eot
// field from the selected drive's Eot property immediately after every real
// seek it performs, mirroring the real callback.

using System;

namespace Usim;

public class TapeController
{
    // 0224 octal = 0x94 (usim/tape-controller.c:23).
    private const int UnibusInterruptVector = 0x94;

    // Register offsets relative to BusAdaptor.TapeControllerLo (0772520 octal
    // base, 6 registers 2 bytes apart -- usim/tape-controller.c's own
    // tape_controller_unibus_read/_write switches).
    private const uint MtsOffset = 0, MtcOffset = 2, MtbrcOffset = 4, MtcmaOffset = 6, MtdOffset = 8, MtrdOffset = 10;

    private readonly UCode _ucode;
    private readonly BusInterface _busInterface;
    private readonly BusAdaptor _busAdaptor;
    private readonly TapeDrive[] _units;

    /// <summary>p->ready (usim/tape-drive.h:50) -- "set when stopped, cleared
    /// when executing sth". Not part of TapeDrive.cs's own surface (Task 1
    /// didn't port it there), so tracked per-unit here instead, toggled
    /// exactly where tape_controller_go toggles it and set true alongside
    /// TapeDrive.Online whenever ConfigureUnit succeeds (mirroring
    /// tape_drive_init's own `p->ready = true; p->online = true;` pairing).</summary>
    private readonly bool[] _ready;

    // --- MTS=STATUS REGISTER (usim/tape-controller.c:94-113) ---
    private bool _ilc;
    private bool _eof;
    private bool _eot;
    private bool _rle;
    private bool _bte;
    private bool _nxm;
    private bool _selr;

    // --- MTC=COMMAND REGISTER (usim/tape-controller.c:117-129) ---
    private bool _den8;
    private bool _den5;
    private bool _pevn;
    private uint _slt;
    private bool _cur;
    private bool _intEnb;
    private uint _fun;

    // --- MTBRC/MTCMA/MTD/MTRD (usim/tape-controller.c:134-153) ---
    private short _mtbrc;
    private uint _mtcma;
    private ushort _mtd;
    private ushort _mtrd;

    public TapeController(UCode ucode, BusInterface busInterface, BusAdaptor busAdaptor)
    {
        _ucode = ucode;
        _busInterface = busInterface;
        _busAdaptor = busAdaptor;

        _units = new TapeDrive[TapeDrive.NUMBER_OF_TAPE_DRIVES];
        for (uint i = 0; i < TapeDrive.NUMBER_OF_TAPE_DRIVES; i++) _units[i] = new TapeDrive(i);
        _ready = new bool[TapeDrive.NUMBER_OF_TAPE_DRIVES];

        BusReset();
    }

    /// <summary>Config-driven mount, mirroring DiskController.ConfigureUnit's
    /// own role. Also sets the unit's "ready" flag alongside Online, matching
    /// tape_drive_init's `p->ready = true; p->online = true;` pairing
    /// (usim/tape-drive.c:201-202).</summary>
    public void ConfigureUnit(uint unit, long lengthFeet, string mode, string filename)
    {
        _units[unit].Configure(lengthFeet, mode, filename);
        _ready[unit] = _units[unit].Online;
    }

    /// <summary>
    /// Test-only accessor for a unit's internals (Pos, Online, etc.) that the
    /// real register interface -- and so this class's public API -- has no
    /// way to expose. Mirrors DiskController.GetUnitForTest's own exact
    /// precedent and doc-comment rationale. Not part of the faithful port
    /// surface.
    /// </summary>
    internal TapeDrive GetUnitForTest(uint unit) => _units[unit];

    private TapeDrive SelectedDrive() => _units[_slt];

    private static bool Bit(ushort v, int pos) => ((v >> pos) & 1) != 0;
    private static uint Bus(ushort v, int start, int end) => (uint)((v >> end) & ((1 << (start - end + 1)) - 1));

    /// <summary>Faithful port of tape_controller_assert_unibus_interrupt
    /// (usim/tape-controller.c:157-164).</summary>
    private void AssertInterruptIfEnabled()
    {
        if (_intEnb) _ucode.AssertUnibusInterrupt(UnibusInterruptVector);
    }

    /// <summary>Faithful port of tape_controller_is_mtc_err
    /// (usim/tape-controller.c:166-171) -- crc/pae/bgl are always false in
    /// usim, so they're simply omitted here (matching the real C's own
    /// comment on this).</summary>
    private bool IsMtcErr() => _ilc || _eof || _eot || _rle || _bte || _nxm;

    /// <summary>Faithful port of tape_controller_reset_mtc_err
    /// (usim/tape-controller.c:173-182).</summary>
    private void ResetMtcErr()
    {
        _ilc = false;
        _eof = false;
        _eot = false;
        _rle = false;
        _bte = false;
        _nxm = false;
    }

    /// <summary>Faithful port of tape_controller_bus_reset
    /// (usim/tape-controller.c:827-835). Does not touch mtcma/mtd/mtrd,
    /// matching the real C exactly (those aren't part of the mts/mtc
    /// structs it memsets).</summary>
    private void BusReset()
    {
        _ilc = false; _eof = false; _eot = false; _rle = false; _bte = false; _nxm = false; _selr = false;
        _den8 = false; _den5 = false; _pevn = false; _slt = 0; _intEnb = false; _fun = 0;
        _mtbrc = 0;
        _cur = true;
    }

    /// <summary>Faithful port of tape_controller_encode_mts
    /// (usim/tape-controller.c:242-294). Bit positions re-verified directly
    /// against this function, not merely transcribed: ILC=15, EOF=14,
    /// CRE=13 (always false), PAE=12 (always false), BGL=11 (always false),
    /// EOT=10, RLE=9, BTE=8, NXM=7, SELR=6, BOT=5, (4 unused), SDWN=3 (always
    /// false), (bit 4/the 7-channel bit are not used, always 0), WRL=2,
    /// RWS=1, TUR=0.</summary>
    private ushort EncodeMts()
    {
        TapeDrive p = SelectedDrive();

        uint v = 0;
        if (_ilc) v |= 1u << 15;
        if (_eof) v |= 1u << 14;
        // cre, pae, bgl never happen in usim -- always false
        if (_eot) v |= 1u << 10;
        if (_rle) v |= 1u << 9;
        if (_bte) v |= 1u << 8;
        if (_nxm) v |= 1u << 7;
        if (_selr) v |= 1u << 6;
        if (p.Bot) v |= 1u << 5;
        // sdwn never happens in usim -- always false
        if (p.ReadOnly) v |= 1u << 2;       // wrl
        if (p.Rws) v |= 1u << 1;            // rws
        if (_ready[p.Unit]) v |= 1u << 0;   // tur

        return (ushort)v;
    }

    /// <summary>Faithful port of tape_controller_encode_mtc
    /// (usim/tape-controller.c:296-317). Bit positions re-verified: ERR=15
    /// (computed, not stored), DEN8=14, DEN5=13, (12 is PCLR, always reads
    /// 0), PEVN=11, SLT=10-8, CUR=7, INT_ENB=6, extended address=5-4 (from
    /// mtcma bits 17-16), FUN=3-1, GO=0 (always reads 0, auto-resets).</summary>
    private ushort EncodeMtc()
    {
        uint v = 0;
        if (IsMtcErr()) v |= 1u << 15;
        if (_den8) v |= 1u << 14;
        if (_den5) v |= 1u << 13;
        if (_pevn) v |= 1u << 11;
        v |= _slt << 8;
        if (_cur) v |= 1u << 7;
        if (_intEnb) v |= 1u << 6;
        v |= ((_mtcma >> 16) & 0x3u) << 4;
        v |= _fun << 1;
        return (ushort)v;
    }

    /// <summary>Faithful port of tape_controller_update_mtc
    /// (usim/tape-controller.c:319-361), collapsed with the go-signalled
    /// run-thread's own body (usim/tape-controller.c:743-780) since this
    /// port is synchronous-only -- both "go" and "no-go" paths converge on
    /// exactly one AssertInterruptIfEnabled() call, matching the real C's
    /// run-thread-unconditional call and the no-go path's own inline call.</summary>
    private void UpdateMtc(ushort v)
    {
        _den8 = Bit(v, 14);
        _den5 = Bit(v, 13);
        bool powerClear = Bit(v, 12);
        _pevn = Bit(v, 11);
        _slt = Bus(v, 10, 8);
        _intEnb = Bit(v, 6);
        uint ea = Bus(v, 5, 4);
        _fun = Bus(v, 3, 1);
        bool go = Bit(v, 0);

        _mtcma = (_mtcma & 0x0000FFFFu) | (ea << 16);

        // "the Select Remote bit is set when the addressed tape drive is on line."
        _selr = SelectedDrive().Online;

        if (powerClear)
        {
            BusReset();
            return;
        }

        if (go)
        {
            if (!_cur)
            {
                // "an interrupt occurs for any tape command initiated during
                // a tape operation (CU RDY bit is false)"
                _ilc = true;
            }
            else
            {
                _cur = false;
                Go();
                _cur = true;
            }
        }
        // else: "an interrupt occurs for an instruction that sets the INT
        // ENB bit but does not set the GO bit" -- and the go path above
        // always asserts too (real C's run-thread calls this unconditionally
        // after every GO cycle), so one call here covers both.
        AssertInterruptIfEnabled();
    }

    /// <summary>Faithful port of tape_controller_go
    /// (usim/tape-controller.c:676-741), minus the COMMAND_STRS trace line.</summary>
    private void Go()
    {
        ResetMtcErr();

        // "any tape command where the selected drive is not on-line (selr
        // bit is false)"
        if (!_selr)
        {
            _ilc = true;
            return;
        }

        TapeDrive p = SelectedDrive();
        _ready[p.Unit] = false;

        // default (0) is 1600 bpi
        p.SetBpi(_den8 ? 800u : 1600u);

        switch (_fun)
        {
            case 0: Offline(p); break;
            case 1: Read(p); break;
            case 2:
            // usim has no concept of ERG, so write/ERG (6) is handled same as write (2)
            case 6: WriteCommand(p); break;
            case 3: WriteEof(p); break;
            case 4: SpaceForward(p); break;
            case 5: SpaceReverse(p); break;
            case 7: Rewind(p); break;
            default:
                throw new InvalidOperationException("tape-controller: invalid command, possible bug");
        }

        _ready[p.Unit] = true;
    }

    /// <summary>Faithful port of tape_controller_offline (usim/tape-controller.c:363-367).</summary>
    private void Offline(TapeDrive p)
    {
        p.RewindAndOffline();
        _eot = p.Eot;
    }

    /// <summary>Faithful port of tape_controller_read (usim/tape-controller.c:370-472).
    /// Does not handle eof/eot cases, matching the real C's own comment.
    /// ReadByte/WriteByte failures are NOT checked -- mtbrc/record_length are
    /// decremented unconditionally regardless of success (0 substituted on a
    /// failed read), exactly matching the real C's own total absence of
    /// error-checking on tape_drive_read_uint8's return value here. This is
    /// what bounds the EOT-mid-transfer case instead of looping forever.</summary>
    private void Read(TapeDrive p)
    {
        _busInterface.ResetBusErrorStatus();

        p.ReadUInt32(out uint preamble);

        if (preamble == TapeDrive.FILE_MARK)
        {
            _eof = true;
            _eot = p.Eot;
            return;
        }

        uint recordLength = preamble;
        bool promDisabled = false;

        while (_mtbrc < 0 && recordLength > 0)
        {
            if (!p.ReadByte(out byte v1)) v1 = 0;
            _mtbrc++;
            recordLength--;

            if (!p.ReadByte(out byte v2)) v2 = 0;
            _mtbrc++;
            recordLength--;

            // little-endian
            ushort data = (ushort)(((uint)v2 << 8) | v1);

            _busAdaptor.WriteUnibus(_mtcma, data, ref promDisabled);

            if (_busInterface.IsUnibusMapError())
            {
                _nxm = true;
                break;
            }
            else if (_busInterface.IsUnibusNxm())
            {
                _nxm = true;
                break;
            }

            // update mtcma here because after NXM error conditions mtcma
            // contains the address of the location in which the failure
            // occurred
            _mtcma += 2;
        }

        if (_nxm)
        {
            // TODO: move tape to interrecord gap (next record start) -- the
            // real C never does this either; see this class's own header
            // comment and TestNxmDuringTransferLeavesPositionWhereErrorOccurred.
        }
        // record length error (RLE): the real record is longer than MTBRC allocated
        else if (recordLength > 0)
        {
            _rle = true;
        }
        else
        {
            p.ReadUInt32(out uint _); // postamble, unused (matches real C)

            p.ReadUInt32(out uint maybeFileMark);
            if (maybeFileMark == TapeDrive.FILE_MARK)
            {
                _eof = true;
            }
            else
            {
                // no file mark, roll back
                p.SeekRelative(-4);
            }
        }

        // Every exit path past the main transfer loop must leave _eot
        // faithful to the drive's true EOT state (real C's tape_drive_seek,
        // usim/tape-drive.c:233-270, recomputes EOT on every byte moved --
        // see this class's header comment). The early file-mark-preamble
        // return above syncs its own case separately since it returns before
        // reaching here.
        _eot = p.Eot;
    }

    /// <summary>Faithful port of tape_controller_write (usim/tape-controller.c:475-570),
    /// named WriteCommand to avoid a name clash with the public
    /// Write(uint offset, ushort value) register-interface method. Every
    /// byte this method moves goes through TapeDrive.WriteByte, which now
    /// (faithfully) advances via TapeDrive.SeekRelative -- mirroring real
    /// C's tape_drive_write_uint8 calling tape_drive_seek(p, 1, SEEK_CUR)
    /// (usim/tape-drive.c:343-353) -- so _eot is synced from p.Eot once
    /// after the transfer completes, matching Read()'s own sync.</summary>
    private void WriteCommand(TapeDrive p)
    {
        // ill_com is set on any write command on a drive which is file protected
        if (p.ReadOnly)
        {
            _ilc = true;
            return;
        }

        // mtbrc is the 2's complement of the number of bytes to transfer
        int recordLength = -_mtbrc;

        // "The minimum record length is two words or four data characters."
        if (recordLength < 4)
        {
            _ilc = true;
            return;
        }
        // LISPM memory addressing is word based
        else if ((recordLength & 0x3) != 0)
        {
            _ilc = true;
            return;
        }

        _busInterface.ResetBusErrorStatus();
        p.WriteRecordLength((uint)recordLength);
        _busInterface.ResetBusErrorStatus();

        while (_mtbrc < 0)
        {
            // read from memory -- ReadUnibus takes no promDisabled parameter
            // (only WriteUnibus does, used by Read(TapeDrive) above instead)
            uint data32 = _busAdaptor.ReadUnibus(_mtcma);
            ushort data = (ushort)data32;

            if (_busInterface.IsUnibusMapError())
            {
                _nxm = true;
                break;
            }
            else if (_busInterface.IsUnibusNxm())
            {
                _nxm = true;
                break;
            }

            // update mtcma here because after NXM error conditions mtcma
            // contains the address of the location in which the failure
            // occurred
            _mtcma += 2;

            // write to tape, little-endian
            byte v1 = (byte)(data & 0xFF);
            byte v2 = (byte)((data >> 8) & 0xFF);

            p.WriteByte(v1);
            _mtbrc++;

            p.WriteByte(v2);
            _mtbrc++;
        }

        if (_nxm)
        {
            // TODO: move tape to interrecord gap (next record start) -- real
            // C's own documented gap, see this class's header comment.
        }
        else if (_mtbrc == 0)
        {
            p.WriteRecordLength((uint)recordLength);
        }

        _eot = p.Eot;
    }

    /// <summary>Faithful port of tape_controller_write_eof (usim/tape-controller.c:572-586).</summary>
    private void WriteEof(TapeDrive p)
    {
        if (p.ReadOnly)
        {
            _ilc = true;
            return;
        }

        p.WriteFileMark();
        _eot = p.Eot;
        // after writing, a read is always performed, so after writing EOF, EOF should be set
        _eof = true;
    }

    /// <summary>Faithful port of tape_drive_space_forward (usim/tape-drive.c:379-407) --
    /// not part of TapeDrive.cs's own surface, so re-implemented here from
    /// the primitives it does expose.</summary>
    private void SpaceForwardOneRecord(TapeDrive p)
    {
        p.ReadUInt32(out uint preamble);
        uint recordLength = preamble;
        // +4 because there is a length also at the end. The real C computes
        // this sum in plain uint32_t arithmetic (no widening cast, unlike
        // SpaceReverseOneRecord's explicit int64_t cast below) -- so it
        // wraps on overflow. This matters: if the current position is
        // sitting exactly on a file mark's own 4 bytes (TAPE_FILE_MARK =
        // 0xFFFFFFFF) rather than before a real record, record_length+4
        // wraps to 3 instead of seeking ~4GB forward. Faithfully
        // reproduced via unchecked 32-bit addition before widening to long.
        uint seekAmount = unchecked(recordLength + 4u);
        p.SeekRelative(seekAmount);

        p.ReadUInt32(out uint maybeFileMark);
        if (maybeFileMark == TapeDrive.FILE_MARK)
        {
            _eof = true;
        }
        else
        {
            // not a filemark, roll back
            p.SeekRelative(-4);
        }

        _eot = p.Eot;
    }

    /// <summary>Faithful port of tape_controller_space_forward
    /// (usim/tape-controller.c:588-618). "To space over a file, the program
    /// can simply give a zero (maximum) byte count."</summary>
    private void SpaceForward(TapeDrive p)
    {
        if (_eot) return;

        if (_mtbrc == 0)
        {
            while (!_eof && !_eot)
            {
                SpaceForwardOneRecord(p);
            }
        }
        else
        {
            while (_mtbrc < 0)
            {
                SpaceForwardOneRecord(p);
                if (_eof || _eot) return;
                _mtbrc++;
            }
        }
    }

    /// <summary>Faithful port of tape_drive_space_reverse (usim/tape-drive.c:409-445) --
    /// not part of TapeDrive.cs's own surface, so re-implemented here. The
    /// real C's "last_pos >= eotpos && new_pos < eotpos -> reset eot"
    /// transition check is redundant with TapeDrive.SeekRelative's own
    /// unconditional Eot recompute (TapeDrive.cs:105), so it's not
    /// separately replicated; this method's own EncodeMts-visible _eot sync
    /// happens via the caller, SpaceReverse.</summary>
    private void SpaceReverseOneRecord(TapeDrive p, bool stopAtFilemark)
    {
        // go back one to read postamble or filemark
        p.SeekRelative(-4);
        p.ReadUInt32(out uint postamble);

        if (postamble == TapeDrive.FILE_MARK)
        {
            if (stopAtFilemark)
            {
                _eof = true;
                _eot = p.Eot;
                return;
            }

            p.SeekRelative(-8);
            p.ReadUInt32(out postamble);
        }

        uint recordLength = postamble;
        // -(record_length+8) because the length is both at the end and at the beginning
        p.SeekRelative(-((long)recordLength + 8));
        _eot = p.Eot;
    }

    /// <summary>Faithful port of tape_controller_space_reverse
    /// (usim/tape-controller.c:620-666).</summary>
    private void SpaceReverse(TapeDrive p)
    {
        if (p.Bot) return;

        if (_mtbrc == 0)
        {
            // the last record might have a file mark -- skip it, go back to
            // the previous file mark. This will not set _eof.
            SpaceReverseOneRecord(p, false);

            while (!_eof && !p.Bot)
            {
                // this will set _eof
                SpaceReverseOneRecord(p, true);
            }
        }
        else
        {
            SpaceReverseOneRecord(p, false);
            _mtbrc++;

            if (!p.Bot)
            {
                while (_mtbrc < 0)
                {
                    SpaceReverseOneRecord(p, true);
                    if (_eof || p.Bot) return;
                    _mtbrc++;
                }
            }
        }
    }

    /// <summary>Faithful port of tape_controller_rewind (usim/tape-controller.c:668-674).
    /// tape_drive_rewind's own Rws true/false bracketing (usim/tape-drive.c:275-287)
    /// has no equivalent in TapeDrive.cs's surface beyond RewindAndOffline's
    /// internal, non-reusable version -- but since this port is fully
    /// synchronous, no caller can ever observe Rws mid-operation regardless
    /// (matches this class's own treatment of the "ready" flag for the same
    /// reason), so a plain SeekAbsolute(0) is observably identical.</summary>
    private void Rewind(TapeDrive p)
    {
        p.SeekAbsolute(0);
        _eot = p.Eot;

        // "an interrupt occurs whenever a rewinding tape unit arrives at BOT"
        if (p.Bot) AssertInterruptIfEnabled();
    }

    /// <summary>Faithful port of tape_controller_unibus_read
    /// (usim/tape-controller.c:867-914). Every one of the 6 real offsets is
    /// handled; anything else throws (matches the real C's own fatal
    /// default-case errx).</summary>
    public void Read(uint offset, out ushort value)
    {
        switch (offset)
        {
            case MtsOffset: value = EncodeMts(); break;
            case MtcOffset: value = EncodeMtc(); break;
            case MtbrcOffset: value = unchecked((ushort)_mtbrc); break;
            case MtcmaOffset: value = (ushort)(_mtcma & 0xFFFFu); break;
            case MtdOffset: value = _mtd; break;
            case MtrdOffset: value = _mtrd; break;
            default:
                throw new InvalidOperationException($"tape-controller: unknown read offset {offset}");
        }
    }

    /// <summary>Faithful port of tape_controller_unibus_write
    /// (usim/tape-controller.c:916-959). MTS (status) and MTD (data buffer)
    /// have NO case in the real C's write switch -- both are read-only
    /// registers, and writing either is an unknown-write there (fatal errx),
    /// not a silent no-op. This matches DiskController.Write's own
    /// "unknown offset throws" convention for its own default case.</summary>
    public void Write(uint offset, ushort value)
    {
        switch (offset)
        {
            case MtcOffset: UpdateMtc(value); break;
            case MtbrcOffset: _mtbrc = unchecked((short)value); break;
            case MtcmaOffset: _mtcma = (_mtcma & 0xFFFF0000u) | value; break;
            case MtrdOffset: _mtrd = value; break;
            default:
                throw new InvalidOperationException($"tape-controller: unknown write offset {offset}");
        }
    }
}
