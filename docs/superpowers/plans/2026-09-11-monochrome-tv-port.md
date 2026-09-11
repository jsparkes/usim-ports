# Monochrome TV Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the entirely-invented `Display.cs` with a faithful port of `usim/tv.c`, fixing a confirmed backwards bit-unpacking bug and adding the real register model.

**Architecture:** Three tasks, ordered by dependency: (1) `Tv.cs` — the class itself, fully standalone. (2) Config — a real `monitor` value wired through `Program.ApplyConfiguration`. (3) Wiring — `BusAdaptor`, `MachineControl`, `WpfBackend`, `Mouse`, deleting `Display.cs`, and fixing every test that referenced it. Task 3 must land as one commit: `Display.cs` has no name collision with `Tv.cs` (unlike the disk port's `DiskUnit` collision), but every one of its five consumers breaks the moment it's deleted, so a partial commit would leave the build broken.

**Tech Stack:** C#, .NET 8.0, no new dependencies.

**Spec:** `docs/superpowers/specs/2026-09-11-monochrome-tv-design.md`

## Global Constraints

- No new NuGet dependencies.
- Every octal literal in this plan's code was independently verified — see the spec's Octal Literals table. Use the hex forms given.
- Dirty-rect tracking (`tv_update_screen`/`accumulate_update`), `the_60_cycle_clock`, and `tv_save_screenshot`/`tv_poll` are not ported — see spec's Decisions/Out of Scope.
- **One deliberate, documented deviation from the literal real C:** `tv_reset()`'s own foreground/background values (`0x000000`/`0xffffff`, no alpha channel) differ from the file-scope static initializers used everywhere else in the real C (`BLACK`/`WHITE` = `0xff000000`/`0xffffffff`, full alpha). Reproducing `tv_reset`'s literal zero-alpha values would render the WPF-backed screen as fully transparent immediately after any reset, for no behavioral benefit — this is treated as an artifact of the real C's conditional-compilation structure for a rendering backend WPF doesn't share, not a data/register-correctness bug worth preserving. `Reset()` in this port uses the same full-alpha `BLACK`/`WHITE` values as every other code path. This is a rendering-color choice, not a register-semantics change — nothing about the mode register, interrupt bits, or screen-buffer contents differs from the real C.
- `Mouse.MaxX`/`MaxY`'s default values become hardcoded (`768`/`896`, the "cpt" monitor's real dimensions) rather than referencing any `Display`/`Tv` class — `Mouse` must not gain a dependency on `Tv` just to pick a reasonable standalone default. `MachineControl` overrides both to the actual configured `Tv.Width`/`Height` after both are constructed.

---

### Task 1: `Tv.cs` — the class itself, fully standalone

**Files:**
- Create: `usim-cs/Tv.cs`
- Test: `usim-cs/TvTests.cs` (new)
- Modify: `usim-cs/Program.cs` (CLI wiring for the new test suite)

**Interfaces:**
- Consumes: `UCode.AssertXbusInterrupt()` (already real, from Phase 8), `UCode.BusInterface.SetXbusNxm()` (already real, from the bus-interface port).
- Produces (for Task 3): `public Tv(UCode ucode, uint width, uint height)`, `public uint Width { get; }`, `public uint Height { get; }`, `public byte[] FrameBuffer { get; }`, `public void Reset()`, `public uint ScreenRead(uint offset)`, `public void ScreenWrite(uint offset, uint v)`, `public uint ControlRead(uint offset)`, `public void ControlWrite(uint offset, uint v)`, `public void Tick()`.

This task does not touch `BusAdaptor.cs`, `MachineControl.cs`, `WpfBackend.cs`, `Mouse.cs`, or `Display.cs` — fully testable standalone.

- [ ] **Step 1: Write `usim-cs/Tv.cs`**

