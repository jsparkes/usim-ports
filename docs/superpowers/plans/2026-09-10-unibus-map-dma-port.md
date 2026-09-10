# Unibus-Map DMA Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `BusAdaptor.cs`'s Unibus-Map-DMA placeholder with a faithful port of `usim/unibus-mapping.c` (the 16 mapping registers) plus the real DMA-translation logic in `usim/bus-adaptor.c` (the part that makes this a real, functioning feature).

**Architecture:** Two tasks. (1) `UnibusMapping.cs` — the register/buffer storage class, fully standalone. (2) Wiring — `BusAdaptor` grows two constructor dependencies (`MainMemory`, `UCode`), gains the real DMA-translation methods, and every existing `BusAdaptor`/`UCode` construction site that this breaks gets fixed, plus the one existing test whose "placeholder" assertions are about to describe something that's no longer a placeholder.

**Tech Stack:** C#, .NET 8.0, no new dependencies.

**Spec:** `docs/superpowers/specs/2026-09-10-unibus-map-dma-design.md`

## Global Constraints

- No new NuGet dependencies.
- Every octal literal in this plan's code was independently verified via script — see the spec's Octal Literals table. Use the hex forms given.
- `ColortvEnabled`-gated device branches are not newly wired by this plan — a DMA transfer that happens to target that range hits today's existing placeholder, unchanged.
- The new `BusAdaptor` constructor dependencies (`MainMemory`, `UCode`) are passed the same safe, store-only way `BusInterface` already is (a plain constructor parameter — `UCode` passes `this` before its own construction finishes, safe because `BusAdaptor`'s constructor never calls back into it). Do not introduce a settable-property pattern for these — there's no genuine two-way dependency here, unlike `DiskController`'s case.

---

### Task 1: `UnibusMapping.cs` — the 16 mapping registers, fully standalone

**Files:**
- Create: `usim-cs/UnibusMapping.cs`
- Test: `usim-cs/UnibusMappingTests.cs` (new)
- Modify: `usim-cs/Program.cs` (CLI wiring for the new test suite)

**Interfaces:**
- Consumes: `BusInterface.SetUnibusNxm()` (already real, from the bus-interface port).
- Produces (for Task 2): `public UnibusMapping(BusInterface busInterface)`, `public ushort GetRegister(uint pageNo)`, `public ushort GetBuffer(uint pageNo)`, `public void SetBuffer(uint pageNo, ushort value)`, `public uint Read(uint uaddr)`, `public void Write(uint uaddr, uint v)`.

This task does not touch `BusAdaptor.cs` or `UCode.cs` — fully testable standalone.

- [ ] **Step 1: Write `usim-cs/UnibusMapping.cs`**

```csharp
// UnibusMapping.cs - Faithful port of usim/unibus-mapping.c's 16 Unibus <-> Xbus
// mapping registers (the storage half of "Unibus Map DMA" -- the real DMA
// translation logic lives in BusAdaptor.cs; see
// docs/superpowers/specs/2026-09-10-unibus-map-dma-design.md).

using System;

namespace Usim;

public class UnibusMapping
{
    private readonly ushort[] _registers = new ushort[16];
    private readonly ushort[] _buffers = new ushort[16];
    private readonly BusInterface _busInterface;

    public UnibusMapping(BusInterface busInterface)
    {
        _busInterface = busInterface;
    }

    public ushort GetRegister(uint pageNo) => _registers[pageNo];
    public ushort GetBuffer(uint pageNo) => _buffers[pageNo];
    public void SetBuffer(uint pageNo, ushort value) => _buffers[pageNo] = value;

    /// <summary>
    /// Faithful port of unibus_mapping_read (usim/unibus-mapping.c:85-89, via
    /// unibus_mapping_rw at :19-83). uaddr is the Unibus word address --
    /// BusAdaptor already range-gates to 0x3EC60..0x3EC7E before calling this,
    /// and every address in that exact range is one of the 16 registers, so
    /// the real C's default case (odd uaddr, or genuinely out of range) is not
    /// reachable via that call path -- ported faithfully anyway, matching this
    /// project's established practice for real-but-practically-unreachable
    /// fallback cases.
    /// </summary>
    public uint Read(uint uaddr)
    {
        if (uaddr >= 0x3EC60 && uaddr <= 0x3EC7E && (uaddr & 1) == 0)
        {
            uint pageNo = (uaddr - 0x3EC60) / 2;
            return _registers[pageNo];
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"unibus-mapping: read invalid uaddr:0x{uaddr:X}");
        _busInterface.SetUnibusNxm();
        return 0;
    }

    /// <summary>Faithful port of unibus_mapping_write (usim/unibus-mapping.c:91-95).</summary>
    public void Write(uint uaddr, uint v)
    {
        if (uaddr >= 0x3EC60 && uaddr <= 0x3EC7E && (uaddr & 1) == 0)
        {
            uint pageNo = (uaddr - 0x3EC60) / 2;
            _registers[pageNo] = (ushort)v;
            return;
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"unibus-mapping: write invalid uaddr:0x{uaddr:X}");
        _busInterface.SetUnibusNxm();
    }
}
```

- [ ] **Step 2: Write `usim-cs/UnibusMappingTests.cs`**

```csharp
// UnibusMappingTests.cs - Tests for the faithful Unibus-mapping register port
// (usim/unibus-mapping.c).

using System;

namespace Usim;

public static class UnibusMappingTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== UnibusMapping Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestRegisterReadWriteRoundTrip()) passed++; else failed++;
        if (TestAllSixteenRegistersIndependent()) passed++; else failed++;
        if (TestBufferAccessors()) passed++; else failed++;
        if (TestInvalidUaddrSetsNxm()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestRegisterReadWriteRoundTrip()
    {
        Console.WriteLine("Test: register read/write round-trip at 0x3EC60 (register 0)");
        try
        {
            var busInterface = new BusInterface(new UCode(new MainMemory()));
            var mapping = new UnibusMapping(busInterface);

            mapping.Write(0x3EC60, 0xABCD);
            Assert(mapping.Read(0x3EC60) == 0xABCD, $"register 0 round-trips, got 0x{mapping.Read(0x3EC60):X}");
            Assert(mapping.GetRegister(0) == 0xABCD, $"GetRegister(0) matches, got 0x{mapping.GetRegister(0):X}");

            Console.WriteLine("  Register round-trip test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Register round-trip test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestAllSixteenRegistersIndependent()
    {
        Console.WriteLine("Test: all 16 registers (0x3EC60-0x3EC7E) are independently addressable");
        try
        {
            var busInterface = new BusInterface(new UCode(new MainMemory()));
            var mapping = new UnibusMapping(busInterface);

            for (uint i = 0; i < 16; i++)
            {
                uint uaddr = 0x3EC60 + (i * 2);
                mapping.Write(uaddr, 0x1000 + i);
            }
            for (uint i = 0; i < 16; i++)
            {
                uint uaddr = 0x3EC60 + (i * 2);
                Assert(mapping.Read(uaddr) == 0x1000 + i, $"register {i} independent, got 0x{mapping.Read(uaddr):X}");
                Assert(mapping.GetRegister(i) == 0x1000 + i, $"GetRegister({i}) matches");
            }

            Console.WriteLine("  All-16-registers test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  All-16-registers test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBufferAccessors()
    {
        Console.WriteLine("Test: word-buffer accessors round-trip, independent of registers");
        try
        {
            var busInterface = new BusInterface(new UCode(new MainMemory()));
            var mapping = new UnibusMapping(busInterface);

            mapping.SetBuffer(3, 0x5678);
            Assert(mapping.GetBuffer(3) == 0x5678, $"buffer 3 round-trips, got 0x{mapping.GetBuffer(3):X}");
            Assert(mapping.GetBuffer(4) == 0, "buffer 4 is untouched (independent)");

            Console.WriteLine("  Buffer-accessors test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Buffer-accessors test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestInvalidUaddrSetsNxm()
    {
        Console.WriteLine("Test: an invalid uaddr (outside 0x3EC60-0x3EC7E, or odd) sets Unibus NXM, doesn't throw");
        try
        {
            var busInterface = new BusInterface(new UCode(new MainMemory()));
            var mapping = new UnibusMapping(busInterface);

            Assert(mapping.Read(0x3EC80) == 0, "out-of-range read returns 0");
            Assert(busInterface.IsUnibusNxm(), "out-of-range read sets Unibus NXM");

            busInterface.ResetBusErrorStatus();
            mapping.Write(0x3EC80, 0x1234);
            Assert(busInterface.IsUnibusNxm(), "out-of-range write sets Unibus NXM");

            busInterface.ResetBusErrorStatus();
            Assert(mapping.Read(0x3EC61) == 0, "odd uaddr within range returns 0"); // defensive, real-C-faithful case
            Assert(busInterface.IsUnibusNxm(), "odd uaddr within range sets Unibus NXM");

            Console.WriteLine("  Invalid-uaddr test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Invalid-uaddr test failed: {ex.Message}\n");
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

- [ ] **Step 3: Wire `UnibusMappingTests` into `Program.cs`**

Add a `--test-unibus-mapping` CLI case, usage-help line, and `RunAllTests()` entry, mirroring the existing `--test-bus-interface`/`BusInterfaceTests` pattern exactly.

- [ ] **Step 4: Build and test**

Run: `dotnet build usim-cs` then `dotnet run --project usim-cs -- --test-unibus-mapping`
Expected: builds clean, `Passed: 4`, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/UnibusMapping.cs usim-cs/UnibusMappingTests.cs usim-cs/Program.cs
git commit -m "Add faithful UnibusMapping register port (usim/unibus-mapping.c)

Standalone class + tests only -- not yet wired into BusAdaptor's real
DMA-translation logic (a later task)."
```

---

### Task 2: Wire real DMA translation into `BusAdaptor`

**Files:**
- Modify: `usim-cs/BusAdaptor.cs`
- Modify: `usim-cs/UCode.cs`
- Modify: `usim-cs/BusAdaptorTests.cs`

**Interfaces:**
- Consumes: `UnibusMapping` (Task 1). `MainMemory.ReadPhysical`/`WritePhysical` (already real). `UCode.MdReg` (already real, from Phase 1).

- [ ] **Step 1: Grow `BusAdaptor`'s constructor**

In `usim-cs/BusAdaptor.cs`, change:

```csharp
    private readonly BusInterface _busInterface;
    private DiskController? _diskController;

    public BusAdaptor(BusInterface busInterface)
    {
        _busInterface = busInterface;
    }
```

to:

```csharp
    private readonly BusInterface _busInterface;
    private readonly MainMemory _mainMemory;
    private readonly UCode _ucode;
    private readonly UnibusMapping _unibusMapping;
    private DiskController? _diskController;

    public BusAdaptor(BusInterface busInterface, MainMemory mainMemory, UCode ucode)
    {
        _busInterface = busInterface;
        _mainMemory = mainMemory;
        _ucode = ucode;
        _unibusMapping = new UnibusMapping(busInterface);
    }
```

(`WireDiskController` below this is unchanged.)

- [ ] **Step 2: Add the DMA-translation and Xbus-dispatch methods**

Add these new private methods to `BusAdaptor.cs` (placement: anywhere after `WriteXbusIo`/`DescribeXbusIo` and before `ReadUnibus`, or wherever reads cleanly — implementer's judgment on exact placement, not behavior):

```csharp
    /// <summary>
    /// Faithful port of the Unibus-Map-handling branch of bus_adaptor_unibus_rw
    /// (usim/bus-adaptor.c:210-367), read side. "Selected mapping register" is
    /// recomputed from pageNo on every call, matching the real C.
    /// </summary>
    private uint UnibusMapDmaRead(uint uaddr)
    {
        uint pageNo = (uaddr - UnibusMapLo) / 0x400;
        ushort mappingRegister = _unibusMapping.GetRegister(pageNo);
        bool mapValid = (mappingRegister & 0x8000) != 0;
        uint xbusPageNumber = (uint)(mappingRegister & 0x3FFF);
        uint paddr = (xbusPageNumber << 8) | ((uaddr >> 2) & 0xFF);

        if (!mapValid)
        {
            _busInterface.SetUnibusMapError();
            return 0;
        }

        bool hiword = ((uaddr >> 1) & 1) != 0;

        // "An additional feature is that writing an Xbus address of 17400000
        // or higher through the Unibus map writes into CADR's MD register."
        if (xbusPageNumber >= 0x3E00)
        {
            return hiword ? (_ucode.MdReg >> 16) & 0xFFFF : _ucode.MdReg & 0xFFFF;
        }

        if (hiword)
        {
            // High half returns the value buffered by the low-half read below --
            // no new Xbus transfer.
            return _unibusMapping.GetBuffer(pageNo);
        }

        uint v32 = XbusRead(paddr);
        _unibusMapping.SetBuffer(pageNo, (ushort)((v32 >> 16) & 0xFFFF));
        return v32 & 0xFFFF;
    }

    /// <summary>Write side of UnibusMapDmaRead's port.</summary>
    private void UnibusMapDmaWrite(uint uaddr, uint v)
    {
        uint pageNo = (uaddr - UnibusMapLo) / 0x400;
        ushort mappingRegister = _unibusMapping.GetRegister(pageNo);
        bool mapValid = (mappingRegister & 0x8000) != 0;
        bool writePermit = (mappingRegister & 0x4000) != 0;
        uint xbusPageNumber = (uint)(mappingRegister & 0x3FFF);
        uint paddr = (xbusPageNumber << 8) | ((uaddr >> 2) & 0xFF);

        if (!mapValid)
        {
            _busInterface.SetUnibusMapError();
            return;
        }
        if (!writePermit)
        {
            _busInterface.SetUnibusMapError();
            return;
        }

        bool hiword = ((uaddr >> 1) & 1) != 0;

        if (xbusPageNumber >= 0x3E00)
        {
            uint v32;
            if (hiword)
            {
                v32 = _ucode.MdReg & 0x0000FFFFu;
                v32 |= (v << 16) & 0xFFFF0000u;
            }
            else
            {
                v32 = _ucode.MdReg & 0xFFFF0000u;
                v32 |= v & 0x0000FFFFu;
            }
            _ucode.MdReg = v32;
            return;
        }

        if (hiword)
        {
            // High half completes the transfer, combining the cached low half
            // (from the write below, on a prior call) with this high half.
            ushort cachedLo = _unibusMapping.GetBuffer(pageNo);
            uint v32 = ((v << 16) & 0xFFFF0000u) | cachedLo;
            XbusWrite(paddr, v32);
        }
        else
        {
            // Low half: cache it, no transfer yet.
            _unibusMapping.SetBuffer(pageNo, (ushort)v);
        }
    }

    /// <summary>
    /// Faithful port of bus_adaptor_xbus_rw (usim/bus-adaptor.c:164-193), read
    /// side, split into a Read/Write pair per this file's existing
    /// ReadXbusIo/WriteXbusIo and ReadUnibus/WriteUnibus convention (the real
    /// C uses one bool-flagged function; this codebase doesn't).
    /// </summary>
    private uint XbusRead(uint paddr)
    {
        uint pn = (paddr >> 8) & 0x3FFF;
        if (pn <= 0x3BFB)
        {
            return _mainMemory.ReadPhysical(paddr);
        }
        if (pn >= 0x3C00 && pn <= 0x3DFF)
        {
            return ReadXbusIo(paddr);
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: xbus read unknown paddr 0x{paddr:X}");
        _busInterface.SetXbusNxm();
        return 0;
    }

    /// <summary>Write side of XbusRead's port.</summary>
    private void XbusWrite(uint paddr, uint v)
    {
        uint pn = (paddr >> 8) & 0x3FFF;
        if (pn <= 0x3BFB)
        {
            _mainMemory.WritePhysical(paddr, v);
            return;
        }
        if (pn >= 0x3C00 && pn <= 0x3DFF)
        {
            WriteXbusIo(paddr, v);
            return;
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: xbus write unknown paddr 0x{paddr:X} v=0x{v:X}");
        _busInterface.SetXbusNxm();
    }
```

- [ ] **Step 3: Wire the new dispatch into `ReadUnibus`/`WriteUnibus`**

Change `ReadUnibus` from:

```csharp
    private uint ReadUnibus(uint uaddr)
    {
        if (uaddr >= DiagnosticLo && uaddr <= DiagnosticHi)
        {
            // No spy register is meaningfully readable back without real debug-IR/
            // clock state, which this phase doesn't implement -- 0 is a safe
            // default; the boot PROM's early boot never reads these back.
            return 0;
        }
        if (uaddr >= BusInterfaceLo && uaddr <= BusInterfaceHi)
        {
            return _busInterface.Read(uaddr);
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: read un-ported Unibus uaddr 0x{uaddr:X} ({DescribeUnibus(uaddr)} -- not implemented, Phase 5B scope)");
        return 0;
    }
```

to:

```csharp
    private uint ReadUnibus(uint uaddr)
    {
        if (uaddr >= DiagnosticLo && uaddr <= DiagnosticHi)
        {
            // No spy register is meaningfully readable back without real debug-IR/
            // clock state, which this phase doesn't implement -- 0 is a safe
            // default; the boot PROM's early boot never reads these back.
            return 0;
        }
        if (uaddr >= BusInterfaceLo && uaddr <= BusInterfaceHi)
        {
            return _busInterface.Read(uaddr);
        }
        if (uaddr >= UnibusMappingLo && uaddr <= UnibusMappingHi)
        {
            return _unibusMapping.Read(uaddr);
        }
        if (uaddr >= UnibusMapLo && uaddr <= UnibusMapHi)
        {
            return UnibusMapDmaRead(uaddr);
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: read un-ported Unibus uaddr 0x{uaddr:X} ({DescribeUnibus(uaddr)} -- not implemented, Phase 5B scope)");
        return 0;
    }
```

Change `WriteUnibus` from:

```csharp
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
        if (uaddr >= BusInterfaceLo && uaddr <= BusInterfaceHi)
        {
            _busInterface.Write(uaddr, v);
            return;
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: write un-ported Unibus uaddr 0x{uaddr:X} v=0x{v:X} ({DescribeUnibus(uaddr)} -- not implemented, Phase 5B scope)");
    }
```

to:

```csharp
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
        if (uaddr >= BusInterfaceLo && uaddr <= BusInterfaceHi)
        {
            _busInterface.Write(uaddr, v);
            return;
        }
        if (uaddr >= UnibusMappingLo && uaddr <= UnibusMappingHi)
        {
            _unibusMapping.Write(uaddr, v);
            return;
        }
        if (uaddr >= UnibusMapLo && uaddr <= UnibusMapHi)
        {
            UnibusMapDmaWrite(uaddr, v);
            return;
        }
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: write un-ported Unibus uaddr 0x{uaddr:X} v=0x{v:X} ({DescribeUnibus(uaddr)} -- not implemented, Phase 5B scope)");
    }
```

In `DescribeUnibus`, remove the now-unreachable branches for both ranges (they no longer fall through to this fallback at all):

```csharp
        if (uaddr >= UnibusMapLo && uaddr <= UnibusMapHi) return "Unibus Map DMA";
```
and
```csharp
        if (uaddr >= UnibusMappingLo && uaddr <= UnibusMappingHi) return "unibus-mapping";
```

(delete both lines from `DescribeUnibus`).

- [ ] **Step 4: Fix `UCode.cs`'s constructor**

In `usim-cs/UCode.cs`, change:

```csharp
        BusInterface = new BusInterface(this);
        BusAdaptor = new BusAdaptor(BusInterface);
```

to:

```csharp
        BusInterface = new BusInterface(this);
        BusAdaptor = new BusAdaptor(BusInterface, mainMemory, this);
```

- [ ] **Step 5: Fix every `new BusAdaptor()` call site in `BusAdaptorTests.cs`**

`BusAdaptor`'s constructor now takes 3 arguments. In `usim-cs/BusAdaptorTests.cs`:

`TestDiagnosticModeRegisterWrite` and `TestDiagnosticOtherRegistersNoThrow` each have:
```csharp
            var busAdaptor = new BusAdaptor(new BusInterface(new UCode(new MainMemory())));
```
Change both to:
```csharp
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = new BusAdaptor(ucode.BusInterface, mainMemory, ucode);
```
(Simplest correct fix: construct `MainMemory`/`UCode` first and pull `BusInterface` off the `UCode` instance, rather than constructing a disconnected `BusInterface` — matches how other tests in this file already do it.)

`TestBusInterfaceRangeDispatchesForReal` currently has:
```csharp
            var ucode = new UCode(new MainMemory());
            var busAdaptor = new BusAdaptor(ucode.BusInterface);
```
Change to:
```csharp
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = new BusAdaptor(ucode.BusInterface, mainMemory, ucode);
```

`TestPlaceholderPathsDoNotThrow` currently has:
```csharp
            var busAdaptor = new BusAdaptor(new BusInterface(new UCode(new MainMemory())));
```
Change to the same pattern:
```csharp
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = new BusAdaptor(ucode.BusInterface, mainMemory, ucode);
```
**Also**, remove this test's now-stale Unibus-Map lines entirely (that range is no longer a placeholder — Step 7 below adds its own, properly-labeled, discriminating test instead):
```csharp
            // Unibus Map (0xC000-0xFFFF uaddr).
            Assert(busAdaptor.Read(UaddrToPaddr(0xC000)) == 0, "Unibus Map read is a safe default (0)");
            busAdaptor.Write(UaddrToPaddr(0xC000), 0x1234, ref promDisabled);

```
(Delete these 4 lines — including the blank line after — from `TestPlaceholderPathsDoNotThrow`; leave the TV/tape/unmapped-XBus-I/O checks in that test untouched, since those ranges genuinely remain placeholders.)

`TestDiskControlStatusRead` and `TestDiskControlDispatchesToRealController` already construct `MainMemory`/`UCode`/`DiskController` directly and use `ucode.BusAdaptor` (not a separate `new BusAdaptor(...)` call) — these need NO changes, since `ucode.BusAdaptor` is built internally by `UCode`'s own (now-fixed) constructor.

- [ ] **Step 6: Add `TestUnibusMapDma` tests**

Add these test methods and register them in `RunAllTests()`:

```csharp
    private static bool TestUnibusMapUnconfiguredPageSetsMapError()
    {
        Console.WriteLine("Test: an unconfigured Unibus-Map page (map_valid=0) sets Unibus Map Error, not NXM");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = ucode.BusAdaptor;
            bool promDisabled = false;

            uint status = busAdaptor.Read(UaddrToPaddr(0xC000)); // page 0, never configured
            Assert(status == 0, $"unconfigured page read returns 0, got 0x{status:X}");
            Assert(ucode.BusInterface.IsUnibusMapError(), "unconfigured page read sets Unibus Map Error");
            Assert(!ucode.BusInterface.IsUnibusNxm(), "unconfigured page read does NOT set Unibus NXM (a different error class)");

            ucode.BusInterface.ResetBusErrorStatus();
            busAdaptor.Write(UaddrToPaddr(0xC000), 0x1234, ref promDisabled);
            Assert(ucode.BusInterface.IsUnibusMapError(), "unconfigured page write sets Unibus Map Error");

            Console.WriteLine("  Unconfigured-page test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Unconfigured-page test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUnibusMapWriteWithoutPermitSetsMapError()
    {
        Console.WriteLine("Test: a write to a MAP_VALID-but-not-WRITE_PERMIT page sets Unibus Map Error, doesn't transfer");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = ucode.BusAdaptor;
            bool promDisabled = false;

            // Configure page 1: MAP_VALID set, WRITE_PERMIT clear, xbus page 0 (main memory).
            busAdaptor.Write(UaddrToPaddr(0x3EC62), 0x8000, ref promDisabled); // register 1 = 0x3EC60 + 2

            mainMemory.WritePhysical(0, 0xDEADBEEF); // sentinel -- must survive untouched

            busAdaptor.Write(UaddrToPaddr(0xC400), 0x1111, ref promDisabled); // page 1 low half
            Assert(ucode.BusInterface.IsUnibusMapError(), "write without permit sets Unibus Map Error");
            Assert(mainMemory.ReadPhysical(0) == 0xDEADBEEF, "no transfer happened -- sentinel untouched");

            Console.WriteLine("  Write-without-permit test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Write-without-permit test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUnibusMapDmaWriteToMainMemory()
    {
        Console.WriteLine("Test: a real Unibus-Map DMA write assembles a 32-bit word into MainMemory");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = ucode.BusAdaptor;
            bool promDisabled = false;

            // Configure page 2: MAP_VALID + WRITE_PERMIT, xbus page 5 (well within main memory).
            busAdaptor.Write(UaddrToPaddr(0x3EC64), 0xC005, ref promDisabled); // register 2 = 0x3EC60 + 4

            // Page 2's Unibus range starts at 0xC000 + 2*0x400 = 0xC800.
            // uaddr bits 9-2 select the word within the page; use uaddr offset 0
            // -> paddr = (5 << 8) | 0 = 0x500.
            uint pageBase = 0xC800;
            busAdaptor.Write(UaddrToPaddr(pageBase), 0x2222, ref promDisabled);      // low half
            busAdaptor.Write(UaddrToPaddr(pageBase + 2), 0x3333, ref promDisabled);  // high half -- completes transfer

            uint expected = (0x3333u << 16) | 0x2222u;
            Assert(mainMemory.ReadPhysical(0x500) == expected,
                $"assembled 32-bit word landed at paddr 0x500, got 0x{mainMemory.ReadPhysical(0x500):X}, expected 0x{expected:X}");

            Console.WriteLine("  DMA-write-to-main-memory test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  DMA-write-to-main-memory test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUnibusMapDmaReadFromMainMemory()
    {
        Console.WriteLine("Test: a real Unibus-Map DMA read splits a 32-bit MainMemory word across two half-cycles");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = ucode.BusAdaptor;
            bool promDisabled = false;

            busAdaptor.Write(UaddrToPaddr(0x3EC64), 0xC005, ref promDisabled); // register 2, xbus page 5

            mainMemory.WritePhysical(0x500, 0x77776666);

            uint pageBase = 0xC800;
            uint lo = busAdaptor.Read(UaddrToPaddr(pageBase));       // low half -- real transfer happens here
            uint hi = busAdaptor.Read(UaddrToPaddr(pageBase + 2));   // high half -- from the buffer, no new transfer

            Assert(lo == 0x6666, $"low half correct, got 0x{lo:X}");
            Assert(hi == 0x7777, $"high half correct (from buffer), got 0x{hi:X}");

            Console.WriteLine("  DMA-read-from-main-memory test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  DMA-read-from-main-memory test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUnibusMapMdRegisterBackdoor()
    {
        Console.WriteLine("Test: an Xbus page number >= 0x3E00 through the Unibus map reads/writes UCode.MdReg directly, not memory");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = ucode.BusAdaptor;
            bool promDisabled = false;

            // Configure page 3: MAP_VALID + WRITE_PERMIT, xbus page 0x3E00 (the MD-register threshold).
            busAdaptor.Write(UaddrToPaddr(0x3EC66), 0xFE00, ref promDisabled); // register 3 = 0x3EC60 + 6; 0x8000|0x4000|0x3E00

            uint pageBase = 0xCC00; // page 3 = 0xC000 + 3*0x400
            mainMemory.WritePhysical(0x500, 0xDEADBEEF); // sentinel -- must stay untouched

            busAdaptor.Write(UaddrToPaddr(pageBase), 0xAAAA, ref promDisabled);      // low half -> MdReg low
            busAdaptor.Write(UaddrToPaddr(pageBase + 2), 0xBBBB, ref promDisabled);  // high half -> MdReg high

            Assert(ucode.MdReg == ((0xBBBBu << 16) | 0xAAAAu),
                $"MdReg set directly, got 0x{ucode.MdReg:X}");
            Assert(mainMemory.ReadPhysical(0x500) == 0xDEADBEEF,
                "main memory untouched by the MD-register backdoor path");

            uint loRead = busAdaptor.Read(UaddrToPaddr(pageBase));
            uint hiRead = busAdaptor.Read(UaddrToPaddr(pageBase + 2));
            Assert(loRead == 0xAAAA && hiRead == 0xBBBB, "MdReg reads back correctly through the same backdoor");

            Console.WriteLine("  MD-register-backdoor test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MD-register-backdoor test failed: {ex.Message}\n");
            return false;
        }
    }
```

Register all 5 new tests (`TestUnibusMapUnconfiguredPageSetsMapError`, `TestUnibusMapWriteWithoutPermitSetsMapError`, `TestUnibusMapDmaWriteToMainMemory`, `TestUnibusMapDmaReadFromMainMemory`, `TestUnibusMapMdRegisterBackdoor`) in `RunAllTests()`.

Before writing these into the file, independently re-verify the register-value/address arithmetic in each test (register indices, page-base addresses, the `UaddrToPaddr` round-trip) against `BusAdaptor.cs`'s actual current constants — this plan's own numbers were derived by hand while writing it and must be re-checked, not trusted blindly, per this project's standing practice.

- [ ] **Step 7: Build and run the full suite**

Run: `dotnet build usim-cs`
Expected: builds clean.

Run: `dotnet run --project usim-cs -- --test-all`
Expected: every suite passes, including `BusAdaptorTests` (now with 5 new tests, one old test's stale lines removed) and every other suite that constructs a `UCode`/`BusAdaptor` (all still work unchanged, since only `BusAdaptor`'s own constructor signature changed, not `UCode`'s public constructors).

- [ ] **Step 8: Commit**

```bash
git add usim-cs/BusAdaptor.cs usim-cs/UCode.cs usim-cs/BusAdaptorTests.cs
git commit -m "Wire real Unibus-Map DMA translation into BusAdaptor

BusAdaptor's 0xC000-0xFFFF Unibus range now performs real page-mapped
DMA translation (to main memory, the Xbus I/O device range, or the
diagnostic MD-register backdoor) instead of a placeholder, per
usim/bus-adaptor.c's bus_adaptor_unibus_rw."
```
