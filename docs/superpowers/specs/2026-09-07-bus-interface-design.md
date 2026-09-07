# Bus Interface Port — Design Spec

## Context

The microcode engine port (`docs/superpowers/specs/2026-08-21-microcode-engine-design.md`,
Phases 1-9) is complete and closed. `usim/bus-interface.c` (312 lines) was
explicitly named as deliberately out of scope in that project's final state.

`bus-interface.c` implements the CADR's Unibus-mapped "bus interface"
register block (`0766040`-`0766114` octal): accumulated bus-error status
(Xbus NXM, Unibus NXM, Unibus Map Error), the interrupt-status register's
alternate write path, and — for the real hardware — a remote-debugger link
("lashup") to a second, physical CADR machine acting as a debuggee.

Today, `BusAdaptor.cs` routes this entire address range to a generic
"unmapped Unibus" fallback that logs a warning and spuriously asserts
Unibus NXM on every access (`BusAdaptor.cs`'s `DescribeUnibus` already
labels this range "bus-interface (already separately deferred, Phase 4)").
This is the first real consumer of that deferred range.

## Decisions

- **`lashup`/`lashup-debugger` (the remote-debuggee protocol) is
  permanently out of scope**, not deferred. It is a serial/socket link to
  a second physical CADR machine for hardware debugging; a software-only
  emulator has no second machine to talk to. This mirrors how other
  physical-hardware-only features were already excluded from this project.