```csharp
// Tv.cs - Faithful port of usim/tv.c (monochrome TV display). Replaces the
// entirely-invented Display.cs. See
// docs/superpowers/specs/2026-09-11-monochrome-tv-design.md.

using System;

namespace Usim;

public class Tv
{
    private const int MaxWords = 0x8000; // tv_screen_buffer's real allocated size (32K words)
    private const uint Black = 0xFF000000u; // usim/tv.c's BLACK
    private const uint White = 0xFFFFFFFFu; // usim/tv.c's WHITE

    public uint Width { get; }
    public uint Height { get; }

    // BGRA32, sized to the FULL possible screen-buffer address range
    // (MaxWords*32 pixels), not Width*Height -- matches the real C's own
    // oversized tv_bitmap[1024*1024] (== MaxWords*32 exactly). See spec's
    // Decisions for why this must not be sized to Width*Height instead.
    public byte[] FrameBuffer { get; } = new byte[MaxWords * 32 * 4];

    private readonly UCode _ucode;
    private readonly uint[] _screenBuffer = new uint[MaxWords];
    private readonly byte[] _syncRam = new byte[4096];
    private uint _mode;
    private uint _syncPtr;
    private uint _vertSpacing;
    private uint _foreground;
    private uint _background;

    public Tv(UCode ucode, uint width, uint height)
    {
        _ucode = ucode;
        Width = width;
        Height = height;
        Reset();
    }

    /// <summary>
    /// Faithful port of tv_reset (usim/tv.c:201-212), with one deliberate
    /// deviation: uses the full-alpha Black/White constants here too,
    /// instead of the real C's literal zero-alpha 0x000000/0xffffff for
    /// this function specifically -- see this plan's Global Constraints.
    /// </summary>
    public void Reset()
    {
        _mode = 0;
        Array.Clear(_screenBuffer);
        _background = Black;
        _foreground = White;
        Array.Clear(FrameBuffer);
    }

    /// <summary>Faithful port of tv_screen_read (usim/tv.c:214-218).</summary>
    public uint ScreenRead(uint offset) => _screenBuffer[offset];

    /// <summary>
    /// Faithful port of tv_screen_write (usim/tv.c:220-246). Unpacks
    /// LSB-first -- bit 0 of v is pixel offset*32+0, bit 1 is pixel
    /// offset*32+1, etc. This is the exact bit order this port fixes; the
    /// previous invented Display.cs unpacked MSB-first.
    /// </summary>
    public void ScreenWrite(uint offset, uint v)
    {
        _screenBuffer[offset] = v;

        uint pixelBase = offset * 32;
        uint word = v;
        for (int i = 0; i < 32; i++)
        {
            uint color = (word & 1) != 0 ? _foreground : _background;
            int byteIndex = (int)((pixelBase + (uint)i) * 4);
            FrameBuffer[byteIndex + 0] = (byte)(color & 0xFF);         // B
            FrameBuffer[byteIndex + 1] = (byte)((color >> 8) & 0xFF);  // G
            FrameBuffer[byteIndex + 2] = (byte)((color >> 16) & 0xFF); // R
            FrameBuffer[byteIndex + 3] = (byte)((color >> 24) & 0xFF); // A
            word >>= 1;
        }
    }

    /// <summary>
    /// Faithful port of tv_control_read (usim/tv.c:251-281). Offsets 2/3
    /// are write-only in the real C (no case for them in the read switch)
    /// -- they correctly fall to default here too, not a separate check.
    /// </summary>
    public uint ControlRead(uint offset)
    {
        switch (offset)
        {
            case 0:
                return _mode;

            case 1:
                return IsSyncPromEnabled() ? 0u : _syncRam[_syncPtr];

            default:
                TraceLog.Instance.Warning(TraceCategory.Display,
                    $"tv: read invalid offset:{offset}");
                _ucode.BusInterface.SetXbusNxm();
                return 0;
        }
    }

    /// <summary>Faithful port of tv_control_write (usim/tv.c:283-335).</summary>
    public void ControlWrite(uint offset, uint v)
    {
        switch (offset)
        {
            case 0:
                {
                    bool wasBow = IsBlackOnWhite();
                    _mode = v;
                    bool isBow = IsBlackOnWhite();
                    // Recomputed unconditionally on every mode write,
                    // matching the real C exactly -- not gated on the BOW
                    // bit actually changing (see spec's Register Model).
                    _foreground = isBow ? Black : White;
                    _background = isBow ? White : Black;
                    if (wasBow != isBow)
                    {
                        uint wordCount = (Width * Height) / 32;
                        for (uint i = 0; i < wordCount; i++)
                        {
                            ScreenWrite(i, ScreenRead(i));
                        }
                    }
                }
                break;

            case 1:
                if (!IsSyncPromEnabled())
                {
                    _syncRam[_syncPtr] = (byte)(v & 0xFF);
                }
                break;

            case 2:
                _syncPtr = v & 0x0FFF;
                break;

            case 3:
                _vertSpacing = v;
                break;

            default:
                TraceLog.Instance.Warning(TraceCategory.Display,
                    $"tv: write invalid offset:{offset}");
                _ucode.BusInterface.SetXbusNxm();
                break;
        }
    }

    /// <summary>
    /// Faithful port of tv_assert_interrupt (usim/tv.c:158-167). Called
    /// once per ~16ms tick from both WpfBackend.Tick() and
    /// MachineControl.Run()'s headless loop (Task 3) -- matches the real
    /// C's run_at_60hz() cadence. Does NOT re-render anything; screen
    /// writes already keep FrameBuffer current (see ScreenWrite above).
    /// </summary>
    public void Tick()
    {
        if (IsInterruptEnabled())
        {
            _mode |= 1u << 4; // VERT FLAG / interrupt-request bit
            _ucode.AssertXbusInterrupt();
        }
    }

    private bool IsBlackOnWhite() => (_mode & 0x4) != 0;
    private bool IsInterruptEnabled() => (_mode & 0x8) != 0;
    private bool IsSyncPromEnabled() => (_vertSpacing & 0x80) == 0;
}
```

