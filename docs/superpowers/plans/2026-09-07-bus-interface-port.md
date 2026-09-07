# Bus Interface Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port `usim/bus-interface.c`'s bus-error-status/interrupt-register block into a real, faithful `BusInterface.cs`, replacing the generic "unmapped Unibus" placeholder that currently swallows this whole address range.

**Architecture:** A new standalone `BusInterface` class holds the real C's four file-scope statics and implements `Read`/`Write`/`BusReset` exactly per the real switch statements, with the `lashup` remote-debugger protocol permanently cut (no second physical machine exists in a software emulator). `UCode`'s constructor builds it, `BusAdaptor` gets it injected and dispatches the bus-interface address range to it instead of the generic fallback, and `MachineControl.PowerOn`/`UCode.MfWrite` (bit 28) call its `BusReset()`.

**Tech Stack:** C#, .NET 8.0, no new dependencies.

**Spec:** `docs/superpowers/specs/2026-09-07-bus-interface-design.md`

## Global Constraints

- No new NuGet dependencies.
- `lashup`/`lashup-debugger` are never ported, in this plan or any future one touching this file — permanent scope boundary, not a deferral.
- Every octal literal and bitmask must already be independently re-verified via script before use (this plan's own masks were: `0x3C01`, `0x83FC`, `0x1`, `0x8`, `0x20` — see spec's "Bit masks" section for the derivation, including one hand-conversion error the spec's own self-review caught and fixed).
- `SetXbusNxm()`/`SetUnibusNxm()` check `nxm_inhibited` and no-op if set; `SetUnibusMapError()` does **not** check it — this asymmetry is in the real C and must not be "fixed."
- Use `TraceCategory.Memory` for all of `BusInterface`'s logging (matching `BusAdaptor.cs`'s existing convention for this exact address space — do not add a new `TraceCategory` enum value for this).
- **C# has no octal integer-literal syntax.** A C-style "leading zero" literal like `0766044` is parsed as the plain decimal integer `766044`, not octal — this bit Task 1's first attempt (found by task review, fixed in a fix round: commit `d2daeee`) and was caught again in this plan's own Task 2 text before dispatch. Every real Unibus register address in this plan must be written as its verified hex equivalent (`0x3EC20`-`0x3EC4C` for the 9 real registers, `0x3EC80` for the test-only "unrecognized address" case), with the octal address noted in a comment/string for readability only.

---

### Task 1: `BusInterface.cs` — the class itself, fully unit-tested standalone

**Files:**
- Create: `usim-cs/BusInterface.cs`
- Create: `usim-cs/BusInterfaceTests.cs`
- Modify: `usim-cs/Program.cs` (CLI wiring for `--test-bus-interface`, `RunAllTests()`)

**Interfaces:**
- Consumes: `UCode.InterruptStatusReg` (`int`, existing getter) and `UCode.SetInterruptStatusReg(int)` (existing method, `UCode.cs:847`).
- Produces (for Task 2): `public BusInterface(UCode ucode)` constructor; `public uint Read(uint uaddr)`; `public void Write(uint uaddr, uint v)`; `public void BusReset()`; `public ushort GetBusErrorStatus()`; `public bool IsXbusNxm()`/`IsUnibusNxm()`/`IsUnibusMapError()`; `public void SetXbusNxm()`/`SetUnibusNxm()`/`SetUnibusMapError()`; `public void ResetBusErrorStatus()`; `public void SetNxmInhibit(bool)`.

This task does not touch `BusAdaptor.cs`, `UCode.cs`, or `MachineControl.cs` — it is fully testable by constructing `BusInterface` directly against a standalone `UCode` instance.

- [ ] **Step 1: Write `BusInterface.cs`**