- Everything else in `bus-interface.c` is a **faithful, real port** — not a
  placeholder. Where a register's behavior depends entirely on lashup
  (e.g. `0766104`, which only a debuggee's status write could ever change),
  "always reads 0" is the *correct* emulator answer (no debuggee is ever
  attached), not a compromise.
- `BusReset()`'s fan-out to `iob_bus_reset()`/`main_memory_bus_reset()`/
  `disk_controller_bus_reset()` is genuinely a no-op even in the real C —
  all three are empty function bodies (`usim/iob.c:429`,
  `usim/main-memory.c:160`, `usim/disk-controller.c:837`). Only
  `tape_controller_bus_reset()` (clears the tape controller's status
  struct) and `tv_bus_reset()` (empty, incidentally) do anything, and
  neither `TapeController` nor `TV` exist in C# yet — this becomes a
  logged placeholder note for those two, consistent with `BusAdaptor.cs`'s
  existing convention for un-ported devices, not a blocker for this spec.

## Register Model

| Octal addr | Real C behavior | This port |
|---|---|---|
| `0766040` r/w | interrupt status reg; write masks `036001` | routes to `UCode.InterruptStatusReg`/`SetInterruptStatusReg` |
| `0766042` w | write masks `0101774` into interrupt status reg | same as above |
| `0766044` r/w | read = `bus_error_status`; write clears `bus_error_status` (and lashup's mirrored status) | read = `bus_error_status`; write clears `bus_error_status` |
| `0766100` r/w | read/write debuggee's bus over lashup | logged no-op; returns 0 on read |
| `0766102` w | remote usim command over lashup | logged no-op |
| `0766104` r | debuggee's mirrored bus-error status (lashup) | always reads 0 (correct: no debuggee ever attached) |
| `0766110` w | modifier bits: `addr17`(bit0), reset(bit1), timeout-inhibit(bit2), debuggee/debugger mark(bits3-4), ping(bit5), mark symbol(bits8-15) — all but `addr17` capture drive lashup calls | captures `addr17` into local state; all other bits are a logged no-op (no lashup calls made) |
| `0766112` w | local usim command (already a log-only no-op in the real C — `__attribute__((unused))` params) | logged no-op, same as real C |
| `0766114` w | sets `addr` (low bits of debuggee's target Unibus address, lashup-only) | captures `addr` into local state (harmless bookkeeping; nothing reads it back without lashup) |
| default | `warnx()` + `bus_interface_set_unibus_nxm()` | same |

Local state fields (mirroring the real C's file-scope statics): `bus_error_status`
(`uint16_t`), `nxm_inhibited` (`bool`), `addr17` (`bool`), `addr` (`uint16_t`).
`modifier_reset` and `debuggee_bus_error_status` are lashup-only state — not
needed since the lashup call sites that would read/write them are already
no-ops; omit both rather than keeping dead state.

### Bit masks (independently re-derived, not hand-copied)

- Xbus NXM: bit 0 → mask `0x1` (octal `01`)
- Unibus NXM: bit 3 → mask `0x8` (octal `010`)
- Unibus Map Error: bit 5 → mask `0x20` (octal `040`)
- `0766040` write mask: octal `036001` = `0x3C01` (bits 0, 10-13 — independently
  verified via script; a naive hand-conversion gives the wrong `0xF001`,
  which would be bits 0 and 12-15 instead)
- `0766042` write mask: octal `0101774` = `0x83FC`

`SetXbusNxm()`/`SetUnibusNxm()` both check `nxm_inhibited` first and return
without effect if set. `SetUnibusMapError()` does **not** check
`nxm_inhibited` — this asymmetry is in the real C as written and must be
preserved, not "fixed."

## Class Shape

New `usim-cs/BusInterface.cs`:

```csharp
public class BusInterface
{
    private readonly UCode _ucode;
    private ushort _busErrorStatus;
    private bool _nxmInhibited;
    private bool _addr17;
    private ushort _addr;

    public BusInterface(UCode ucode) { _ucode = ucode; }

    public ushort GetBusErrorStatus() => _busErrorStatus;
    public bool IsXbusNxm() => (_busErrorStatus & 0x1) != 0;
    public bool IsUnibusNxm() => (_busErrorStatus & 0x8) != 0;
    public bool IsUnibusMapError() => (_busErrorStatus & 0x20) != 0;
    public void ResetBusErrorStatus() => _busErrorStatus = 0;
    public void SetNxmInhibit(bool inhibit) => _nxmInhibited = inhibit;
    public void SetXbusNxm() { if (!_nxmInhibited) _busErrorStatus |= 0x1; }
    public void SetUnibusNxm() { if (!_nxmInhibited) _busErrorStatus |= 0x8; }
    public void SetUnibusMapError() => _busErrorStatus |= 0x20;

    public uint Read(uint uaddr) { ... } // switch on uaddr, per Register Model table
    public void Write(uint uaddr, uint v) { ... }
    public void BusReset() { ... } // clears local state; logs tape/TV placeholder note
}
```

`Read`/`Write` use `uaddr` (word address, matching the real C's parameter
name and `BusAdaptor`'s existing `ReadUnibus`/`WriteUnibus` convention) —
not byte/paddr. The default case in both calls `SetUnibusNxm()` and logs a
warning, matching the real C's `default: warnx(...); bus_interface_set_unibus_nxm();`.

## Wiring

1. **`BusAdaptor.cs`**: add a `BusInterface` field/constructor parameter.
   In `ReadUnibus`/`WriteUnibus`, before falling through to the generic
   "unmapped Unibus" warning, check `uaddr >= BusInterfaceLo && uaddr <=
   BusInterfaceHi` (existing constants) and dispatch to
   `_busInterface.Read(uaddr)`/`.Write(uaddr, v)`. Addresses in that range
   `BusInterface` itself doesn't recognize still fall to its own internal
   default case (which mirrors the real C's own `default:` exactly) —
   `BusAdaptor`'s outer fallback is no longer reached for this range at all.
2. **`UCode.cs`**: `UCode`'s constructor already builds `BusAdaptor` itself
   (`BusAdaptor = new BusAdaptor();` at `UCode.cs:46`) — verified directly,
   not assumed. `BusAdaptor` is currently parameterless. Change the
   construction order inside `UCode`'s own constructor to:
   `BusInterface = new BusInterface(this); BusAdaptor = new
   BusAdaptor(BusInterface);` — plain constructor injection, no property,
   no cycle (passing `this` out of a constructor is safe here since
   `BusInterface`'s constructor only stores the reference, it doesn't call
   back into `UCode` during construction). Add a public `BusInterface
   BusInterface { get; }` property on `UCode`, mirroring the existing
   `BusAdaptor` property. `MfWrite` code 2 (bit 28) calls
   `BusInterface.BusReset()` instead of only logging.
3. **`MachineControl.cs`**: after `InitializeComponents()` in `PowerOn()`,
   call `UCode.BusInterface.BusReset()` (`UCode` is `MachineControl`'s
   existing public property, `UCode.cs:82`'s constructed instance).

## Testing

New `usim-cs/BusInterfaceTests.cs`:
- `bus_error_status` get/set/reset roundtrip.
- `SetXbusNxm`/`SetUnibusNxm` respect `nxm_inhibited`; `SetUnibusMapError`
  does not (explicit asymmetry test).
- `0766040` write only touches mask `0x3C01` of `UCode.InterruptStatusReg`;
  `0766042` write only touches mask `0x83FC`; other bits of
  `InterruptStatusReg` are provably untouched by either.
- `0766044` read returns `bus_error_status`; write clears it.
- `0766104` always reads 0.
- `0766100`/`0766102`/`0766110`/`0766112`/`0766114` are no-ops that don't
  throw; `0766114` write is observable via a subsequent internal-state
  check (not externally readable, so test via reflection or an internal
  accessor scoped to the test assembly, matching existing test patterns
  in this codebase for other internal-only state).
- `BusReset()` clears `bus_error_status`, `nxm_inhibited`, `addr17`, `addr`.
- Default-case read/write sets `IsUnibusNxm()` true and doesn't throw.

Updates to existing tests:
- `BusAdaptorTests.cs`: add a case confirming a `0766040`-range address now
  dispatches to `BusInterface` (e.g. write `0766044` then read it back)
  instead of the old generic-fallback warning.
- A `MachineControl`-level test (new or existing) confirming `PowerOn()`
  calls `BusReset()` — observable by pre-setting `bus_error_status` via
  `BusInterface` directly, then calling `PowerOn()`, then asserting it
  reads 0.
- A `UCode` M-register test confirming `MfWrite` code 2 (bit 28) calls
  `BusReset()` — same observable pattern.

## Global Constraints

- No new dependencies; .NET 8.0 as established.
- Follow this project's established faithful-port discipline: every octal
  literal and bitmask above must be independently re-verified via script
  (not hand-copied) before the implementation plan is written, and the
  implementation must be checked against real boot behavior
  (`sys/ubin/promh.mcr`/`ucadr.mcr`) in its final review, per
  `[[feedback-verify-spec-against-c-source]]` and this project's
  established "single biggest lesson" (real-data verification over
  source-reading alone).
- `lashup`/`lashup-debugger` are not ported, now or later, under this or
  any future spec touching this file — this is a permanent boundary, not
  a deferral.

## Out of Scope (for this spec)

- `TapeController`/`TV` — do not exist yet; `BusReset()`'s fan-out to them
  is a logged placeholder, tracked as part of those subsystems' own future
  specs, not this one.
- Real disk data transfer, tape, TV, Unibus-Map DMA device emulation —
  each is its own future spec, decomposed from the combined "Phase 5B
  placeholders" scope per the brainstorming discussion that preceded this
  document.