- [ ] **Step 2: Write `usim-cs/TvTests.cs`**

```csharp
// TvTests.cs - Tests for the faithful Tv port (usim/tv.c).

using System;

namespace Usim;

public static class TvTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== Tv Test Suite ===\n");

        int passed = 0;
        int failed = 0;

        if (TestScreenWriteUnpacksLsbFirst()) passed++; else failed++;
        if (TestBowToggleTriggersFullRepaint()) passed++; else failed++;
        if (TestControlRegisterRoundTrips()) passed++; else failed++;
        if (TestSyncPromEnabledGating()) passed++; else failed++;
        if (TestTickAssertsInterruptOnlyWhenEnabled()) passed++; else failed++;
        if (TestDefaultCaseSetsXbusNxm()) passed++; else failed++;
        if (TestWriteBeyondVisibleScreenDoesNotThrow()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static Tv MakeTv() => new Tv(new UCode(new MainMemory()), 768, 896);

    private static bool TestScreenWriteUnpacksLsbFirst()
    {
        Console.WriteLine("Test: ScreenWrite unpacks bits LSB-first (the bug this port fixes)");
        try
        {
            var tv = MakeTv();

            // Bit 0 set, bit 1 clear. Pixel 0 must be foreground (bit 0),
            // pixel 1 must be background (bit 1) -- the OLD invented code
            // would have put bit 0 at pixel 31, not pixel 0.
            tv.ScreenWrite(0, 0x00000001);

            Assert(tv.FrameBuffer[0] == 0xFF && tv.FrameBuffer[1] == 0xFF &&
                   tv.FrameBuffer[2] == 0xFF && tv.FrameBuffer[3] == 0xFF,
                $"pixel 0 (bit 0, set) is foreground/white, got B={tv.FrameBuffer[0]:X2} G={tv.FrameBuffer[1]:X2} R={tv.FrameBuffer[2]:X2} A={tv.FrameBuffer[3]:X2}");

            Assert(tv.FrameBuffer[4] == 0x00 && tv.FrameBuffer[5] == 0x00 &&
                   tv.FrameBuffer[6] == 0x00 && tv.FrameBuffer[7] == 0xFF,
                $"pixel 1 (bit 1, clear) is background/black, got B={tv.FrameBuffer[4]:X2} G={tv.FrameBuffer[5]:X2} R={tv.FrameBuffer[6]:X2} A={tv.FrameBuffer[7]:X2}");

            Console.WriteLine("  LSB-first unpacking test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  LSB-first unpacking test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBowToggleTriggersFullRepaint()
    {
        Console.WriteLine("Test: a BOW-bit transition triggers a full-screen repaint with the new colors");
        try
        {
            var tv = MakeTv();

            tv.ScreenWrite(0, 0x00000001); // bit 0 set -> pixel 0 = white (default, BOW=0)
            Assert(tv.FrameBuffer[0] == 0xFF, "pixel 0 is white before the BOW toggle");

            tv.ControlWrite(0, 0x4); // set BOW bit (mode = 0x4) -- transition 0 -> 1

            // BOW=1 means foreground=Black. The repaint re-applies ScreenWrite
            // for every word, so pixel 0 (bit 0, still set in the stored word)
            // must now be black -- with NO additional ScreenWrite call.
            Assert(tv.FrameBuffer[0] == 0x00,
                $"pixel 0 repainted to black after the BOW toggle, got B={tv.FrameBuffer[0]:X2}");

            Console.WriteLine("  BOW-toggle repaint test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  BOW-toggle repaint test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestControlRegisterRoundTrips()
    {
        Console.WriteLine("Test: control register read/write round-trips (offsets 0-3)");
        try
        {
            var tv = MakeTv();

            tv.ControlWrite(0, 0xC); // BOW + interrupt-enable
            Assert(tv.ControlRead(0) == 0xC, $"mode register round-trips, got 0x{tv.ControlRead(0):X}");

            // Offset 2 (sync pointer) and 3 (vert spacing) are write-only --
            // writing must not throw, and reading them falls to the default
            // (NXM) case, not a dedicated read path.
            tv.ControlWrite(2, 0x123);
            tv.ControlWrite(3, 0x00); // sync PROM enabled (bit 7 clear)

            // Offset 1 (sync data) with sync PROM enabled reads back 0.
            Assert(tv.ControlRead(1) == 0, "sync data reads 0 while sync PROM is enabled");

            Console.WriteLine("  Control-register round-trip test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Control-register round-trip test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestSyncPromEnabledGating()
    {
        Console.WriteLine("Test: sync-RAM read/write only takes effect when sync PROM is disabled (vert-spacing bit 7 set)");
        try
        {
            var tv = MakeTv();

            tv.ControlWrite(3, 0x80); // sync PROM DISABLED (bit 7 set)
            tv.ControlWrite(2, 0x05); // sync pointer = 5
            tv.ControlWrite(1, 0x42); // write sync data

            Assert(tv.ControlRead(1) == 0x42, $"sync RAM round-trips when sync PROM is disabled, got 0x{tv.ControlRead(1):X}");

            tv.ControlWrite(3, 0x00); // sync PROM ENABLED (bit 7 clear)
            Assert(tv.ControlRead(1) == 0, "sync data always reads 0 while sync PROM is enabled, even with prior data stored");

            Console.WriteLine("  Sync-PROM-enabled gating test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Sync-PROM-enabled gating test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestTickAssertsInterruptOnlyWhenEnabled()
    {
        Console.WriteLine("Test: Tick() asserts the real Xbus interrupt only when interrupt-enable is set");
        try
        {
            var ucode = new UCode(new MainMemory());
            var tv = new Tv(ucode, 768, 896);

            tv.Tick();
            Assert(!ucode.InterruptPendingFlag, "no interrupt asserted while interrupt-enable is clear");

            tv.ControlWrite(0, 0x8); // interrupt-enable bit set
            tv.Tick();
            Assert(ucode.InterruptPendingFlag, "interrupt asserted once interrupt-enable is set");

            Console.WriteLine("  Tick-interrupt-gating test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Tick-interrupt-gating test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDefaultCaseSetsXbusNxm()
    {
        Console.WriteLine("Test: an unrecognized control-register offset sets Xbus NXM, doesn't throw");
        try
        {
            var ucode = new UCode(new MainMemory());
            var tv = new Tv(ucode, 768, 896);

            Assert(tv.ControlRead(4) == 0, "unrecognized read returns 0");
            Assert(ucode.BusInterface.IsXbusNxm(), "unrecognized read sets Xbus NXM");

            ucode.BusInterface.ResetBusErrorStatus();
            tv.ControlWrite(4, 0x1234);
            Assert(ucode.BusInterface.IsXbusNxm(), "unrecognized write sets Xbus NXM");

            Console.WriteLine("  Default-case test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Default-case test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestWriteBeyondVisibleScreenDoesNotThrow()
    {
        Console.WriteLine("Test: writing at a screen-buffer offset beyond the visible geometry doesn't throw");
        try
        {
            var tv = MakeTv(); // 768x896 -> visible word count = 768*896/32 = 21504

            // An offset well beyond the visible screen but still within the
            // real 0x8000-word address range -- must not throw, exercising
            // the oversized FrameBuffer this spec's Decisions section
            // requires (a FrameBuffer sized to Width*Height*4 would throw here).
            tv.ScreenWrite(30000, 0xFFFFFFFF);
            Assert(tv.ScreenRead(30000) == 0xFFFFFFFF, "the write is still stored and reads back correctly");

            Console.WriteLine("  Write-beyond-visible-screen test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Write-beyond-visible-screen test failed: {ex.Message}\n");
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

- [ ] **Step 3: Wire `TvTests` into `Program.cs`**

Add a CLI case, next to the existing `case "--test-bus-interface": BusInterfaceTests.RunAllTests(); break;` (around line 185):

```csharp
                case "--test-tv":
                    TvTests.RunAllTests();
                    break;
