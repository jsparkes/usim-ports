# Unibus-Map DMA Port — Design Spec

## Context

This project has already closed faithful ports of `bus-interface.c` and the disk subsystem. Both left "Unibus Map DMA" explicitly out of scope, currently a logged, honestly-labeled placeholder in `BusAdaptor.cs` at the `UnibusMapLo`-`UnibusMapHi` range (`0xC000`-`0xFFFF`, matching `0140000`-`0177777` octal exactly).

This feature is smaller-looking than it is: `usim/unibus-mapping.c` (95 lines) only holds the 16 mapping registers' storage (read/write at `0766140`-`0766176` octal = `0x3EC60`-`0x3EC7E`, already anticipated by `BusAdaptor.cs`'s existing constants). The actual DMA *translation* — the part that makes this a real feature, not just registers — lives in `usim/bus-adaptor.c:210-367`, inside `bus_adaptor_unibus_rw`. When microcode addresses the Unibus range `0140000`-`0177777` (16 pages of 512 Unibus words each, matching the CADR's own virtual-memory page size), that function looks up the matching mapping register and translates the access into a real Xbus physical address, targeting either main memory (the common real-world case — tape/disk DMA into main memory goes through this exact path) or the Xbus I/O device range (TV/color-TV/disk-control), using the same page-number split `usim/bus-adaptor.c`'s own `bus_adaptor_xbus_rw` uses internally. A page whose translated Xbus page number is `≥037000` (`0x3E00`) is a documented diagnostic backdoor that reads/writes the CPU's own `MD` register directly instead of touching memory.

Real source: `usim/unibus-mapping.c` (95 lines) + `usim/unibus-mapping.h`, plus `usim/bus-adaptor.c:164-367` (the `bus_adaptor_xbus_rw`/`bus_adaptor_unibus_rw` functions' Unibus-Map-handling branch).

## Decisions

- **This spec ports both halves as one feature**, not just the register file. A register-only port would leave the actual DMA behavior — the reason this feature exists — unported, the same mistake that would have happened porting `bus-interface.c` without its consumer wiring.
- **`BusAdaptor` gains two new constructor dependencies: `MainMemory` and `UCode`.** The DMA-translation path can legitimately target main memory (page number `≤035773` = `0x3BFB`), which `BusAdaptor`'s existing public `Read`/`Write(paddr)` entry points cannot handle — those assume `UCode.Vm()` has already filtered out the main-memory range before ever calling `BusAdaptor` (true for microcode-issued accesses, not true for a DMA transfer routed through the Unibus map). This needs its own internal dispatcher replicating `bus_adaptor_xbus_rw`'s full split (main memory / Xbus I/O / NXM), not a call through the existing Unibus-focused entry points. `UCode` is needed for the diagnostic `MdReg` backdoor (already a real field, from Phase 1). Both are passed the same safe way `BusInterface` already is: `UCode`'s constructor passes itself into `BusAdaptor` before its own construction finishes, and `BusAdaptor`'s constructor only stores the reference — it's never touched until a later DMA call, not during construction. This is a one-way constructor growth, not a new circular dependency.
- **`ColortvEnabled`-gated dispatch is out of scope for now.** The real C guards the color-TV branches of `bus_adaptor_xbusio_rw` with a `colortv_enabled` runtime flag; since color TV isn't ported yet, `BusAdaptor.cs`'s existing placeholder for that range is untouched by this spec — DMA transfers that happen to target it still fall through to that existing, honestly-labeled placeholder, exactly as a normal (non-DMA) access to that range already does today.

## Octal Literals — Independently Verified

| Octal | Decimal | Hex | Meaning |
|---|---|---|---|
| `0140000` | 49152 | `0xC000` | Unibus-Map range start (already `BusAdaptor.UnibusMapLo`) |
| `0177777` | 65535 | `0xFFFF` | Unibus-Map range end (already `BusAdaptor.UnibusMapHi`) |
| `02000` | 1024 | `0x400` | Bytes per mapped page (512 Unibus words) |
| `037777` | 16383 | `0x3FFF` | Xbus page number mask (14 bits) within a mapping register |
| `037000` | 15872 | `0x3E00` | MD-register diagnostic backdoor threshold (`xbus_page_number >=`) |
| `0766140` | 257120 | `0x3EC60` | First mapping register address (already `BusAdaptor.UnibusMappingLo`) |
| `0766176` | 257150 | `0x3EC7E` | Last mapping register address (already `BusAdaptor.UnibusMappingHi`) |
| `0377` | 255 | `0xFF` | Low-8-bits-of-uaddr mask in the paddr-assembly formula |
| `035773` | 15355 | `0x3BFB` | Main-memory/Xbus-I/O split, high end of main memory (already used by `UCode.Vm()`) |
| `036000` | 15360 | `0x3C00` | Xbus-I/O range start |
| `036777` | 15871 | `0x3DFF` | Xbus-I/O range end (already `BusAdaptor`'s own `dispatchPn <= 0x3DFF` check) |

Register bit masks (`(1 << N)` forms in the real C, no octal-literal risk): bit 15 = MAP_VALID (`0x8000`), bit 14 = WRITE_PERMIT (`0x4000`).

## Register Model (the 16 mapping registers, `unibus-mapping.c`'s own scope)

`Read(uaddr)`/`Write(uaddr, v)` at `0x3EC60`-`0x3EC7E` (16 registers, 2 bytes apart — every address in this exact range is a valid register; there is no "in-range but invalid" case reachable from `BusAdaptor`'s own range-gated dispatch, though the real C's `default:` case, `warnx()` + `SetUnibusNxm()`, is still ported faithfully for completeness):

- `pageNo = (uaddr - 0x3EC60) / 2`
- Read: return the stored register value.
- Write: store the value.

Each register: bit 15 = MAP_VALID, bit 14 = WRITE_PERMIT, bits 13-0 = Xbus page number.

Plus 16 word-buffers (`unibus_mapping_buffers[16]`), used only by the DMA-translation path below to bridge one 16-bit Unibus half-cycle to the next.

## DMA Translation (`bus-adaptor.c`'s scope — the real feature)

Called when `BusAdaptor`'s Unibus dispatch sees `uaddr` in `0xC000`-`0xFFFF`:

1. `pageNo = (uaddr - 0xC000) / 0x400`.
2. Look up mapping register `pageNo`; decode `mapValid` (bit 15), `writePermit` (bit 14), `xbusPageNumber` (bits 13-0, mask `0x3FFF`).
3. Assemble `paddr = (xbusPageNumber << 8) | ((uaddr >> 2) & 0xFF)` — bits 9-2 of `uaddr` become the low 8 bits of `paddr`.
4. `!mapValid` → on read, `pv = 0`; either way call `BusInterface.SetUnibusMapError()`; return.
5. `write && !writePermit` → call `BusInterface.SetUnibusMapError()`; return (do not zero `pv`; the real C doesn't for this case, only for `!mapValid`).
6. `hiword = (uaddr >> 1) & 1` — Unibus is 16-bit, Xbus is 32-bit, so each Xbus word takes two Unibus half-cycles.
7. If `xbusPageNumber >= 0x3E00`: diagnostic backdoor — read/write `UCode.MdReg` directly (16 bits at a time, `hiword` selecting the high or low half), never touching memory or any device.
8. Otherwise, real Xbus transfer, buffered across the two halves via the 16 word-buffers:
   - Write, low half: cache `pv` in `buffers[pageNo]`; no transfer yet.
   - Write, high half: combine the cached low half with `pv` (now the high half) into a 32-bit word; transfer it via the Xbus dispatcher below.
   - Read, low half: transfer a 32-bit word via the Xbus dispatcher; return its low 16 bits; cache the high 16 bits in `buffers[pageNo]` for the next (high-half) read.
   - Read, high half: return the cached value from `buffers[pageNo]` — no new transfer.

**Xbus dispatcher** (faithful port of `bus_adaptor_xbus_rw`, split into new internal `XbusRead`/`XbusWrite` methods — distinct from the existing public `Read`/`Write(paddr)`, which cannot be reused here per the Decisions section):
- `pn = (paddr >> 8) & 0x3FFF`.
- `pn <= 0x3BFB` → real main memory: `MainMemory.ReadPhysical`/`WritePhysical(paddr, ...)`.
- `0x3C00 <= pn <= 0x3DFF` → Xbus I/O device range: the existing private `ReadXbusIo`/`WriteXbusIo(paddr, ...)` methods (already handle TV/color-TV/disk-control dispatch, including today's honestly-labeled placeholders for not-yet-ported devices).
- Otherwise → log a warning, `BusInterface.SetXbusNxm()`, and (on read) return 0 — matches the real C's own `else` branch in `bus_adaptor_xbus_rw`.

## Class Shape

`usim-cs/UnibusMapping.cs` (new):

```csharp
public class UnibusMapping
{
    private readonly ushort[] _registers = new ushort[16];
    private readonly ushort[] _buffers = new ushort[16];
    private readonly BusInterface _busInterface;

    public UnibusMapping(BusInterface busInterface) { _busInterface = busInterface; }

    public ushort GetRegister(uint pageNo) => _registers[pageNo];
    public ushort GetBuffer(uint pageNo) => _buffers[pageNo];
    public void SetBuffer(uint pageNo, ushort value) => _buffers[pageNo] = value;

    // Faithful port of unibus_mapping_read/write (usim/unibus-mapping.c:19-95).
    public uint Read(uint uaddr) { ... }
    public void Write(uint uaddr, uint v) { ... }
}
```

`BusAdaptor.cs` additions (constructor grows to accept `MainMemory` and `UCode`; a `UnibusMapping` instance is built internally, mirroring how `BusInterface` already owns its own state):

```csharp
private readonly MainMemory _mainMemory;
private readonly UCode _ucode;
private readonly UnibusMapping _unibusMapping;

public BusAdaptor(BusInterface busInterface, MainMemory mainMemory, UCode ucode)
{
    _busInterface = busInterface;
    _mainMemory = mainMemory;
    _ucode = ucode;
    _unibusMapping = new UnibusMapping(busInterface);
}

// Dispatched from ReadUnibus/WriteUnibus for the UnibusMapLo..UnibusMapHi range.
private uint UnibusMapDmaRead(uint uaddr) { ... }
private void UnibusMapDmaWrite(uint uaddr, uint v) { ... }

// Faithful port of bus_adaptor_xbus_rw (usim/bus-adaptor.c:164-193), split
// into a Read/Write pair to match this file's existing ReadXbusIo/
// WriteXbusIo and ReadUnibus/WriteUnibus convention rather than the real
// C's single bool-flagged function.
private uint XbusRead(uint paddr) { ... }
private void XbusWrite(uint paddr, uint v) { ... }
```

## Wiring

1. `UCode.cs`'s constructor changes from `BusAdaptor = new BusAdaptor(BusInterface);` to `BusAdaptor = new BusAdaptor(BusInterface, mainMemory, this);` — passing the constructor's own `mainMemory` parameter (already held before this line runs) and `this` (safe, per Decisions).
2. `BusAdaptor.cs`'s `ReadUnibus`/`WriteUnibus`: the existing `UnibusMappingLo`/`UnibusMappingHi` branch (register access, `0x3EC60`-`0x3EC7E`) dispatches to the new `UnibusMapping.Read`/`Write`. A NEW branch for `UnibusMapLo`/`UnibusMapHi` (`0xC000`-`0xFFFF`) dispatches to `UnibusMapDmaRead`/`UnibusMapDmaWrite`, replacing that range's current generic-fallback placeholder.
3. Every existing test/call site that constructs a bare `BusAdaptor` directly (there are several, from the bus-interface and disk work) needs updating for the new 3-argument constructor.

## Testing

New `UnibusMappingTests.cs`:
- Register read/write round-trip for all 16 registers.
- The buffer accessors round-trip.
- Real C default-case behavior for the (practically unreachable via `BusAdaptor`, but still faithfully ported) out-of-range case.

New `BusAdaptorTests.cs` additions (or a new `UnibusMapDmaTests.cs` — implementer's choice, follow existing file-size conventions):
- `!MAP_VALID` read/write → `SetUnibusMapError()` called, read returns 0.
- Write with `!WRITE_PERMIT` → `SetUnibusMapError()` called, no transfer.
- A real main-memory DMA write: two half-cycles (low then high) assembling a 32-bit word, confirmed to land correctly in `MainMemory` at the translated `paddr`.
- A real main-memory DMA read: confirmed the low half triggers a real memory read and the high half returns the buffered value, not a second memory access.
- The MD-register diagnostic backdoor: a page whose Xbus page number is `≥0x3E00` reads/writes `UCode.MdReg` directly, both halves, and does NOT touch `MainMemory` or the Xbus-I/O dispatch at all.
- An Xbus-I/O-range target (e.g. the already-real disk-control status register) reached via the Unibus map, confirmed it dispatches to the same real device path a direct (non-DMA) access would.
- A translated address outside both main memory and Xbus I/O → `SetXbusNxm()` called.

## Global Constraints

- No new NuGet dependencies.
- Every octal literal in this spec was independently verified via script — use the hex forms given.
- `ColortvEnabled`-gated device branches are not newly wired by this spec — DMA to that range hits today's existing placeholder, same as a direct access already does.
- The new `BusAdaptor` constructor dependencies (`MainMemory`, `UCode`) must be passed the same safe, store-only, no-callback-during-construction way `BusInterface` already is — do not restructure this into a settable-property pattern; a constructor parameter is correct here since, unlike `DiskController`, there is no genuine two-way dependency (this spec's new code only ever calls into `UCode`/`MainMemory`, never the reverse).

## Out of Scope

- TV, color-TV, tape device emulation — each remains its own future spec. This spec's Xbus-I/O dispatch path correctly routes to their existing placeholders, not new implementations.
- Any change to `UCode.Vm()`'s own main-memory dispatch (already real, untouched by this spec).
