# Disk Subsystem Port — Design Spec

## Context

This project has already closed a full 9-phase microcode-engine port and, separately, a faithful port of `usim/bus-interface.c`. Both left the real disk subsystem explicitly out of scope. Today, `usim-cs/DiskController.cs` and its nested `DiskUnit` class are **entirely invented** — fabricated register names (`StatusRegister`/`ErrorRegister`/`DataRegister`), fabricated geometry constants (`SECTOR_SIZE=256`, `SECTORS_PER_TRACK=16`, `TRACKS_PER_CYLINDER=8`), and a `Seek()`/`ReadSector()`/`WriteSector()` API with no relationship to the real hardware. This is the same category of problem the original `UCode.cs` had before this whole project started, and needs the same treatment: a full faithful rewrite, not incremental fixes.

`BusAdaptor.cs`'s existing disk-control stub (`ReadXbusIo`/`WriteXbusIo` at the `DiskControlLo`-`DiskControlHi` range) is a deliberate, honestly-labeled placeholder from the earlier bus-adaptor work: offset 0 (status) always reports "ready, online, no errors"; every other offset/write is a no-op. This spec replaces that stub with real dispatch.

Real source: `usim/disk-controller.c` (839 lines) + `usim/disk-controller.h`, `usim/disk-unit.c` (345 lines) + `usim/disk-unit.h`.

## Decisions

- **Synchronous transfer only, no background thread.** The real C supports two real, always-available modes gated by a compile flag: a blocking mode (a transfer completes immediately, synchronously, within the call that starts it — the `#ifndef WITH_NONBLOCKING_DISKIO do_xfer(); #endif` path) and a non-blocking mode (a pthread services a queued `xfer_req` asynchronously, with `disk_unit_rotate()` simulating platter rotation during idle polling). `usim-cs` is single-threaded and step-driven throughout — no other subsystem uses real threads. This port implements the blocking-mode semantics only: `submit_xfer`'s `xfer_req` handoff struct is unnecessary (nothing defers it) and collapses into one direct call chain (decode DA → seek → transfer → interrupt). `disk_unit_rotate()` has no synchronous-mode caller and is not ported.
- **`FileStream`, not `mmap`.** The real C memory-maps the disk-pack file (`mmap`/`memcpy`). This is a C performance/implementation detail, not a behavioral requirement — a `FileStream` with `Seek`+`Read`/`Write` at the same byte offsets produces identical observable behavior and matches how `MainMemory.cs`'s own `LoadFromFile`/`SaveToFile` already do file I/O in this codebase.
- **`idle_disk_activity()` is not ported.** It's a macro that's either a real call (`idle_activity()`, under `WITH_SDL3`) marking disk activity for an idle/auto-power-off timer, or a no-op otherwise. The whole `idle.c`/`idle.h` activity-timer subsystem is unrelated pre-existing out-of-scope work — nothing in `usim-cs` implements it (confirmed: no `Idle`-named class or member exists anywhere in `usim-cs/`). This call site is simply omitted, not stubbed.
- **`LABEL_LABL` (`usim/disk-unit.c:44`) is dead code** — a `#define` with zero references anywhere in the real C (confirmed via grep). Not ported.
- **Real bus-error/NXM assertion on the disk-control range remains explicitly out of scope**, per the bus-interface spec's own ruling (`docs/superpowers/specs/2026-09-07-bus-interface-design.md`'s Out of Scope section) — this port does not change that.
- **Config format changes to match the real C.** The current `ConfigManager.cs`'s `[Disk]` section (`disk_image`/`disk_size`/`cache_size`/`enable_dma`) is itself invented and matches nothing in the real C. Replaced with the real `[disk]` section: `disk0`-`disk7`, each `TYPE,filename` (e.g. `disk0 = T-300,/path/to/pack.img`), parsed the same way `disk_unit_init`'s `strtok(config_string, ",")` does. A unit with no config line, or an empty config string, stays offline — matching the real C exactly (no config-driven behavior is invented here).
- **`DiskUnit.ReadOnly` is permanently `false` in this port** — verified via grep: unlike `tape-drive.c` (which genuinely has a config-driven `"ro"`/`"rw"` mode), nothing in `disk-unit.c`/`disk-controller.c` ever sets `read_only` to `true`; disk units are always opened read-write. The field still exists and correctly gates `EncodeStatus()`'s bit 7 and `start_write()`'s fault behavior (for fidelity, and in case a future config format adds it), but the config format in this spec has no read-only option — do not invent one.