```csharp
// BusInterface.cs - Faithful port of usim/bus-interface.c's bus-error-status
// and interrupt-register block. Deliberately excludes the "lashup" remote-
// debugger protocol (a serial/socket link to a SECOND, PHYSICAL CADR machine
// acting as a debuggee) -- a software-only emulator has no second machine to
// talk to, so every lashup-only register becomes a logged no-op (or, for
// 0766104, a faithfully-always-0 read: with no debuggee ever attached, 0 IS
// the real C's correct answer, not a compromise). See
// docs/superpowers/specs/2026-09-07-bus-interface-design.md for the full
// register-by-register rationale.

using System;

namespace Usim;

public class BusInterface
{
    private readonly UCode _ucode;

    // Mirrors the real C's file-scope statics (usim/bus-interface.c:26-45).
    // modifier_reset and debuggee_bus_error_status are lashup-only state --
    // omitted, since every call site that would read or write them is
    // already a no-op in this port.
    private ushort _busErrorStatus;
    private bool _nxmInhibited;
    private bool _addr17;
    private ushort _addr;

    public BusInterface(UCode ucode)
    {
        _ucode = ucode;
    }

    // usim/bus-interface.c:53-57
    public ushort GetBusErrorStatus() => _busErrorStatus;

    // usim/bus-interface.c:59-75. Masks independently re-verified via
    // script: Xbus NXM = octal 01 = 0x1, Unibus NXM = octal 010 = 0x8,
    // Unibus Map Error = octal 040 = 0x20.
    public bool IsXbusNxm() => (_busErrorStatus & 0x1) != 0;
    public bool IsUnibusNxm() => (_busErrorStatus & 0x8) != 0;
    public bool IsUnibusMapError() => (_busErrorStatus & 0x20) != 0;

    // usim/bus-interface.c:77-81
    public void ResetBusErrorStatus() => _busErrorStatus = 0;

    // usim/bus-interface.c:89-93
    public void SetNxmInhibit(bool inhibit) => _nxmInhibited = inhibit;

    // usim/bus-interface.c:95-103. Gated by nxm_inhibited.
    public void SetXbusNxm()
    {
        if (_nxmInhibited) return;
        _busErrorStatus |= 0x1;
    }

    // usim/bus-interface.c:106-114. Gated by nxm_inhibited.
    public void SetUnibusNxm()
    {
        if (_nxmInhibited) return;
        _busErrorStatus |= 0x8;
    }

    // usim/bus-interface.c:116-123. NOT gated by nxm_inhibited -- this
    // asymmetry with SetXbusNxm/SetUnibusNxm is in the real C as written.
    public void SetUnibusMapError()
    {
        _busErrorStatus |= 0x20;
    }

    /// <summary>
    /// Faithful port of bus_interface_read (usim/bus-interface.c:125-171),
    /// minus lashup. uaddr is the Unibus word address, matching BusAdaptor's
    /// ReadUnibus/WriteUnibus convention.
    /// </summary>
    public uint Read(uint uaddr)
    {
        switch (uaddr)
        {
            case 0766040:
                return (uint)_ucode.InterruptStatusReg;

            // 0766042 is write-only in the real C -- falls to default below.

            case 0766044:
                TraceLog.Instance.Debug(TraceCategory.Memory,
                    $"bus-interface: read bus status: {Convert.ToString(_busErrorStatus, 8)}");
                return _busErrorStatus;

            case 0766100:
                // Real C attempts a lashup read of the debuggee's bus; no
                // debuggee exists here.
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    "bus-interface: read data -- no debuggee attached (lashup not ported)");
                return 0;

            case 0766104:
                // Debuggee's mirrored bus-error status (lashup-only). Always
                // 0 here -- the real, correct answer with no debuggee ever
                // attached, not a placeholder.
                return 0;

            default:
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"bus-interface: read invalid uaddr:{Convert.ToString((int)uaddr, 8)}");
                SetUnibusNxm();
                return 0;
        }
    }

    /// <summary>
    /// Faithful port of bus_interface_write (usim/bus-interface.c:173-292),
    /// minus lashup.
    /// </summary>
    public void Write(uint uaddr, uint v)
    {
        switch (uaddr)
        {
            case 0766040:
                // "Writing this location writes into bits 0 and 10-13 (mask
                // 36001)." Octal 036001 = 0x3C01 -- independently verified
                // via script; a naive hand-conversion gives the wrong
                // 0xF001 (bits 0, 12-15).
                _ucode.SetInterruptStatusReg((_ucode.InterruptStatusReg & ~0x3C01) | ((int)v & 0x3C01));
                break;

            case 0766042:
                // "Writing this location writes into bits 2-9 and 15 (mask
                // 101774)." Octal 0101774 = 0x83FC.
                _ucode.SetInterruptStatusReg((_ucode.InterruptStatusReg & ~0x83FC) | ((int)v & 0x83FC));
                break;

            case 0766044:
                TraceLog.Instance.Debug(TraceCategory.Memory,
                    "bus-interface: write (clear) bus status");
                _busErrorStatus = 0;
                break;

            case 0766100:
                // Real C writes to the debuggee's bus over lashup; no-op here.
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"bus-interface: write data 0x{v:X} -- no debuggee attached (lashup not ported)");
                break;

            case 0766102:
                // Remote usim command over lashup; no-op here.
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"bus-interface: remote usim command 0x{v:X} -- no debuggee attached (lashup not ported)");
                break;

            case 0766110:
                // Modifier bits. Only addr17 (bit 0) has any meaning without
                // lashup (it's part of the debuggee-target-address
                // computation, itself only consumed by the 0766100 lashup
                // path) -- captured anyway as harmless bookkeeping. Every
                // other bit (reset, timeout-inhibit, debugger/debuggee mark,
                // ping) drives a lashup_debugger_* call in the real C; all
                // become logged no-ops here.
                _addr17 = (v & 0x1) != 0;
                TraceLog.Instance.Debug(TraceCategory.Memory,
                    $"bus-interface: write modifier bits 0x{v:X} (addr17={_addr17}) -- reset/timeout-inhibit/mark/ping are lashup-only, no-op");
                break;

            case 0766112:
                // Local usim command -- already a log-only no-op in the real
                // C itself (its cmd/param params are marked unused there).
                TraceLog.Instance.Debug(TraceCategory.Memory,
                    $"bus-interface: local usim command 0x{v:X}");
                break;

            case 0766114:
                _addr = (ushort)v;
                TraceLog.Instance.Debug(TraceCategory.Memory,
                    $"bus-interface: write address: 0x{v:X}");
                break;

            default:
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"bus-interface: write invalid uaddr:{Convert.ToString((int)uaddr, 8)}");
                SetUnibusNxm();
                break;
        }
    }

    /// <summary>
    /// Faithful port of bus_interface_bus_reset (usim/bus-interface.c:294-312),
    /// minus lashup state. The real C's fan-out to iob_bus_reset()/
    /// main_memory_bus_reset()/disk_controller_bus_reset() is a genuine no-op
    /// even there (all three are empty function bodies) -- nothing to call.
    /// tape_controller_bus_reset()/tv_bus_reset() would do something (clear a
    /// status struct; the latter is empty too) but neither TapeController nor
    /// TV exist in C# yet -- logged as a placeholder note, not silently
    /// dropped.
    /// </summary>
    public void BusReset()
    {
        _busErrorStatus = 0;
        _nxmInhibited = false;
        _addr17 = false;
        _addr = 0;

        TraceLog.Instance.Info(TraceCategory.Memory,
            "bus-interface: bus reset (tape-controller/tv bus_reset not yet ported -- no-op)");
    }
}
```

