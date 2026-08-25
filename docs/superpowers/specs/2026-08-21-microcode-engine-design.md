# CADR Microcode Execution Engine — Faithful Port Design

**Date:** 2026-08-21
**Status:** Approved, pending phase-by-phase implementation plans

## Context

`usim-cs/UCode.cs` claims (per `docs/MICROCODE_ENGINE.md` and `docs/IMPLEMENTATION_STATUS.md`) to be a complete microcode engine. It is not: its entry point `Step()` is a one-line stub, and the `ExecuteInstruction` method that looks complete implements an **invented, non-authentic instruction format** — a single ALU-op-then-jump shape unrelated to the real CADR hardware encoding. The real reference implementation, `usim/uexec.c` (1159 lines, ~6x the size of all of `UCode.cs`), defines four structurally different instruction classes (ALU / JUMP / DISPATCH / BYTE) selected by a 2-bit opcode field, plus ~20 special memory-mapped registers with side effects, plus a virtual-memory subsystem (`usim/uvmem.c`) that has no C# equivalent at all.

This repo targets hardware generation **LISPM_SYSTEM300** (`defmic300.h`/`qcom300.h`) — confirmed by `usim/usim.h:16` (`#define LISPM_SYSTEM LISPM_SYSTEM300`), the C# port's own `config/usim.ini:6` (`system_version = 300`), and the ini file that actually loads the available boot files (`usim/usim-303-0.ini`, referencing `sys/ubin/promh.mcr`/`promh.sym`/`ucadr.mcr`/`ucadr.sym`).

**Goal:** replace the invented engine with a faithful 1:1 port of `uexec.c`'s semantics, validated with targeted unit tests against known CADR behavior. Booting `promh.mcr`/`ucadr.mcr` to a Lisp prompt is a later milestone (it also needs disk/display/MMU integration working together) — this port's bar is instruction-level fidelity, not an end-to-end boot.

## Decisions

1. **`UCode` becomes an instance class**, matching this codebase's existing convention (`Display`, `Keyboard`, `Mouse`, `DiskController` are all instance-based, held by `MachineControl`). Today `UCode` is the only fully-static class in the port, which is also why `MachineControl.UCode.Pc` (an instance property) looks disconnected from the real execution state (`Npc`, currently static) — this inconsistency gets resolved as part of the rewrite.
2. **Faithful-shape port, not a reinvented abstraction.** One C# method per C function, same field/register names wherever the current code already uses names that match the real hardware (many already do — see §1). An `Ir(pos, len)` helper mirrors the C `ir()` bit-extractor exactly. Each opcode-class handler (`Alu`, `Jmp`, `Dsp`, `Byt`) extracts its own additional fields directly from the raw instruction register, matching the C structure — a single upfront "decode everything" struct doesn't fit a format where the same bit positions mean different things depending on opcode class (e.g. bits 5-7 are `len` for DISPATCH but `widthm1` for BYTE).
3. **`Uvmem.cs` (new) replaces `MainMemory`'s role for CPU-driven address translation.** `MainMemory.cs`'s existing "virtual memory with paging" is a different, simplified, single-level page map — not the CADR's real two-level L1/L2 map with access/write permission bits that `uvmem.c` implements and that `Dsp()`/`MfRead`/`MfWrite` depend on. `Uvmem` is a true port of the L1/L2 map structure and drives CPU address translation; `MainMemory` is extended with a physical-address-only `ReadPhysical`/`WritePhysical` pair used as the raw backing store once `Uvmem.Vtop` has produced a physical address, bypassing `MainMemory`'s own incompatible virtual-address translation for this path. `MainMemory`'s existing `Read`/`Write`/`TranslateAddress`/page-map methods stay as-is for whatever (if anything) already calls them, but the microcode engine no longer uses them.
4. **Error handling matches the C's severity split.** `err()`/`errx()` sites (fatal, process-aborting in C) become a thrown exception in C# (halts the emulator). `warn()` sites (non-fatal) become `TraceLog.Instance.Warning(...)` calls, matching this codebase's existing tracing convention.
5. **Testing**: unit tests per pure-function operation table (`LogiOps`/`ArithOps`/`DivOps`/`QControl`/`OutControl`/`CheckJumpCondition`) against hand-computed expected values derived directly from the C semantics below. Plus a decode-only sanity test loading the real `promh.mcr` and checking opcode-class distribution/field extraction against manual disassembly (using `promh.sym`/`promh.tbl`) — this validates decode correctness without needing execution semantics to be complete yet. Full-boot testing is explicitly deferred.
6. **Phased implementation**, in dependency order: (1) register model + common fetch/decode, (2) ALU instructions, (3) jump instructions, (4) special M-registers, (5) virtual memory, (6) dispatch instructions, (7) byte instructions, (8) consumer rework (`Disassembler.cs`, `MicrocodeDebugger.cs`), (9) new test suite. Each phase gets its own implementation plan and `subagent-driven-development` run; this document is the shared target design all of them build toward.

## Register Model (Phase 1)

Global C variables (`usim/uexec.c:34-117`) map to `UCode` instance fields/properties. Names marked **(existing)** already exist in `UCode.cs` with a matching name and should be preserved as-is (fixing type/semantics where the current type is wrong, e.g. `CarryFlag`/`OverflowFlag`/`NegativeFlag`/`ZeroFlag` do not exist on real hardware and are removed — see Phase 8).