```

Add a usage-help line, next to the existing `Console.WriteLine("  --test-bus-interface    Run bus interface tests only");` (around line 289):

```csharp
        Console.WriteLine("  --test-tv               Run TV tests only");
```

Add a call into the `--test-all` aggregator, next to the existing `BusInterfaceTests.RunAllTests();` block (around line 583-586):

```csharp
        // Run TV tests
        Console.WriteLine("Running TV Tests...\n");
        TvTests.RunAllTests();
        Console.WriteLine();
```

- [ ] **Step 4: Build and test**

Run: `dotnet build usim-cs` then `dotnet run --project usim-cs -- --test-tv`
Expected: builds clean, `Passed: 7`, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/Tv.cs usim-cs/TvTests.cs usim-cs/Program.cs
git commit -m "Add faithful Tv port (usim/tv.c)

Standalone class + tests only -- not yet wired into BusAdaptor/
MachineControl/WpfBackend, and Display.cs is not yet deleted (a later
task)."
```

---

### Task 2: Real `monitor` config, wired through the live config path

**Files:**
- Modify: `usim-cs/UsimConstants.cs` (add `UsimState.TvWidth`/`TvHeight`)
- Modify: `usim-cs/Program.cs` (`ApplyConfiguration` gains a `monitor` read)
- Test: `usim-cs/ConfigTests.cs`

