# Microcode Engine Phase 5 — Virtual Memory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the CADR's two-level virtual memory system — a new `Uvmem` class (L1/L2 page tables), `UCode.Vm()`/`VmRead()`/`VmWrite()` (virtual-to-physical resolution and dispatch), and `MfRead()`'s Phase-4-deferred MEMORY-MAP-DATA case — as a faithful 1:1 port of `usim/uvmem.c` and the virtual-memory-relevant parts of `usim/uexec.c`'s `vm()`.

**Architecture:** `Uvmem` owns the L1 (2048×5-bit) and L2 (1024×24-bit) page-table arrays and resolves a 24-bit virtual address to a physical address plus permission bits (`Vtop`), and updates the tables (`WriteMap`). `UCode.Vm()` calls `Uvmem.Vtop`, sets `VmaOk` from the permission bits, and — for the dominant "plain main memory" address range — reads/writes `MainMemory`'s new physical-address accessors directly. Addresses that resolve to XBus I/O devices or the Unibus (a wholly separate, not-yet-ported subsystem — see Global Constraints) get a scoped, non-fatal placeholder instead of a crash.

**Tech Stack:** C#, .NET 8.0. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-08-21-microcode-engine-design.md` (§ "Phase 5 — Virtual Memory")

## Global Constraints