| C# name | C source | Type | Notes |
|---|---|---|---|
| `P0` | `p0` | `ulong` | current pipeline instruction register (48 bits used) |
| `P0Pc` | `p0_pc` | `uint` | PC for `P0` |
| `P0Imem` | `p0_imem` | `bool` | true if `P0` came from `IMem` rather than `Prom` |
| `P1` | `p1` | `ulong` | prefetched next-stage instruction register |
| `P1Pc` | `p1_pc` | `uint` | PC for `P1` |
| `P1Imem` | `p1_imem` | `bool` | true if `P1` came from `IMem` |
| `Npc` **(existing)** | `npc` | `uint` | next PC, 14-bit wraparound at `0x3FFF` |
| `Iwr` | `iwr` | `ulong` | latched every cycle: `(AData & 0xFFFF) << 32 \| (uint)MData`; used by `Jmp()`'s microcode-self-write path |
| `Prom` **(existing)** | `prom[512]` | `ulong[]` | read-only microcode PROM |
| `IMem` **(existing)** | `imem[16*1024]` | `ulong[]` | writable control store |
| `AMem` **(existing)** | `amem[1024]` | `uint[]` | A-memory |
| `AAddr` | `aaddr` | `uint` | latched 10-bit A-memory address |
| `AData` | `adata` | `int` | latched A operand |
| `MMem` **(existing)** | `mmem[32]` | `uint[]` | plain M-register shadow |
| `MAddr` | `maddr` | `uint` | latched 5-bit M-source address |
| `MData` **(existing)** | `mdata` | `int` | latched M operand |
| `DMem` **(existing)** | `dmem[2048]` | `uint[]` | dispatch memory — **verify/resize to 2048 entries; current `UCode.cs` may size this differently** |
| `Pdl` **(existing)** | `pdl[1024]` | `uint[]` | push-down list |
| `Spc` **(existing)** | `spc[32]` | `uint[]` | micro-PC stack |
| `SpcPtr` | `spcptr` | `uint` | 5-bit SPC stack pointer |
| `DispatchConstant` | `dispatch_constant` | `uint` | 10-bit, functional source `0` |
| `PdlPointer` **(existing)** | `pdl_pointer` | `uint` | 10-bit; functional source `02`, dest `014` |
| `PdlIndex` | `pdl_index` | `uint` | 10-bit; functional source `03`, dest `013` |
| `VmaReg` **(existing)** | `vma_reg` | `uint` | functional source `010`, dest `020` |
| `MdReg` **(existing)** | `md_reg` | `uint` | functional source `012`, dest `030` |
| `Lc` | `lc` | `uint` | 26-bit location counter + flag bits (NEED-FETCH at bit 31); functional source `013`, dest `01` |
| `OaRegLow` **(existing)** | `oa_reg_low` | `uint` | OA<25-0>; dest `016` |
| `OaRegHigh` **(existing)** | `oa_reg_high` | `uint` | OA<47-26>; dest `017` |
| `Opc` **(existing)** | `opc` | `uint` | snapshot of `P0Pc`, updated in `IncNpc`; functional source `6` |
| `Q` **(existing)** | `q` | `uint` | Q register |
| `OldQ` | `old_q` | `uint` | `Q` before this cycle's `QControl` update |
| `InterruptControl` | `interrupt_control` | `uint` | bit26=seq-break-pending, bit27=interrupt-enable, bit28=bus-reset, bit29=LC-byte-mode, bit31=need-fetch |
| `Inhibit` | `inhibit` | `bool` | true = skip next fetched instruction |
| `Op` | `op` | `uint` | 2-bit opcode class (0=ALU,1=JUMP,2=DISPATCH,3=BYTE) |
| `Popj` | `popj` | `bool` | POPJ-after flag (IR bit 42) |
| `NewMd` | `new_md` | `uint` | pending value from `VmRead`, committed to `MdReg` after delay |
| `NewMdDelay` | `new_md_delay` | `uint` | cycles remaining before `NewMd` commits |
| `AluCarry` | `alu_carry` | `uint` | ALU carry-out for current cycle (replaces the invented `CarryFlag`/etc.) |
| `AluOut` | `alu_out` | `uint` | ALU result |
| `Oal` | `oal` | `bool` | `OaRegLow` pending merge into next `P0` |
| `Oah` | `oah` | `bool` | `OaRegHigh` pending merge into next `P0` |
| `Out` **(existing)** | `out` | `uint` | final bus value fed to `WriteDest` |

`debug_ir` (`uexec.c:45`) is declared but never used in the reference and is omitted from the port.

### Common decode (`Ir` helper + `Step()`)

```csharp
private ulong Ir(int pos, int len) => (P0 >> pos) & ((1UL << len) - 1);
```

`Step()` replaces the current one-line stub and becomes the faithful `uexec_step()` port:

1. `IncNpc()` — pipeline advance: `P0/P0Pc/P0Imem = P1/P1Pc/P1Imem`; prefetch `P1 = P1Imem ? IMem[Npc] : Prom[Npc]` (`P1Imem` from whatever the PROM-disabled flag is in this port — confirm source during implementation); `Npc` increments with 14-bit wraparound; `Opc = P0Pc`.
2. If `NewMdDelay != 0`, decrement it; at 0, commit `MdReg = NewMd`.
3. If `Inhibit`, clear it and return — the fetched instruction is skipped entirely.
4. OA-register merge: if `Oal`, clear it and OR `OaRegLow` into `P0` bits 0-25; if `Oah`, clear it and OR `(OaRegHigh << 26)` into `P0` bits 26-47.
5. Common field decode: `Op = (uint)Ir(43,2)`, `Popj = Ir(42,1) == 1`, `AAddr = (uint)Ir(32,10)`, `msource = Ir(31,1)`, `MAddr = (uint)Ir(26,5)`.
6. `MData = msource == 0 ? (int)MMem[MAddr] : MfRead(MAddr);`
7. `AData = (int)AMem[AAddr];`
8. `Iwr = ((ulong)(uint)(AData & 0xFFFF) << 32) | (uint)MData;`
9. Dispatch: `switch (Op) { case 0: Alu(); break; case 1: Jmp(); break; case 2: Dsp(); break; case 3: Byt(); break; }`
10. POPJ handling: if `Popj` is still true after the handler ran (handlers clear it themselves when they take their own branch target — see Phases 3 and 6), pop `Npc` from `Spc` via `PopSpc()`; if bit 14 of the popped value is set, `Npc = AdvanceLc(Npc)`; mask `Npc &= 0x3FFF`.

`MachineCycles++` still increments once per `Step()` call, matching current behavior.

## Phase 2 — ALU Instructions (`Alu()`)