**Interfaces:**
- Produces (for Task 3): `UsimState.TvWidth` / `UsimState.TvHeight` (`uint`, defaulting to the "cpt" geometry: 768/896).

- [ ] **Step 1: Add `UsimState.TvWidth`/`TvHeight`**

In `usim-cs/UsimConstants.cs`, inside the `UsimState` static class, add:

```csharp
    public static uint TvWidth { get; set; } = 768;
    public static uint TvHeight { get; set; } = 896;
```

- [ ] **Step 2: Parse the real `monitor` value in `Program.cs`'s `ApplyConfiguration`**

Add, inside `ApplyConfiguration`, after its existing lines (before the disk-units loop is fine, or after — placement doesn't matter, just don't interleave with the disk loop's own lines):

```csharp
        string monitor = config.GetString("usim", "monitor", "cpt");
        switch (monitor)
        {
            case "cpt":
                UsimState.TvWidth = 768;
                UsimState.TvHeight = 896;
                break;
            case "other":
                UsimState.TvWidth = 768;
                UsimState.TvHeight = 963;
                break;
            default:
                Console.WriteLine($"Warning: unknown monitor type '{monitor}', using cpt");
                UsimState.TvWidth = 768;
                UsimState.TvHeight = 896;
                break;
        }
```

(Matches `usim/ucfg.c`'s real string values and its real warn-and-fall-back-to-default behavior for an unrecognized value — see this plan's spec, Wiring point 2, for why this must NOT be a fatal error.)

- [ ] **Step 3: Add a test to `ConfigTests.cs`**

```csharp
private static bool TestApplyConfigurationParsesMonitorType()
{
    Console.WriteLine("Test: ApplyConfiguration parses the monitor type into UsimState.TvWidth/TvHeight");
    // UsimState.TvWidth/TvHeight are process-wide static state, same as
    // UsimState.DiskUnits above -- save/restore so this test can't leak
    // into any test that runs after it in the same process.
    var savedWidth = UsimState.TvWidth;
    var savedHeight = UsimState.TvHeight;
    try
    {
        var parser = new ConfigParser();
        parser.SetString("usim", "monitor", "other");

        Program.ApplyConfiguration(parser);

        Assert(UsimState.TvWidth == 768, $"TvWidth is 768 for 'other', got {UsimState.TvWidth}");
        Assert(UsimState.TvHeight == 963, $"TvHeight is 963 for 'other', got {UsimState.TvHeight}");

        var parser2 = new ConfigParser();
        parser2.SetString("usim", "monitor", "not-a-real-monitor");
        Program.ApplyConfiguration(parser2);

        Assert(UsimState.TvWidth == 768 && UsimState.TvHeight == 896,
            $"an unrecognized monitor value falls back to cpt's dimensions (768x896), got {UsimState.TvWidth}x{UsimState.TvHeight}");

        Console.WriteLine("  ApplyConfiguration monitor-type test passed\n");
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ApplyConfiguration monitor-type test failed: {ex.Message}\n");
        return false;
    }
    finally
    {
        UsimState.TvWidth = savedWidth;
        UsimState.TvHeight = savedHeight;
    }
}
```

`ConfigParser.cs:132` already has `public void SetString(string section, string key, string value)` (confirmed present — no new method needed). Register the new test in `ConfigTests.cs`'s `RunAllTests()`, next to the existing `TestApplyConfigurationParsesDiskUnits` registration.

- [ ] **Step 4: Build and test**

Run: `dotnet build usim-cs` then `dotnet run --project usim-cs -- --test-config`
Expected: builds clean, all `ConfigTests` pass including the new one.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/UsimConstants.cs usim-cs/Program.cs usim-cs/ConfigTests.cs
git commit -m "Add real monitor-type config, wired through ApplyConfiguration"
```

---

### Task 3: Wire `Tv` in, delete `Display.cs`

**Files:**
- Modify: `usim-cs/BusAdaptor.cs`
- Modify: `usim-cs/MachineControl.cs`
- Modify: `usim-cs/WpfBackend.cs`
- Modify: `usim-cs/Mouse.cs`
- Modify: `usim-cs/WpfBackendTests.cs`
- Modify: `usim-cs/BusAdaptorTests.cs`
- Delete: `usim-cs/Display.cs`

**Interfaces:**
- Consumes: `Tv` (Task 1), `UsimState.TvWidth`/`TvHeight` (Task 2).

- [ ] **Step 1: Wire `Tv` into `BusAdaptor`**

In `usim-cs/BusAdaptor.cs`, add a field and a settable-once wiring method (mirroring `WireDiskController`/`_diskController` exactly):

```csharp
    private Tv? _tv;

    public void WireTv(Tv tv)
    {
        _tv = tv;
    }
```

Change `ReadXbusIo`'s `TvScreenLo`/`Hi` handling. Currently (verify exact current line numbers before editing, since they may have drifted since this plan was written):

```csharp
        // Main TV screen (XBus I/O, 0x3C0000-0x3C7FFF).
```
(a comment inside `DescribeXbusIo`, and the generic fallback catches this range today — there is no dedicated `if` branch for it yet in `ReadXbusIo`/`WriteXbusIo`, only in `DescribeXbusIo`'s description helper). Add new branches to `ReadXbusIo`/`WriteXbusIo`, placed before the generic fallback (same pattern `DiskControlLo`/`Hi` already uses):

```csharp
        if (paddr >= TvScreenLo && paddr <= TvScreenHi)
        {
            uint offset = paddr - TvScreenLo;
            if (_tv == null)
            {
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"BusAdaptor: TV screen read at offset {offset} with no Tv wired");
                return 0;
            }
            return _tv.ScreenRead(offset);
        }
        if (paddr >= TvControlLo && paddr <= TvControlHi)
        {
            uint offset = paddr - TvControlLo;
            if (_tv == null)
            {
                TraceLog.Instance.Warning(TraceCategory.Memory,
                    $"BusAdaptor: TV control read at offset {offset} with no Tv wired");
                return 0;
            }
            return _tv.ControlRead(offset);
        }