## Octal Literals — Independently Verified (read this before writing any C#)

Every C-style octal literal in `disk-controller.c`/`disk-unit.c`, converted via script (`python -c "print(hex(int('LIT',8)))"`), not by hand. C# has no octal integer-literal syntax — this exact class of bug already cost two fix rounds in the bus-interface port; every value below must be used as its hex form in the C# port, with the octal source noted only in a comment.

| Octal (as written in C) | Decimal | Hex | Where / what it means |
|---|---|---|---|
| `07` | 7 | `0x7` | `SELECTED_UNIT()`/`decode_da`'s unit field mask (3 bits) — safe either way (single octal digit) but use `0x7` for consistency |
| `07777` | 4095 | `0xFFF` | `decode_da`'s cylinder field mask (12 bits) — **bug risk: naive `07777` in C# = decimal 7777** |
| `0377` | 255 | `0xFF` | `decode_da`'s head/block field masks (8 bits) — **bug risk: naive `0377` in C# = decimal 377** |
| `017` | 15 | `0xF` | `start()`'s `cmd & 017` (low 4 command bits) — **bug risk: naive `017` = decimal 17** |
| `010` | 8 | `0x8` | command value: read-compare |
| `011` | 9 | `0x9` | command value: write |
| `002` | 2 | `0x2` | command value: read-all (unimplemented in real C too — `errx(1, "read all not implemented")`) |
| `013` | 11 | `0xB` | command value: write-all (unimplemented in real C too — `errx(1, "write all not implemented")`) |
| `004` | 4 | `0x4` | command value: seek |
| `005` | 5 | `0x5` | command value: at-ease |
| `006` | 6 | `0x6` | command value: offset-clear |
| `000` | 0 | `0x0` | command value: read |
| `01000` | 512 | `0x200` | combined-with-at-ease bit: recalibrate (bit 9 of `cmd`) |
| `00400` | 256 | `0x100` | combined-with-at-ease bit: fault-clear (bit 8 of `cmd`) |
| `016` (also written `0016`) | 14 | `0xE` | reset command's magic value (`v == 016`) — **bug risk: naive `016`/`0016` = decimal 16** |
| `04000` | 2048 | `0x800` | command-register write mask: done-interrupt-enable (bit 11) |
| `02000` | 1024 | `0x400` | command-register write mask: attention-interrupt-enable (bit 10) |

`encode_status()`'s bit masks are all written as `(1 << N)` in the real C (bits 0,1,2,3,6,7,9,10,20,21,22) — no octal-literal risk there, port as `1 << N` directly.

## Register Model