- [ ] **Step 2: Write `BusInterfaceTests.cs`**

```csharp
// BusInterfaceTests.cs - Tests for the faithful bus-interface port
// (usim/bus-interface.c), minus the permanently-out-of-scope lashup
// remote-debugger protocol.

using System;

namespace Usim;

public static class BusInterfaceTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== BusInterface Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestBusErrorStatusRoundtrip()) passed++; else failed++;
        if (TestNxmInhibitGating()) passed++; else failed++;
        if (TestUnibusMapErrorNotGated()) passed++; else failed++;
        if (TestInterruptStatusRegWriteMasks()) passed++; else failed++;
        if (TestBusStatusReadWriteRegister()) passed++; else failed++;
        if (TestDebuggeeStatusAlwaysZero()) passed++; else failed++;
        if (TestLashupOnlyRegistersNoOp()) passed++; else failed++;
        if (TestBusResetClearsState()) passed++; else failed++;
        if (TestDefaultCaseSetsUnibusNxm()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestBusErrorStatusRoundtrip()
    {
        Console.WriteLine("Test: bus_error_status get/set/reset roundtrip");
        try
        {
            var bi = new BusInterface(new UCode(new MainMemory()));

            Assert(bi.GetBusErrorStatus() == 0, "starts at 0");
            bi.SetXbusNxm();
            Assert(bi.IsXbusNxm(), "xbus nxm set");
            Assert(bi.GetBusErrorStatus() == 0x1, "bus_error_status == 0x1 after xbus nxm");
            bi.ResetBusErrorStatus();
            Assert(bi.GetBusErrorStatus() == 0, "reset clears status");

            Console.WriteLine("  Roundtrip test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Roundtrip test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestNxmInhibitGating()
    {
        Console.WriteLine("Test: SetXbusNxm/SetUnibusNxm respect nxm_inhibited");
        try
        {
            var bi = new BusInterface(new UCode(new MainMemory()));

            bi.SetNxmInhibit(true);
            bi.SetXbusNxm();
            Assert(!bi.IsXbusNxm(), "xbus nxm suppressed while inhibited");
            bi.SetUnibusNxm();
            Assert(!bi.IsUnibusNxm(), "unibus nxm suppressed while inhibited");

            bi.SetNxmInhibit(false);
            bi.SetXbusNxm();
            Assert(bi.IsXbusNxm(), "xbus nxm sets once inhibit lifted");

            Console.WriteLine("  Inhibit-gating test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Inhibit-gating test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestUnibusMapErrorNotGated()
    {
        Console.WriteLine("Test: SetUnibusMapError is NOT gated by nxm_inhibited (real C asymmetry)");
        try
        {
            var bi = new BusInterface(new UCode(new MainMemory()));

            bi.SetNxmInhibit(true);
            bi.SetUnibusMapError();
            Assert(bi.IsUnibusMapError(), "unibus map error sets even while nxm-inhibited");

            Console.WriteLine("  Unibus-map-error asymmetry test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Unibus-map-error asymmetry test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestInterruptStatusRegWriteMasks()
    {
        Console.WriteLine("Test: 0766040/0766042 writes only touch their real bitmasks");
        try
        {
            var ucode = new UCode(new MainMemory());
            var bi = new BusInterface(ucode);

            // 0766040 mask is 0x3C01 (bits 0, 10-13). Write all-1s and
            // confirm only masked bits land.
            bi.Write(0766040, 0xFFFFFFFF);
            Assert(ucode.InterruptStatusReg == 0x3C01,
                $"0766040 write masks to 0x3C01, got 0x{ucode.InterruptStatusReg:X}");

            ucode.SetInterruptStatusReg(0);

            // 0766042 mask is 0x83FC (bits 2-9, 15).
            bi.Write(0766042, 0xFFFFFFFF);
            Assert(ucode.InterruptStatusReg == 0x83FC,
                $"0766042 write masks to 0x83FC, got 0x{ucode.InterruptStatusReg:X}");

            // Confirm the two masks are disjoint (no accidental overlap
            // corrupting the other register's bits) and their union is
            // exactly what a combined write would produce.
            ucode.SetInterruptStatusReg(0);
            bi.Write(0766040, 0xFFFFFFFF);
            bi.Write(0766042, 0xFFFFFFFF);
            Assert(ucode.InterruptStatusReg == (0x3C01 | 0x83FC),
                $"combined writes == 0x{(0x3C01 | 0x83FC):X}, got 0x{ucode.InterruptStatusReg:X}");

            Console.WriteLine("  Interrupt-status-reg write-mask tests passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Interrupt-status-reg write-mask tests failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBusStatusReadWriteRegister()
    {
        Console.WriteLine("Test: 0766044 read returns bus_error_status; write clears it");
        try
        {
            var bi = new BusInterface(new UCode(new MainMemory()));

            bi.SetXbusNxm();
            bi.SetUnibusMapError();
            Assert(bi.Read(0766044) == (0x1 | 0x20),
                $"0766044 read == 0x{(0x1 | 0x20):X}, got 0x{bi.Read(0766044):X}");

            bi.Write(0766044, 0); // value written is ignored; write always clears
            Assert(bi.GetBusErrorStatus() == 0, "0766044 write clears bus_error_status");

            Console.WriteLine("  Bus-status register test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Bus-status register test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDebuggeeStatusAlwaysZero()
    {
        Console.WriteLine("Test: 0766104 always reads 0 (no debuggee ever attached)");
        try
        {
            var bi = new BusInterface(new UCode(new MainMemory()));
            Assert(bi.Read(0766104) == 0, "0766104 reads 0");

            Console.WriteLine("  Debuggee-status test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Debuggee-status test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestLashupOnlyRegistersNoOp()
    {
        Console.WriteLine("Test: lashup-only registers (0766100/102/110/112/114) are non-fatal no-ops");
        try
        {
            var bi = new BusInterface(new UCode(new MainMemory()));

            Assert(bi.Read(0766100) == 0, "0766100 read is a safe default (0)");
            bi.Write(0766100, 0x1234);
            bi.Write(0766102, 0x1234);
            bi.Write(0766110, 0x1234);
            bi.Write(0766112, 0x1234);
            bi.Write(0766114, 0x1234);

            Console.WriteLine("  Lashup-only-registers test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Lashup-only-registers test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBusResetClearsState()
    {
        Console.WriteLine("Test: BusReset clears all local state");
        try
        {
            var bi = new BusInterface(new UCode(new MainMemory()));

            bi.SetNxmInhibit(true);
            bi.SetUnibusMapError(); // not gated by inhibit, so this sets bus_error_status
            bi.Write(0766110, 0x1); // addr17 = true
            bi.Write(0766114, 0x42); // addr = 0x42

            bi.BusReset();

            Assert(bi.GetBusErrorStatus() == 0, "bus_error_status cleared");
            bi.SetXbusNxm();
            Assert(bi.IsXbusNxm(), "nxm_inhibited cleared (xbus nxm now takes effect)");

            Console.WriteLine("  BusReset test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  BusReset test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDefaultCaseSetsUnibusNxm()
    {
        Console.WriteLine("Test: an unrecognized uaddr sets Unibus NXM on both read and write, without throwing");
        try
        {
            var bi = new BusInterface(new UCode(new MainMemory()));

            Assert(bi.Read(0766200) == 0, "unrecognized read returns 0");
            Assert(bi.IsUnibusNxm(), "unrecognized read sets unibus nxm");

            bi.ResetBusErrorStatus();
            bi.Write(0766200, 0x1234);
            Assert(bi.IsUnibusNxm(), "unrecognized write sets unibus nxm");

            Console.WriteLine("  Default-case test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Default-case test failed: {ex.Message}\n");
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

- [ ] **Step 3: Wire `BusInterfaceTests` into `Program.cs`'s CLI**

This suite only depends on `BusInterface`, `UCode`, and `MainMemory` (all
already existing before this task), so it can be wired for real now rather
than run through a scratch harness. In `usim-cs/Program.cs`, add a CLI case
(mirroring the existing `--test-bus-adaptor` case at `Program.cs:179-182`):

```csharp
                case "--test-bus-interface":
                    BusInterfaceTests.RunAllTests();
                    Environment.Exit(0);
                    break;