```

(add the mirror-image `WriteXbusIo` branches too, calling `_tv.ScreenWrite(offset, v)` / `_tv.ControlWrite(offset, v)` with the same null-guard-and-warn convention, `return;` instead of `return 0;`). Read the actual current `ReadXbusIo`/`WriteXbusIo` bodies first to place these correctly relative to the existing `DiskControlLo`/`Hi` and `KnownBenignOverrunPaddr` checks — do not disturb their existing order/behavior, only add these two new checks. `ColorTvScreenLo`/`Hi` and `ColorTvControlLo`/`Hi` are untouched — they still fall through to the generic placeholder.

In `DescribeXbusIo`, the `"main TV screen"`/`"main TV control"` description lines become unreachable for their respective ranges (since real dispatch now happens before the fallback) — remove those two lines from `DescribeXbusIo` (leave the color-TV description lines, which are still reachable).

- [ ] **Step 2: Wire construction order and call sites in `MachineControl.cs`**

Change the constructor from:

```csharp
    public MachineControl()
    {
        Memory = new MainMemory();
        UCode = new UCode(Memory);
        DiskController = new DiskController(Memory, UCode);
        UCode.BusAdaptor.WireDiskController(DiskController);
        Keyboard = new Keyboard();
        Mouse = new Mouse();
        Display = new Display();
        IOBus = new IOBus();

        State = PowerState.Off;
        IsStopped = true;
    }
