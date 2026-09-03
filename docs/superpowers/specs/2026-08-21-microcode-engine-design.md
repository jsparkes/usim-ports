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

**Correction (Phase 5 implementation, 2026-08-25):** `Uvmem` has NO `MainMemory` dependency — the `_mainMemory` field/constructor-parameter below is dead weight. Neither `Vtop` nor `WriteMap` ever reads or writes through it, matching the real C (`uvmem_vtop`/`uvmem_write_map` never touch main memory either). Actual physical memory access belongs to `UCode.Vm()` (below), which holds its own separate `MainMemory` reference.

```csharp
public class Uvmem
{
    private readonly uint[] _l1Map = new uint[2048];
    private readonly uint[] _l2Map = new uint[1024];

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

`UCode` holds its `Uvmem` dependency as a property: `public Uvmem Uvmem { get; }`. As implemented, `UCode` has two constructors — `public UCode() : this(new MainMemory()) { }` (preserves every existing Phases 1-4 call site's behavior unchanged) and `public UCode(MainMemory mainMemory)` (stores it in a private `_mainMemory` field for `Vm()`'s real physical-memory access, and always builds `Uvmem = new Uvmem();`) — so `Uvmem` and `_mainMemory` are non-null regardless of which constructor is used. Every call site below (`Vm`, `MfRead` code 9, `Dsp`'s L2-map step) uses this same `Uvmem` property — there is exactly one virtual-memory instance per `UCode`, never a static/singleton. **`MachineControl` does not yet use the `UCode(MainMemory)` overload** (it still does `Memory = new MainMemory(); UCode = new UCode();` separately, so microcode-driven physical writes are currently invisible to `MachineControl.Memory`) — see the new Open Items entry below; wiring this through is Phase 8's job, already named in that phase's own `new UCode(mainMemory)` bullet.

`Vm`/`VmRead`/`VmWrite` (on `UCode`, calling into `Uvmem` and `MainMemory`'s new physical-address API):
```csharp
private void Vm(bool write, uint vaddr, ref uint v)
{
    vaddr &= 0x00FFFFFF;
    uint paddr = Uvmem.Vtop(vaddr, out _, out _, out uint pn, out bool wp, out bool ap);
    VmaOk = write ? (ap && wp) : ap;
    if (!VmaOk) { v = 0; return; }

    // TV-screen quirk: this line is the WORKAROUND, not the bug -- usim/uvmem.c's
    // vm() comments on a symptom in the plain (pn<<8)|(vaddr&0xFF) formula for this
    // one page ("this is not working for the access below... no idea why"), and this
    // override is what actually produces the correct address (reproduces the C
    // comment's own worked "actual paddr should be 17'051'765" example exactly).
    // 036000 octal = 0x3C00 (NOT 0x1E00, an earlier draft's mistranslation) and
    // 017000000 octal = 0x3C0000 (NOT 0x0F00000, likewise) -- re-derive from the
    // literal octal digits directly if this ever needs re-checking, never trust a
    // restated hex value in this document (this is the fourth such mistranslation
    // found in this spec, after Phase 1's LC/OA masks and Phase 4's MfRead mask).
    if (pn == 0x3C00) paddr = 0x3C0000 | (vaddr & 0x7FFF);

    // The real C dispatches through bus_adaptor_read/write -> bus_adaptor_rw, which
    // re-derives ITS OWN page number from the (possibly quirk-overridden) paddr, not
    // from Vtop's original pn, and routes across three ranges: pn<=0x3BFB ("xbus
    // main memory" -- confirmed a bare pass-through to real main memory) is
    // implemented for real below; 0x3C00-0x3DFF (XBus I/O devices) and
    // 0x3E00-0x3FFF (Unibus) need a wholly separate, not-yet-ported "bus adaptor"
    // subsystem (usim/bus-adaptor.c, usim/tv.c, usim/iob.c, etc.) -- see the new
    // Open Items entry below. Real-microcode calibration (decoding this repo's own
    // sys/ubin/promh.mcr) found the PROM's own bootstrap reaches this deferred range
    // within ~10 instructions of its first page-map setup (disk control registers
    // and diagnostic/spy registers) -- this is the very next real boot blocker, not
    // a backwater; a throw here would abort the boot immediately, so the deferral is
    // a non-fatal, warning-logged placeholder instead.
    uint dispatchPn = (paddr >> 8) & 0x3FFF;
    if (dispatchPn <= 0x3BFB)
    {
        if (write) _mainMemory.WritePhysical(paddr, v);
        else v = _mainMemory.ReadPhysical(paddr);
    }
    else
    {
        // TraceLog.Instance.Warning(...); if (!write) v = 0;
    }
}
private void VmRead(uint vaddr, out uint v) { v = 0; Vm(false, vaddr, ref v); }
private void VmWrite(uint vaddr, uint v) { Vm(true, vaddr, ref v); }
```
The C reference's `035774 <= pn <= 035777` (`0x3BFC-0x3BFF`) A-memory-mapped branch always hits `assert(false)` in the original (dead/broken as written) — **omitted entirely** in the port (implemented in Phase 5); its range is folded into the `dispatchPn > 0x3BFB` placeholder above, a safe superset since that branch has no reachable behavior to be faithful to.

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

## Phase 5B — Bus Adaptor (`BusAdaptor.cs`, new file)

**Not one of the original 9 phases** — added 2026-08-30 after Phase 5's final review found the deferred XBus-I/O/Unibus placeholder in `Vm()` is hit within ~10 instructions of the boot PROM's own bootstrap (see the Open Items entry this replaces). Scope is deliberately **minimal and honest**, not a full device-emulation port: it replaces `Vm()`'s blind "warn and return/discard zero" placeholder with a *faithful address-routing layer* (matching `usim/bus-adaptor.c`'s actual dispatch structure) plus the two specific register behaviors the PROM's early boot actually depends on. It does **not** implement real disk data transfer, TV/color-TV screens, tape, or Unibus-Map DMA — those stay scoped placeholders, now routed to correctly instead of caught by one blind top-level catch-all. Full-port scope (if ever wanted) would additionally touch `usim/disk-controller.c`'s real transfer logic, `usim/tv.c`, `usim/colortv.c`, `usim/iob.c`, `usim/tape-controller.c`, and `usim/unibus-mapping.c` — roughly 3,150 more lines across 6 files; not attempted here.

### Why this is needed now, not later

Real-microcode calibration (decoding `sys/ubin/promh.mcr`, cross-checked against the annotated `sys/ucadr/promh.text`) found the PROM's `SET-UP-FOUR-PAGES` maps a page to XBus disk-control registers (physical page `0o36777` = `0x3DFF`) and a page to Unibus diagnostic/spy registers (physical page `0o37766` = `0x3FF6`, resolving via the Unibus page formula to Unibus address `0o766012`) — both reachable almost immediately after the PROM's first page-map setup, roughly 10 instructions in. Half the PROM's 28 `Vm()`-dispatching sites land in this range. A throw here would abort the boot; the current placeholder is non-fatal but gives every device the same wrong answer (0), including registers the PROM actually polls in a loop.

### `usim/bus-adaptor.c`'s real dispatch structure (502 lines; verified by direct read, not the earlier per-phase summaries)

- **`bus_adaptor_xbus_rw(write, paddr, pv)`** — the function `Vm()`'s `dispatchPn` split already inlines: `pn<=0x3BFB` → real main memory (already faithful, Phase 5); `0x3C00<=pn<=0x3DFF` → `bus_adaptor_xbusio_rw`; else → warn + NXM (`bus_interface_set_xbus_nxm()`, from the already-deferred `bus-interface.c` — treat as a no-op, same ruling as Phase 4's `bus_interface_bus_reset()`).
- **`bus_adaptor_xbusio_rw(write, paddr, pv)`** — dispatches by absolute `paddr` (not page number) within the XBus-I/O window:
  | Range (octal) | Device | This phase's treatment |
  |---|---|---|
  | `017000000`-`017077777` | Main TV screen (`tv_screen_read`/`write`) | Scoped placeholder (not implemented) |
  | `017200000`-`017277777` | Color TV screen, if `colortv_enabled` | Scoped placeholder |
  | `017377750`-`017377757` | Color TV control | Scoped placeholder |
  | `017377760`-`017377767` | Main TV control | Scoped placeholder |
  | `017377774`-`017377777` | (First) disk control, offset = `paddr - 017377774` ∈ {0,1,2,3} | **Real, minimal** (see below) |
  | else | Unmapped | warn + NXM (no-op) + `*pv=0` on read |
  Convert every octal boundary above to hex by direct computation, not digit-counting, before implementing — this project has had four octal-to-hex mistranslations already.
- **`bus_adaptor_unibus_rw(write, uaddr, pv16)`** (16-bit words) — `uaddr` is derived from `paddr` by `Vm()`'s existing Unibus-range branch (`((pn-0x3E00)<<8 | (paddr&0xFF)) << 1`, already the formula `bus_adaptor_rw` uses, per Phase 5's own end-to-end trace B). Dispatch:
  | Range (octal) | Device | This phase's treatment |
  |---|---|---|
  | `0140000`-`0177777` | Unibus Map (DMA remapping into XBus space, incl. the MD-register diagnostic path) | Scoped placeholder — real logic needs `unibus_mapping_registers[]` state and hi/lo-word buffering not built yet |
  | `0764000`-`0764176` | `iob_unibus_read`/`write` | Scoped placeholder |
  | `0766000`-`0766036` | `diagnostic-interface.c` ("spy" registers) | **Real for one register** (see below); scoped placeholder for the rest |
  | `0766040`-`0766136` | `bus-interface.c` | Already a separate deferred subsystem (Phase 4 ruling) |
  | `0766140`-`0766176` | `unibus-mapping.c` | Scoped placeholder |
  | `0772520`-`0772532` | `tape-controller.c` | Scoped placeholder |
  | else | Unmapped | warn + NXM (no-op) |

### Real behavior #1: diagnostic-interface mode register (`0766012`)

`usim/diagnostic-interface.c:286-291`:
```c
case 0766012:
    machine_state.promdisabled = ((v & (1<<5)) != 0);
    break;
