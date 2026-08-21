# Microcode Engine Phase 1: Register Model & Fetch/Decode Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `UCode`'s invented instruction format with the real CADR register model and a faithful fetch/decode/pipeline foundation (`Step()`, `IncNpc()`, common field decode, POPJ handling), with stubbed `Alu()`/`Jmp()`/`Dsp()`/`Byt()` handlers that later phases (2, 3, 6, 7) will replace one at a time.

**Architecture:** `UCode` becomes an instance class (matching `Display`/`Keyboard`/`Mouse`'s existing pattern) holding the real CADR registers. `Step()` is rewritten as a faithful port of `uexec_step()`: pipeline advance, inhibit check, OA-register merge, common field decode, dispatch to one of four (currently stubbed) opcode-class handlers, and POPJ handling. Three consumer files (`Disassembler.cs`, `MicrocodeDebugger.cs`, `DebugCommands.cs`) and `Program.cs`'s `--debug-microcode` wiring are updated to compile against the new instance-based API — `Disassembler`'s mnemonic-level output becomes a placeholder (full disassembly is Phase 8, once real ALU/jump semantics exist to name).

**Tech Stack:** C# / .NET 8.0, no new dependencies.

**Spec:** [docs/superpowers/specs/2026-08-21-microcode-engine-design.md](../specs/2026-08-21-microcode-engine-design.md) — this plan implements that spec's "Register Model (Phase 1)" and "Common decode (`Ir` helper + `Step()`)" sections, plus the "Phase 1 (continued) — `AdvanceLc`/`LcByteMode`" section and `PushSpc`/`PopSpc`.

## Global Constraints

- `UCode` becomes an instance class — no `static` on any of the members this plan touches. (Do not go through the file removing `static` from members outside this plan's scope, like `InstructionTraceEnabled`/`MaxTraceLines`/performance-counter properties — those are untouched by this phase and stay exactly as they are, static or not, for later phases to deal with if ever.)
- Field/property names must match the spec's Register Model table exactly (e.g. `AAddr` not `Aaddr`, `MData` not `Mdata`) — later phases' plans reference these exact names.
- `Alu()`, `Jmp()`, `Dsp()`, `Byt()` are stubs in this phase (`throw new NotImplementedException(...)` with a message naming which phase implements them) — do not attempt real logic for any of them here.
- `CarryFlag`/`OverflowFlag`/`NegativeFlag`/`ZeroFlag`/`UpdateFlags`/the `AluOp` enum/`ExecuteAlu`/`ExecuteInstruction`/`FetchInstruction`/`GetAluOp`/`GetMSource`/`GetASource`/`GetDest`/`GetJumpCond`/`GetNextPC`/`ExtractField`/`ReadMSource`/`ReadASource`/`WriteDestination`/`EvaluateJumpCondition`/`BarrelShift`/`ALU_OP_POS` and friends (the field-position constants) are all part of the invented format and are deleted in this phase, not kept alongside the new model.
- `Pc` (the orphaned instance property, distinct from the real `Npc`) is deleted; every consumer that read/wrote it is updated to use `Npc` instead, since `Pc` never reflected real execution state.

---

## Task 1: Register model, `Ir` helper, and `Init()`

**Files:**
- Modify: `usim-cs/UCode.cs` (lines 1-522 of the current file — everything from the top through the end of the `AluOp` enum and `ExecuteAlu`/`BarrelShift`, i.e. `#region Constants` through the end of `#region ALU Operations`)

**Interfaces:**
- Produces: the full register model as instance fields (see table below) — every later task and every later phase's plan refers to these exact names and types.
- Produces: `private ulong Ir(int pos, int len)` — the bit-extraction helper every opcode-class handler will use starting in Phase 2.
- Produces: `public void Init()` — instance method (was `public static void Init()`), zeroing every register.

- [ ] **Step 1: Replace the constants region and all field/property declarations**

Delete the current `#region Constants` block (lines 23-48 — the `PROM_SIZE`/`IMEM_SIZE`/etc. size constants AND the `ALU_OP_POS`/`M_SOURCE_POS`/`A_SOURCE_POS`/`DEST_POS`/`JUMP_COND_POS`/`NEXT_PC_POS` field-position constants — the sizes are fine to keep, the field-position constants are not, since they describe the invented format; re-add just the size constants):

```csharp
    #region Constants

    // Memory sizes
    public const int PROM_SIZE = 512;
    public const int IMEM_SIZE = 16 * 1024;
    public const int AMEM_SIZE = 1024;
    public const int MMEM_SIZE = 32;
    public const int DMEM_SIZE = 2048;
    public const int PDL_SIZE = 1024;
    public const int SPC_SIZE = 32;

    #endregion
```

Delete everything from `// Machine cycles counter` (current line 50) through the end of the `AluOp` enum and `ExecuteAlu`/`BarrelShift` (current line 619, just before `#endregion` that closes `#region ALU Operations` — i.e. delete lines 50-619, but re-add the `#endregion` for ALU Operations as an empty region for now, since Phase 2 will populate it). Replace with:

```csharp
    // Machine cycles counter
    public ulong MachineCycles { get; set; }

    // Interrupt status
    public int InterruptStatusReg { get; private set; }
    public bool InterruptPendingFlag { get; set; }

    // Microcode memory
    public bool PromEnabledFlag { get; set; }
    public ulong[] Prom { get; } = new ulong[PROM_SIZE];
    public ulong[] IMem { get; } = new ulong[IMEM_SIZE];

    // A, M, and D memories
    public uint[] AMem { get; } = new uint[AMEM_SIZE];
    public uint[] MMem { get; } = new uint[MMEM_SIZE];
    public uint[] DMem { get; } = new uint[DMEM_SIZE];

    // Push-down list (stack)
    public uint[] Pdl { get; } = new uint[PDL_SIZE];

    // Stack pointer cache
    public uint[] Spc { get; } = new uint[SPC_SIZE];
    public uint SpcPtr { get; set; }

    // Named registers
    public uint DispatchConstant { get; set; }
    public uint PdlPointer { get; set; }
    public uint PdlIndex { get; set; }
    public uint VmaReg { get; set; }
    public uint MdReg { get; set; }
    public uint Lc { get; set; }
    public uint OaRegHigh { get; set; }
    public uint OaRegLow { get; set; }
    public uint Opc { get; set; }
    public uint Q { get; set; }
    public uint OldQ { get; set; }
    public uint InterruptControl { get; set; }

    // Pipeline registers
    public ulong P0 { get; set; }
    public uint P0Pc { get; set; }
    public bool P0Imem { get; set; }

    public ulong P1 { get; set; }
    public uint P1Pc { get; set; }
    public bool P1Imem { get; set; }

    public ulong Iwr { get; set; }

    public uint Npc { get; set; }

    // Latched operands for the current cycle
    public uint AAddr { get; set; }
    public int AData { get; set; }
    public uint MAddr { get; set; }
    public int MData { get; set; }

    public uint Out { get; set; }

    public bool Inhibit { get; set; }
    public bool UExecHasRunOnce { get; set; }

    // Decoded common fields for the current cycle
    public uint Op { get; set; }
    public bool Popj { get; set; }

    // Pending delayed memory read
    public uint NewMd { get; set; }
    public uint NewMdDelay { get; set; }

    // ALU result/carry for the current cycle (real hardware has no
    // persistent flags register — these replace the old Carry/Overflow/
    // Negative/Zero flags, which were an invented artifact)
    public uint AluCarry { get; set; }
    public uint AluOut { get; set; }

    // OA-register pending-merge flags
    public bool Oal { get; set; }
    public bool Oah { get; set; }

    /// <summary>
    /// Read len bits of P0 starting at bit pos (mirrors uexec.c's ir()).
    /// </summary>
    private ulong Ir(int pos, int len)
    {
        return (P0 >> pos) & ((1UL << len) - 1);
    }

    /// <summary>
    /// Initialize the microcode system
    /// </summary>
    public void Init()
    {
        MachineCycles = 0;
        InterruptStatusReg = 0;
        InterruptPendingFlag = false;
        PromEnabledFlag = false;

        Array.Clear(Prom);
        Array.Clear(IMem);
        Array.Clear(AMem);
        Array.Clear(MMem);
        Array.Clear(DMem);
        Array.Clear(Pdl);
        Array.Clear(Spc);

        SpcPtr = 0;
        DispatchConstant = 0;
        PdlPointer = 0;
        PdlIndex = 0;
        VmaReg = 0;
        MdReg = 0;
        Lc = 0;
        OaRegHigh = 0;
        OaRegLow = 0;
        Opc = 0;
        Q = 0;
        OldQ = 0;
        InterruptControl = 0;

        P0 = 0;
        P0Pc = 0;
        P0Imem = false;

        P1 = 0;
        P1Pc = 0;
        P1Imem = false;

        Iwr = 0;
        Npc = 0;

        AAddr = 0;
        AData = 0;
        MAddr = 0;
        MData = 0;

        Out = 0;
        Inhibit = false;
        UExecHasRunOnce = false;

        Op = 0;
        Popj = false;
        NewMd = 0;
        NewMdDelay = 0;
        AluCarry = 0;
        AluOut = 0;
        Oal = false;
        Oah = false;
    }

    #region ALU Operations
    #endregion
```

- [ ] **Step 2: Build**

Run: `dotnet build LispMachine.sln`
Expected: build FAILS at this point — `MicrocodeDebugger.cs`, `Disassembler.cs`, `DebugCommands.cs`, and `Program.cs` still reference members this step deleted (`CarryFlag`, `GetAluOp`, `AluOp`, `FetchInstruction`, `ExecuteInstruction`, `Pc`, etc.), and every call site in this file and others that used `UCode.Xxx` as a *static* reference (there are none left within `UCode.cs` itself calling its own members statically from outside, but `Program.cs`'s `new MicrocodeDebugger()` construction and the debugger's own internal calls will fail). This is expected — Task 3 fixes the consumers. Do not attempt to fix consumer files in this task.

- [ ] **Step 3: Commit**

```bash
git add usim-cs/UCode.cs
git commit -m "Replace UCode's invented register model with the real CADR register set"
```

---

## Task 2: `Step()`, `IncNpc()`, `AdvanceLc`/`LcByteMode`, `PushSpc`/`PopSpc`, and stubbed opcode handlers

**Files:**
- Modify: `usim-cs/UCode.cs` (replaces `Step()`, `ExecuteInstruction`, `FetchInstruction`, `ReadMSource`, `ReadASource`, `WriteDestination`, `EvaluateJumpCondition`, `MachRun`, `Run` — current lines ~269-481 — plus the `#region Pipeline and Control` block's `AdvancePipeline`/`FlushPipeline`/`CheckPageFault`/`MemoryCycle`, current lines ~847-902, and the `#region SPC (Stack Pointer Cache)` block, current lines ~816-845)
- Test: none new in this task — Task 3 adds the decode-sanity test once the build is green again (there's no point testing against a codebase that doesn't compile).

**Interfaces:**
- Consumes: the register model and `Ir()` helper from Task 1.
- Produces: `public void Step()` (instance method, real fetch/decode/dispatch loop — this is what `MachineControl.Step()`/`DebugCommands.StepCommand` call), `private void IncNpc()`, `private int LcByteMode()`, `private uint AdvanceLc(uint ppc)`, `private void PushSpc(uint pc)`, `private uint PopSpc()`, and stub methods `private void Alu()`, `private void Jmp()`, `private void Dsp()`, `private void Byt()` that each throw `NotImplementedException` — Phases 2/3/6/7 replace these stub bodies one at a time and must find these exact method signatures.

- [ ] **Step 1: Delete the old `Step()`/`ExecuteInstruction`/`FetchInstruction`/etc. and pipeline/SPC regions**

Delete (from the current file, before Task 1's edits — if Task 1 already ran, these are now further up/down depending on line drift, locate by content not line number):
- `public static void Step() { ... }` (the placeholder stub)
- `public static void ExecuteInstruction(uint pc, bool useImem) { ... }`
- `public static ulong FetchInstruction(uint pc, bool useImem) { ... }`
- `public static uint ReadMSource(uint mSource) { ... }`
- `public static uint ReadASource(uint aSource) { ... }`
- `public static void WriteDestination(uint dest, uint value) { ... }`
- `public static bool EvaluateJumpCondition(uint condition, uint aluResult) { ... }`
- `public static bool MachRun() { ... }`
- `public static void Run() { ... }`
- The entire `#region Pipeline and Control` ... `#endregion` block (`AdvancePipeline`, `FlushPipeline`, `CheckPageFault`, `MemoryCycle`)
- The entire `#region SPC (Stack Pointer Cache)` ... `#endregion` block (`PushSpc`, `PopSpc`, `ReadSpc`)

Note: `#region Instruction Decode` (`ExtractField`/`GetAluOp`/`GetMSource`/`GetASource`/`GetDest`/`GetJumpCond`/`GetNextPC`) and `#region Memory Access`'s `ReadAMem`/`WriteAMem`/`ReadMMem`/`WriteMMem`/`ReadDMem`/`WriteDMem`/`PushPdl`/`PopPdl`/`ReadPdl` are NOT touched by this task — `ReadAMem`/`WriteAMem`/etc. stay (they're generic memory accessors, still useful), but `#region Instruction Decode`'s six `Get*` methods ARE part of the invented format and should be deleted too, along with the region markers, since nothing in the new design calls them (field extraction now happens inline via `Ir()` inside each opcode-class handler, per the spec). Delete `#region Instruction Decode` through its `#endregion` entirely, including `ExtractField`.

- [ ] **Step 2: Add the new fetch/decode/dispatch loop, pipeline advance, LC helpers, and SPC push/pop**

Add (anywhere in the class body — following the file's existing region-based organization, a new `#region Fetch, Decode, and Dispatch` after the `Init()`/`Ir()` block from Task 1 is a natural spot):

```csharp
    #region Fetch, Decode, and Dispatch

    /// <summary>
    /// Advance the pipeline: P1 becomes P0, then prefetch the next word into P1.
    /// </summary>
    private void IncNpc()
    {
        P0 = P1; P0Pc = P1Pc; P0Imem = P1Imem;

        P1Imem = !PromEnabledFlag;
        P1 = P1Imem ? IMem[Npc] : Prom[Npc];
        P1Pc = Npc;

        if (Npc == 0x3FFF) Npc = 0; else Npc++;

        Opc = P0Pc;
    }

    /// <summary>
    /// Execute one microcode cycle (faithful port of uexec_step()).
    /// </summary>
    public void Step()
    {
        UExecHasRunOnce = true;

        IncNpc();

        if (NewMdDelay != 0)
        {
            NewMdDelay--;
            if (NewMdDelay == 0) MdReg = NewMd;
        }

        if (Inhibit)
        {
            Inhibit = false;
            MachineCycles++;
            return;
        }

        if (Oal) { Oal = false; P0 |= OaRegLow & 0x03FFFFFF; }
        if (Oah) { Oah = false; P0 |= (ulong)(OaRegHigh & 0x003FFFFF) << 26; }

        Op = (uint)Ir(43, 2);
        Popj = Ir(42, 1) == 1;
        AAddr = (uint)Ir(32, 10);
        ulong msource = Ir(31, 1);
        MAddr = (uint)Ir(26, 5);

        MData = msource == 0 ? (int)MMem[MAddr] : MfRead(MAddr);
        AData = (int)AMem[AAddr];

        Iwr = ((ulong)(uint)(AData & 0xFFFF) << 32) | (uint)MData;

        switch (Op)
        {
            case 0: Alu(); break;
            case 1: Jmp(); break;
            case 2: Dsp(); break;
            case 3: Byt(); break;
        }

        if (Popj)
        {
            uint target = PopSpc();
            if ((target >> 14 & 1) != 0) target = AdvanceLc(target);
            Npc = target & 0x3FFF;
        }

        MachineCycles++;
    }

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
        uint oldLc = Lc & 0x0FFFFFFF;
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

    private void PushSpc(uint pc)
    {
        SpcPtr = (SpcPtr + 1) & 0x1F;
        Spc[SpcPtr] = pc;
    }

    private uint PopSpc()
    {
        uint v = Spc[SpcPtr];
        SpcPtr = (SpcPtr - 1) & 0x1F;
        return v;
    }

    private int MfRead(uint addr)
    {
        throw new NotImplementedException("MfRead is implemented in Phase 4 (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md)");
    }

    private void Alu()
    {
        throw new NotImplementedException("Alu is implemented in Phase 2 (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md)");
    }

    private void Jmp()
    {
        throw new NotImplementedException("Jmp is implemented in Phase 3 (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md)");
    }

    private void Dsp()
    {
        throw new NotImplementedException("Dsp is implemented in Phase 6 (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md)");
    }

    private void Byt()
    {
        throw new NotImplementedException("Byt is implemented in Phase 7 (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md)");
    }

    /// <summary>
    /// VMA-ok state for the current cycle (real page-fault detection lands in Phase 5;
    /// defaults to true — "no page fault" — until then).
    /// </summary>
    public bool VmaOk { get; set; } = true;

    private void VmRead(uint vaddr, out uint v)
    {
        // Real virtual-memory read lands in Phase 5. Until then, treat every
        // read as a page fault-free no-op returning 0, matching "VmaOk = true"
        // above (Phase 5 replaces this with the real Vm()/Uvmem-backed path).
        v = 0;
    }

    #endregion
```

Note on `IncNpc`'s `P1Imem = !PromEnabledFlag;`: the spec's Section 3 step 2 describes the C source as `p1_imem = machine_state.promdisabled` — this port uses the existing `PromEnabledFlag` (inverted, since "enabled" and "disabled" are opposite senses) as the closest existing equivalent, since no separate `promdisabled` flag exists in this port. If a later phase discovers this mapping is wrong (e.g. PROM-disabled should be independently settable, not just the inverse of enabled), fix it then — this is called out explicitly rather than silently assumed correct forever.

Note on `MfRead` stub: Phase 1's `Step()` needs *some* `MfRead` to compile (used in the common-decode `MData` assignment when `msource != 0`), but the real 20-register implementation is Phase 4's job — stub it exactly like `Alu`/`Jmp`/`Dsp`/`Byt`.

- [ ] **Step 3: Build**

Run: `dotnet build LispMachine.sln`
Expected: still FAILS — same consumer-file errors as Task 1's Step 2, now joined by any remaining references to the deleted pipeline/SPC/decode methods. Confirm the *specific* errors remaining are all in `Disassembler.cs`, `MicrocodeDebugger.cs`, `DebugCommands.cs`, or `Program.cs` (i.e. `UCode.cs` itself compiles standalone) before moving to Task 3 — if `UCode.cs` itself has errors, fix those first.

- [ ] **Step 4: Commit**

```bash
git add usim-cs/UCode.cs
git commit -m "Add faithful Step()/IncNpc() fetch-decode-dispatch loop with stubbed opcode handlers"
```

---

## Task 3: Fix consumers and restore a green build

**Files:**
- Modify: `usim-cs/Disassembler.cs` (whole file simplified to a placeholder)
- Modify: `usim-cs/MicrocodeDebugger.cs`
- Modify: `usim-cs/DebugCommands.cs`
- Modify: `usim-cs/Program.cs`
- Test: `usim-cs/UCodeTests.cs` — remove tests that reference deleted members (see Step 5)

**Interfaces:**
- Consumes: `UCode`'s new instance-based register model and `Step()` from Tasks 1-2.
- Produces: nothing new for later phases — this task only restores compilability. `MicrocodeDebugger` gains a `public MicrocodeDebugger(UCode uCode)` constructor; later phases don't need to know about this beyond "it takes a `UCode` now."

- [ ] **Step 1: Simplify `Disassembler.cs` to a placeholder**

Full disassembly needs real ALU/jump mnemonics, which don't exist until Phases 2/3 — Phase 8 does the real rework. For now, replace the whole file with a version that compiles and returns a generic, honest placeholder:

```csharp
// Disassembler.cs - Microcode disassembly
// Converted from udiss.h and udiss.c
//
// NOTE: This is a placeholder pending Phase 8 of the microcode engine port
// (see docs/superpowers/specs/2026-08-21-microcode-engine-design.md). Real
// mnemonic-level disassembly needs the ALU/jump/dispatch/byte semantics from
// Phases 2/3/6/7 to have names to print.

namespace Usim;

/// <summary>
/// Microcode disassembler
/// </summary>
public static class Disassembler
{
    /// <summary>
    /// Disassemble instruction at PC
    /// </summary>
    public static string DisassemblePC(uint pc)
    {
        return DisassemblePC2(pc, false);
    }

    /// <summary>
    /// Disassemble instruction at PC with memory selection
    /// </summary>
    public static string DisassemblePC2(uint pc, bool pcImem)
    {
        return $"[PC {pc:X4} ({(pcImem ? "IMEM" : "PROM")}): disassembly pending Phase 8]";
    }

    /// <summary>
    /// Disassemble a microcode instruction
    /// </summary>
    public static string DisassembleInst(ulong instruction)
    {
        return DisassembleInst2(instruction, false);
    }

    /// <summary>
    /// Disassemble a microcode instruction with memory selection
    /// </summary>
    public static string DisassembleInst2(ulong instruction, bool pcImem)
    {
        if (instruction == 0)
            return "[NOP]";

        uint op = (uint)((instruction >> 43) & 0x3);
        string[] opNames = { "ALU", "JUMP", "DISPATCH", "BYTE" };
        return $"[{opNames[op]} raw=0x{instruction:X12}] (disassembly pending Phase 8)";
    }
}
```

- [ ] **Step 2: Fix `MicrocodeDebugger.cs`**

Add a `UCode` field and constructor. `MicrocodeDebugger` is currently constructed with no arguments (`new MicrocodeDebugger()` in `Program.cs`) and never had access to a real running machine's `UCode` — it operated on the (formerly static, implicitly singleton) `UCode` class directly. Since `UCode` is no longer static, give `MicrocodeDebugger` its own private `UCode` instance so its current (already-disconnected-from-any-real-boot) behavior is preserved exactly, just now compiling against the instance API:

Add near the top of the class (after the existing field declarations, e.g. after `_lastMMemValues`):
```csharp
    private readonly UCode _uCode = new UCode();
```

Then replace every `UCode.Xxx` reference in this file with `_uCode.Xxx`, with these specific fixes at the call sites already identified:

Replace (around line 117):
```csharp
        // Execute one instruction
        UCode.ExecuteInstruction(pc, useImem);
        
        // Update PC
        UCode.Opc = pc;
        // UCode.Npc is updated by ExecuteInstruction
```
with:
```csharp
        // Execute one instruction
        _uCode.Step();
```
(the explicit `Opc = pc` assignment is now redundant — `Step()`'s `IncNpc()` sets `Opc` itself — and `useImem`/`pc` are no longer meaningful parameters to a specific fetch call since `Step()` always fetches from wherever `Npc` points; leave the `ExecuteSingleStep()` method's own signature and its `pc`/`useImem` locals as they are for now, just unused after this change, since removing them is a larger rework out of this task's scope).

Replace (around line 135):
```csharp
            uint pc = UCode.Npc;
            ulong instruction = UCode.FetchInstruction(pc, true);
            string disasm = Disassembler.DisassembleInst2(instruction, true);
```
with:
```csharp
            uint pc = _uCode.Npc;
            ulong instruction = pc < UCode.IMEM_SIZE ? _uCode.IMem[pc] : 0;
            string disasm = Disassembler.DisassembleInst2(instruction, true);
```

Replace (around lines 156-159):
```csharp
            Console.Write(UCode.CarryFlag ? "C" : "c");
            Console.Write(UCode.OverflowFlag ? "V" : "v");
            Console.Write(UCode.NegativeFlag ? "N" : "n");
            Console.Write(UCode.ZeroFlag ? "Z" : "z");
```
with:
```csharp
            Console.Write($"CARRY={_uCode.AluCarry}");
```

Replace (around line 462):
```csharp
            ulong instruction = UCode.FetchInstruction(pc, true);
```
with:
```csharp
            ulong instruction = pc < UCode.IMEM_SIZE ? _uCode.IMem[pc] : 0;
```

For every OTHER `UCode.Xxx` reference in this file (register reads for display: `UCode.MachineCycles`, `UCode.Out`, `UCode.Q`, `UCode.MdReg`, `UCode.VmaReg`, `UCode.Lc`, `UCode.OaRegHigh`, `UCode.OaRegLow`, `UCode.MData`, `UCode.AData`, `UCode.PdlPointer`, `UCode.DumpState()`, `UCode.Init()`, `UCode.ReadAMem`/`WriteAMem`/`ReadMMem`/`WriteMMem`/`ReadDMem`, `UCode.Pdl[...]`, `UCode.ReadPdl`, `UCode.InstructionTraceEnabled`/`MicrocodeTraceEnabled`, `UCode.Npc` reads/writes elsewhere in the file): mechanically replace `UCode.` with `_uCode.` — these are all plain instance-member accesses with no semantic change needed, since these members' names and meanings are unchanged from before (they were already correctly named in the old code; only the static→instance shape changed). Search the whole file for `UCode.` after making the specific fixes above and confirm every remaining occurrence is one of these mechanical replacements, not another special case like the four already handled.

- [ ] **Step 3: Fix `DebugCommands.cs`**

Replace (around line 153):
```csharp
            UCode.Step();
```
with:
```csharp
            _machine.UCode.Step();
```
(confirm the field holding the `MachineControl` reference in this class is named `_machine` — if it's a different name, use that instead; do not introduce a new field.)

Replace (around lines 156, 164, 167) every `_machine.UCode.Pc` with `_machine.UCode.Npc` (this is the fix noted in the spec: `Pc` was an orphaned property disconnected from real execution state; `Npc` is the real, live program counter). Specifically:
```csharp
            Console.WriteLine($"PC: 0x{_machine.UCode.Pc:X4}");
```
→
```csharp
            Console.WriteLine($"PC: 0x{_machine.UCode.Npc:X4}");
```
and
```csharp
            _machine.UCode.Pc = (ushort)addr;
```
→
```csharp
            _machine.UCode.Npc = addr;
```
(drop the `(ushort)` cast — `Npc` is `uint`, and `addr` from `uint.TryParse` is already `uint`)
and
```csharp
        Console.WriteLine($"Running from PC=0x{_machine.UCode.Pc:X4}");
```
→
```csharp
        Console.WriteLine($"Running from PC=0x{_machine.UCode.Npc:X4}");
```

- [ ] **Step 4: Fix `Program.cs`**

Replace (around lines 169-173):
```csharp
                case "--debug-microcode":
                case "--debug-ucode":
                    var debugger = new MicrocodeDebugger();
                    debugger.StartDebugSession();
                    Environment.Exit(0);
                    break;
```
with:
```csharp
                case "--debug-microcode":
                case "--debug-ucode":
                    var debugger = new MicrocodeDebugger(new UCode());
                    debugger.StartDebugSession();
                    Environment.Exit(0);
                    break;
```
(This preserves today's actual behavior exactly — the debugger has never had access to a real booted machine, since this code path runs inside `ParseArguments`, before `Initialize()` creates `_machine`. Wiring the debugger to a real, booted `MachineControl` instance is a real improvement but is out of this task's scope — it's a UX/feature change, not a compile fix. Note it as a follow-up if it comes up again.)

Then update `MicrocodeDebugger`'s constructor to accept the `UCode` parameter (from Step 2's added field, make it settable via constructor instead of a field initializer):
```csharp
    private readonly UCode _uCode;

    public MicrocodeDebugger(UCode uCode)
    {
        _uCode = uCode;
    }
```
(replace the `private readonly UCode _uCode = new UCode();` field-initializer version from Step 2 with this constructor-injected version — Step 2 above should be read as producing this final shape, not two separate versions; do both edits in Step 2 and Step 4 as one coherent change if easier.)

- [ ] **Step 5: Replace `UCodeTests.cs` with a compiling placeholder**

`UCodeTests.cs`'s 8 test methods (`TestAluOperations`, `TestBarrelShifter`, `TestMemoryOperations`, `TestStackOperations`, `TestProcessorFlags`, `TestInstructionDecode`, `TestJumpConditions`, `TestPipeline`) and its 3 demo/benchmark methods (`DemoInstructionExecution`, `RunBenchmark`, `DemoInstructionTracing`) all reference the deleted invented-format API and will not compile. `Program.cs` calls `UCodeTests.RunAllTests()` (from `--test-microcode` and the master `RunAllTests()`), `UCodeTests.DemoInstructionExecution()` (`--demo-execution`), `UCodeTests.DemoInstructionTracing()` (`--demo-tracing`), and `UCodeTests.RunBenchmark()` (`--benchmark`) — keep these four public method signatures so `Program.cs` doesn't need any changes here, but replace every method body. Phase 9 replaces this whole file with tests of the real semantics; this is a compiling placeholder only.

Replace the entire contents of `usim-cs/UCodeTests.cs` with:

```csharp
// UCodeTests.cs - Microcode engine tests and examples
//
// NOTE: Placeholder pending Phase 9 of the microcode engine port (see
// docs/superpowers/specs/2026-08-21-microcode-engine-design.md). The 8 tests
// and 3 demo/benchmark methods this file used to have all exercised the
// invented instruction format removed in Phase 1 — Phase 9 replaces this
// file with tests of the real CADR semantics from Phases 2-7.

using System;

namespace Usim;

/// <summary>
/// Test suite and examples for the microcode execution engine
/// </summary>
public static class UCodeTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== CADR Microcode Engine Test Suite ===\n");
        Console.WriteLine("(pending Phase 9 — see usim-cs/UCodeFetchDecodeTests.cs for current coverage)\n");
        Console.WriteLine("=== Test Summary ===");
        Console.WriteLine("Passed: 0");
        Console.WriteLine("Failed: 0");
        Console.WriteLine("Total:  0");
    }

    public static void DemoInstructionExecution()
    {
        Console.WriteLine("Instruction execution demo pending Phase 9.");
    }

    public static void DemoInstructionTracing()
    {
        Console.WriteLine("Instruction tracing demo pending Phase 9.");
    }

    public static void RunBenchmark()
    {
        Console.WriteLine("Benchmark pending Phase 9.");
    }
}
```

- [ ] **Step 6: Build**

Run: `dotnet build LispMachine.sln`
Expected: 0 errors. Some warnings are fine (e.g. unused `pc`/`useImem` locals in `MicrocodeDebugger.ExecuteSingleStep` noted in Step 2 — acceptable per this task's scope).

- [ ] **Step 7: Run the existing regression suite**

Run: `dotnet run --project usim-cs -- --test-config` (ConfigTests — unrelated to microcode, should be unaffected) and `dotnet run --project usim-cs -- --test-wpf` (WpfBackendTests — unrelated, should be unaffected).
Expected: both report 0 failures. (Microcode's own tests are addressed in Task 4 below and fully rebuilt in Phase 9 — don't expect `UCodeTests` to be meaningful yet.)

- [ ] **Step 8: Commit**

```bash
git add usim-cs/Disassembler.cs usim-cs/MicrocodeDebugger.cs usim-cs/DebugCommands.cs usim-cs/Program.cs usim-cs/UCodeTests.cs
git commit -m "Update microcode consumers for the instance-based UCode register model"
```

---

## Task 4: Decode-only sanity tests for `IncNpc`/`Ir`/common decode

**Files:**
- Create: `usim-cs/UCodeFetchDecodeTests.cs`
- Modify: `usim-cs/Program.cs` (wire a `--test-microcode-decode` CLI flag, following this codebase's established test-flag convention)

**Interfaces:**
- Consumes: `UCode`'s register model (Task 1), `Ir()` (Task 1, private — tests exercise it indirectly through `Step()`/`IncNpc()`'s effects on `Op`/`AAddr`/`MAddr`/`Npc`, not by calling it directly, since it's private), `Step()`/`IncNpc()` (Task 2).
- Produces: nothing new for later phases — this is verification only. Phase 9 subsumes/expands this file into the full test suite; keep this file's tests self-contained so Phase 9 can either keep them as-is or fold them in.

This task deliberately does NOT run full multi-step sequences through real PROM content via repeated `Step()` calls: since `Alu`/`Jmp`/`Dsp`/`Byt` are still stubs that throw, and even once real jump/dispatch logic exists in later phases, letting `Step()` freely follow real control flow isn't a controlled test — it's testing whatever the real microcode happens to do. Instead, test `IncNpc`'s pipeline mechanics directly and controllably, and test decode straight-line over PROM content without executing it.

- [ ] **Step 1: Write the pipeline-advance test**

Create `usim-cs/UCodeFetchDecodeTests.cs`:

```csharp
// UCodeFetchDecodeTests.cs - Tests for UCode's fetch/decode/pipeline foundation
// (Phase 1 of the microcode engine port). Phase 9 expands this into the full
// microcode test suite once Phases 2-7 land real instruction semantics.

using System;
using System.IO;

namespace Usim;

public static class UCodeFetchDecodeTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== UCode Fetch/Decode Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestPipelineAdvance()) passed++; else failed++;
        if (TestNpcWraparound()) passed++; else failed++;
        if (TestCommonFieldDecode()) passed++; else failed++;
        if (TestPromDecodeSanity()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestPipelineAdvance()
    {
        Console.WriteLine("Test: Pipeline Advance (IncNpc via two Step() calls)");
        try
        {
            var ucode = new UCode();
            ucode.Init();
            ucode.PromEnabledFlag = true;
            ucode.Prom[0] = 0x1111_2222_3333UL;
            ucode.Prom[1] = 0x4444_5555_6666UL;
            ucode.Npc = 0;

            // First Step(): IncNpc moves the (empty) P1 into P0, then
            // prefetches Prom[0] into P1 for next time; Npc advances to 1.
            ucode.Step();
            Assert(ucode.P0 == 0, "first Step(): P0 is still the initial (empty) P1");
            Assert(ucode.P1 == ucode.Prom[0], "first Step(): P1 prefetched Prom[0]");
            Assert(ucode.Npc == 1, "first Step(): Npc advanced to 1");

            // Second Step(): P1 (Prom[0]) becomes P0; P1 prefetches Prom[1].
            ucode.Step();
            Assert(ucode.P0 == ucode.Prom[0], "second Step(): P0 is now Prom[0]");
            Assert(ucode.P1 == ucode.Prom[1], "second Step(): P1 prefetched Prom[1]");
            Assert(ucode.Npc == 2, "second Step(): Npc advanced to 2");

            Console.WriteLine("  Pipeline Advance tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Pipeline Advance tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestNpcWraparound()
    {
        Console.WriteLine("Test: Npc 14-bit Wraparound");
        try
        {
            var ucode = new UCode();
            ucode.Init();
            ucode.PromEnabledFlag = true;
            ucode.Npc = 0x3FFF;

            ucode.Step();
            Assert(ucode.Npc == 0, "Npc wraps from 0x3FFF to 0");

            Console.WriteLine("  Npc Wraparound tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Npc Wraparound tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestCommonFieldDecode()
    {
        Console.WriteLine("Test: Common Field Decode (Op/AAddr/MAddr)");
        try
        {
            var ucode = new UCode();
            ucode.Init();
            ucode.PromEnabledFlag = true;

            // Build one instruction word by hand:
            //   Op (bits 43-44) = 1 (JUMP)
            //   AAddr (bits 32-41) = 0x2AA
            //   MAddr (bits 26-30) = 0x15
            // msource (bit 31) = 0, so MData comes from MMem[MAddr], not MfRead.
            ulong word = ((ulong)1 << 43) | ((ulong)0x2AA << 32) | ((ulong)0x15 << 26);
            ucode.Prom[0] = word;
            ucode.MMem[0x15] = 0xABCDEF;
            ucode.AMem[0x2AA] = 0x123456;

            // Two Step() calls needed: first prefetches Prom[0] into P1,
            // second promotes it to P0 and actually decodes it. The second
            // call will throw (Jmp() is still a stub) — that's fine, the
            // decode happens before the stub throws, so we can check the
            // decoded fields via a try/catch around just that call.
            ucode.Npc = 0;
            ucode.Step();
            try { ucode.Step(); } catch (NotImplementedException) { /* expected: Jmp() stub */ }

            Assert(ucode.Op == 1, $"Op decoded as 1 (JUMP), got {ucode.Op}");
            Assert(ucode.AAddr == 0x2AA, $"AAddr decoded as 0x2AA, got 0x{ucode.AAddr:X}");
            Assert(ucode.MAddr == 0x15, $"MAddr decoded as 0x15, got 0x{ucode.MAddr:X}");
            Assert(ucode.AData == 0x123456, $"AData read from AMem[AAddr], got 0x{ucode.AData:X}");
            Assert(ucode.MData == 0xABCDEF, $"MData read from MMem[MAddr] (msource=0), got 0x{ucode.MData:X}");

            Console.WriteLine("  Common Field Decode tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Common Field Decode tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestPromDecodeSanity()
    {
        Console.WriteLine("Test: Real promh.mcr Decode Sanity");
        try
        {
            string path = Path.Combine("..", "sys", "ubin", "promh.mcr");
            if (!File.Exists(path))
            {
                Console.WriteLine("  SKIPPED (sys/ubin/promh.mcr not found at expected path)\n");
                return true;
            }

            var ucode = new UCode();
            ucode.Init();
            ucode.LoadPromFromFile(path);

            int[] opCounts = new int[4];
            int sampleSize = Math.Min(200, UCode.PROM_SIZE);
            for (int i = 0; i < sampleSize; i++)
            {
                ulong word = ucode.Prom[i];
                uint op = (uint)((word >> 43) & 0x3);
                opCounts[op]++;
            }

            int nonZeroClasses = 0;
            foreach (int c in opCounts) if (c > 0) nonZeroClasses++;

            Assert(nonZeroClasses >= 2,
                $"real microcode uses at least 2 distinct opcode classes across the first {sampleSize} words " +
                $"(ALU={opCounts[0]}, JUMP={opCounts[1]}, DISPATCH={opCounts[2]}, BYTE={opCounts[3]}) " +
                "— a degenerate single-class distribution would suggest the decode's bit positions are wrong");

            Console.WriteLine($"  Opcode class distribution: ALU={opCounts[0]} JUMP={opCounts[1]} DISPATCH={opCounts[2]} BYTE={opCounts[3]}");
            Console.WriteLine("  Real promh.mcr Decode Sanity tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Real promh.mcr Decode Sanity tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
```

- [ ] **Step 2: Wire a `--test-microcode-decode` CLI flag**

In `usim-cs/Program.cs`, add a case next to `--test-wpf`:
```csharp
                case "--test-microcode-decode":
                    UCodeFetchDecodeTests.RunAllTests();
                    Environment.Exit(0);
                    break;
```
Add a usage line next to `--test-wpf`'s:
```csharp
        Console.WriteLine("  --test-microcode-decode Run microcode fetch/decode tests only");
```
Do NOT add this to the master `RunAllTests()` yet if that method currently calls `UCodeTests.RunAllTests()` (Task 3, Step 5, may have left that partially working or stubbed) — wire it in only if the master `RunAllTests()` already runs cleanly with the other suites; otherwise leave it as a standalone flag for this phase and let Phase 9 fold everything into the master suite once `UCodeTests.cs` is fully replaced.

- [ ] **Step 3: Run the tests**

Run: `dotnet run --project usim-cs -- --test-microcode-decode`
Expected: `Passed: 4`, `Failed: 0`. If `sys/ubin/promh.mcr` isn't reachable from the working directory `dotnet run` uses (likely `usim-cs/` or the repo root, depending on how it's invoked), the sanity test will report SKIPPED rather than fail — that's acceptable, but try running from the repo root first (`dotnet run --project usim-cs -- --test-microcode-decode` from `D:\src\lisp\lm-csharp\l`) since the relative path assumes a repo-root working directory; adjust the path in the test if the actual working directory convention this project uses differs (check how `LoadPromFromFile`/`UsimState.SysDirectory` resolve paths elsewhere in the codebase for the established convention, e.g. `ConfigManager`'s `GetMicrocodeConfig().PromFile` default of `"./sys/prom.bin"` suggests paths are repo-root-relative when running from the repo root).

- [ ] **Step 4: Run the full build + existing regression suites once more**

Run: `dotnet build LispMachine.sln` (0 errors) and `dotnet run --project usim-cs -- --test-config` / `--test-wpf` (0 failures each) as a final confirmation nothing regressed.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/UCodeFetchDecodeTests.cs usim-cs/Program.cs
git commit -m "Add decode-only sanity tests for UCode's fetch/decode foundation"
```

---

## Self-Review Notes

- **Spec coverage:** Register Model table (Task 1), `Ir`/`Step`/`IncNpc`/POPJ handling (Task 2), `AdvanceLc`/`LcByteMode`/`PushSpc`/`PopSpc` (Task 2) — all covered. `Alu`/`Jmp`/`Dsp`/`Byt`/`MfRead` are explicitly OUT of scope (stubbed) per the spec's phase sequencing; `Uvmem`/`VmRead`/`VmWrite` real implementation is Phase 5's job — Task 2's `VmRead` stub and `VmaOk` default are named placeholders for that, not attempts at the real thing.
- **Placeholder scan:** `NotImplementedException` stubs for `Alu`/`Jmp`/`Dsp`/`Byt`/`MfRead` are intentional, phase-sequenced placeholders (not vague TODOs) — each names the exact phase and spec file that replaces it. `Disassembler.cs`'s placeholder output is similarly intentional and named. No bare "TODO" or "handle appropriately" language anywhere in the plan.
- **Type consistency:** `Ir(int pos, int len)` returns `ulong`; every call site in Task 2 either compares against a `ulong` constant or explicitly casts to `uint`/`int`/`bool`, matching the spec's own snippets. `MfRead` returns `int` (matching `MData`'s type), consistent with how Task 2's `Step()` assigns `MData = ... : MfRead(MAddr);`.
- **Gap found during review:** the spec's `IncNpc` description references `machine_state.promdisabled`, which has no direct C# equivalent in this port. Task 2 resolves this by using the existing `PromEnabledFlag` (inverted) as the closest available signal, explicitly flagged as a "confirm/fix later if wrong" note rather than silently assumed — this is the right call since inventing a brand-new flag here would be scope creep beyond "register model + fetch/decode," and the existing flag is at least directionally sound (something has to distinguish PROM-sourced fetches from IMem-sourced ones, and `PromEnabledFlag` already exists for exactly that purpose elsewhere in `LoadPromFromFile`/`FetchInstruction`'s old logic).
- **Gap found during review:** `MicrocodeDebugger`'s total disconnection from any real booted `MachineControl` (confirmed: it's constructed inside `ParseArguments`, before `Initialize()` creates `_machine`) predates this phase and isn't fixed by it — Task 3 preserves this exact pre-existing behavior (a debugger session against a fresh, unbooted `UCode`) rather than expanding scope to wire it to the real machine, which would be a UX feature change, not a compile fix for the register-model rewrite.