```

to:

```csharp
    public MachineControl()
    {
        Memory = new MainMemory();
        UCode = new UCode(Memory);
        DiskController = new DiskController(Memory, UCode);
        UCode.BusAdaptor.WireDiskController(DiskController);
        Keyboard = new Keyboard();
        Mouse = new Mouse();
        Tv = new Tv(UCode, UsimState.TvWidth, UsimState.TvHeight);
        UCode.BusAdaptor.WireTv(Tv);
        Mouse.MaxX = (int)Tv.Width;
        Mouse.MaxY = (int)Tv.Height;
        IOBus = new IOBus();

        State = PowerState.Off;
        IsStopped = true;
    }
```

Change the property declaration from `public Display Display { get; private set; }` to `public Tv Tv { get; private set; }`.

Change every `Display.Initialize()` call site (in both `Reset()` and `InitializeComponents()`) to `Tv.Reset()`.

Change `InitializeDisplay`'s `DisplayBackend = new WpfBackend(Display, Keyboard, Mouse, onTick: RunMicrocodeBatch)` to `DisplayBackend = new WpfBackend(Tv, Keyboard, Mouse, onTick: RunMicrocodeBatch)`.

In the headless branch of `Run()`, change `Display.Update();` to `Tv.Tick();`.

Change the status-print block from:

```csharp
        Console.WriteLine($"Display:    {Display.WIDTH}x{Display.HEIGHT} @ {Display.CurrentFPS:F1} FPS");
        Console.WriteLine($"Frames:     {Display.FrameCount:N0}");
```

to:

```csharp
        Console.WriteLine($"Display:    {Tv.Width}x{Tv.Height}");
```

- [ ] **Step 3: Update `WpfBackend.cs`**

Change every `Display` type reference to `Tv`: the `_display` field becomes `_tv` (type `Tv`), the constructor parameter `Display display` becomes `Tv tv` (and its null-check/assignment updates accordingly), and every `Display.WIDTH`/`Display.HEIGHT` (static) becomes `_tv.Width`/`_tv.Height` (instance) — in the `WriteableBitmap` construction, the `Image` sizing, the `Viewbox` sizing, and the `WritePixels` call's rect/stride. `_display.FrameBuffer` becomes `_tv.FrameBuffer`. The `Tick()` method's `_display.Update();` call becomes `_tv.Tick();`. `TraceCategory.Display` (a log category, unrelated to the `Display` class) is untouched everywhere it appears.

- [ ] **Step 4: Update `Mouse.cs`**

Change:

```csharp
    // Display bounds
    public int MaxX { get; set; } = Display.WIDTH;
    public int MaxY { get; set; } = Display.HEIGHT;
```

to:

```csharp
    // Display bounds -- hardcoded to the "cpt" monitor's real dimensions as
    // a reasonable standalone default; MachineControl overrides both to the
    // actual configured Tv's dimensions once both exist (see this plan's
    // Global Constraints for why Mouse itself must not depend on Tv).
    public int MaxX { get; set; } = 768;
    public int MaxY { get; set; } = 896;
```

- [ ] **Step 5: Delete `usim-cs/Display.cs`**

Delete the file entirely.

- [ ] **Step 6: Fix `WpfBackendTests.cs`**

Replace `TestDisplayByteOrder` entirely — the old test exercised the invented color-mode rendering path, which no longer exists:

```csharp
    private static bool TestTvFrameBufferByteOrder()
    {
        Console.WriteLine("Test: Tv frame buffer byte order and LSB-first bit unpacking");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var tv = new Tv(ucode, 768, 896);

            // Bit 0 set -> pixel 0 must be foreground (white, BGRA32 full
            // alpha) -- the OLD invented code's MSB-first bug would have
            // put this bit at pixel 31, not pixel 0.
            tv.ScreenWrite(0, 0x00000001);

            byte b = tv.FrameBuffer[0];
            byte g = tv.FrameBuffer[1];
            byte r = tv.FrameBuffer[2];
            byte a = tv.FrameBuffer[3];

            Assert(b == 0xFF && g == 0xFF && r == 0xFF && a == 0xFF,
                $"pixel 0 (bit 0, set) is foreground/white BGRA, got B={b:X2},G={g:X2},R={r:X2},A={a:X2}");

            Console.WriteLine("  Tv frame buffer byte order test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Tv frame buffer byte order test failed: {ex.Message}\n");
            return false;
        }
    }