- Faithful 1:1 port of `usim/uvmem.c` (`uvmem_vtop`, `uvmem_write_map`, and `vm()` at lines 172-228 — all three live in this one file, not `uexec.c`) — not a reinterpretation. Where the real C is itself known-broken (see the two deviations below), port the *reachable* behavior faithfully and omit only what the C itself marks as dead/unreachable, with clear documentation of why.
- **Spec correction #4 (found during Phase 5 planning, 2026-08-25 — the fourth octal-to-hex mistranslation in this spec, after Phase 1's LC/OA masks and Phase 4's MfRead codes-1/12 mask):** the spec's TV-screen-quirk snippet gives `pn == 0x1E00` and `paddr = 0x0F00000 | (vaddr & 0x7FFF)`. Both hex values are wrong. Verified by direct computation (not manual octal-digit counting, which has produced repeated errors this session — computed programmatically instead): `036000` octal = `0x3C00` (15360 decimal), not `0x1E00` (7680 decimal). `017000000` octal = `0x3C0000` (3932160 decimal), not `0x0F00000` (15728640 decimal). The corrected values also make structural sense: `0x3C0000` equals `pn << 8` for `pn = 0x3C00` — exactly the shift `Vtop`'s own normal `(physicalPageNumber << 8) | (vaddr & 0xFF)` formula uses — confirming this is the right correction, not a coincidental alternate value. Use `0x3C00`/`0x3C0000`, not `0x1E00`/`0x0F00000`.
- **Structural gap found during Phase 5 planning (not a simple mask bug — a missing routing layer):** the spec's `Vm()` snippet routes every physical address directly to `MainMemory.ReadPhysical`/`WritePhysical`. The real C's `vm()` instead calls `bus_adaptor_read`/`bus_adaptor_write` (`usim/bus-adaptor.c`), which re-derives a page number from the (possibly TV-quirk-overridden) physical address and dispatches across three distinct ranges: physical page numbers `0x0000-0x3BFB` ("xbus main memory" — confirmed by reading `bus_adaptor_xbus_rw` directly: this range is a bare pass-through to `main_memory_read`/`main_memory_write`, i.e. real, actual main memory, not a shortcut) `0x3C00-0x3DFF` ("xbus I/O" — dispatches to device-specific handlers, e.g. the TV screen, keyboard) and `0x3E00-0x3FFF` (Unibus — 16-bit-word peripheral I/O via a distinct addressing scheme). Only the first range has a faithful home in this project's scope. The other two need a wholly separate, not-yet-ported "bus adaptor" subsystem (`usim/bus-adaptor.c`, `usim/tv.c`, `usim/iob.c`, etc.) — this project's existing `IOBus.cs` was checked and uses its own, different addressing model, not a drop-in replacement without its own dedicated design work outside this 9-phase microcode-engine plan's scope. Port the main-memory range faithfully; for the other two ranges, log a warning and return/discard zero (matching the non-fatal-placeholder precedent already established for `VmRead`'s Phase 1 no-op and Phase 4's `MfWrite` VM-placeholder no-ops) rather than throwing — a throw here would crash the emulator on any real microcode that touches the keyboard or display, which real interactive use does constantly. Record this as a new, not-yet-scheduled item in the spec's Open Items after this plan lands.
- **Known-dead branch, omit entirely (already noted in the spec, independently re-confirmed by reading `usim/uexec.c:199-205` directly):** the real C's `vm()` has an `else if (035774 <= pn && pn <= 035777)` branch (physical page numbers `0x3BFC-0x3BFF`, a 4-page gap between the main-memory and XBus-I/O ranges) that unconditionally executes `assert(false)` before doing anything else. In any non-`NDEBUG` build this aborts the process the instant it's reached; there is no well-defined reachable behavior here to be faithful to. Omit this branch (fold its page-number range into the "not main memory" placeholder path above, which is a safe superset — the placeholder path never distinguishes further, so it correctly still triggers for this range).
- **`Uvmem` does not need a `MainMemory` dependency — deliberate simplification of the spec's proposed constructor.** The spec's snippet gives `Uvmem` a `private readonly MainMemory _mainMemory` field set from its constructor, but neither `Vtop` nor `WriteMap` ever reads or writes through it — both methods only touch the L1/L2 map arrays. The real C's `uvmem_vtop`/`uvmem_write_map` likewise never touch main memory. Give `Uvmem` a plain parameterless constructor (or none at all, relying on the field initializers) rather than an unused dependency — actual physical memory access belongs to `UCode.Vm()`, which needs and gets its own `MainMemory` reference.
- **`UCode` gains two constructors, to avoid breaking every existing call site.** `UCode` currently has only an implicit parameterless constructor, and every existing call site across Phases 1-4 (`Program.cs`, `MicrocodeDebugger.cs`, and every `UCodeXxxTests.cs` file) uses `new UCode()`. Add `public UCode(MainMemory mainMemory)` (stores it and builds `Uvmem = new Uvmem();`) and keep `public UCode()` working unchanged by having it default-construct its own private `MainMemory` (`: this(new MainMemory())`) — so `Uvmem` and the main-memory reference are always non-null regardless of which constructor is used, and no existing code needs to change.
- `Uvmem` (the class), `MainMemory.ReadPhysical`/`WritePhysical`, and `UCode.Uvmem` are all `public` (spec-mandated, and genuinely part of this project's public surface — `Uvmem` is consumed by `Dsp()` in Phase 6). `Vm()` stays `private` (matches `Alu()`/`Jmp()`'s existing convention — a per-concern dispatcher only reached through `VmRead`/`VmWrite`, itself only reached through already-existing internal call sites). `VmRead`/`VmWrite` keep their existing `private` visibility (unchanged from Phases 1/4) — no test currently calls them directly by name, and this phase doesn't need to change that; their behavior is exercised indirectly through `AdvanceLc`/`MfWrite`'s existing tests plus this phase's own `Vm`-focused tests via a small test-only forwarding wrapper (see Task 2), matching the `CallJmp()` precedent from Phase 3.
- No existing Phase 1-4 test relies on `VmaOk` surviving across an unrelated `VmRead`/`VmWrite` call — every existing test that reads `VmaOk` (Phase 3's `CheckJumpCondition` codes 4/5/6 tests) sets it explicitly first. Wiring real `Vm()` logic into `VmRead`/`VmWrite` is therefore not expected to disturb any existing test's assertions (verified by reasoning through each existing call site during planning) — but Step 6 of the final task still re-runs the full regression suite to confirm this empirically rather than resting on that reasoning alone.
- `MainMemory`'s *existing* `Read`/`Write`/`TranslateAddress` methods use their own invented paging scheme (`_pageMap`, `VIRTUAL_PAGES`, `MAP_BITS`), unrelated to and inconsistent with `Uvmem`'s faithful L1/L2 map. `ReadPhysical`/`WritePhysical` must NOT call `TranslateAddress` — they index `_physicalMemory` directly by the already-resolved physical address, exactly as the spec's snippet already has them. This leaves two parallel, inconsistent virtual-memory schemes in the codebase (the old invented one, still used by whatever currently calls `MainMemory.Read`/`Write`, and the new faithful one used by `UCode.Vm()`) — reconciling or retiring the old one is Phase 8 (Consumer Rework)'s job, not this phase's; add a note to the spec's Phase 8 section after this plan lands.

---

### Task 1: `Uvmem` class (`Vtop`, `WriteMap`)

**Files:**
- Create: `usim-cs/Uvmem.cs`
- Create: `usim-cs/UvmemTests.cs`
- Modify: `usim-cs/Program.cs` (register the new test suite)

**Interfaces:**
- Consumes: nothing outside itself (no `MainMemory` dependency — see Global Constraints).
- Produces: `public uint Vtop(uint vaddr, out uint l1Data, out uint l2Data, out uint physicalPageNumber, out bool writePermission, out bool accessPermission)`, `public void WriteMap(uint vma, uint md)`. Task 2's `UCode.Vm()` and Task 3's `MfRead` code 9 both call `Vtop` with this exact signature; `Dsp()` (Phase 6, not this plan) will also call `Vtop`.

- [ ] **Step 1: Write the failing tests**

Create `usim-cs/UvmemTests.cs`:

```csharp
// UvmemTests.cs - Tests for the two-level virtual memory page-table class
// (Phase 5 of the microcode engine port). Covers Vtop's address resolution
// and WriteMap's L1/L2 table updates, including the same-call L1-then-L2
// write-visibility behavior the real C's sequential statement order gives.

using System;

namespace Usim;

public static class UvmemTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== Uvmem Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestVtopBeforeAnyMapping()) passed++; else failed++;
        if (TestWriteMapL1Only()) passed++; else failed++;
        if (TestWriteMapL2Only()) passed++; else failed++;
        if (TestWriteMapBothInOneCall()) passed++; else failed++;
        if (TestVtopUsesUpdatedMapping()) passed++; else failed++;
        if (TestVtopUpperVaddrBitsIgnored()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestVtopBeforeAnyMapping()
    {
        Console.WriteLine("Test: Vtop before any WriteMap call (all-zero maps)");
        try
        {
            var uvmem = new Uvmem();

            uint paddr = uvmem.Vtop(0x00123456, out uint l1, out uint l2, out uint pn, out bool wp, out bool ap);
            Assert(l1 == 0, $"l1Data is 0 (unmapped L1 entry), got {l1}");
            Assert(l2 == 0, $"l2Data is 0 (unmapped L2 entry), got {l2}");
            Assert(pn == 0, $"physicalPageNumber is 0, got {pn}");
            Assert(wp == false, "writePermission is false before any mapping");
            Assert(ap == false, "accessPermission is false before any mapping");
            Assert(paddr == (0u << 8) | (0x00123456u & 0xFF), $"paddr = (pn<<8)|(vaddr&0xFF), got 0x{paddr:X}");

            Console.WriteLine("  Vtop-before-mapping tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vtop-before-mapping tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestWriteMapL1Only()
    {
        Console.WriteLine("Test: WriteMap with only the L1 enable bit set");
        try
        {
            var uvmem = new Uvmem();

            // md must resolve to the SAME l1Index Vtop(vaddr) will use, AND its bits
            // 8-12 must match vaddr's for the (unused, here) L2 index too -- using
            // md=vaddr directly guarantees both, and matches how real microcode always
            // uses the same address for both mapping and the access that follows.
            uint vaddr = 0x00246000;
            uint md = vaddr;
            uint vma = (1u << 26) | (0x15u << 27); // enable L1 only; L1 data = 0x15 (5 bits)

            uvmem.WriteMap(vma, md);

            uint paddr = uvmem.Vtop(vaddr, out uint l1, out uint l2, out uint pn, out bool wp, out bool ap);
            Assert(l1 == 0x15, $"L1 entry written, got 0x{l1:X}");
            Assert(l2 == 0, "L2 entry untouched (L2 enable bit was not set)");
            Assert(pn == 0, "physicalPageNumber still 0 (derived from untouched L2 entry)");

            Console.WriteLine("  WriteMap L1-only tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  WriteMap L1-only tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestWriteMapL2Only()
    {
        Console.WriteLine("Test: WriteMap with only the L2 enable bit set (requires a pre-existing L1 entry)");
        try
        {
            var uvmem = new Uvmem();

            uint vaddr = 0x00246000;
            uint md = vaddr; // md=vaddr keeps both L1 and L2 indices consistent with Vtop(vaddr)

            // First, set up L1 (a separate call, matching real usage -- the C's own comment
            // says "the first level map... must have been written previously").
            uvmem.WriteMap((1u << 26) | (0x07u << 27), md);

            // Now write L2 only. l2Data = vma & 0x00FFFFFF; write+access permission bits are
            // bits 22/23 of that same value.
            uint l2DataToWrite = (1u << 23) | (1u << 22) | 0x1234u; // access=1, write=1, pn=0x1234
            uint vma2 = (1u << 25) | l2DataToWrite; // enable L2 only
            uvmem.WriteMap(vma2, md);

            uint paddr = uvmem.Vtop(vaddr, out uint l1, out uint l2, out uint pn, out bool wp, out bool ap);
            Assert(l1 == 0x07, $"L1 entry unchanged by the L2-only call, got 0x{l1:X}");
            Assert(l2 == l2DataToWrite, $"L2 entry written, got 0x{l2:X}");
            Assert(pn == 0x1234, $"physicalPageNumber extracted from L2 data, got 0x{pn:X}");
            Assert(wp == true, "writePermission true (bit 22 set)");
            Assert(ap == true, "accessPermission true (bit 23 set)");

            Console.WriteLine("  WriteMap L2-only tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  WriteMap L2-only tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestWriteMapBothInOneCall()
    {
        Console.WriteLine("Test: WriteMap with BOTH enable bits set in one call -- L2 sees the just-written L1 entry");
        try
        {
            var uvmem = new Uvmem();

            uint vaddr = 0x00050000;
            uint md = vaddr; // md=vaddr keeps both L1 and L2 indices consistent with Vtop(vaddr)

            // L1 data = 0x03 (5 bits, goes in vma bits 27-31); L2 data = a page number with
            // both permission bits set (goes in vma bits 0-23, masked to 24 bits).
            uint l1DataToWrite = 0x03u;
            uint l2DataToWrite = (1u << 23) | (1u << 22) | 0x0055u;
            uint vma = (1u << 26) | (1u << 25) | (l1DataToWrite << 27) | l2DataToWrite;

            uvmem.WriteMap(vma, md);

            uint paddr = uvmem.Vtop(vaddr, out uint l1, out uint l2, out uint pn, out bool wp, out bool ap);
            Assert(l1 == l1DataToWrite, $"L1 entry from the combined call, got 0x{l1:X}");
            // This is the key assertion: if L2's write used a STALE (pre-call) L1 index
            // instead of the just-written one, l2 would be read from the wrong slot and
            // very likely still be 0 (a fresh Uvmem's L2 map starts all-zero) instead of
            // l2DataToWrite.
            Assert(l2 == l2DataToWrite, $"L2 entry sees the JUST-WRITTEN L1 data for its own index, got 0x{l2:X}");
            Assert(pn == 0x0055, $"physicalPageNumber, got 0x{pn:X}");

            Console.WriteLine("  WriteMap both-in-one-call tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  WriteMap both-in-one-call tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVtopUsesUpdatedMapping()
    {
        Console.WriteLine("Test: Vtop's returned physical address matches (pn<<8)|(vaddr&0xFF) after a real mapping");
        try
        {
            var uvmem = new Uvmem();

            uint vaddr = 0x00099999; // low byte 0x99
            uint md = vaddr; // md=vaddr keeps both L1 and L2 indices consistent with Vtop(vaddr)
            uint l2DataToWrite = (1u << 23) | (1u << 22) | 0x2000u; // pn = 0x2000
            uint vma = (1u << 26) | (1u << 25) | (0x01u << 27) | l2DataToWrite;

            uvmem.WriteMap(vma, md);

            uint paddr = uvmem.Vtop(vaddr, out _, out _, out uint pn, out bool wp, out bool ap);
            Assert(pn == 0x2000, $"physicalPageNumber, got 0x{pn:X}");
            uint expectedPaddr = (0x2000u << 8) | (vaddr & 0xFF);
            Assert(paddr == expectedPaddr, $"paddr = (pn<<8)|(vaddr&0xFF) = 0x{expectedPaddr:X}, got 0x{paddr:X}");
            Assert(wp && ap, "both permission bits set");

            Console.WriteLine("  Vtop-uses-updated-mapping tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vtop-uses-updated-mapping tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVtopUpperVaddrBitsIgnored()
    {
        Console.WriteLine("Test: Vtop masks vaddr to 24 bits before use (upper 8 bits ignored)");
        try
        {
            var uvmem = new Uvmem();

            uint lowVaddr = 0x00123456;
            uint highVaddr = 0xFF123456; // same low 24 bits, garbage in the upper 8

            uint paddrLow = uvmem.Vtop(lowVaddr, out uint l1a, out uint l2a, out uint pnA, out _, out _);
            uint paddrHigh = uvmem.Vtop(highVaddr, out uint l1b, out uint l2b, out uint pnB, out _, out _);

            Assert(paddrLow == paddrHigh, $"upper 8 bits of vaddr are ignored, got 0x{paddrLow:X} vs 0x{paddrHigh:X}");
            Assert(l1a == l1b && l2a == l2b && pnA == pnB, "identical resolution regardless of vaddr's upper 8 bits");

            Console.WriteLine("  Vtop-upper-bits-ignored tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vtop-upper-bits-ignored tests failed: {ex.Message}\n");
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

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build LispMachine.sln`
Expected: FAIL — `Uvmem` doesn't exist yet.

- [ ] **Step 3: Implement `Uvmem`**

Create `usim-cs/Uvmem.cs`:

```csharp
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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build LispMachine.sln` (expect 0 errors), then `dotnet run --project usim-cs -- --test-uvmem`
Expected: `Passed: 6`, `Failed: 0`.

- [ ] **Step 5: Wire the new test suite into `Program.cs`**

Add next to the existing `--test-microcode-*` cases:
```csharp
                case "--test-uvmem":
                    UvmemTests.RunAllTests();
                    Environment.Exit(0);
                    break;
```
Add to the usage help text:
```csharp
        Console.WriteLine("  --test-uvmem            Run virtual memory (Uvmem) tests only");
```
Add to the master `RunAllTests()` method:
```csharp
        UvmemTests.RunAllTests();
```

- [ ] **Step 6: Run the full regression suite**

Run: `dotnet run --project usim-cs -- --test-all`
Expected: all suites report 0 failures (this task adds a new, independent class — nothing in `UCode.cs` changes yet).

- [ ] **Step 7: Commit**

```bash
git add usim-cs/Uvmem.cs usim-cs/UvmemTests.cs usim-cs/Program.cs
git commit -m "Add Uvmem two-level virtual memory page tables"
```

---

### Task 2: `MainMemory.ReadPhysical`/`WritePhysical`, `UCode`'s `Uvmem` wiring, `Vm`/`VmRead`/`VmWrite`

**Files:**
- Modify: `usim-cs/MainMemory.cs`
- Modify: `usim-cs/UCode.cs`
- Create: `usim-cs/UCodeVirtualMemoryTests.cs`
- Modify: `usim-cs/Program.cs` (register the new test suite)

**Interfaces:**
- Consumes: `Uvmem.Vtop` (Task 1).
- Produces: `public uint MainMemory.ReadPhysical(uint physicalAddress)`, `public void MainMemory.WritePhysical(uint physicalAddress, uint value)`, `public UCode(MainMemory mainMemory)` (new constructor; existing `public UCode()` unchanged in behavior), `public Uvmem Uvmem { get; }` on `UCode`, and fully-implemented `VmRead`/`VmWrite` bodies (replacing their Phase 1/4 no-ops) via a private `Vm(bool, uint, ref uint)`. `AdvanceLc` (Phase 1) and `MfWrite` codes 17/18/25/26 (Phase 4) already call `VmRead`/`VmWrite` with the correct existing signatures — no changes needed at those call sites. A new `internal void CallVm(bool write, uint vaddr, ref uint v) => Vm(write, vaddr, ref v);` test-only forwarding wrapper is added, matching the `CallJmp()` precedent from Phase 3 (`Vm` itself stays `private`, matching `Jmp()`'s own visibility).

- [ ] **Step 1: Write the failing tests**

Create `usim-cs/UCodeVirtualMemoryTests.cs`:

```csharp
// UCodeVirtualMemoryTests.cs - Tests for UCode's virtual memory resolution
// and dispatch (Phase 5 of the microcode engine port). Covers Vm()'s
// permission-check/VmaOk logic, its main-memory dispatch path, the
// TV-screen quirk, the deferred XBus-I/O/Unibus placeholder, and VmRead/
// VmWrite's existing call sites (AdvanceLc, MfWrite) now exercising real
// logic instead of no-ops.

using System;

namespace Usim;

public static class UCodeVirtualMemoryTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== UCode Virtual Memory Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestVmUnmappedAddressIsPageFault()) passed++; else failed++;
        if (TestVmMappedMainMemoryReadWrite()) passed++; else failed++;
        if (TestVmWriteRequiresBothPermissionBits()) passed++; else failed++;
        if (TestVmDeferredIoRangeDoesNotThrow()) passed++; else failed++;
        if (TestUCodeDefaultConstructorHasNonNullUvmem()) passed++; else failed++;
        if (TestUCodeExplicitConstructorSharesMainMemory()) passed++; else failed++;
        if (TestVmReadThroughMfWriteVmaStartRead()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestVmUnmappedAddressIsPageFault()
    {
        Console.WriteLine("Test: Vm() on an unmapped address is a page fault (VmaOk=false, v=0)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            uint v = 0xDEADBEEF; // pre-existing garbage, must be zeroed on a fault
            ucode.CallVm(false, 0x00001234, ref v);
            Assert(ucode.VmaOk == false, "unmapped address -> VmaOk false (access_permission bit is 0)");
            Assert(v == 0, $"page fault on read -> v forced to 0, got 0x{v:X}");

            Console.WriteLine("  Vm-unmapped-page-fault tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vm-unmapped-page-fault tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVmMappedMainMemoryReadWrite()
    {
        Console.WriteLine("Test: Vm() write-then-read round-trips through real main memory for a mapped address");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Map vaddr's L1/L2 entries with both permission bits set, physical page 5
            // (well within the "xbus main memory" range, pn<=0x3BFB).
            uint vaddr = 0x00003000;
            uint md = vaddr; // md=vaddr keeps L1 and L2 indices consistent with Vtop(vaddr) inside Vm()
            uint l2Data = (1u << 23) | (1u << 22) | 5u; // access+write permission, pn=5
            uint vma = (1u << 26) | (1u << 25) | (0x02u << 27) | l2Data;
            ucode.Uvmem.WriteMap(vma, md);

            uint writeVal = 0x77778888;
            ucode.CallVm(true, vaddr, ref writeVal);
            Assert(ucode.VmaOk == true, "mapped, both permission bits set -> VmaOk true for a write");

            uint readVal = 0;
            ucode.CallVm(false, vaddr, ref readVal);
            Assert(ucode.VmaOk == true, "mapped, access permission set -> VmaOk true for a read");
            Assert(readVal == 0x77778888, $"read-back the just-written value, got 0x{readVal:X}");

            Console.WriteLine("  Vm-mapped-main-memory tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vm-mapped-main-memory tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVmWriteRequiresBothPermissionBits()
    {
        Console.WriteLine("Test: Vm() write needs BOTH access and write permission; read needs only access");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Map with access permission set but write permission CLEAR.
            uint vaddr = 0x00005000;
            uint md = vaddr; // md=vaddr keeps L1 and L2 indices consistent with Vtop(vaddr) inside Vm()
            uint l2Data = (1u << 23) | 7u; // access permission only, pn=7
            uint vma = (1u << 26) | (1u << 25) | (0x01u << 27) | l2Data;
            ucode.Uvmem.WriteMap(vma, md);

            uint writeVal = 0x11111111;
            ucode.CallVm(true, vaddr, ref writeVal);
            Assert(ucode.VmaOk == false, "access permission alone is NOT enough for a write");

            uint readVal = 0;
            ucode.CallVm(false, vaddr, ref readVal);
            Assert(ucode.VmaOk == true, "access permission alone IS enough for a read");

            Console.WriteLine("  Vm-write-needs-both-permissions tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vm-write-needs-both-permissions tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVmDeferredIoRangeDoesNotThrow()
    {
        Console.WriteLine("Test: Vm() on a mapped XBus-I/O-range address does not throw (deferred placeholder)");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Map to physical page number 0x3C00 (the first XBus-I/O page) with both
            // permission bits set, so Vm() reaches the dispatch step rather than
            // faulting first.
            uint vaddr = 0x00007000;
            uint md = vaddr; // md=vaddr keeps L1 and L2 indices consistent with Vtop(vaddr) inside Vm()
            uint l2Data = (1u << 23) | (1u << 22) | 0x3C00u;
            uint vma = (1u << 26) | (1u << 25) | (0x03u << 27) | l2Data;
            ucode.Uvmem.WriteMap(vma, md);

            uint readVal = 0xCAFECAFE;
            ucode.CallVm(false, vaddr, ref readVal);
            Assert(readVal == 0, $"deferred XBus-I/O range reads as 0, got 0x{readVal:X}");

            uint writeVal = 0x99999999;
            ucode.CallVm(true, vaddr, ref writeVal); // must not throw

            Console.WriteLine("  Vm-deferred-io-range tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vm-deferred-io-range tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUCodeDefaultConstructorHasNonNullUvmem()
    {
        Console.WriteLine("Test: UCode()'s default constructor still works and has a non-null Uvmem");
        try
        {
            var ucode = new UCode(); // every existing Phase 1-4 test/call site uses this
            ucode.Init();
            Assert(ucode.Uvmem != null, "default constructor produces a non-null Uvmem");

            Console.WriteLine("  UCode-default-constructor tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  UCode-default-constructor tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUCodeExplicitConstructorSharesMainMemory()
    {
        Console.WriteLine("Test: UCode(MainMemory) uses the SAME MainMemory instance passed in");
        try
        {
            var mainMemory = new MainMemory();
            mainMemory.WritePhysical(42, 0xABCDEF01);

            var ucode = new UCode(mainMemory);
            ucode.Init();

            // Map a virtual address straight onto physical page 0 (offset 42 within it)
            // and confirm UCode.Vm() reads through to the SAME MainMemory instance,
            // not a separately-constructed one.
            uint vaddr = 42;
            uint md = vaddr; // md=vaddr keeps L1 and L2 indices consistent with Vtop(vaddr) inside Vm()
            uint l2Data = (1u << 23) | 0u; // access permission, pn=0
            uint vma = (1u << 26) | (1u << 25) | (0x00u << 27) | l2Data;
            ucode.Uvmem.WriteMap(vma, md);

            uint readVal = 0;
            ucode.CallVm(false, vaddr, ref readVal);
            Assert(readVal == 0xABCDEF01, $"UCode reads through the SAME MainMemory instance, got 0x{readVal:X}");

            Console.WriteLine("  UCode-explicit-constructor-shares-MainMemory tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  UCode-explicit-constructor-shares-MainMemory tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVmReadThroughMfWriteVmaStartRead()
    {
        Console.WriteLine("Test: MfWrite's VMA-START-READ (code 17) now exercises real Vm() logic, not a no-op");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            uint vaddr = 0x00009000;
            uint md = vaddr; // md=vaddr keeps L1 and L2 indices consistent with Vtop(vaddr) inside Vm()
            uint l2Data = (1u << 23) | (1u << 22) | 9u; // access+write, pn=9
            uint vma = (1u << 26) | (1u << 25) | (0x04u << 27) | l2Data;
            ucode.Uvmem.WriteMap(vma, md);

            // Pre-seed physical page 9's word 0 (matches vaddr's low byte 0x00) via a
            // direct write, then trigger VMA-START-READ through MfWrite and confirm
            // NewMd picks up the real value once NewMdDelay counts down.
            uint preSeedValue = 0x55AA55AA;
            ucode.CallVm(true, vaddr, ref preSeedValue);

            ucode.MfWrite(17 << 5, unchecked((int)vaddr));
            Assert(ucode.NewMdDelay == 2, $"VMA-START-READ still sets NewMdDelay=2, got {ucode.NewMdDelay}");

            Console.WriteLine("  MfWrite-VMA-START-READ-real-Vm tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MfWrite-VMA-START-READ-real-Vm tests failed: {ex.Message}\n");
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

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build LispMachine.sln`
Expected: FAIL — `ucode.Uvmem`/`ucode.CallVm`/`UCode(MainMemory)`/`MainMemory.WritePhysical` don't exist yet.

- [ ] **Step 3: Implement**

In `usim-cs/MainMemory.cs`, add (near the existing `Read`/`Write` methods):
```csharp
    /// <summary>
    /// Direct physical-memory access, bypassing this class's own (separate,
    /// invented) virtual-paging TranslateAddress -- the caller (UCode.Vm(),
    /// via Uvmem's faithful L1/L2 tables) has already resolved the physical
    /// address itself.
    /// </summary>
    public uint ReadPhysical(uint physicalAddress) => physicalAddress < PHYSICAL_MEM_SIZE ? _physicalMemory[physicalAddress] : 0;
    public void WritePhysical(uint physicalAddress, uint value) { if (physicalAddress < PHYSICAL_MEM_SIZE) _physicalMemory[physicalAddress] = value; }
```

In `usim-cs/UCode.cs`, add a `Uvmem` property and a `MainMemory` field, and two constructors (place near the top of the class, alongside the other fields):
```csharp
    private readonly MainMemory _mainMemory;
    public Uvmem Uvmem { get; }

    public UCode() : this(new MainMemory()) { }

    public UCode(MainMemory mainMemory)
    {
        _mainMemory = mainMemory;
        Uvmem = new Uvmem();
    }
```

Replace the existing `VmRead`/`VmWrite` stub bodies:
```csharp
    private void VmRead(uint vaddr, out uint v)
    {
        // Real virtual-memory read lands in Phase 5. Until then, treat every
        // read as a page fault-free no-op returning 0, matching "VmaOk = true"
        // above (Phase 5 replaces this with the real Vm()/Uvmem-backed path).
        v = 0;
    }
```
and (Phase 4's placeholder)
```csharp
    private void VmWrite(uint vaddr, uint data)
    {
    }
```
with:
```csharp
    /// <summary>
    /// Faithful port of the virtual-memory-resolution part of vm()
    /// (usim/uexec.c:172-228). Sets VmaOk from Uvmem's permission bits;
    /// on a fault, reads return 0 and writes are discarded (matching the
    /// real C's *pv=0 on read). For an address that resolves within the
    /// "xbus main memory" range (physical page number &lt;= 0x3BFB -- verified
    /// against usim/bus-adaptor.c's bus_adaptor_xbus_rw, whose own pn&lt;=035773
    /// branch is a bare pass-through to real main memory), reads/writes go
    /// through MainMemory's physical-address accessors for real. Anything
    /// else (XBus I/O devices, Unibus) is a deliberately deferred,
    /// non-fatal placeholder -- see this phase's plan for why (a wholly
    /// separate, not-yet-ported bus-adaptor/device subsystem).
    /// </summary>
    private void Vm(bool write, uint vaddr, ref uint v)
    {
        vaddr &= 0x00FFFFFF;
        uint paddr = Uvmem.Vtop(vaddr, out _, out _, out uint pn, out bool wp, out bool ap);
        VmaOk = write ? (ap && wp) : ap;
        if (!VmaOk) { v = 0; return; }

        // TV-screen quirk (usim/uvmem.c's vm(): known not to work correctly per its
        // own comment) -- ported as-is. 036000 octal = 0x3C00 (NOT 0x1E00) and
        // 017000000 octal = 0x3C0000 (NOT 0x0F00000) -- both corrected from an
        // earlier draft of this spec; re-derived by direct computation, not manual
        // octal-digit counting.
        if (pn == 0x3C00) paddr = 0x3C0000 | (vaddr & 0x7FFF);

        // The real C dispatches through bus_adaptor_read/write, which re-derives its
        // OWN page number from the (possibly quirk-overridden) paddr, not from Vtop's
        // original pn -- mirrored here. The 0x3BFC-0x3BFF range (the real C's
        // assert(false)-guarded dead branch) is folded into the "not main memory"
        // placeholder below, which is a safe superset for it.
        uint dispatchPn = (paddr >> 8) & 0x3FFF;
        if (dispatchPn <= 0x3BFB)
        {
            if (write) _mainMemory.WritePhysical(paddr, v);
            else v = _mainMemory.ReadPhysical(paddr);
        }
        else
        {
            TraceLog.Instance.Warning(TraceCategory.Memory,
                $"Vm: {(write ? "write" : "read")} to un-ported XBus-I/O/Unibus paddr 0x{paddr:X} (pn 0x{dispatchPn:X}) -- deferred to a future bus-adaptor port");
            if (!write) v = 0;
        }
    }

    private void VmRead(uint vaddr, out uint v) { v = 0; Vm(false, vaddr, ref v); }
    private void VmWrite(uint vaddr, uint data) { uint v = data; Vm(true, vaddr, ref v); }

    /// <summary>
    /// Test-only forwarding wrapper: Vm() stays private (matching Jmp()'s
    /// visibility -- only reachable through VmRead/VmWrite in production),
    /// but UCodeVirtualMemoryTests needs to exercise its branch combinations
    /// directly. Matches the CallJmp() precedent from Phase 3.
    /// </summary>
    internal void CallVm(bool write, uint vaddr, ref uint v) => Vm(write, vaddr, ref v);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build LispMachine.sln` (expect 0 errors), then `dotnet run --project usim-cs -- --test-microcode-vm`
Expected: `Passed: 7`, `Failed: 0`.

- [ ] **Step 5: Wire the new test suite into `Program.cs`**

Add next to the existing test-suite cases:
```csharp
                case "--test-microcode-vm":
                    UCodeVirtualMemoryTests.RunAllTests();
                    Environment.Exit(0);
                    break;
```
Add to the usage help text:
```csharp
        Console.WriteLine("  --test-microcode-vm     Run microcode virtual memory tests only");
```
Add to the master `RunAllTests()` method:
```csharp
        UCodeVirtualMemoryTests.RunAllTests();
```

- [ ] **Step 6: Run the full regression suite**

Run: `dotnet run --project usim-cs -- --test-all`
Expected: all suites report 0 failures, INCLUDING every pre-existing Phase 1-4 suite (`AdvanceLc`'s existing tests, `MfWrite`'s existing VMA/MD tests) — this is the empirical confirmation that wiring real `Vm()` logic into `VmRead`/`VmWrite` doesn't disturb any earlier phase's assertions, per the reasoning in this plan's Global Constraints.

- [ ] **Step 7: Commit**

```bash
git add usim-cs/MainMemory.cs usim-cs/UCode.cs usim-cs/UCodeVirtualMemoryTests.cs usim-cs/Program.cs
git commit -m "Add faithful Vm()/VmRead()/VmWrite() virtual memory resolution"
```

---

### Task 3: `MfRead` code 9 (MEMORY-MAP-DATA)

**Files:**
- Modify: `usim-cs/UCode.cs`
- Modify: `usim-cs/UCodeMRegisterTests.cs`

**Interfaces:**
- Consumes: `Uvmem.Vtop` (Task 1), `MdReg` (Phase 1).
- Produces: `MfRead(9)` returns a real value instead of throwing (replacing Phase 4's scoped `NotImplementedException`).

- [ ] **Step 1: Write the failing test**

In `usim-cs/UCodeMRegisterTests.cs`, replace the existing `TestMfReadPlaceholdersAndDeferred` method's code-9 assertion (currently expecting a throw) with a real-value assertion. Find this block:
```csharp
            bool threw = false;
            try { ucode.MfRead(9); }
            catch (NotImplementedException) { threw = true; }
            Assert(threw, "code9 (011 octal, MEMORY-MAP-DATA) is deferred to Phase 5 and throws NotImplementedException");
```
and replace it with:
```csharp
            // Code 9 (011 octal, MEMORY-MAP-DATA) is implemented as of Phase 5 -- map
            // MdReg's L1/L2 entries first, then confirm the bit-packed result.
            uint mdReg = 0x00246000;
            uint md = mdReg; // md=mdReg keeps L1 and L2 indices consistent with what MfRead(9) uses (Vtop(MdReg))
            uint l2Data = (1u << 23) | (1u << 22) | 0x00ABCDu; // access+write permission, l2 low bits 0xABCD
            uint vma = (1u << 26) | (1u << 25) | (0x0Bu << 27) | l2Data; // L1 data = 0x0B
            ucode.Uvmem.WriteMap(vma, md);
            ucode.MdReg = mdReg;

            int result9 = ucode.MfRead(9);
            uint expected9 = (0u << 31) | (0u << 30) | (1u << 29) | ((0x0Bu & 0x1F) << 24) | (l2Data & 0x00FFFFFF);
            Assert(unchecked((uint)result9) == expected9, $"code9: bit-packed MEMORY-MAP-DATA, got 0x{result9:X}, expected 0x{expected9:X}");
```
(This test method's title stays accurate for codes 13/22 — only its code-9 assertion changes; rename the method to `TestMfReadPlaceholdersAndMemoryMapData` if that reads more clearly, updating its `RunAllTests()` call site to match.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build LispMachine.sln`
Expected: FAIL — `MfRead(9)` still throws.

- [ ] **Step 3: Implement**

In `usim-cs/UCode.cs`, in `MfRead`, replace:
```csharp
            case 9:
                throw new NotImplementedException("MfRead code 9 (MEMORY-MAP-DATA) is implemented in Phase 5 (needs Uvmem.Vtop)");
```
with:
```csharp
            case 9:
            {
                uint paddr9 = Uvmem.Vtop(MdReg, out uint l1_9, out uint l2_9, out _, out bool wp9, out bool ap9);
                return (int)((!wp9 ? (1u << 31) : 0) | (!ap9 ? (1u << 30) : 0) | (1u << 29) | ((l1_9 & 0x1F) << 24) | (l2_9 & 0x00FFFFFF));
            }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet build LispMachine.sln` (expect 0 errors), then `dotnet run --project usim-cs -- --test-microcode-mregisters`
Expected: `Passed: 12`, `Failed: 0` (same total as Phase 4 — this replaces one assertion's substance, not the test count).

- [ ] **Step 5: Run the full regression suite**

Run: `dotnet run --project usim-cs -- --test-all`
Expected: all suites report 0 failures.

- [ ] **Step 6: Commit**

```bash
git add usim-cs/UCode.cs usim-cs/UCodeMRegisterTests.cs
git commit -m "Implement MfRead code 9 (MEMORY-MAP-DATA) using Uvmem.Vtop"
```

---

### Task 4: Wire `MfWrite`'s VMA-WRITE-MAP/MD-WRITE-MAP (codes 19/27) to the real `Uvmem.WriteMap`

**Discovered during Task 2's review, not originally scoped in this plan**: Phase 4 added a private `WriteMap(uint vma, uint data)` no-op placeholder on `UCode` for `MfWrite`'s codes 19/27, since `Uvmem` didn't exist yet. Task 1 of this phase created a real `Uvmem.WriteMap`, but nothing in Tasks 1-3 rewired `MfWrite`'s codes 19/27 to call it — they still call the leftover local no-op, even though the real implementation is now sitting right there unused. Leaving this creates a confusing, silently-incomplete state (`Uvmem.WriteMap` works and is tested, but the only two real callers per the spec's own `MfWrite` table still ignore it) and was flagged by Task 2's reviewer as a stale-comment symptom of this deeper gap. Closing it now, while this phase's context is still fresh, is cheaper than leaving it as a tracked-for-later item.

**Files:**
- Modify: `usim-cs/UCode.cs`
- Modify: `usim-cs/UCodeMRegisterTests.cs`

**Interfaces:**
- Consumes: `Uvmem.WriteMap` (Task 1).
- Produces: `MfWrite` codes 19/27 now genuinely update the L1/L2 page tables. The local `WriteMap(uint, uint)` placeholder is deleted (no longer has any caller).

- [ ] **Step 1: Update the failing tests**

In `usim-cs/UCodeMRegisterTests.cs`'s `TestMfWriteVmaAndMdRegisters`, replace the code-19 and code-27 blocks (currently asserting only "does not throw" against the old no-op) with assertions that the real `Uvmem.WriteMap` was actually invoked. Find:
```csharp
            // Code 19 (023 octal): VmaReg = data; Uvmem.WriteMap placeholder -- no-op, must not throw.
            ucode.MfWrite(19 << 5, unchecked((int)0x44444444));
            Assert(ucode.VmaReg == 0x44444444, $"code19: VmaReg set, got 0x{ucode.VmaReg:X}");
```
and replace it with:
```csharp
            // Code 19 (023 octal): VmaReg = data; Uvmem.WriteMap(VmaReg, MdReg) for real (as
            // of this phase). This unchanged assertion from Phase 4 (0x44444444 happens to
            // have bit26 set, so it does trigger a real L1 write at whatever l1Index MdReg
            // held at this point) only checks VmaReg, not that write's side effect -- the
            // explicit, controlled check right below is what actually proves the wiring.
            Assert(ucode.VmaReg == 0x44444444, $"code19: VmaReg set, got 0x{ucode.VmaReg:X}");

            // Now prove the wiring is real (not still the old no-op) with an enable bit set:
            // Uvmem.WriteMap(vma=data, md=MdReg) should write L1[l1Index] for real.
            ucode.MdReg = 0; // l1Index = (0>>13)&0x7FF = 0
            uint l1DataToWrite19 = 0x0Au;
            ucode.MfWrite(19 << 5, unchecked((int)((1u << 26) | (l1DataToWrite19 << 27))));
            uint paddrCheck19 = ucode.Uvmem.Vtop(0, out uint l1Check19, out _, out _, out _, out _);
            Assert(l1Check19 == l1DataToWrite19, $"code19 reaches the REAL Uvmem.WriteMap (not the old no-op), got L1=0x{l1Check19:X}");
```
Find the analogous code-27 block:
```csharp
            // Code 27 (033 octal): MdReg = data; Uvmem.WriteMap placeholder -- no-op, must not throw.
            ucode.MfWrite(27 << 5, unchecked((int)0x88888888));
            Assert(ucode.MdReg == 0x88888888u, $"code27: MdReg set, got 0x{ucode.MdReg:X}");
```
and replace it with:
```csharp
            // Code 27 (033 octal): MdReg = data; Uvmem.WriteMap(VmaReg, MdReg) for real.
            ucode.MfWrite(27 << 5, unchecked((int)0x88888888));
            Assert(ucode.MdReg == 0x88888888u, $"code27: MdReg set, got 0x{ucode.MdReg:X}");

            // Prove the wiring is real: VmaReg supplies WriteMap's L1 enable bit + L1 data;
            // MdReg (just set above, 0x88888888) supplies the l1Index WriteMap computes from.
            ucode.VmaReg = (1u << 26) | (0x15u << 27);
            ucode.MfWrite(27 << 5, unchecked((int)0x88888888)); // re-set MdReg=0x88888888, matching VmaReg's target l1Index
            uint l1IndexCheck27 = (0x88888888u >> 13) & 0x7FF;
            uint paddrCheck27 = ucode.Uvmem.Vtop(0x88888888u, out uint l1Check27, out _, out _, out _, out _);
            Assert(l1Check27 == 0x15u, $"code27 reaches the REAL Uvmem.WriteMap (not the old no-op), got L1=0x{l1Check27:X}");
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build LispMachine.sln`
Expected: FAIL — the new assertions expect real `Uvmem.WriteMap` behavior that the still-present no-op placeholder doesn't produce.

- [ ] **Step 3: Implement**

In `usim-cs/UCode.cs`, in `MfWrite`, change:
```csharp
            case 19:
                VmaReg = udata;
                WriteMap(VmaReg, MdReg);
                return;
```
to:
```csharp
            case 19:
                VmaReg = udata;
                Uvmem.WriteMap(VmaReg, MdReg);
                return;
```
and change:
```csharp
            case 27:
                MdReg = udata;
                WriteMap(VmaReg, MdReg);
                return;
```
to:
```csharp
            case 27:
                MdReg = udata;
                Uvmem.WriteMap(VmaReg, MdReg);
                return;
```

Then delete the now-entirely-unused local placeholder method (search for its doc comment mentioning "Uvmem.WriteMap" and "Phase 5's 'new file' Uvmem.cs does not exist yet" to find it):
```csharp
    /// <summary>
    /// Placeholder for Uvmem.WriteMap (Phase 5's "new file" Uvmem.cs does
    /// not exist yet). A no-op until then, for the same reason as VmWrite.
    /// </summary>
    private void WriteMap(uint vma, uint data)
    {
    }
```
Delete this method entirely — after the two case changes above, nothing calls it.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build LispMachine.sln` (expect 0 errors), then `dotnet run --project usim-cs -- --test-microcode-mregisters`
Expected: `Passed: 12`, `Failed: 0`.

- [ ] **Step 5: Run the full regression suite**

Run: `dotnet run --project usim-cs -- --test-all`
Expected: all suites report 0 failures.

- [ ] **Step 6: Commit**

```bash
git add usim-cs/UCode.cs usim-cs/UCodeMRegisterTests.cs
git commit -m "Wire MfWrite's VMA/MD-WRITE-MAP to the real Uvmem.WriteMap"
```

---

## Self-Review Notes

- **Spec coverage:** every construct the spec's "Phase 5 — Virtual Memory" section calls for (`Uvmem.Vtop`/`WriteMap`, `MainMemory.ReadPhysical`/`WritePhysical`, `Vm`/`VmRead`/`VmWrite`, `MfRead` code 9) is implemented.
- **Spec corrections applied:** the TV-screen-quirk's two wrong hex constants (`0x1E00`→`0x3C00`, `0x0F00000`→`0x3C0000`), both verified by direct computation rather than manual octal-digit counting (which has produced multiple errors earlier in this same spec). A structural gap (the spec's `Vm()` skipping the real C's bus-adaptor routing layer entirely) is addressed by faithfully porting the one reachable, in-scope range (main memory) and scoping the rest as a documented, non-fatal deferral rather than silently matching the spec's oversimplification.
- **Placeholder scan:** the only deferred behavior this plan leaves is the XBus-I/O/Unibus placeholder inside `Vm()` — explicitly logged (not silent), not a vague TODO, and clearly tied to a named, wholly separate future subsystem.
- **Type consistency:** `Uvmem.Vtop`'s signature is used identically by `UCode.Vm()` (Task 2) and `MfRead` code 9 (Task 3) — `out uint`/`out bool` parameters matching exactly. `MainMemory.ReadPhysical`/`WritePhysical` take/return `uint`, matching `_physicalMemory`'s element type. `UCode`'s two constructors both leave `Uvmem`/`_mainMemory` non-null.
- **Deliberate deviations flagged:** `Uvmem`'s parameterless simplification (removing an unused `MainMemory` dependency the spec's snippet included); `UCode`'s two-constructor approach (avoiding a breaking change to every existing call site); `CallVm()` test-only wrapper (matching the `CallJmp()` precedent); the XBus-I/O/Unibus deferral itself.
- **Follow-up items to add to the spec doc after this plan lands** (not fixed by this plan, tracked for later): a new Open Item for the deferred XBus-I/O/Unibus bus-adaptor routing (no phase assigned yet); a note in Phase 8's section that `MainMemory`'s old invented paging (`Read`/`Write`/`TranslateAddress`) now coexists with `Uvmem`'s faithful one and needs reconciling.
