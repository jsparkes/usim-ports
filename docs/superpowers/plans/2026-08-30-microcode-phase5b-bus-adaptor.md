# Microcode Engine Phase 5B — Bus Adaptor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `UCode.Vm()`'s blind "warn and return/discard zero" placeholder (for physical addresses outside the "xbus main memory" range) with a faithful address-routing layer matching `usim/bus-adaptor.c`'s real dispatch structure, plus the two specific register behaviors the boot PROM's early boot actually depends on.

**Architecture:** A new `BusAdaptor` class dispatches by physical address into XBus-I/O (TV, color TV, disk control) or Unibus (Unibus Map, IOB, diagnostic-interface "spy" registers, bus-interface, unibus-mapping, tape controller) ranges, matching `bus_adaptor_xbusio_rw`/`bus_adaptor_unibus_rw`'s real boundaries exactly. Two registers get real behavior (the diagnostic-interface mode register, which sets `UCode.PromDisabled`; a minimal disk-controller status register, enough to satisfy the boot PROM's `DISK-RECALIBRATE` poll loop). Everything else stays a logged, non-fatal placeholder — this is **not** a full device-emulation port (see Global Constraints).

**Tech Stack:** C#, .NET 8.0. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-08-21-microcode-engine-design.md` (§ "Phase 5B — Bus Adaptor")

## Global Constraints