```

Add a usage-help line (mirroring `Program.cs:267`):

```csharp
        Console.WriteLine("  --test-bus-interface    Run bus interface tests only");
```

Add to `RunAllTests()` (mirroring the `BusAdaptorTests` block at
`Program.cs:527-530`):

```csharp
        // Run bus interface tests
        Console.WriteLine("Running Bus Interface Tests...\n");
        BusInterfaceTests.RunAllTests();
        Console.WriteLine();
```

- [ ] **Step 4: Build and run the new suite**

Run: `dotnet build usim-cs`
Expected: builds clean.

Run: `dotnet run --project usim-cs -- --test-bus-interface`
Expected: `Passed: 9`, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/BusInterface.cs usim-cs/BusInterfaceTests.cs usim-cs/Program.cs
git commit -m "Add faithful BusInterface port (bus-error-status + interrupt-register block)

Standalone class + tests, wired into --test-bus-interface/--test-all --
not yet consumed by BusAdaptor/UCode/MachineControl (Task 2)."
```

---

### Task 2: Wire `BusInterface` into `BusAdaptor`, `UCode`, `MachineControl`

**Files:**
- Modify: `usim-cs/UCode.cs:36-47` (constructor), `usim-cs/UCode.cs`'s `MfWrite` case 2 (bit 28)
- Modify: `usim-cs/BusAdaptor.cs` (constructor, field, `ReadUnibus`/`WriteUnibus`, `DescribeUnibus`)
- Modify: `usim-cs/MachineControl.cs`'s `PowerOn`
- Modify: `usim-cs/BusAdaptorTests.cs` (every `new BusAdaptor()` call site)
- Test: `usim-cs/MachineControlTests.cs` (new test), `usim-cs/UCodeMRegisterTests.cs` (strengthen existing bit-28 check)