Four registers at the `DiskControlLo`-`DiskControlHi` offsets `BusAdaptor.cs` already defines (offset = `paddr - DiskControlLo`, 0-3). **While `_resetCondition` is set, every read (all four offsets) returns `0` unconditionally** — this check happens before the offset switch in the real C (`disk_controller_read`'s very first line), not per-offset; port it the same way, as a single early return at the top of `Read()`.

| Offset | Read | Write |
|---|---|---|
| 0 | `EncodeStatus()` | Command register: `0` exits reset; `0xE` (`016` octal) enters reset (subsequent writes to offsets 1-3 are ignored until a `0` write exits it); otherwise sets `cmd`, and bits `0x800`/`0x400` gate `doneInterruptEnable`/`attentionInterruptEnable` (deasserting the interrupt if both become false) |
| 1 | Selected unit's `LastMemoryAddress` | CLP (channel list pointer) register |
| 2 | `da` (disk-address register, read back as last set) | `da` (disk-address register) |
| 3 | `0` (no ECC errors modeled, matching the real C's comment) | `Start()` — dispatches on `cmd & 0xF` to the command state machine |

`EncodeStatus()` bits (independently verified, see table above for method): bit 0 = not-active, bit 1 = any-unit-attention (OR across all 8 units), bit 2 = selected-unit attention, bit 3 = interrupt-request, bit 6 = selected-unit has-fault, bit 7 = selected-unit read-only, bit 9 = **not** online (inverted), bit 10 = selected-unit seek-error, bit 20 = nonexistent-memory-error, bit 21 = ccw-cycle, bit 22 = read-compare-difference. "Selected unit" is **not** a separately-tracked field — it's computed fresh on every call as `(da >> 28) & 0x7` from the current `da` register value, exactly like `DecodeDa` below (the real C's `SELECTED_UNIT_PTR()` macro does the same recomputation on every use, never caches it).

`DecodeDa(da)`: unit = bits 31-28 (`0x7`), cylinder = bits 27-16 (`0xFFF`), head = bits 15-8 (`0xFF`), block = bits 7-0 (`0xFF`). `Da()` (encode, for the read-back after a transfer): `(unit << 28) | (cylinder << 16) | (head << 8) | sector`.

Command values (low 4 bits of `cmd`, dispatched in `Start()`): `0x0`=read, `0x8`=read-compare, `0x9`=write, `0x2`=read-all (real C: fatal, unimplemented — port the same fatal behavior, do not implement), `0xB`=write-all (same), `0x4`=seek, `0x5`=at-ease (plus, if `cmd & 0x200` set, also recalibrate; if `cmd & 0x100` set, also fault-clear), `0x6`=offset-clear. Any other value is fatal in the real C (`errx`) — port as a thrown exception, matching this codebase's existing convention for the real C's other `errx()` fatal-misuse guards (e.g. `UCode.MfRead`'s unknown-register case).

## Disk Unit Model

Two real drive types (`disk_unit_types[]`, name lookup case-insensitive matching `strcasecmp`):

| Name / short name | Cylinders | Heads | Blocks/track |
|---|---|---|---|
| "Trident T-80" / "T-80" | 815 | 5 | 17 |
| "Trident T-300" / "T-300" | 815 | 19 | 17 |

Block size: 1024 bytes = 256 words (matches `MainMemory.PAGE_SIZE` exactly — disk blocks and main-memory pages are the same size, which is why the real C's DMA transfer works page-at-a-time). On mount, the backing file's size MUST exactly equal `cylinders * heads * blocks_per_track * 1024` — a mismatch is a fatal error in the real C (`errx`), not a warning; port the same.

`DiskUnit` state: `Unit`, `TypeName`, `Cylinders`/`Heads`/`BlocksPerTrack`/`BlocksPerCylinder` (`= Heads * BlocksPerTrack`), `Configured`/`Online`/`ReadOnly`, dynamic `SeekError`/`HasFault`/`Attention`, `LastMemoryAddress`, current `Cylinder`/`Head`/`Sector` + `Lba`.

`DiskUnit` methods (faithful ports): `Read(buffer)`/`Write(buffer)` (256-word block at `Lba * 1024` byte offset, `false` if the offset is past the file's end — matching the real C's bounds check, not throwing); `Seek(cylinder, head, sector)` (no-op success if already there; sets `SeekError` and returns `false` if any component is out of range; otherwise updates position and recomputes `Lba`); `SeekNextLba()` (advances sector, rolling into head then cylinder, then calls `Seek`); `RaiseAttention()` (sets `Attention` and notifies the controller — see Wiring); `Da()` (encode current position, see above).

## Class Shape

`usim-cs/DiskController.cs` (full rewrite):

```csharp
public class DiskController
{
    public const int NUMBER_OF_DISK_UNITS = 8;

    private readonly MainMemory _mainMemory;
    private readonly UCode _ucode;
    private readonly DiskUnit[] _units;

    private uint _cmd;
    private uint _clp;
    private uint _da;
    private bool _resetCondition;

    private bool _readCompareDifference;
    private bool _ccwCycle;
    private bool _nonexistentMemoryError;
    private bool _interruptRequest;
    private bool _notActive = true;

    private bool _doneInterruptEnable;
    private bool _attentionInterruptEnable;

    public DiskController(MainMemory mainMemory, UCode ucode)
    {
        _mainMemory = mainMemory;
        _ucode = ucode;
        _units = new DiskUnit[NUMBER_OF_DISK_UNITS];
        for (int i = 0; i < NUMBER_OF_DISK_UNITS; i++) _units[i] = new DiskUnit((uint)i);
    }

    // Config-driven mount, called once per configured unit at power-on --
    // mirrors disk_unit_init's role, not disk_controller_init's (which
    // only logs and, in the excluded non-blocking mode, starts the thread).
    public void ConfigureUnit(uint unit, string typeName, string filename) { ... }

    public uint Read(uint offset) { ... }   // faithful port of disk_controller_read
    public void Write(uint offset, uint v) { ... } // faithful port of disk_controller_write
    public void BusReset() { ... }          // disk_controller_bus_reset -- empty in the real C too
}
```

`usim-cs/DiskUnit.cs` (full rewrite, separate file — this codebase's convention is one responsibility per file, and the old `DiskController.cs` wrongly nested `DiskUnit` inside it):

```csharp
public class DiskUnit
{
    public uint Unit { get; }
    public string TypeName { get; private set; } = "";
    public uint Cylinders { get; private set; }
    public uint Heads { get; private set; }
    public uint BlocksPerTrack { get; private set; }
    public uint BlocksPerCylinder { get; private set; }

    public bool Configured { get; private set; }
    public bool Online { get; private set; }
    public bool ReadOnly { get; private set; }

    public bool SeekError { get; set; }
    public bool HasFault { get; set; }
    public bool Attention { get; private set; }

    public uint LastMemoryAddress { get; set; }
    public uint Cylinder { get; private set; }
    public uint Head { get; private set; }
    public uint Sector { get; private set; }
    public uint Lba { get; private set; }

    private FileStream? _file;

    public DiskUnit(uint unit) { Unit = unit; }

    public void Configure(string typeName, string filename) { ... } // faithful port of disk_unit_init
    public bool Read(uint[] buffer) { ... }         // disk_unit_read
    public bool Write(uint[] buffer) { ... }        // disk_unit_write
    public bool Seek(uint cylinder, uint head, uint sector) { ... } // disk_unit_seek
    public bool SeekNextLba() { ... }               // disk_unit_seek_next_lba
    public void RaiseAttention() => Attention = true; // disk_unit_raise_attention, minus the
                                                        // callback -- see Wiring for why
    public uint Da() { ... }                        // disk_unit_da
}
```

`MainMemory.cs` additions:

```csharp
public bool ReadPage(uint physicalAddress, uint[] buffer) // buffer.Length == PAGE_SIZE
{
    uint pn = (physicalAddress >> PAGE_SIZE_BITS) & 0x3FFF;
    if (pn < _npages)
    {
        Array.Copy(_physicalMemory, (int)(pn * PAGE_SIZE), buffer, 0, PAGE_SIZE);
        return true;
    }
    LogOutOfRangeAccess(pn, "read page");
    return false;
}

public bool WritePage(uint physicalAddress, uint[] buffer)
{
    uint pn = (physicalAddress >> PAGE_SIZE_BITS) & 0x3FFF;
    if (pn < _npages)
    {
        Array.Copy(buffer, 0, _physicalMemory, (int)(pn * PAGE_SIZE), PAGE_SIZE);
        return true;
    }
    LogOutOfRangeAccess(pn, "write page");
    return false;
}

// Real bool success/failure, unlike ReadPhysical's 0xFFFFFFFF-sentinel
// convention -- perform_xfer's channel-command-word read needs to tell
// "out of range" apart from "the word happens to be all-ones", which the
// sentinel convention cannot (a real, if narrow, ambiguity already
// accepted elsewhere in this codebase for UCode.Vm(), not repeated here
// since this consumer's correctness is more exposed to it).
public bool TryReadWord(uint physicalAddress, out uint value)
{
    uint pn = (physicalAddress >> PAGE_SIZE_BITS) & 0x3FFF;
    if (pn < _npages && physicalAddress < PHYSICAL_MEM_SIZE)
    {
        value = _physicalMemory[physicalAddress];
        return true;
    }
    LogOutOfRangeAccess(pn, "read");
    value = 0xFFFFFFFF;
    return false;
}
```

## Wiring

This introduces a genuine two-way dependency, unlike the bus-interface port's one-way case: `BusAdaptor` needs `DiskController` (to dispatch the disk-control register range), and `DiskController` needs `UCode` (to assert/deassert the Xbus interrupt on completion, via the already-real `UCode.AssertXbusInterrupt`/`DeassertXbusInterrupt` from Phase 8). A pure constructor-injection chain can't resolve this the way bus-interface's did (there, only `BusInterface→UCode` was a real dependency; `UCode` never needed `BusInterface` until `BusAdaptor` was built).

Resolution: keep `UCode`'s constructor signature exactly as it is today (unchanged — no new parameter), and add one explicit post-construction wiring call:

1. `MachineControl`'s constructor order changes from `Memory → DiskController → ... → UCode` to: `Memory = new MainMemory(); UCode = new UCode(Memory); DiskController = new DiskController(Memory, UCode); UCode.BusAdaptor.WireDiskController(DiskController);` (rest of the constructor — `Keyboard`/`Mouse`/`Display`/`IOBus` — unchanged, order relative to these doesn't matter).
2. `BusAdaptor.cs` gains a settable-once `DiskController` field and a `WireDiskController(DiskController dc)` method (not a constructor parameter — `BusAdaptor` is built inside `UCode`'s own constructor, before `DiskController` can exist). `ReadXbusIo`/`WriteXbusIo`'s disk-control-range branch changes from the hardcoded stub to `_diskController.Read(offset)`/`.Write(offset, v)`.
3. The real C's `disk_unit_s` stores a persistent function-pointer field (`disk_controller_get_attention`), set once at init, and `disk_unit_raise_attention` calls it internally. Since `DiskController` is the only caller of `RaiseAttention` either way, this collapses to two explicit, sequential calls at each of the three real call sites (`start_seek`, `start_recalibrate`, `start_at_ease`'s equivalents): `unit.RaiseAttention(); OnDiskUnitAttention(unit);` — where `OnDiskUnitAttention` is `DiskController`'s own private method (mirrors `disk_controller_get_attention`'s real body: asserts the interrupt only if `_notActive && _attentionInterruptEnable`). Same effective call sequence and timing as the real C, without introducing a stored-callback field this codebase doesn't use elsewhere.
4. `MachineControl`'s existing `DiskController.Mount(0, diskImage)` call (from the old invented API, in `LoadSystemFiles` or similar) is replaced with reading the new `[disk]` config section and calling `ConfigureUnit` once per configured unit (0-7).

## Testing

New `DiskUnitTests.cs`:
- `Seek` success (already-there no-op, and a real move) and failure for each out-of-range component (cylinder/head/sector), confirming `SeekError` is set only on failure.
- `SeekNextLba`'s sector→head→cylinder rollover, using a backing file sized for a real type (created via `File.SetLength` — sparse, not a real multi-hundred-MB write).
- `Read`/`Write` round-trip a 256-word buffer at a known `Lba`; both return `false` past end-of-file without throwing.
- `Da()`/round-trip against a hand-computed value.

New `DiskControllerTests.cs`:
- Every `Start()` command path (read/read-compare/write/seek/recalibrate/fault-clear/at-ease/offset-clear, and the combined at-ease+recalibrate+fault-clear bit pattern) against a mounted synthetic unit.
- `EncodeStatus()` bit-for-bit against the verified table, one bit at a time.
- `DecodeDa`/`Da` round-trip.
- Reset protocol: writing `0xE` to offset 0 enters reset (subsequent offset 1-3 writes are ignored); writing `0` exits it.
- Read-only unit + write command sets `HasFault`, does not transfer.
- A full read/write transfer through `MainMemory` (a real CCW chain of at least 2 blocks, to exercise the "is it the last CCW" bit-0-of-CCW-word chaining), confirming both the disk-side and memory-side contents end up correct and `LastMemoryAddress` matches the real C's documented off-by-one-page convention (set to the transfer's start address before the page call, then `start+255` after success).
- Interrupt assert/deassert observed through `UCode.InterruptStatusReg`, not a mock.

`MainMemoryTests.cs` additions: `ReadPage`/`WritePage` round-trip; `TryReadWord`'s bool return discriminates in-range from out-of-range (a real, non-trivial assertion, not a "doesn't throw" check).

Config test additions (wherever `ConfigParser`/`ConfigManager` tests live): `[disk]` section parses `disk0`-`disk7` into type+filename; a unit with no line, or an empty value, stays unconfigured/offline.

`BusAdaptorTests.cs` addition: a real-dispatch-confirmation test for the disk-control range, built the same discriminating way the bus-interface fix rounds required (assert on a value only the real `DiskController` path can produce — e.g. `EncodeStatus()`'s not-active bit reflecting actual transfer state, not the old stub's hardcoded always-ready value).

**Final-review real-data verification (not a checked-in fixture):** mount the real `usim/disk.img` (T-300, already in the repo, confirmed exactly `815*19*17*1024 = 269,562,880` bytes) read-only, issue a real read command through the register interface for a known cylinder/head/block, and cross-check the returned 256-word buffer against an independent raw read of the same file offset via a plain `FileStream` — proving the ported code reads the exact right bytes from a real CADR disk pack.

## Global Constraints

- No new NuGet dependencies.
- Every octal literal in this spec was independently verified via script (table above) — the implementer must use the hex forms given, not re-derive by hand.
- Non-blocking/threaded disk I/O, `disk_unit_rotate`, `idle_disk_activity`, and `LABEL_LABL` are not ported — permanent scope boundaries for this spec, not deferrals (see Decisions).
- Read-all/write-all commands remain fatal/unimplemented, matching the real C exactly (`errx(1, ...)` in C ports to a thrown exception in C#, following this codebase's established convention for the real C's other fatal-misuse guards).
- A disk-pack file whose size doesn't match its configured type's expected size is a fatal error at mount time, matching the real C — not a silent truncation or a warning.
- **A configured disk-pack file that doesn't already exist is also a fatal error at mount time** — the real C opens with `O_RDWR` only, never creates the file (`errx` if `open()` fails). The old invented `DiskUnit.Mount` used `FileMode.OpenOrCreate`, silently creating an empty file; that behavior is not faithful and must not be preserved.

## Out of Scope (for this spec)

- Non-blocking (threaded) disk I/O and disk rotation simulation — a real, separate mode in the C; not needed given this codebase's single-threaded architecture (see Decisions).
- `idle.c`/`idle.h`'s whole activity-timer/auto-power-off subsystem — unrelated pre-existing gap, nothing in `usim-cs` implements it today.
- Wiring `BusInterface`'s NXM setters into this or any other still-placeholder `BusAdaptor` device path — already ruled out of scope by the bus-interface spec; unaffected by this one.
- Tape, TV/color-TV, Unibus-Map DMA device emulation — each remains its own future spec.