- **This is a deliberately minimal, honest scope — not a full device-emulation port.** Real disk data transfer, TV/color-TV screens, tape, Unibus-Map DMA, and IOB unibus devices are NOT implemented — they get a logged, non-fatal placeholder identical in spirit to `Vm()`'s current top-level one, just routed through the correct address ranges instead of caught by one blind catch-all. Do not add real transfer/threading/device logic beyond the two registers named below; that is tracked as a separate future item in the spec, not this plan's job.
- Every octal boundary in this plan has been verified by direct programmatic computation (Python's `0o` literal + `hex()`), not manual digit-counting — this project has had four octal-to-hex mistranslations already from manual counting. If you need to re-derive any boundary during implementation, use the same method; do not count digits by eye.
- `BusAdaptor` is `public` (parallel to `Uvmem`) so `BusAdaptorTests.cs` can exercise it directly. `UCode` gains a `public BusAdaptor BusAdaptor { get; }` property, constructed unconditionally in both existing constructors (mirroring `Uvmem`'s wiring from Phase 5 — always non-null regardless of which `UCode()` overload ran).
- `BusAdaptor.Write` takes `ref bool promDisabled` because the one real register it implements (the diagnostic-interface mode register) needs to mutate `UCode.PromDisabled` directly — this is a genuine, justified coupling (unlike `Uvmem`, which needed no `UCode`/`MainMemory` dependency at all, because neither of its methods ever touches anything outside its own L1/L2 arrays).
- `Vm()`'s existing "xbus main memory" fast path (`dispatchPn <= 0x3BFB`, reading/writing real `MainMemory`) is untouched — only the `else` placeholder branch is replaced with a real call into `BusAdaptor`.
- The real C's fatal misuse guards (`diagnostic-interface.c`'s clock-control/OPC-control registers `errx()` on any unexpected write, since real microcode is never expected to touch them) are ported as no-ops, not as faithful throws — replicating a "should never happen" fatal guard has no value here, and if real boot microcode genuinely never hits it (as expected), the distinction never surfaces. Similarly, `bus_interface_set_xbus_nxm()`/`bus_interface_set_unibus_map_error()` (both from the already-separately-deferred `bus-interface.c`, per Phase 4's ruling) are omitted entirely from the placeholder paths — they would just set flags in that still-unported subsystem.

---

### Task 1: `BusAdaptor` class

**Files:**
- Create: `usim-cs/BusAdaptor.cs`
- Create: `usim-cs/BusAdaptorTests.cs`
- Modify: `usim-cs/Program.cs` (register the new test suite)

**Interfaces:**
- Consumes: nothing outside itself for reads; `ref bool promDisabled` for writes (a plain parameter, not a dependency on `UCode`).
- Produces: `public uint Read(uint paddr)`, `public void Write(uint paddr, uint v, ref bool promDisabled)`. Task 2's `UCode.Vm()` calls both with these exact signatures.

- [ ] **Step 1: Write the failing tests**

Create `usim-cs/BusAdaptorTests.cs`:

```csharp
// BusAdaptorTests.cs - Tests for the deliberately-minimal bus-adaptor device
// routing (Phase 5B of the microcode engine port). Covers the two real
// registers (disk-controller status, diagnostic-interface mode register) and
// confirms every placeholder path is non-fatal (never throws).

using System;

namespace Usim;

public static class BusAdaptorTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== BusAdaptor Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestDiskControlStatusRead()) passed++; else failed++;
        if (TestDiskControlOtherOffsetsAndWrites()) passed++; else failed++;
        if (TestDiagnosticModeRegisterWrite()) passed++; else failed++;
        if (TestDiagnosticOtherRegistersNoThrow()) passed++; else failed++;
        if (TestPlaceholderPathsDoNotThrow()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestDiskControlStatusRead()
    {
        Console.WriteLine("Test: disk-controller status register (offset 0) satisfies the boot PROM's poll");
        try
        {
            var busAdaptor = new BusAdaptor();

            // Disk control range is paddr 0x3DFFFC-0x3DFFFF (017377774-017377777 octal);
            // offset 0 (status) is at 0x3DFFFC.
            uint status = busAdaptor.Read(0x3DFFFC);
            Assert((status & 1) != 0, $"bit0 (not_active/ready) must be set, got 0x{status:X}");
            Assert((status & (1u << 9)) == 0, $"bit9 (!online) must be clear (i.e. online), got 0x{status:X}");

            Console.WriteLine("  Disk-control status tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Disk-control status tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDiskControlOtherOffsetsAndWrites()
    {
        Console.WriteLine("Test: disk-controller other offsets read 0; writes are a no-op (not a working disk)");
        try
        {
            var busAdaptor = new BusAdaptor();
            bool promDisabled = false;

            // Offsets 1 (memory address), 2 (disk address), 3 (ECC) -- no real disk
            // state, all read 0.
            Assert(busAdaptor.Read(0x3DFFFD) == 0, "offset 1 (memory address) reads 0");
            Assert(busAdaptor.Read(0x3DFFFE) == 0, "offset 2 (disk address) reads 0");
            Assert(busAdaptor.Read(0x3DFFFF) == 0, "offset 3 (ECC) reads 0");

            // Writes to any disk-control offset (command/CLP/DA) must not throw.
            busAdaptor.Write(0x3DFFFC, 0x16, ref promDisabled); // command register, "reset" value
            busAdaptor.Write(0x3DFFFD, 0, ref promDisabled);
            busAdaptor.Write(0x3DFFFE, 0x1234, ref promDisabled);
            Assert(promDisabled == false, "disk-control writes never touch promDisabled");

            Console.WriteLine("  Disk-control other-offsets tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Disk-control other-offsets tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDiagnosticModeRegisterWrite()
    {
        Console.WriteLine("Test: diagnostic-interface mode register (Unibus 0766012) sets PromDisabled");
        try
        {
            var busAdaptor = new BusAdaptor();
            bool promDisabled = false;

            // Unibus uaddr 0766012 octal = 0x3EC0A. Bit 5 set -> promDisabled = true.
            busAdaptor.Write(UaddrToPaddr(0x3EC0A), 1 << 5, ref promDisabled);
            Assert(promDisabled == true, "bit5 set -> PromDisabled becomes true");

            busAdaptor.Write(UaddrToPaddr(0x3EC0A), 0, ref promDisabled);
            Assert(promDisabled == false, "bit5 clear -> PromDisabled becomes false");

            busAdaptor.Write(UaddrToPaddr(0x3EC0A), 0xFFFFFFFF, ref promDisabled);
            Assert(promDisabled == true, "bit5 set among other bits -> still true");

            Console.WriteLine("  Diagnostic mode-register tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Diagnostic mode-register tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDiagnosticOtherRegistersNoThrow()
    {
        Console.WriteLine("Test: other diagnostic-interface registers are a no-op, not the real C's fatal errx()");
        try
        {
            var busAdaptor = new BusAdaptor();
            bool promDisabled = false;

            // DEBUG-IR (0766000-0766004), clock control (0766006, real C errx()s if v!=1),
            // OPC control (0766010, real C errx()s unconditionally) -- all must be safe no-ops here.
            busAdaptor.Write(UaddrToPaddr(0x3EC00), 0x1234, ref promDisabled);
            busAdaptor.Write(UaddrToPaddr(0x3EC03), 0x99, ref promDisabled); // clock control, NOT 1 -- would errx() in the real C
            busAdaptor.Write(UaddrToPaddr(0x3EC05), 0x42, ref promDisabled); // OPC control -- always errx()s in the real C
            Assert(promDisabled == false, "none of these touch promDisabled");

            Assert(busAdaptor.Read(UaddrToPaddr(0x3EC00)) == 0, "reading a non-mode spy register is a safe default (0)");

            Console.WriteLine("  Diagnostic other-registers tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Diagnostic other-registers tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestPlaceholderPathsDoNotThrow()
    {
        Console.WriteLine("Test: every un-implemented device path is non-fatal (TV, color TV, Unibus Map, IOB, tape, unmapped)");
        try
        {
            var busAdaptor = new BusAdaptor();
            bool promDisabled = false;

            // Main TV screen (XBus I/O, 0x3C0000-0x3C7FFF).
            Assert(busAdaptor.Read(0x3C0000) == 0, "TV screen read is a safe default (0)");
            busAdaptor.Write(0x3C0000, 0x1234, ref promDisabled);

            // Unibus Map (0xC000-0xFFFF uaddr).
            Assert(busAdaptor.Read(UaddrToPaddr(0xC000)) == 0, "Unibus Map read is a safe default (0)");
            busAdaptor.Write(UaddrToPaddr(0xC000), 0x1234, ref promDisabled);

            // Tape controller (0x3F550-0x3F55A uaddr).
            Assert(busAdaptor.Read(UaddrToPaddr(0x3F550)) == 0, "tape-controller read is a safe default (0)");
            busAdaptor.Write(UaddrToPaddr(0x3F550), 0x1234, ref promDisabled);

            // Genuinely unmapped XBus I/O address (within the XBus-I/O page-number
            // range but outside every named device's absolute-paddr window).
            Assert(busAdaptor.Read(0x3C8000) == 0, "unmapped XBus-I/O read is a safe default (0)");
            busAdaptor.Write(0x3C8000, 0x1234, ref promDisabled);

            Assert(promDisabled == false, "none of these placeholder paths touch promDisabled");

            Console.WriteLine("  Placeholder-paths tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Placeholder-paths tests failed: {ex.Message}\n");
            return false;
        }
    }

    /// <summary>
    /// Inverse of BusAdaptor's own uaddr formula, for test setup: given a Unibus
    /// uaddr, produce a paddr whose dispatchPn resolves into the Unibus range and
    /// which BusAdaptor.Read/Write will convert back to exactly that uaddr.
    /// uaddr = (((dispatchPn - 0x3E00) &lt;&lt; 8) | (paddr &amp; 0xFF)) &lt;&lt; 1, so with
    /// paddr's low byte held at 0, dispatchPn = (uaddr &gt;&gt; 1 &gt;&gt; 8) + 0x3E00.
    /// </summary>
    private static uint UaddrToPaddr(uint uaddr)
    {
        uint halfWordIndex = uaddr >> 1;
        uint dispatchPn = (halfWordIndex >> 8) + 0x3E00;
        uint paddrLowByte = halfWordIndex & 0xFF;
        return (dispatchPn << 8) | paddrLowByte;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
```

**Before implementing, verify `UaddrToPaddr`'s inverse formula yourself**: trace `UaddrToPaddr(0x3EC0A)` forward through `BusAdaptor.Read`/`Write`'s own `uaddr = (((dispatchPn - 0x3E00) << 8) | (paddr & 0xFF)) << 1` computation and confirm it reproduces `0x3EC0A` exactly, before trusting the test values below it.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build LispMachine.sln`
Expected: FAIL — `BusAdaptor` doesn't exist yet.

- [ ] **Step 3: Implement `BusAdaptor`**

Create `usim-cs/BusAdaptor.cs`:

```csharp
// BusAdaptor.cs - Faithful (but deliberately scoped) port of usim/bus-adaptor.c's
// XBus-I/O and Unibus device routing. UCode.Vm() already handles the "xbus main
// memory" range (physical page number <= 0x3BFB) for real; this class handles
// everything above that -- the boot PROM's disk-control and diagnostic-register
// accesses, plus non-fatal placeholders for every other device bus-adaptor.c
// would route to (TV, color TV, tape, Unibus Map DMA, IOB, unibus-mapping,
// bus-interface). NOT a full device-emulation port -- see the Phase 5B spec
// section for what's deliberately out of scope and why.

using System;

namespace Usim;

public class BusAdaptor
{
    // XBus I/O absolute physical-address range for disk control (usim/bus-adaptor.c's
    // bus_adaptor_xbusio_rw). 017377774-017377777 octal = 0x3DFFFC-0x3DFFFF.
    private const uint DiskControlLo = 0x3DFFFC;
    private const uint DiskControlHi = 0x3DFFFF;

    // Unibus 16-bit-word address range for the diagnostic-interface "spy" registers
    // (usim/bus-adaptor.c's bus_adaptor_unibus_rw). 0766000-0766036 octal =
    // 0x3EC00-0x3EC1E; the mode register specifically is 0766012 octal = 0x3EC0A.
    private const uint DiagnosticLo = 0x3EC00;
    private const uint DiagnosticHi = 0x3EC1E;
    private const uint DiagnosticModeRegister = 0x3EC0A;

    /// <summary>
    /// Faithful port of bus_adaptor_read (usim/bus-adaptor.c), for the XBus-I/O and
    /// Unibus ranges only -- UCode.Vm() already handles "xbus main memory" for real
    /// before ever calling this. paddr's page number (paddr>>8 &amp; 0x3FFF) must
    /// already be &gt; 0x3BFB, matching Vm()'s own dispatch split.
    /// </summary>
    public uint Read(uint paddr)
    {
        uint dispatchPn = (paddr >> 8) & 0x3FFF;
        if (dispatchPn <= 0x3DFF) return ReadXbusIo(paddr);
        uint uaddr = (((dispatchPn - 0x3E00) << 8) | (paddr & 0xFF)) << 1;
        return ReadUnibus(uaddr);
    }

    /// <summary>
    /// Faithful port of bus_adaptor_write (usim/bus-adaptor.c), same scope note as
    /// Read. promDisabled is UCode's own PromDisabled field, threaded through
    /// because the one real register this class implements (the diagnostic-
    /// interface mode register) sets it directly, matching the real C's
    /// machine_state.promdisabled = (v &amp; (1&lt;&lt;5)) != 0.
    /// </summary>
    public void Write(uint paddr, uint v, ref bool promDisabled)
    {
        uint dispatchPn = (paddr >> 8) & 0x3FFF;
        if (dispatchPn <= 0x3DFF) { WriteXbusIo(paddr, v); return; }
        uint uaddr = (((dispatchPn - 0x3E00) << 8) | (paddr & 0xFF)) << 1;
        WriteUnibus(uaddr, v, ref promDisabled);
    }

    private uint ReadXbusIo(uint paddr)
    {
        if (paddr >= DiskControlLo && paddr <= DiskControlHi)
        {
            uint offset = paddr - DiskControlLo;
            // Faithful port of encode_status() (usim/disk-controller.c:174-219) for
            // the two bits the boot PROM's DISK-RECALIBRATE polls: bit0=not_active
            // (ready/idle)=1, bit9=!online=0 (i.e. online). Every other status bit
            // (seek_error, read_only, has_fault, attention, interrupt_request, any
            // real error condition) defaults to 0 -- this is not a real disk, just
            // "no errors, ready, online". Offsets 1 (memory address), 2 (disk
            // address), 3 (ECC, "no ECC errors in usim, so this always returns 0")
            // have no real disk state to report either.
            return offset == 0 ? 1u : 0u;
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: read un-ported XBus-I/O paddr 0x{paddr:X} (TV/color-TV -- not implemented, Phase 5B scope)");
        return 0;
    }

    private void WriteXbusIo(uint paddr, uint v)
    {
        if (paddr >= DiskControlLo && paddr <= DiskControlHi)
        {
            // Command/CLP/DA writes: a no-op. This is NOT a working disk -- no real
            // transfer happens. Tracked as a deliberate, out-of-scope gap in the
            // Phase 5B spec section, not silently implied to work.
            return;
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: write un-ported XBus-I/O paddr 0x{paddr:X} v=0x{v:X} (TV/color-TV -- not implemented, Phase 5B scope)");
    }

    private uint ReadUnibus(uint uaddr)
    {
        if (uaddr >= DiagnosticLo && uaddr <= DiagnosticHi)
        {
            // No spy register is meaningfully readable back without real debug-IR/
            // clock state, which this phase doesn't implement -- 0 is a safe
            // default; the boot PROM's early boot never reads these back.
            return 0;
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: read un-ported Unibus uaddr 0x{uaddr:X} (Unibus Map/IOB/tape/unibus-mapping/bus-interface -- not implemented, Phase 5B scope)");
        return 0;
    }

    private void WriteUnibus(uint uaddr, uint v, ref bool promDisabled)
    {
        if (uaddr == DiagnosticModeRegister)
        {
            promDisabled = (v & (1 << 5)) != 0;
            return;
        }
        if (uaddr >= DiagnosticLo && uaddr <= DiagnosticHi)
        {
            // Other spy registers (DEBUG-IR, clock control, OPC control) are fatal
            // misuse guards in the real C (errx() -- real microcode is never
            // expected to trigger them) -- a no-op here, not a faithful throw. See
            // Global Constraints in the Phase 5B plan for why.
            return;
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: write un-ported Unibus uaddr 0x{uaddr:X} v=0x{v:X} (Unibus Map/IOB/tape/unibus-mapping/bus-interface -- not implemented, Phase 5B scope)");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build LispMachine.sln` (expect 0 errors), then `dotnet run --project usim-cs -- --test-bus-adaptor`
Expected: `Passed: 5`, `Failed: 0`.

- [ ] **Step 5: Wire the new test suite into `Program.cs`**

Add next to the existing test-suite cases:
```csharp
                case "--test-bus-adaptor":
                    BusAdaptorTests.RunAllTests();
                    Environment.Exit(0);
                    break;
```
Add to the usage help text:
```csharp
        Console.WriteLine("  --test-bus-adaptor      Run bus adaptor tests only");
```
Add to the master `RunAllTests()` method:
```csharp
        BusAdaptorTests.RunAllTests();
```

- [ ] **Step 6: Run the full regression suite**

Run: `dotnet run --project usim-cs -- --test-all`
Expected: all suites report 0 failures (this task only adds a new, independent class — nothing in `UCode.cs` changes yet).

- [ ] **Step 7: Commit**

```bash
git add usim-cs/BusAdaptor.cs usim-cs/BusAdaptorTests.cs usim-cs/Program.cs
git commit -m "Add BusAdaptor XBus-I/O and Unibus device routing"
```

---

### Task 2: Wire `BusAdaptor` into `UCode.Vm()`

**Files:**
- Modify: `usim-cs/UCode.cs`
- Create: `usim-cs/UCodeBusAdaptorTests.cs`
- Modify: `usim-cs/Program.cs` (register the new test suite)

**Interfaces:**
- Consumes: `BusAdaptor.Read`/`Write` (Task 1).
- Produces: `UCode.BusAdaptor` property; `Vm()`'s placeholder branch replaced with a real call. `VmRead`/`VmWrite`/`CallVm`'s existing signatures are unchanged — every existing Phase 1-5 call site (`AdvanceLc`, `MfWrite` codes 17/18/25/26) needs no changes.

- [ ] **Step 1: Write the failing tests**

Create `usim-cs/UCodeBusAdaptorTests.cs`:

```csharp
// UCodeBusAdaptorTests.cs - Tests for UCode.Vm()'s wiring into BusAdaptor
// (Phase 5B of the microcode engine port). Covers the end-to-end path from a
// mapped virtual address through Vm()/Uvmem.Vtop to BusAdaptor's real
// registers, and confirms the existing "xbus main memory" fast path (Phase 5)
// is untouched.

using System;

namespace Usim;

public static class UCodeBusAdaptorTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== UCode BusAdaptor Wiring Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestUCodeHasNonNullBusAdaptor()) passed++; else failed++;
        if (TestVmReadsRealDiskStatusThroughFullChain()) passed++; else failed++;
        if (TestVmWriteSetsPromDisabledThroughFullChain()) passed++; else failed++;
        if (TestVmMainMemoryFastPathStillWorks()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestUCodeHasNonNullBusAdaptor()
    {
        Console.WriteLine("Test: UCode() (both constructors) produces a non-null BusAdaptor");
        try
        {
            var ucode1 = new UCode();
            Assert(ucode1.BusAdaptor != null, "default constructor -> non-null BusAdaptor");

            var ucode2 = new UCode(new MainMemory());
            Assert(ucode2.BusAdaptor != null, "explicit constructor -> non-null BusAdaptor");

            Console.WriteLine("  UCode-has-BusAdaptor tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  UCode-has-BusAdaptor tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVmReadsRealDiskStatusThroughFullChain()
    {
        Console.WriteLine("Test: Vm() read of a mapped disk-control address returns BusAdaptor's real status");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            // Map vaddr 0x00008000 to physical page number 0x3DFFFC's page (i.e.
            // physical page 0x3DFFFC's own page number, so that Vtop's
            // (pn<<8)|(vaddr&0xFF) reproduces the disk-control status paddr exactly
            // when vaddr's low byte is 0). Disk-control status is at paddr
            // 0x3DFFFC, so pn = 0x3DFFFC >> 8 = 0x3DFFFC... actually pn is the
            // FULL page number the L2 map stores, and paddr = (pn<<8)|(vaddr&0xFF);
            // to land exactly on 0x3DFFFC (whose low byte is 0xFC), vaddr's low
            // byte must be 0xFC and pn must be 0x3DFFFC>>8 = 0x3DFF.
            uint vaddr = 0x000080FC; // low byte 0xFC
            uint md = vaddr;
            uint l2Data = (1u << 23) | (1u << 22) | 0x3DFFu; // access+write permission, pn=0x3DFF
            uint vma = (1u << 26) | (1u << 25) | (0x01u << 27) | l2Data;
            ucode.Uvmem.WriteMap(vma, md);

            uint v = 0xDEADBEEF;
            ucode.CallVm(false, vaddr, ref v);
            Assert((v & 1) != 0, $"disk-control status bit0 (ready) reaches Vm()'s caller, got 0x{v:X}");
            Assert(ucode.VmaOk == true, "mapped with access permission -> VmaOk true");

            Console.WriteLine("  Vm-reads-real-disk-status tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vm-reads-real-disk-status tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVmWriteSetsPromDisabledThroughFullChain()
    {
        Console.WriteLine("Test: Vm() write to the diagnostic mode register sets UCode.PromDisabled through the full chain");
        try
        {
            var ucode = new UCode();
            ucode.Init();
            Assert(ucode.PromDisabled == false, "PromDisabled starts false after Init()");

            // Unibus uaddr 0766012 octal = 0x3EC0A. Working backwards through
            // BusAdaptor's uaddr formula (uaddr = (((pn-0x3E00)<<8) | paddrLowByte) << 1,
            // with dispatchPn==pn and paddrLowByte==vaddr's low byte when Vtop's paddr =
            // (pn<<8)|(vaddr&0xFF)): half = 0x3EC0A>>1 = 0x1F605; the needed low byte is
            // half&0xFF = 0x05 (NOT 0 -- an earlier draft of this test wrongly assumed a
            // 0 low byte, which gives uaddr 0x3EC00, not 0x3EC0A); pn = (half>>8)+0x3E00
            // = 0x1F6+0x3E00 = 0x3FF6. Independently verified by forward substitution
            // (Python one-liner): pn=0x3FF6, vaddr low byte=0x05 reproduces uaddr=0x3EC0A
            // exactly. This also cross-confirms Phase 5's own final-review calibration,
            // which independently found physical page 0x3FF6 to be the real
            // diagnostic/spy-register page reachable from the boot PROM.
            uint pn = 0x3FF6;
            uint vaddr = 0x00000005; // low byte 0x05 -- see derivation above
            uint md = vaddr;
            uint l2Data = (1u << 23) | (1u << 22) | (pn & 0x3FFFu);
            uint vma = (1u << 26) | (1u << 25) | (0x02u << 27) | l2Data;
            ucode.Uvmem.WriteMap(vma, md);

            uint writeVal = 1u << 5; // sets promdisabled
            ucode.CallVm(true, vaddr, ref writeVal);
            Assert(ucode.PromDisabled == true, "diagnostic mode register write reaches PromDisabled through the full Vm()->BusAdaptor chain");

            Console.WriteLine("  Vm-sets-PromDisabled tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vm-sets-PromDisabled tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestVmMainMemoryFastPathStillWorks()
    {
        Console.WriteLine("Test: Vm()'s existing 'xbus main memory' fast path (Phase 5) is untouched by this wiring");
        try
        {
            var ucode = new UCode();
            ucode.Init();

            uint vaddr = 0x00003000;
            uint md = vaddr;
            uint l2Data = (1u << 23) | (1u << 22) | 5u; // access+write, pn=5 (well within main-memory range)
            uint vma = (1u << 26) | (1u << 25) | (0x02u << 27) | l2Data;
            ucode.Uvmem.WriteMap(vma, md);

            uint writeVal = 0x77778888;
            ucode.CallVm(true, vaddr, ref writeVal);
            uint readVal = 0;
            ucode.CallVm(false, vaddr, ref readVal);
            Assert(readVal == 0x77778888, $"main-memory read/write round-trip still works, got 0x{readVal:X}");

            Console.WriteLine("  Vm-main-memory-fast-path tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Vm-main-memory-fast-path tests failed: {ex.Message}\n");
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

Note on `TestVmWriteSetsPromDisabledThroughFullChain`'s `pn`/`vaddr` values above: these were independently verified by the controller via forward substitution (a Python one-liner) before this plan was finalized — an earlier draft of this exact test wrongly assumed a 0 low byte and would have failed against a correct implementation. If you re-derive this by hand while implementing, you should reach the same `pn=0x3FF6`, `vaddr` low byte `0x05`; if you get a different answer, trust your own re-derivation and flag the discrepancy rather than assuming this plan is right.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build LispMachine.sln`
Expected: FAIL — `ucode.BusAdaptor` doesn't exist yet, and (once the `pn` placeholder above is resolved) the diagnostic-register test fails because `Vm()` still routes to the old placeholder instead of `BusAdaptor`.

- [ ] **Step 3: Implement**

In `usim-cs/UCode.cs`, add a `BusAdaptor` property next to `Uvmem`'s, and construct it the same way in both constructors:
```csharp
    public Uvmem Uvmem { get; }
    public BusAdaptor BusAdaptor { get; }

    public UCode() : this(new MainMemory()) { }

    public UCode(MainMemory mainMemory)
    {
        _mainMemory = mainMemory;
        Uvmem = new Uvmem();
        BusAdaptor = new BusAdaptor();
    }
```

Replace `Vm()`'s placeholder `else` branch:
```csharp
        else
        {
            TraceLog.Instance.Warning(TraceCategory.Memory,
                $"Vm: {(write ? "write" : "read")} to un-ported XBus-I/O/Unibus paddr 0x{paddr:X} (pn 0x{dispatchPn:X}) -- deferred to a future bus-adaptor port");
            if (!write) v = 0;
        }
```
with:
```csharp
        else
        {
            bool promDisabled = PromDisabled;
            if (write) BusAdaptor.Write(paddr, v, ref promDisabled);
            else v = BusAdaptor.Read(paddr);
            PromDisabled = promDisabled;
        }
```
(`BusAdaptor.Write` only ever mutates `promDisabled` for the one diagnostic-mode-register case; reading it back out into `PromDisabled` afterward is cheap and correct regardless of which path was taken.)

Update `Vm()`'s doc comment to remove the now-inaccurate "deliberately deferred, non-fatal placeholder... a wholly separate, not-yet-ported bus-adaptor/device subsystem" line, replacing it with a short note that the non-main-memory range now routes through `BusAdaptor` (Phase 5B), which itself has its own deliberately-scoped placeholders — do not leave the old comment's blanket "not yet ported" claim standing once this task lands.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build LispMachine.sln` (expect 0 errors), then `dotnet run --project usim-cs -- --test-microcode-bus-adaptor`
Expected: `Passed: 4`, `Failed: 0`.

- [ ] **Step 5: Wire the new test suite into `Program.cs`**

Add next to the existing test-suite cases:
```csharp
                case "--test-microcode-bus-adaptor":
                    UCodeBusAdaptorTests.RunAllTests();
                    Environment.Exit(0);
                    break;
```
Add to the usage help text:
```csharp
        Console.WriteLine("  --test-microcode-bus-adaptor Run UCode/BusAdaptor wiring tests only");
```
Add to the master `RunAllTests()` method:
```csharp
        UCodeBusAdaptorTests.RunAllTests();
```

- [ ] **Step 6: Run the full regression suite**

Run: `dotnet run --project usim-cs -- --test-all`
Expected: all suites report 0 failures, INCLUDING every pre-existing Phase 1-5 suite (`AdvanceLc`'s existing tests, `MfWrite`'s existing VMA/MD tests, `UCodeVirtualMemoryTests`'s existing deferred-placeholder test — which now exercises real `BusAdaptor` routing instead of the old blind placeholder; if its exact assertions about "reads as 0"/"does not throw" no longer hold verbatim because the address it uses now resolves to one of `BusAdaptor`'s real registers rather than a placeholder, that is an expected consequence of this task's own work — update that pre-existing test's assertions to match the new, more accurate behavior rather than leaving it red, following the same pattern Phase 4/5 already used for exactly this kind of cross-task regression).

- [ ] **Step 7: Commit**

```bash
git add usim-cs/UCode.cs usim-cs/UCodeBusAdaptorTests.cs usim-cs/Program.cs
git commit -m "Wire BusAdaptor into UCode.Vm(), replacing the blind placeholder"
```

---

## Self-Review Notes

- **Spec coverage:** both pieces of real behavior the Phase 5B spec section calls for (diagnostic-interface mode register, disk-controller status register) are implemented; every other device path is a logged, non-fatal placeholder, matching the spec's explicit scope boundary.
- **Placeholder scan:** the placeholders left by this plan (TV, color TV, tape, Unibus Map, IOB, unibus-mapping, bus-interface) are all explicitly named and phase-tagged in code comments, not vague TODOs — matching this project's established convention (`MfRead`'s Phase-5-deferred code 9 before this phase, `Vm()`'s own placeholder before this phase, etc.).
- **Type consistency:** `BusAdaptor.Read(uint) -> uint`, `BusAdaptor.Write(uint, uint, ref bool)` — used identically from `UCode.Vm()`. `UCode.BusAdaptor` is constructed in both constructors, exactly mirroring `Uvmem`'s existing wiring pattern from Phase 5.
- **Self-review caught a real bug before this plan was finalized:** Task 2's `TestVmWriteSetsPromDisabledThroughFullChain` originally assumed a 0 low byte for `vaddr`, which would have produced `uaddr=0x3EC00` instead of the intended `0x3EC0A` (off by the exact low-byte contribution, doubled by the formula's final `<<1`) — the same class of two-stage address-formula bug that hit several of Phase 5's own tests. Caught by forward-verifying with a Python one-liner rather than trusting the first hand-derivation; the plan now carries the corrected, independently-verified values (`pn=0x3FF6`, `vaddr` low byte `0x05`).