**Interfaces:**
- Consumes: `BusInterface` from Task 1 (constructor, `Read`/`Write`/`BusReset`, `GetBusErrorStatus`).
- Produces: `UCode.BusInterface` (public property, mirrors existing `UCode.BusAdaptor`); `BusAdaptor(BusInterface busInterface)` constructor.

- [ ] **Step 1: Wire `BusInterface` into `UCode`'s constructor**

In `usim-cs/UCode.cs`, change:

```csharp
    private readonly MainMemory _mainMemory;
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

to:

```csharp
    private readonly MainMemory _mainMemory;
    public Uvmem Uvmem { get; }
    public BusInterface BusInterface { get; }
    public BusAdaptor BusAdaptor { get; }

    public UCode() : this(new MainMemory()) { }

    public UCode(MainMemory mainMemory)
    {
        _mainMemory = mainMemory;
        Uvmem = new Uvmem();
        BusInterface = new BusInterface(this);
        BusAdaptor = new BusAdaptor(BusInterface);
    }
```

(Passing `this` before the constructor body finishes is safe here: `BusInterface`'s constructor only stores the reference in a field, it never calls back into `UCode` members during construction.)

- [ ] **Step 2: Wire `BusReset()` into `MfWrite`'s bit-28 case**

In `usim-cs/UCode.cs`, inside `MfWrite`, change:

```csharp
            case 2:
                InterruptControl = udata;
                if ((InterruptControl & (1 << 28)) != 0)
                {
                    TraceLog.Instance.Info(TraceCategory.MicroCode, "usim: ic.bus reset");
                }
                Lc = (Lc & ~(0xFu << 26)) | (InterruptControl & (0xFu << 26));
                return;