```csharp
private void Alu()
{
    uint dest = (uint)Ir(14, 12);
    uint aluop = (uint)Ir(3, 6);
    AluCarry = 0;
    switch (aluop)
    {
        case >= 0 and <= 0x0F: LogiOps(aluop); break;
        case >= 0x10 and <= 0x1F: ArithOps(aluop); break;
        case 0x20 or 0x21 or 0x25 or 0x29: DivOps(aluop); break;
    }
    QControl();
    OutControl();
    WriteDest(dest);
}
```
(Octal codes below are converted to hex/decimal for C# `switch` clarity but the case boundaries are exact: `000-017`→`0x00-0x0F`, `020-037`→`0x10-0x1F`, `040,041,045,051`→`0x20,0x21,0x25,0x29`.)

Any `aluop` not covered by these three ranges/values is a silent no-op (matches C — no `default` case, `AluOut`/`AluCarry` retain their prior value).

### `LogiOps(op)` — codes 0-15
| Code | Semantic | C# expression |
|---|---|---|
| 0 | SETZ | `AluOut = 0` |
| 1 | AND | `MData & AData` |
| 2 | ANDCA | `MData & ~AData` |
| 3 | SETM | `MData` |
| 4 | ANDCM | `~MData & AData` |
| 5 | SETA | `AData` |
| 6 | XOR | `MData ^ AData` |
| 7 | IOR | `MData \| AData` |
| 8 | NOR | `~AData & ~MData` |
| 9 | EQV | `AData == MData ? 1 : 0` (boolean test, not bitwise XNOR) |
| 10 | SETCA | `~AData` |
| 11 | ORCA | `MData \| ~AData` |
| 12 | SETCM | `~MData` |
| 13 | ORCM | `~MData \| AData` |
| 14 | ORCB | `~MData \| ~AData` |
| 15 | SETO | `~0` |

All results assign to `AluOut` (as `uint`); `AluCarry` stays 0 for all of these (already zeroed at the top of `Alu()`).

### `ArithOps(op)` — codes 16-31 (octal 020-037); `cin = Ir(2,1) != 0`
Use a signed 64-bit intermediate `long lv` for the carry-detecting cases; `AluCarry = (lv >> 32) != 0 ? 1u : 0u`. `Add32`/`Sub32` are direct ports of the C `add32`/`sub32` macros (see Phase 1 support helpers below).

| Code | C# logic |
|---|---|
| 16 | `AluOut = cin ? 0u : uint.MaxValue; AluCarry = 0;` |
| 17 | `lv = (long)(uint)(MData & AData) - (cin?0:1);` |
| 18 | `lv = (long)(uint)(MData & ~AData) - (cin?0:1);` |
| 19 | `lv = (long)(uint)MData - (cin?0:1);` |
| 20 | `lv = (long)(uint)(MData \| ~AData) + (cin?1:0);` |
| 21 | `lv = (long)(uint)(MData\|~AData) + (uint)(MData&AData) + (cin?1:0);` |
| 22 | `(AluOut, AluCarry) = Sub32(MData, AData, cin);` |
| 23 | `lv = (long)(uint)(MData\|~AData) + (uint)MData + (cin?1:0);` |
| 24 | `lv = (long)(uint)(MData\|AData) + (cin?1:0);` |
| 25 | `(AluOut, AluCarry) = Add32(MData, AData, cin);` |
| 26 | `lv = (long)(uint)(MData\|AData) + (uint)(MData & ~AData) + (cin?1:0);` |
| 27 | `lv = (long)(uint)(MData\|AData) + (uint)MData + (cin?1:0);` |
| 28 | `AluOut = (uint)(MData + (cin?1:0)); AluCarry = 0; if (MData == -1 && cin) AluCarry = 1;` |
| 29 | `lv = (long)(uint)MData + (uint)(MData & AData) + (cin?1:0);` |
| 30 | `lv = (long)(uint)MData + (uint)(MData\|~AData) + (cin?1:0);` |
| 31 | `(AluOut, AluCarry) = Add32(MData, MData, cin);` |

For every `lv`-based row: `AluOut = (uint)lv; AluCarry = (lv >> 32) != 0 ? 1u : 0u;` after computing `lv`.

### `DivOps(op)` — codes 32,33,37,41 (octal 040,041,045,051); `cin = Ir(2,1) != 0`
| Code | C# logic |
|---|---|
| 32 (mult step) | `if ((Q & 1) != 0) (AluOut, AluCarry) = Add32(AData, MData, cin); else { AluOut = (uint)MData; AluCarry = (AluOut & 0x80000000) != 0 ? 1u : 0u; }` |
| 33 (divide step) | `if ((Q & 1) != 0) (AluOut, AluCarry) = Sub32(MData, Abs32(AData), !cin); else (AluOut, AluCarry) = Add32(MData, Abs32(AData), cin);` |
| 37 (remainder correction) | `if ((Q & 1) != 0) AluCarry = 0; else (AluOut, AluCarry) = Add32((int)AluOut, Abs32(AData), cin);` |
| 41 (initial divide step) | `(AluOut, AluCarry) = Sub32(MData, Abs32(AData), !cin);` (unconditional) |

### `QControl()` — dispatches on `Ir(0,2)`
`OldQ = Q;` first (unconditionally), then:
| Code | C# logic |
|---|---|
| 0 | no-op |
| 1 | `Q <<= 1; if ((AluOut & 0x80000000) == 0) Q \|= 1;` |
| 2 | `Q >>= 1; if ((AluOut & 1) != 0) Q \|= 0x80000000;` |
| 3 | `Q = AluOut;` |

### `OutControl()` — dispatches on `(P0 >> 12) & 3` (raw, not via `Ir`)
| Code | C# logic |
|---|---|
| 0 | `TraceLog warning "out == 0"`; `Out = Rol32((uint)MData, (int)(P0 & 0x1F));` |
| 1 | `Out = AluOut;` |
| 2 | `Out = (AluOut >> 1) \| (AluCarry != 0 ? 0x80000000u : 0);` |
| 3 | `Out = (AluOut << 1) \| ((OldQ & 0x80000000) != 0 ? 1u : 0);` |

### Support helpers (ported from `m32.c`/`m32.h`)
```csharp
private static (uint Out, uint Carry) Add32(int a, int b, bool ci)
{
    uint outv = unchecked((uint)a + (uint)b + (ci ? 1u : 0u));
    uint co = ci ? ((uint)b >= (uint)~a ? 0u : 1u) : ((uint)b > (uint)~a ? 0u : 1u);
    return (outv, co);
}
private static (uint Out, uint Carry) Sub32(int a, int b, bool ci)
{
    uint outv = unchecked((uint)a - (uint)b - (ci ? 0u : 1u));
    uint co = outv < (uint)a ? 1u : 0u;
    return (outv, co);
}
private static int Abs32(int a) => a < 0 ? ~a + 1 : a;
private static uint Rol32(uint value, int bits)
{
    if (bits == 0) return value;
    int mask = unchecked((int)0x80000000) >> bits;
    uint tmp = (uint)(((ulong)(value & (uint)mask)) >> (32 - bits));
    return (value << bits) | tmp;
}
```

## Phase 3 — Jump Instructions (`Jmp()`, `CheckJumpCondition()`)

Fields: `target = (uint)Ir(12,14)`, `r = Ir(9,1)!=0`, `p = Ir(8,1)!=0`, `n = Ir(7,1)!=0`, `invertSense = Ir(6,1)!=0`.

```csharp
private void Jmp()
{
    uint target = (uint)Ir(12, 14);
    bool r = Ir(9, 1) != 0, p = Ir(8, 1) != 0, n = Ir(7, 1) != 0;
    bool invertSense = Ir(6, 1) != 0;

    if (Ir(10, 2) == 1) { /* ILLOP: TraceLog notice, Halted = true — continues, does not return */ }
    if (Ir(10, 2) == 3) { /* MISC-3: TraceLog warning */ }

    if (p && r) { IMem[target] = Iwr; return; }

    bool cond = CheckJumpCondition();
    if (invertSense) cond = !cond;

    if (p && cond)
    {
        if (!n) PushSpc(Npc); else PushSpc(Npc - 1);
    }
    if (r && cond)
    {
        target = PopSpc();
        if ((target >> 14 & 1) != 0) target = AdvanceLc(target);
        target &= 0x3FFF;
    }
    if (cond)
    {
        if (n) Inhibit = true;
        Npc = target;
        Popj = false;
    }
}
```

`CheckJumpCondition()`:
```csharp
private bool CheckJumpCondition()
{
    if (Ir(5, 1) == 0)
    {
        int rot = (int)Ir(0, 5);
        MData = (int)Rol32((uint)MData, rot);
        return (MData & 1) != 0;
    }
    return Ir(0, 4) switch
    {
        1 => MData < AData,
        2 => MData <= AData,
        3 => MData == AData,
        4 => !VmaOk,
        5 => !VmaOk || (((InterruptControl & (1<<27)) != 0) && InterruptPending),
        6 => !VmaOk || (((InterruptControl & (1<<27)) != 0) && InterruptPending) || (InterruptControl & (1<<26)) != 0,
        7 => true,
        _ => throw new InvalidOperationException($"unknown jump condition {Ir(0,4)}"), // includes code 0, matching the C's fall-through-to-err()
    };
}
```
`VmaOk`/`InterruptPending` correspond to `machine_state.vmaok`/`interrupt_pending_flag` in C — confirm/introduce their C# equivalents during implementation (likely fields on `MachineControl` or `UCode` itself; `VmaOk` in particular is set by the virtual-memory path in Phase 5, so this condition is only meaningfully non-true once Phase 5 lands — until then it defaults to `true`, i.e. no page faults).

## Phase 4 — Special M-Registers (`MfRead`/`MfWrite`)

### `MfRead(addr)` — switch on `addr & 0x1F`
| Code (oct) | C# logic |
|---|---|
| 0 | `return (int)DispatchConstant;` |
| 1 | `return (int)((SpcPtr << 24) \| (Spc[SpcPtr] & 0x7FFFF));` |
| 2 | `return (int)(PdlPointer & 0x3FF);` |
| 3 | `return (int)(PdlIndex & 0x3FF);` |
| 5 | `return (int)Pdl[PdlIndex];` |
| 6 | `return (int)Opc;` |
| 7 | `return (int)Q;` |
| 8 | `return (int)VmaReg;` |
| 9 | MEMORY-MAP-DATA — see Phase 5 for the `Uvmem.Vtop` call and exact bit packing |
| 10 | `return (int)MdReg;` |
| 11 | `return (int)((InterruptControl & (1<<29)) != 0 ? Lc : Lc & ~1u);` |
| 12 | `{ int res = (int)((SpcPtr<<24) \| (Spc[SpcPtr]&0x7FFFF)); SpcPtr = (SpcPtr - 1) & 0x1F; return res; }` |
| 13 | `return 0;` (placeholder, matches C's `??? `) |
| 20 | `{ int res = (int)Pdl[PdlPointer]; PdlPointer = (PdlPointer - 1) & 0x3FF; return res; }` |
| 21 | `return (int)Pdl[PdlPointer];` |
| 22 | `return 0;` (placeholder) |
| *default* | throw (matches C's fatal `err()`) |

(Octal 4, 14-17 (excluding those listed), 23-37 are not present and fall to the fatal default — note codes above are given in decimal matching the octal values 0,1,2,3,5,6,7,8,9(011),10(012),11(013),12(014),13(015),20(024),21(025),22(026) — implementer should re-derive from the octal literals directly rather than trust this parenthetical, and cross-check against the extracted reference's exact octal codes in the phase-4 implementation plan.)

**Correction (found during Phase 4 planning, 2026-08-24; independently re-derived four separate times across planning, implementation, task review, and final review — all four agree):** codes 1 and 12's mask was originally given above as `0x1FFFFF` (21 bits). The real C literal is `01777777` (octal, 7 digits: `1777777`), which is `0x7FFFF` (19 bits, `2^19-1`) — confirmed both by direct digit-by-digit octal arithmetic and by converting `0x7FFFF` back to octal (`1777777`, matching the literal exactly; `0x1FFFFF` converts back to `7777777`, which does not match). This is the third octal-to-hex mistranslation caught in this spec (after Phase 1's LC/OA-REG masks) — always re-derive from the literal octal digits directly, never trust a restated hex value in this document without checking the C.

**Resolved (Phase 4, 2026-08-24):** the `bus_interface_bus_reset()` open item below is answered — it is a no-op plus an `Info`-level log, not a call into `IOBus.cs`. `bus_interface_bus_reset()` lives in `usim/bus-interface.c`, a wholly separate, not-yet-ported subsystem (it clears Unibus/Xbus-adaptor-level state and fans out to several other not-yet-ported reset routines); `IOBus.cs`'s existing `Reset()` is a C#-invented device-loop abstraction with no correspondence to it, so wiring that in would be a plausible-looking wrong substitution, not a faithful port. Real-microcode calibration (decoding `sys/ubin/promh.mcr` and `sys/ubin/ucadr.mcr`) found this path IS on the boot path — the boot PROM contains a dedicated single-bit deposit to INTERRUPT-CONTROL bit 28 (loc `0o261`) — so the no-op (not a throw) was the right shape, but a real `bus_interface_bus_reset()`-equivalent may eventually be needed once that subsystem gets its own port.

**Real-microcode calibration (Phase 4 final review, 2026-08-24):** decoding both boot images found several Phase-4 deferrals sit on non-trivial paths, worth Phase 5 treating as priorities rather than backwaters: `MfRead` code 9 (MEMORY-MAP-DATA, throws today) — 1 site in `promh.mcr` (inside the boot PROM itself, ~10 words after its INTERRUPT-CONTROL setup) and 34 in `ucadr.mcr`; `MfWrite`'s VMA/MD-map-write codes 18/19/26/27 (silent no-ops today, pending `Uvmem`) — 361 static sites combined across both images, with code 18 (VMA-START-WRITE, the main-memory-write instruction) alone at 186 sites in `ucadr.mcr`, second only to VMA-START-READ. Also confirmed safe: neither `MfRead`'s fatal default nor `MfWrite`'s non-fatal-warning default is ever reached by real microcode in either image.

### `MfWrite(dest, data)` — switch on `dest >> 5`
| Code (oct) | Register | C# logic |
|---|---|---|
| 0 | (unused) | no-op |
| 1 | LC | `Lc = (Lc & ~0x03FFFFFFu) \| ((uint)data & 0x03FFFFFF);` (26-bit mask — see the correction note below the table) then if not byte mode, `Lc &= ~1u;` then `Lc \|= (1u<<31);` |
| 2 | INTERRUPT-CONTROL | `InterruptControl = (uint)data;` then if bit 28 set, call bus-reset equivalent; then `Lc = (Lc & ~(0xFu<<26)) \| (InterruptControl & (0xFu<<26));` |
| 8 | C-PDL-BUFFER-POINTER | `Pdl[PdlPointer] = (uint)data;` |
| 9 | C-PDL-BUFFER-POINTER-PUSH | `PdlPointer = (PdlPointer+1)&0x3FF; Pdl[PdlPointer] = (uint)data;` |
| 10 | C-PDL-BUFFER-INDEX | `Pdl[PdlIndex] = (uint)data;` |
| 11 | PDL-BUFFER-INDEX | `PdlIndex = (uint)data & 0x3FF;` |
| 12 | PDL-BUFFER-POINTER | `PdlPointer = (uint)data & 0x3FF;` |
| 13 | MICRO-STACK-DATA-PUSH | `PushSpc((uint)data);` |
| 14 | OA-REG-LO | `OaRegLow = (uint)data & 0x03FFFFFF; Oal = true;` (26-bit mask — see correction note below) |
| 15 | OA-REG-HI | `OaRegHigh = (uint)data & 0x7FFFFF; Oah = true;` (23-bit mask, matching the C literal `037777777` exactly — one bit wider than the `OA<47-26>` (22-bit) comment in the register table above; port the literal, not the comment, since the comment appears to be the original hardware documentation's own imprecision, not something to "fix") |
| 16 | VMA | `VmaReg = (uint)data;` |
| 17 | VMA-START-READ | `VmaReg = (uint)data; VmRead(VmaReg, out NewMd); NewMdDelay = 2;` |
| 18 | VMA-START-WRITE | `VmaReg = (uint)data; VmWrite(VmaReg, MdReg);` |
| 19 | VMA-WRITE-MAP | `VmaReg = (uint)data; Uvmem.WriteMap(VmaReg, MdReg);` |
| 24 | MD (plain) | `MdReg = (uint)data;` |
| 25 | MD-START-READ | `MdReg = (uint)data; VmRead(VmaReg, out NewMd); NewMdDelay = 2;` |
| 26 | MD-START-WRITE | `MdReg = (uint)data; VmWrite(VmaReg, MdReg);` |
| 27 | MD-WRITE-MAP | `MdReg = (uint)data; Uvmem.WriteMap(VmaReg, MdReg);` |
| *default* | `TraceLog.Instance.Warning(...)` (matches C's non-fatal `warn()`) |

(As with `MfRead`, the table above uses decimal restated from octal for readability — re-derive exact octal case labels from the extracted reference during Phase 4 implementation; the octal source values are `1,2,010,011,012,013,014,015,016,017,020,021,022,023,030,031,032,033`.)

**Correction (found during Phase 1's task review, applied 2026-08-22):** the first draft of this spec mistranslated three C octal masks to hex. The real C masks: LC's `0377777777` and OA-REG-LO's `0377777777` are both `0x03FFFFFF` (26 bits) — the draft had `0x0FFFFFFF` (28 bits) for both, which is wrong by 2 bits and, left uncorrected, would have let `InterruptControl` bits 26-27 leak into `AdvanceLc`'s address computation once Phase 5 makes that path reachable. OA-REG-HI's `037777777` is `0x7FFFFF` (23 bits) — the draft had `0x00FFFFFF` (24 bits). All three are fixed above and in Phase 1's `AdvanceLc` code (§ below); if you're implementing Phase 4 from an older read of this file cached elsewhere, use the values in this current version, not any earlier copy.

`WriteDest(dest)` (used by `Alu()`/`Byt()`, ported once as part of Phase 2 since both need it):
```csharp
private void WriteDest(uint dest)
{
    if ((dest & 0x800) != 0)
    {
        AMem[dest & 0x3FF] = Out;
    }
    else
    {
        MfWrite(dest, (int)Out);
        MMem[dest & 0x1F] = AMem[dest & 0x1F] = Out;
    }
}
```

## Phase 5 — Virtual Memory (`Uvmem.cs`, new file)

```csharp
public class Uvmem
{
    private readonly uint[] _l1Map = new uint[2048];
    private readonly uint[] _l2Map = new uint[1024];
    private readonly MainMemory _mainMemory;

    public Uvmem(MainMemory mainMemory) { _mainMemory = mainMemory; }

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

    public void WriteMap(uint vma, uint md)
    {
        bool enableL1 = (vma & (1 << 26)) != 0;
        bool enableL2 = (vma & (1 << 25)) != 0;
        uint l1Index = (md >> 13) & 0x7FF;
        if (enableL1)
            _l1Map[l1Index] = (vma >> 27) & 0x1F;
        if (enableL2)
        {
            uint l1Data = _l1Map[l1Index] & 0x1F;
            uint l2Index = (l1Data << 5) | ((md >> 8) & 0x1F);
            _l2Map[l2Index] = vma & 0x00FFFFFF;
        }
    }
}
```
Note: `WriteMap` re-reads `_l1Map[l1Index]` for the L2 write *after* the L1 write executes (matching the C's sequential-statement-order behavior) — if both enable bits are set in one call, the L2 write sees the just-written L1 entry.

`UCode` holds its `Uvmem` dependency as a property: `public Uvmem Uvmem { get; }`, set once from the constructor (`new UCode(mainMemory)` internally does `Uvmem = new Uvmem(mainMemory)`, or `UCode` receives an already-constructed `Uvmem` — finalize whichever fits `MachineControl`'s wiring better during Phase 5's plan). Every call site below (`Vm`, `MfRead` code 9, `Dsp`'s L2-map step) uses this same `Uvmem` property — there is exactly one virtual-memory instance per `UCode`, never a static/singleton.

`Vm`/`VmRead`/`VmWrite` (on `UCode`, calling into `Uvmem` and `MainMemory`'s new physical-address API):
```csharp
private void Vm(bool write, uint vaddr, ref uint v)
{
    vaddr &= 0x00FFFFFF;
    uint paddr = Uvmem.Vtop(vaddr, out _, out _, out uint pn, out bool wp, out bool ap);
    VmaOk = write ? (ap && wp) : ap;
    if (!VmaOk) { v = 0; return; }
    // TV-screen quirk (usim/uvmem.c comment: known not to work correctly) — port as-is, flag in Phase 5's plan
    if (pn == 0x1E00 /* 036000 octal */) paddr = 0x0F00000 | (vaddr & 0x7FFF); // 017000000 octal | vaddr&077777
    if (write) _mainMemory.WritePhysical(paddr, v);
    else v = _mainMemory.ReadPhysical(paddr);
}
private void VmRead(uint vaddr, out uint v) { v = 0; Vm(false, vaddr, ref v); }
private void VmWrite(uint vaddr, uint v) { Vm(true, vaddr, ref v); }
```
The C reference's `035774 <= pn <= 035777` A-memory-mapped branch always hits `assert(false)` in the original (dead/broken as written) — **omit this branch entirely** in the port rather than faithfully porting an assertion-guarded dead path; note this omission in the Phase 5 plan.

`MainMemory.cs` gains:
```csharp
public uint ReadPhysical(uint physicalAddress) => physicalAddress < PHYSICAL_MEM_SIZE ? _physicalMemory[physicalAddress] : 0;
public void WritePhysical(uint physicalAddress, uint value) { if (physicalAddress < PHYSICAL_MEM_SIZE) _physicalMemory[physicalAddress] = value; }
```
(exact bounds-check/error behavior to be finalized in the Phase 5 plan — the existing `Read`/`Write` log-and-return-0 pattern is a reasonable default to match.)

MEMORY-MAP-DATA (`MfRead` code 9, deferred from Phase 4 since it needs `Uvmem`):
```csharp
uint paddr = Uvmem.Vtop(MdReg, out uint l1, out uint l2, out _, out bool wp, out bool ap);
return (int)((!wp ? (1u<<31) : 0) | (!ap ? (1u<<30) : 0) | (1u<<29) | ((l1 & 0x1F)<<24) | (l2 & 0x00FFFFFF));
```

## Phase 6 — Dispatch Instructions (`Dsp()`)

Fields: `pos = (int)Ir(0,5)`, `len = (int)Ir(5,3)`, `map = Ir(8,2)`, `dispAddr = (uint)Ir(12,11)`, `nPlus1 = Ir(25,1)!=0`, `enableIsh = Ir(24,1)!=0`, `dispConst = (uint)Ir(32,10)`.

```csharp
private void Dsp()
{
    uint dispAddr = (uint)Ir(12, 11);
    if (Ir(10, 2) == 2) { DMem[dispAddr] = (uint)AData; return; }

    int pos = (int)Ir(0, 5);
    if (Ir(10, 2) == 3) pos = LcByteMode();

    MData = (int)Rol32((uint)MData, pos);

    int len = (int)Ir(5, 3);
    int leftMaskIndex = (len - 1) & 0x1F;
    int mask = len == 0 ? 0 : unchecked((int)(~0u >> (31 - leftMaskIndex)));
    dispAddr |= (uint)MData & (uint)mask;

    uint map = (uint)Ir(8, 2);
    if (map != 0)
    {
        Uvmem.Vtop(MdReg, out _, out uint l2MapBits, out _, out _, out _);
        uint bit19 = (l2MapBits >> 19) & 1, bit18 = (l2MapBits >> 18) & 1;
        dispAddr |= map switch { 1 => bit18, 2 => bit19, 3 => bit18 | bit19, _ => 0 };
    }

    dispAddr &= 0x7FF;
    uint dispWord = DMem[dispAddr];
    DispatchConstant = (uint)Ir(32, 10);

    uint target = dispWord & 0x3FFF;
    bool n = ((dispWord >> 14) & 1) != 0, p = ((dispWord >> 15) & 1) != 0, r = ((dispWord >> 16) & 1) != 0;

    if (Ir(25, 1) != 0 && n) Npc--;
    if (Ir(24, 1) != 0) AdvanceLc(0);
    if (n) Inhibit = true;
    if (p && r) return;

    if (p) { if (!n) PushSpc(Npc); else PushSpc(Npc - 1); }
    if (r)
    {
        target = PopSpc();
        if ((target >> 14 & 1) != 0) target = AdvanceLc(target);
        target &= 0x3FFF;
    }
    Npc = target;
    Popj = false;
}
```

## Phase 7 — Byte Instructions (`Byt()`, `Msk()`)

```csharp
private uint Msk(int pos)
{
    int widthm1 = (int)Ir(5, 5);
    int rightMaskIndex = pos;
    int leftMaskIndex = (rightMaskIndex + widthm1) & 0x1F;
    uint leftMask = unchecked((uint)(~0 >> (31 - leftMaskIndex)));
    uint rightMask = unchecked((uint)(~0 << rightMaskIndex));
    return leftMask & rightMask;
}

private void Byt()
{
    uint dest = (uint)Ir(14, 12);
    uint mrSrBits = (uint)Ir(12, 2);
    int pos = (int)Ir(0, 5);
    if (Ir(10, 2) == 3) pos = LcByteMode();

    uint mask = Msk((mrSrBits & 2) != 0 ? pos : 0);
    switch (mrSrBits)
    {
        case 0:
            // TraceLog warning "mr_sr_bits == 0"
            Out = 0;
            break;
        case 1: // LDB
        case 3: // DPB
            MData = (int)Rol32((uint)MData, pos);
            Out = ((uint)MData & mask) | ((uint)AData & ~mask);
            break;
        case 2: // SEL-DEP
            Out = ((uint)MData & mask) | ((uint)AData & ~mask);
            break;
    }
    WriteDest(dest);
}
```
Note the LDB/DPB distinction lives entirely in where `Msk` was built (position 0 for LDB, position `pos` for DPB via the `mrSrBits & 2` test in the mask-construction call) — both cases execute textually identical code after that point, matching the C exactly.

## Phase 1 (continued) — `AdvanceLc`/`LcByteMode`

```csharp
private int LcByteMode()
{
    if ((InterruptControl & (1 << 29)) != 0)
    {
        int ir4 = (int)(P0 >> 4) & 1, ir3 = (int)(P0 >> 3) & 1;
        int lc1 = (int)(Lc >> 1) & 1, lc0 = (int)Lc & 1;
        int pos = (int)(P0 & 7);
        pos |= ((ir4 ^ (lc1 ^ lc0)) << 4) | ((ir3 ^ lc0) << 3);
        return pos;
    }
    else
    {
        int ir4 = (int)(P0 >> 4) & 1, lc1 = (int)(Lc >> 1) & 1;
        int pos = (int)(P0 & 0xF);
        pos |= ((ir4 ^ lc1) == 0 ? 1 : 0) << 4;
        return pos;
    }
}

private uint AdvanceLc(uint ppc)
{
    uint oldLc = Lc & 0x03FFFFFF; // 26-bit mask (LC is 26 bits; see correction note above)
    if ((InterruptControl & (1 << 29)) != 0) Lc++; else Lc += 2;

    if ((Lc & (1u << 31)) != 0)
    {
        Lc &= ~(1u << 31);
        VmaReg = oldLc >> 2;
        VmRead(oldLc >> 2, out NewMd);
        NewMdDelay = 2;
    }
    else
    {
        ppc |= 2;
    }

    uint lc0b = ((InterruptControl & (1 << 29)) != 0 ? 1u : 0u) & (Lc & 1);
    uint lc1 = (Lc & 2) != 0 ? 1u : 0u;
    bool lastByteInWord = (~lc0b & ~lc1 & 1) != 0;
    if (lastByteInWord) Lc |= (1u << 31);

    return ppc;
}
```

`PushSpc`/`PopSpc`:
```csharp
private void PushSpc(uint pc) { SpcPtr = (SpcPtr + 1) & 0x1F; Spc[SpcPtr] = pc; }
private uint PopSpc() { uint v = Spc[SpcPtr]; SpcPtr = (SpcPtr - 1) & 0x1F; return v; }
```

## Phase 8 — Consumer Rework

- **`Disassembler.cs`**: entirely rewritten. Field extraction must use the real layout (§ above) and mnemonic tables come from the real ALU op names (§ Phase 2 tables) and jump condition names (§ Phase 3), not the current invented `AluOp` enum. Dispatch-instruction disassembly can use `defmic300.h`'s `defmics[]` table (function-number → Lisp primitive name) for symbolic output where applicable.
- **`MicrocodeDebugger.cs`**: register-display code (`Npc`, `Opc`, `Out`, `Q`, `MdReg`, `VmaReg`, `Lc`, `OaRegHigh`/`Low`, `MData`, `AData`, `PdlPointer`) keeps working unchanged (same names, same meanings). The `CarryFlag`/`OverflowFlag`/`NegativeFlag`/`ZeroFlag` display block is replaced with `AluCarry` (the real hardware has no persistent flags register — those four were an invented artifact). `ExecuteInstruction(pc, useImem)`/`FetchInstruction` call sites are replaced by the new `Step()`-based execution path.
- **`ConfigManager.cs`**: no changes needed (its `UCode` references are pure tracing-flag configuration, already generic).
- **`MachineControl.cs`**: `UCode` field becomes a normal instance reference (`new UCode(mainMemory)` or similar, wiring in the `Uvmem`/`MainMemory` dependency from Phase 5); state save/restore's register list (`Npc`, `PdlPointer`, `VmaReg`, `MdReg`) stays valid since those registers are unchanged by the rewrite.
- **`UCode.Halted` needs a consumer.** Phase 3 added `Halted` (set by `Jmp()`'s ILLOP handling) as a plain field with no run-loop reading it, since no continuous `Step()`-driving loop exists yet (`MachineControl.Run()` loops on `PowerState`, not on calling `Step()` in a loop; the only `Step()` callers today are single-step debug commands). Whatever this rework introduces as the real "run until halted" loop (mirroring `ucode.c`'s `ucode_run()`: `while (!machine_state.halted) { uexec_step(); ... }`) must check `Halted` and stop.

## Phase 9 — Test Suite

Replaces `UCodeTests.cs`'s current self-consistency-only tests (which validate the invented format against itself) with:
- One test method per operation table in Phase 2/3 (`LogiOps`, `ArithOps`, `DivOps`, `QControl`, `OutControl`, `CheckJumpCondition`), asserting specific input→output pairs against the hand-derived semantics documented above.
- `Add32`/`Sub32`/`Abs32`/`Rol32` helper tests (pure functions, trivial to cover exhaustively for edge cases like `bits=0`, `MinValue`/`MaxValue` operands).
- A decode-only sanity test: load `sys/ubin/promh.mcr` into `Prom`, iterate a sample of instructions, decode `Op`/`AAddr`/`MAddr`/etc., and assert the opcode-class distribution is non-degenerate (not 100% one class) as a smoke check that decode isn't systematically broken. Cross-referencing specific instructions against `promh.sym`/`promh.tbl` for known-good disassembly is a stretch goal for this phase, not a hard requirement.
- `Uvmem` gets its own focused tests: `Vtop`/`WriteMap` round-trip (write a mapping, read it back), and the "L2 write sees the just-written L1 entry in the same call" behavior explicitly (§ Phase 5 note).

## Open Items For Implementation-Time Resolution

These are flagged rather than resolved here because they need the actual current `UCode.cs` code in front of the implementer, not because they're architecturally ambiguous:
- Exact current size/type of `DMem` in `UCode.cs` (must be `uint[2048]`) — verify and resize if needed.
- Where `P1Imem`'s "PROM disabled" source flag currently lives in this port (`machine_state.promdisabled` equivalent) — likely a `MachineControl`/config flag; confirm during Phase 1.
- `VmaOk`/`InterruptPending` (`machine_state.vmaok`/`interrupt_pending_flag` equivalents) — introduce as fields during Phase 1 (defaulting to `VmaOk = true` until Phase 5 wires real page-fault detection through `Vm()`).
- ~~`bus_interface_bus_reset()` equivalent...~~ **Resolved in Phase 4:** a no-op plus an `Info`-level log — see the "Resolved (Phase 4...)" note above the `MfWrite` table. Not wired to `IOBus.cs` (would be a faithful-looking but wrong substitution); `bus-interface.c` remains a wholly separate, not-yet-ported subsystem.
- ~~Exact `Halted` flag location...~~ **Resolved in Phase 3:** a plain `public bool Halted { get; set; }` field directly on `UCode`, deliberately NOT wired to `MachineControl`'s richer power-state machinery (no run-loop exists yet that would read either). See the new Phase 8 bullet below — this still needs a consumer once a continuous `Step()`-driving loop exists.
- **New (found during Phase 3's final review, 2026-08-24):** `UCode`'s interrupt-pending PRODUCERS (`SetInterruptStatusReg`, `AssertUnibusInterrupt`, `AssertXbusInterrupt`, `DeassertUnibusInterrupt`, `DeassertXbusInterrupt` — all pre-existing Phase-1-era code) diverge materially from the real C's `set_interrupt_status_reg`/`assert_unibus_interrupt`/etc. (`usim/ucode.c:114-188`): the real C masks `interrupt_status_reg` with `0140000` to derive `interrupt_pending_flag`, gates `assert_unibus_interrupt` on the `02000` enable bit, masks the stored vector with `01774`, and routes Xbus asserts through the same status register (`|= 040000`) rather than setting the pending flag directly. The C# versions currently just set `InterruptPendingFlag = (newValue != 0)` unconditionally and never route Xbus asserts through the status register at all. This was latent and inert until Phase 3 made `CheckJumpCondition()` the first real consumer of `InterruptPendingFlag` (jump condition codes 5/6 — confirmed via decoding `ucadr.mcr` to have 589 real call sites) — meaning it can now cause `Jmp()` to branch differently than the real hardware for any code exercising interrupt-conditional jumps. Not assigned to any phase yet; Phase 4 covers `MfWrite`'s INTERRUPT-CONTROL register but not this status-register/pending-flag producer logic. Needs its own task in a future phase (most naturally alongside Phase 4, since both touch interrupt state) — re-derive `SetInterruptStatusReg`/`AssertUnibusInterrupt`/`AssertXbusInterrupt`/`DeassertUnibusInterrupt`/`DeassertXbusInterrupt` against `usim/ucode.c:114-188` directly when that task is planned.
  **Escalation (Phase 4's final review, 2026-08-24):** Phase 4's `MfWrite` case 2 makes `InterruptControl` microcode-writable for the first time (previously it was always 0 in any real run, so jump codes 5/6 could never even reach the `InterruptPendingFlag` term) — real-microcode calibration found 9 static INTERRUPT-CONTROL write sites (2 in the boot PROM, 7 in `ucadr.mcr`), including a dedicated single-bit write to bit 27 (interrupt enable). Combined with Phase 3's 589 condition-5/6 call sites, this divergence is now **live, not merely latent** — still no Phase 4/5 code change was needed or made, but this should be prioritized accordingly whenever it is finally scheduled.