```
This is directly load-bearing: `machine_state.promdisabled` is the exact flag this port's `UCode.PromDisabled` already models (resolved as a Phase 1 Open Item — `IncNpc()` already branches on it to choose `IMem`/`Prom`). Port as:
```csharp
case 0766012: PromDisabled = (data & (1 << 5)) != 0; break;
```
Every other spy register (`0766000`-`0766006` DEBUG-IR, `0766010` OPC control — both `errx()`-fatal in the real C on any write, since real microcode is never expected to touch them; `0766014`/`0766016` unused) gets a scoped placeholder (log + no-op), not a faithful `errx`-equivalent throw — replicating a "should never happen" fatal guard isn't useful here, and if real boot microcode genuinely never hits it (as expected), the distinction never surfaces.

### Real behavior #2: disk-controller status register (offset 0, i.e. paddr `017377774`)

`usim/disk-controller.c:174-219` (`encode_status()`) builds a composite status word from live disk-unit/controller state. The PROM's `DISK-RECALIBRATE` polls exactly two bits in a loop (confirmed against `sys/ucadr/promh.text`): bit 0 (`not_active` — "ready/idle") and bit 9 (`!online`, i.e. "online" when clear). A full port needs real per-unit state (`seek_error`, `read_only`, `has_fault`, `attention`, real command/transfer handling); this phase ports only enough to satisfy the poll:
```csharp
// offset 0 (status), read: bit0=1 (not_active/ready), bit9=0 (online), everything else 0 (no errors).
case 0: return 1u; // (1<<0), matching encode_status()'s default-no-error/ready/online composition
```
Offsets 1 (memory address) and 3 (ECC, "no ECC errors in usim, so this always returns 0") get a plain `0`; offset 2 (disk address) can round-trip a written value (matching the real `da` register's read/write semantics) or also return 0 — the PROM's early boot only reads offset 0, so this is a minor, low-stakes choice, not one to over-engineer. Writes to any disk-control offset (the command/CLP/DA registers) are a scoped placeholder (no-op) — **explicitly not a working disk**: nothing here reads a real disk image or performs a real transfer, so any code path that expects `SAVE-A-PAGE`/actual boot-sector data to arrive correctly will not get real data. This is intentionally out of scope; note it as a tracked Open Item, not silently implied to work.

### `UCode` wiring

`BusAdaptor` needs to reach back into `UCode.PromDisabled` for the one real register above — unlike `Uvmem` (which needed no `UCode`/`MainMemory` dependency at all), this is a genuine, justified coupling. Simplest shape: `BusAdaptor` is a plain class with no constructor dependencies, and its `Read`/`Write` methods take `ref bool promDisabled` (or `UCode.Vm()` handles the `0766012` special case itself, inline, before delegating everything else to `BusAdaptor` — whichever reads cleaner; finalize in the Phase 5B plan). `UCode` gains a `BusAdaptor` property analogous to `Uvmem`, constructed the same way (always non-null, no dependency on which `UCode` constructor ran). `Vm()`'s existing placeholder branch (`dispatchPn > 0x3BFB`) is replaced with a real call into `BusAdaptor`, keeping the existing `dispatchPn<=0x3BFB` main-memory fast path exactly as Phase 5 left it.

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
    uint leftMask = ~0u >> (31 - leftMaskIndex);
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

**Verified against `usim/uexec.c:958-1017` (`msk()`/`byt()`) fresh, field by field — one bug found and fixed above.** The real `left_mask` is declared `uint32_t`, so its `>>=` is a logical (zero-filling) shift; a bare `~0` in C# is a signed `int` (value -1), and `>>` on a negative signed `int` is an *arithmetic* (sign-extending) shift, which would leave `leftMask` at `0xFFFFFFFF` for every `leftMaskIndex` — silently breaking the sliding mask. The fix (`~0u`) exactly matches the pattern `Dsp()` already uses correctly (`UCode.cs`'s `~0u >> (31 - leftMaskIndex)`) for the identical trap. `rightMask`'s `<<` has no such issue — left-shift is bit-pattern-identical for signed/unsigned `int`/`uint` in C#, so `~0 << rightMaskIndex` is fine as written. Also confirmed: unlike `Dsp()`'s `len`, `Msk()`'s `widthm1` field has no zero-is-special case in the real C (`msk()` has no such branch) — width is always `widthm1+1 ∈ [1,32]`, so none is needed here either. `Byt()`'s case 1/3 merge (both LDB and DPB execute textually identical bodies in the real C after `mask` is built at a different position) is a faithful simplification, not a deviation.

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
- **`MachineControl.cs`**: `UCode` field becomes a normal instance reference built via `new UCode(mainMemory)` (the constructor already exists and is tested, as of Phase 5 — `MachineControl` just doesn't call it yet, still building `Memory`/`UCode` as two disconnected instances); state save/restore's register list (`Npc`, `PdlPointer`, `VmaReg`, `MdReg`) stays valid since those registers are unchanged by the rewrite. Also reconcile `MainMemory`'s pre-existing `Read`/`Write`/`TranslateAddress` (an invented paging scheme) with `Uvmem`'s faithful L1/L2 map, now that both exist side by side (Phase 5) — decide whether the old paging survives for any real consumer or should be retired.
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
- ~~`VmaOk`/`InterruptPending`...~~ **Resolved.** `VmaOk`/`InterruptPendingFlag` introduced in Phase 1 as fields; `VmaOk` is now set for real by Phase 5's `Vm()` from `Uvmem`'s permission bits (defaults to `true` before the first cycle that calls it).
- ~~`bus_interface_bus_reset()` equivalent...~~ **Resolved in Phase 4:** a no-op plus an `Info`-level log — see the "Resolved (Phase 4...)" note above the `MfWrite` table. Not wired to `IOBus.cs` (would be a faithful-looking but wrong substitution); `bus-interface.c` remains a wholly separate, not-yet-ported subsystem.
- ~~Exact `Halted` flag location...~~ **Resolved in Phase 3:** a plain `public bool Halted { get; set; }` field directly on `UCode`, deliberately NOT wired to `MachineControl`'s richer power-state machinery (no run-loop exists yet that would read either). See the new Phase 8 bullet below — this still needs a consumer once a continuous `Step()`-driving loop exists.
- **New (found during Phase 3's final review, 2026-08-24):** `UCode`'s interrupt-pending PRODUCERS (`SetInterruptStatusReg`, `AssertUnibusInterrupt`, `AssertXbusInterrupt`, `DeassertUnibusInterrupt`, `DeassertXbusInterrupt` — all pre-existing Phase-1-era code) diverge materially from the real C's `set_interrupt_status_reg`/`assert_unibus_interrupt`/etc. (`usim/ucode.c:114-188`): the real C masks `interrupt_status_reg` with `0140000` to derive `interrupt_pending_flag`, gates `assert_unibus_interrupt` on the `02000` enable bit, masks the stored vector with `01774`, and routes Xbus asserts through the same status register (`|= 040000`) rather than setting the pending flag directly. The C# versions currently just set `InterruptPendingFlag = (newValue != 0)` unconditionally and never route Xbus asserts through the status register at all. This was latent and inert until Phase 3 made `CheckJumpCondition()` the first real consumer of `InterruptPendingFlag` (jump condition codes 5/6 — confirmed via decoding `ucadr.mcr` to have 589 real call sites) — meaning it can now cause `Jmp()` to branch differently than the real hardware for any code exercising interrupt-conditional jumps. Not assigned to any phase yet; Phase 4 covers `MfWrite`'s INTERRUPT-CONTROL register but not this status-register/pending-flag producer logic. Needs its own task in a future phase (most naturally alongside Phase 4, since both touch interrupt state) — re-derive `SetInterruptStatusReg`/`AssertUnibusInterrupt`/`AssertXbusInterrupt`/`DeassertUnibusInterrupt`/`DeassertXbusInterrupt` against `usim/ucode.c:114-188` directly when that task is planned.
  **Escalation (Phase 4's final review, 2026-08-24):** Phase 4's `MfWrite` case 2 makes `InterruptControl` microcode-writable for the first time (previously it was always 0 in any real run, so jump codes 5/6 could never even reach the `InterruptPendingFlag` term) — real-microcode calibration found 9 static INTERRUPT-CONTROL write sites (2 in the boot PROM, 7 in `ucadr.mcr`), including a dedicated single-bit write to bit 27 (interrupt enable). Combined with Phase 3's 589 condition-5/6 call sites, this divergence is now **live, not merely latent** — still no Phase 4/5 code change was needed or made, but this should be prioritized accordingly whenever it is finally scheduled.
- ~~New, high priority (Phase 5's final review, 2026-08-25): the deferred XBus-I/O/Unibus "bus adaptor" port is the next real boot blocker...~~ **Scheduled as Phase 5B, 2026-08-30** (see that section) — scoped minimally (address routing + two real registers: the diagnostic-interface mode register and a minimal disk-status register), not a full device-emulation port. Real disk transfer, TV, tape, and Unibus-Map DMA remain deferred placeholders within Phase 5B itself; a full port is still unscheduled.
- **New (Phase 5's final review, 2026-08-25): `MachineControl` doesn't yet use `UCode(MainMemory)`.** It still does `Memory = new MainMemory(); UCode = new UCode();` as two independent, disconnected instances (confirmed in both `usim-cs/MachineControl.cs` and the untracked `usim-cs-sdl/MachineControl.cs` copy) — so microcode-driven physical writes via `Vm()` are currently invisible to `MachineControl.Memory`, state save/restore, and any dumper. `UCode(MainMemory)` exists and is tested (`UCodeVirtualMemoryTests.cs`) but has zero production call sites. This is squarely Phase 8's `MachineControl.cs` bullet (already named there) — flagging it explicitly so it isn't missed as "already done" just because the constructor exists.
- **New (Phase 5's final review, 2026-08-25): `MainMemory.ReadPhysical`/`WritePhysical` have no populated-page-count gate.** The real C's `main_memory_read`/`write` gate on a configurable populated-page count (`usim.ini`'s `memory.size`, default 8192 pages) and return `0xffffffff`/fail above it — the C's own comment notes this can happen "during memory probing by the prom". The C# always has all 16384 pages populated and returns plain `0` out of bounds. This can't currently crash (the main-memory dispatch path's max address is within `PHYSICAL_MEM_SIZE`), and the spec's own Phase 5 text sanctioned the return-0 shape — but any microcode that sizes memory by probing will see a different answer than the reference. Belongs on Phase 8's list alongside the `MainMemory`-reconciliation item below.
- **New (Phase 5's final review, 2026-08-25): `MainMemory`'s pre-existing `Read`/`Write`/`TranslateAddress` use an invented paging scheme unrelated to `Uvmem`'s faithful L1/L2 map.** Two parallel, inconsistent virtual-memory schemes now coexist in the codebase. Reconciling or retiring the old one is Phase 8's job — see its `MachineControl.cs` bullet, which should be expanded to cover this explicitly, not just the constructor wiring.