```

to:

```csharp
            case 2:
                InterruptControl = udata;
                if ((InterruptControl & (1 << 28)) != 0)
                {
                    TraceLog.Instance.Info(TraceCategory.MicroCode, "usim: ic.bus reset");
                    BusInterface.BusReset();
                }
                Lc = (Lc & ~(0xFu << 26)) | (InterruptControl & (0xFu << 26));
                return;
```

- [ ] **Step 3: Wire `BusInterface` into `BusAdaptor`**

In `usim-cs/BusAdaptor.cs`, add a field and constructor (there is currently no constructor at all -- the class relies on the implicit parameterless one):

```csharp
public class BusAdaptor
{
    private readonly BusInterface _busInterface;

    public BusAdaptor(BusInterface busInterface)
    {
        _busInterface = busInterface;
    }

    // ... existing constants unchanged ...
```

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
        TraceLog.Instance.Warning(TraceCategory.Memory,
            $"BusAdaptor: write un-ported Unibus uaddr 0x{uaddr:X} v=0x{v:X} ({DescribeUnibus(uaddr)} -- not implemented, Phase 5B scope)");
    }
```

In `DescribeUnibus`, remove the now-unreachable bus-interface branch (that range no longer falls through to this fallback path at all):

```csharp
        if (uaddr >= BusInterfaceLo && uaddr <= BusInterfaceHi) return "bus-interface (already separately deferred, Phase 4)";
```

(delete this line entirely from `DescribeUnibus`).

- [ ] **Step 4: Wire `BusReset()` into `MachineControl.PowerOn`**

In `usim-cs/MachineControl.cs`, inside `PowerOn`, immediately after the `InitializeComponents();` call, add:

```csharp
        UCode.BusInterface.BusReset();
```

- [ ] **Step 5: Fix every `new BusAdaptor()` call site in `BusAdaptorTests.cs`**

`BusAdaptor` now requires a `BusInterface` constructor argument. In `usim-cs/BusAdaptorTests.cs`, every occurrence of:

```csharp
            var busAdaptor = new BusAdaptor();
```

becomes:

```csharp
            var busAdaptor = new BusAdaptor(new BusInterface(new UCode(new MainMemory())));
```

There are 5 occurrences (`TestDiskControlStatusRead`, `TestDiskControlOtherOffsetsAndWrites`, `TestDiagnosticModeRegisterWrite`, `TestDiagnosticOtherRegistersNoThrow`, `TestPlaceholderPathsDoNotThrow`) -- update all 5.

- [ ] **Step 6: Add a `BusAdaptorTests.cs` case confirming real dispatch**

Add a new test method and register it in `RunAllTests`:

```csharp
    private static bool TestBusInterfaceRangeDispatchesForReal()
    {
        Console.WriteLine("Test: 0766040-range Unibus addresses dispatch to BusInterface, not the generic fallback");
        try
        {
            var busAdaptor = new BusAdaptor(new BusInterface(new UCode(new MainMemory())));
            bool promDisabled = false;

            // 0766044 (bus status register): write clears status, read
            // reflects it. If this still hit the old generic fallback, the
            // write would be a no-op warning and the read would always
            // return 0 -- true today regardless, so additionally confirm
            // NO Unibus-NXM side effect occurs (the old fallback path,
            // still reachable for genuinely unmapped addresses, always
            // asserts NXM; real bus-interface register access must not).
            busAdaptor.Write(UaddrToPaddr(0x3EC24), 0, ref promDisabled); // 0766044 octal
            uint status = busAdaptor.Read(UaddrToPaddr(0x3EC24)); // 0766044 octal
            Assert(status == 0, $"0766044 reads back 0 after clear, got 0x{status:X}");

            Console.WriteLine("  Bus-interface real-dispatch test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Bus-interface real-dispatch test failed: {ex.Message}\n");
            return false;
        }
    }
```

Register it in `RunAllTests()` alongside the other `if (TestX()) passed++; else failed++;` lines.

- [ ] **Step 7: Add a `MachineControlTests.cs` case confirming `PowerOn` calls `BusReset`**

Add a new test method (follow this file's existing pattern of `new MachineControl(); mc.PowerOn(BootMode.Cold);`):

```csharp
    private static bool TestPowerOnCallsBusReset()
    {
        Console.WriteLine("Test: PowerOn() resets the bus interface's error status");
        try
        {
            var mc = new MachineControl();
            mc.UCode.BusInterface.SetXbusNxm(); // dirty the state before PowerOn

            mc.PowerOn(BootMode.Cold);

            Assert(mc.UCode.BusInterface.GetBusErrorStatus() == 0,
                "bus_error_status is 0 after PowerOn (BusReset was called)");

            Console.WriteLine("  PowerOn-calls-BusReset test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  PowerOn-calls-BusReset test failed: {ex.Message}\n");
            return false;
        }
    }
```

Register it in `RunAllTests()`.

- [ ] **Step 8: Strengthen the existing bit-28 check in `TestMfWriteInterruptControl`**

`UCodeMRegisterTests.cs:285-310`'s `TestMfWriteInterruptControl` already exercises bit 28, but only asserts it "does not throw" -- written before `BusReset()` existed to call. Change:

```csharp
            // Bit 28 (bus reset) does not throw -- it's a deferred, different-subsystem no-op.
            ucode.MfWrite(2 << 5, unchecked((int)(1u << 28)));
            Assert(true, "code2 bit28 (bus reset) does not throw");
```

to:

```csharp
            // Bit 28 (bus reset) now calls the real BusInterface.BusReset().
            ucode.BusInterface.SetXbusNxm(); // dirty the state so the reset is observable
            ucode.MfWrite(2 << 5, unchecked((int)(1u << 28)));
            Assert(ucode.BusInterface.GetBusErrorStatus() == 0,
                "code2 bit28 calls BusInterface.BusReset(), clearing bus_error_status");
```

No new test method or `RunAllTests()` registration needed -- this strengthens an existing, already-registered test in place.

- [ ] **Step 9: Build and run the full suite**

Run: `dotnet build usim-cs`
Expected: builds clean, no warnings about unused `_busInterface`/`BusInterface` fields.

Run: `dotnet run --project usim-cs -- --test-all`
Expected: every existing suite still passes (in particular `BusAdaptorTests`, `MachineControlTests`, and `UCodeMRegisterTests`, all touched this task) plus the new `BusInterfaceTests` suite, all green.

- [ ] **Step 10: Commit**

```bash
git add usim-cs/UCode.cs usim-cs/BusAdaptor.cs usim-cs/MachineControl.cs usim-cs/BusAdaptorTests.cs usim-cs/MachineControlTests.cs usim-cs/UCodeMRegisterTests.cs
git commit -m "Wire BusInterface into BusAdaptor/UCode/MachineControl

Real dispatch for the 0766040-0766136 octal register range, replacing
the generic unmapped-Unibus fallback. BusReset() now runs on power-on
and on the microcode-driven bit-28 bus-reset instruction."
```