```

Update `RunAllTests()`'s registration line from `if (TestDisplayByteOrder())` to `if (TestTvFrameBufferByteOrder())`.

Fix `TestMouseDefaults`'s two assertions from:

```csharp
            Assert(mouse.MaxX == Display.WIDTH, "MaxX defaults to Display.WIDTH");
            Assert(mouse.MaxY == Display.HEIGHT, "MaxY defaults to Display.HEIGHT");
```

to:

```csharp
            Assert(mouse.MaxX == 768, "MaxX defaults to 768 (the cpt monitor's real width)");
            Assert(mouse.MaxY == 896, "MaxY defaults to 896 (the cpt monitor's real height)");
```

- [ ] **Step 7: Add a real-dispatch-confirmation test to `BusAdaptorTests.cs`**

Add a test confirming the TV screen/control ranges dispatch to a real, wired `Tv` — build the same discriminating way prior ports' fix rounds required (assert on a value only the real `Tv` path can produce, not the old generic-fallback placeholder every prior test for this range relied on):

```csharp
    private static bool TestTvRangeDispatchesToRealTv()
    {
        Console.WriteLine("Test: TV screen/control ranges dispatch to a real, wired Tv, not the generic placeholder");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var busAdaptor = ucode.BusAdaptor;
            var tv = new Tv(ucode, 768, 896);
            busAdaptor.WireTv(tv);

            bool promDisabled = false;

            // Write a control-register mode value, confirm it round-trips
            // through BusAdaptor -- the old generic placeholder always
            // returned 0 regardless of what was "written" (a no-op), so a
            // nonzero round-trip only happens through the real Tv path.
            // TvControlLo = 0x3DFFF0 (offset 0, mode register); this is
            // already a physical XBus-I/O paddr, not a Unibus uaddr, so
            // (unlike the bus-interface/Unibus-Map-DMA tests elsewhere in
            // this file) no UaddrToPaddr conversion applies here.
            busAdaptor.Write(0x3DFFF0, 0xC, ref promDisabled);
            uint mode = busAdaptor.Read(0x3DFFF0);
            Assert(mode == 0xC, $"TV control register round-trips through real Tv, got 0x{mode:X}");
            Assert(promDisabled == false, "TV control writes never touch promDisabled");

            Console.WriteLine("  TV real-dispatch test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  TV real-dispatch test failed: {ex.Message}\n");
            return false;
        }
    }
```

Register the test in `RunAllTests()`.

- [ ] **Step 8: Build and run the full suite**

Run: `dotnet build usim-cs`
Expected: builds clean — in particular, confirm no remaining reference to the deleted `Display`/`DisplayMode` types anywhere in `usim-cs/` (a repo-wide grep for `\bDisplay\b` excluding `TraceCategory.Display`/`DisplayBackend`/`DisplayConfig`/`InitializeDisplay` is a reasonable sanity check before considering this step done).

Run: `dotnet run --project usim-cs -- --test-all`
Expected: every suite passes, including `BusAdaptorTests`, `MachineControlTests`, `WpfBackendTests`, `TvTests`, `ConfigTests` (all touched across this plan).

- [ ] **Step 9: Commit**

```bash
git add usim-cs/BusAdaptor.cs usim-cs/MachineControl.cs usim-cs/WpfBackend.cs usim-cs/Mouse.cs usim-cs/WpfBackendTests.cs usim-cs/BusAdaptorTests.cs
git rm usim-cs/Display.cs
git commit -m "Wire real Tv into BusAdaptor/MachineControl/WpfBackend, delete Display.cs

BusAdaptor's TV screen/control ranges now dispatch to a real, faithful
Tv instead of the generic un-ported-device placeholder. Display.cs's
entirely-invented video-memory model and backwards MSB-first bit
unpacking are gone."
```
