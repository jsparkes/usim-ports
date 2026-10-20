# TV Device Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Faithfully port `usim/tv.c`'s main (black-and-white) TV device — the real 32768-word screen buffer, its control register, and a bit-for-bit port of the real `tv_save_screenshot()`'s PBM writer — and wire it into `BusAdaptor`, proving it works by writing a synthetic pattern through the real register interface and saving a verifiable screenshot.

**Architecture:** A new, independent `Tv.cs` class (Task 1) holding the device state and logic, with no dependency on `Display.cs`/`WpfBackend.cs` or any GUI type. `BusAdaptor` constructs and wires it (Task 2), replacing the current logged-no-op fallback for the TV address ranges with real dispatch, proven by tests that go through `BusAdaptor`'s own register interface, not `Tv`'s internals directly.

**Tech Stack:** C#, .NET 8.0, no new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-20-tv-device-design.md`

## Global Constraints

- No new NuGet dependencies. PBM is plain ASCII text — no image library needed.
- `Tv.cs` must not reference `Display.cs`, `WpfBackend.cs`, or any WPF type.
- Screen buffer is exactly `32768` words (`0x8000`), matching real `tv_screen_buffer` and exactly matching `BusAdaptor`'s existing `TvScreenHi - TvScreenLo + 1`.
- `Width = 768`, `Height = 896` (the real `tv_monitor` case 0 / "CPT" monitor values) — hardware constants, not configurable in this port's scope.
- Mode register write (control offset 0) applies **no masking** — `Mode = value` wholesale, matching the real C's own unmasked behavior exactly. Do not add validation that would prevent a bare write from sticking in every bit.
- Resolved discrepancy (code wins over the C source's own comment): sync-prom-enabled state is `(vertSpacing & 0x80) != 0` — i.e. bit 7 of `tv_vert_spacing`, NOT bit 7 of the mode register despite `tv.c`'s top-of-file comment claiming otherwise.
- An invalid control-register offset (anything other than 0-3) must call `BusInterface.SetXbusNxm()` (already exists, confirmed this session), matching the real C's `bus_interface_set_xbus_nxm()` call. The real C's read path leaves `*pv` untouched on this path; C#'s `out` parameter requires a definite assignment, so assign `value = 0` and document this as the one intentional, honest deviation from the real C's pointer semantics.
- Confirmed this session: `BusAdaptor`'s constructor (`BusAdaptor(BusInterface busInterface)`) has no construction-order dependency problem for `Tv` the way `DiskController` has for `UCode` — construct `Tv` directly as a field in `BusAdaptor`, no `WireTv`-style method needed.
- `SaveScreenshotPbm` iterates exactly `Width * Height` bits (not the full 32768-word buffer) — matching the real `tv_save_screenshot`'s own loop bound. Any write past the visible `Width*Height` bit range (but still within the 32768-word buffer) must never appear in a screenshot.

## Review Focus

- **An invalid control-register offset (4 or above)** — a reasonable person expects this to behave like the real hardware's bus-error path (set Xbus NXM), not crash or silently succeed. Covered by Task 1's control-register tests.
- **Sync-RAM read/write gating by the sync-prom-enabled bit** — the spec's sync-RAM logic is only a few lines, but it's conditionally gated (reads return 0 / writes are ignored when sync-prom is enabled) — a test exercising only the default (disabled) state would miss a real bug in the gate itself. Covered by Task 1.
- **Screen buffer's last valid word (offset `0x7FFF`)** — an off-by-one in the address-range-to-offset arithmetic would only show up at the boundary, not at offset 0. Covered by Task 2's through-`BusAdaptor` round-trip test.
- **A wholesale mode-register write (all bits set) really sticks with no masking** — the spec explicitly chose no validation; a test should prove that choice is real, not just assumed. Covered by Task 1.
- **A screenshot reflects only the visible `Width*Height` bit range, not writes to the buffer's unused tail** — since the buffer (32768 words) is larger than what's needed for 768×896 bits (exactly 21504 words), a bug that iterates by buffer size instead of by `Width*Height` would corrupt or extend the screenshot. Covered by Task 2's screenshot test, which writes a recognizable pattern into the tail words too and confirms they're absent from the output.

---

### Task 1: `Tv.cs` — the device class

**Files:**
- Create: `usim-cs/Tv.cs`
- Test: `usim-cs/TvTests.cs` (new file)

**Interfaces:**
- Consumes: `BusInterface.SetXbusNxm()` (existing, `usim-cs/BusInterface.cs:50`).
- Produces (for Task 2): `public class Tv` with constructor `public Tv(BusInterface busInterface)`; `public uint[] ScreenBuffer` (`new uint[0x8000]`); `public uint Width { get; } = 768`; `public uint Height { get; } = 896`; `public uint Mode { get; private set; }`; `public void ScreenRead(uint offset, out uint value)`; `public void ScreenWrite(uint offset, uint value)`; `public void ControlRead(uint offset, out uint value)`; `public void ControlWrite(uint offset, uint value)`; `public void Reset()`; `public void SaveScreenshotPbm(string filename)`.

- [ ] **Step 1: Write the failing tests**

In a new `usim-cs/TvTests.cs`:

```csharp
// TvTests.cs - Tests for the faithful Tv port (usim/tv.c), main
// (black-and-white) TV only. Exercises Tv.cs directly -- the
// through-BusAdaptor register-interface tests live in BusAdaptorTests.cs
// (Task 2), matching this codebase's established split (e.g.
// DiskControllerTests.cs tests DiskController directly; the real-disk
// BusAdaptor-wired tests are elsewhere).

using System;
using System.IO;

namespace Usim;

public static class TvTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== Tv Test Suite ===\n");
        int passed = 0, failed = 0;

        if (TestScreenReadWriteRoundTrip()) passed++; else failed++;
        if (TestScreenBufferLastWordRoundTrip()) passed++; else failed++;
        if (TestReset()) passed++; else failed++;
        if (TestModeRegisterWriteNoMasking()) passed++; else failed++;
        if (TestSyncRamGatedByProm()) passed++; else failed++;
        if (TestInvalidControlOffsetSetsXbusNxm()) passed++; else failed++;
        if (TestSaveScreenshotPbmMatchesIndependentComputation()) passed++; else failed++;
        if (TestSaveScreenshotPbmIgnoresBufferTail()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static Tv MakeTv() => new Tv(new BusInterface(new UCode()));

    private static bool TestScreenReadWriteRoundTrip()
    {
        Console.WriteLine("Test: ScreenWrite then ScreenRead round-trips a word at offset 0");
        try
        {
            var tv = MakeTv();
            tv.ScreenWrite(0, 0xDEADBEEF);
            tv.ScreenRead(0, out uint v);
            Assert(v == 0xDEADBEEF, $"round-trip at offset 0, got 0x{v:X}");
            Console.WriteLine("  Screen-read-write round-trip test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Screen-read-write round-trip test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestScreenBufferLastWordRoundTrip()
    {
        Console.WriteLine("Test: ScreenWrite/ScreenRead round-trips the last valid word, offset 0x7FFF");
        try
        {
            var tv = MakeTv();
            tv.ScreenWrite(0x7FFF, 0x12345678);
            tv.ScreenRead(0x7FFF, out uint v);
            Assert(v == 0x12345678, $"round-trip at offset 0x7FFF, got 0x{v:X}");
            Console.WriteLine("  Last-word round-trip test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Last-word round-trip test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestReset()
    {
        Console.WriteLine("Test: Reset() clears Mode and the screen buffer");
        try
        {
            var tv = MakeTv();
            tv.ScreenWrite(5, 0xFFFFFFFF);
            tv.ControlWrite(0, 0xFF);
            tv.Reset();
            tv.ScreenRead(5, out uint v);
            Assert(v == 0, "screen buffer cleared after Reset()");
            Assert(tv.Mode == 0, "Mode cleared after Reset()");
            Console.WriteLine("  Reset test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Reset test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestModeRegisterWriteNoMasking()
    {
        Console.WriteLine("Test: ControlWrite(0, ...) applies no masking -- a wholesale write sticks in every bit");
        try
        {
            var tv = MakeTv();
            tv.ControlWrite(0, 0xFFFFFFFF);
            tv.ControlRead(0, out uint v);
            Assert(v == 0xFFFFFFFF, $"mode register holds every bit written, got 0x{v:X}");
            Console.WriteLine("  Mode-register-no-masking test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Mode-register-no-masking test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestSyncRamGatedByProm()
    {
        Console.WriteLine("Test: sync-RAM read/write (control offset 1) is gated by sync-prom-enabled (vertSpacing bit 7)");
        try
        {
            var tv = MakeTv();

            // sync-prom-enabled is FALSE by default (vertSpacing starts at 0) -- sync RAM is live.
            tv.ControlWrite(2, 0x010); // sync ptr = 0x10
            tv.ControlWrite(1, 0xAB);  // sync_ram[0x10] = 0xAB
            tv.ControlRead(1, out uint v1);
            Assert(v1 == 0xAB, $"sync RAM live when sync-prom disabled, got 0x{v1:X}");

            // Enable sync-prom (vertSpacing bit 7 set) -- sync RAM reads must return 0, writes ignored.
            tv.ControlWrite(3, 0x80); // vertSpacing bit 7 set -> sync-prom-enabled
            tv.ControlWrite(1, 0xCD); // must be ignored
            tv.ControlRead(1, out uint v2);
            Assert(v2 == 0, $"sync RAM reads 0 when sync-prom enabled, got 0x{v2:X}");

            // Disable again -- the original 0xAB at ptr 0x10 must still be there (the 0xCD write was ignored).
            tv.ControlWrite(3, 0x00);
            tv.ControlRead(1, out uint v3);
            Assert(v3 == 0xAB, $"ignored write during sync-prom-enabled didn't stick, got 0x{v3:X}");

            Console.WriteLine("  Sync-RAM-gated-by-prom test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Sync-RAM-gated-by-prom test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestInvalidControlOffsetSetsXbusNxm()
    {
        Console.WriteLine("Test: an invalid control-register offset (4) sets Xbus NXM on both read and write, without throwing");
        try
        {
            var ucode = new UCode();
            var busInterface = new BusInterface(ucode);
            var tv = new Tv(busInterface);

            Assert(!busInterface.IsXbusNxm(), "sanity: Xbus NXM starts clear");
            tv.ControlWrite(4, 0x1);
            Assert(busInterface.IsXbusNxm(), "invalid-offset write sets Xbus NXM");

            // Reset and check the read path too (SetXbusNxm's real behavior, if idempotent-reset
            // is needed, is up to BusInterface -- just confirm the read path also sets it fresh).
            tv.ControlRead(5, out uint v);
            Assert(busInterface.IsXbusNxm(), "invalid-offset read also sets Xbus NXM");
            Assert(v == 0, $"invalid-offset read assigns 0 (documented deviation from the real C's untouched-*pv), got 0x{v:X}");

            Console.WriteLine("  Invalid-control-offset-sets-NXM test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Invalid-control-offset-sets-NXM test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestSaveScreenshotPbmMatchesIndependentComputation()
    {
        Console.WriteLine("Test: SaveScreenshotPbm's output matches an independently-computed PBM for a simple pattern");
        string path = Path.GetTempFileName();
        try
        {
            var tv = MakeTv();
            // Fill the whole visible range with alternating-bit words (0xAAAAAAAA).
            uint visibleWords = tv.Width * tv.Height / 32;
            for (uint i = 0; i < visibleWords; i++) tv.ScreenWrite(i, 0xAAAAAAAA);

            tv.SaveScreenshotPbm(path);

            // Independently compute the expected PBM text -- written fresh here, not by
            // calling into Tv's own bit-extraction code.
            var expected = new System.Text.StringBuilder();
            expected.Append("P1\n").Append(tv.Width).Append(' ').Append(tv.Height).Append('\n');
            for (uint i = 0; i < tv.Width * tv.Height; i++)
            {
                uint offset = i / 32;
                int bitpos = (int)(i % 32);
                bool bit = (0xAAAAAAAAu & (1u << bitpos)) != 0;
                expected.Append(bit ? '1' : '0');
                if (i % 70 == 0 && i > 0) expected.Append('\n');
            }

            string actual = File.ReadAllText(path);
            Assert(actual == expected.ToString(), "PBM output matches independently-computed expected text exactly");

            Console.WriteLine("  Screenshot-matches-independent-computation test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Screenshot-matches-independent-computation test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static bool TestSaveScreenshotPbmIgnoresBufferTail()
    {
        Console.WriteLine("Test: SaveScreenshotPbm reflects only the visible Width*Height bits, not writes to the buffer's unused tail");
        string path = Path.GetTempFileName();
        try
        {
            var tv = MakeTv();
            uint visibleWords = tv.Width * tv.Height / 32; // 21504 -- strictly less than the 32768-word buffer
            Assert(visibleWords < 0x8000, "sanity: the visible range is smaller than the full buffer");

            // Leave the visible range all-zero; fill the UNUSED tail with all-ones.
            for (uint i = visibleWords; i < 0x8000; i++) tv.ScreenWrite(i, 0xFFFFFFFF);

            tv.SaveScreenshotPbm(path);
            string actual = File.ReadAllText(path);
            Assert(!actual.Contains('1'), "screenshot contains no set bits -- the all-ones tail must not leak into the visible output");

            Console.WriteLine("  Screenshot-ignores-buffer-tail test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  Screenshot-ignores-buffer-tail test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception($"Assertion failed: {message}");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail to compile (`Tv` doesn't exist yet)**

Run: `dotnet build usim-cs`
Expected: FAIL — `Tv` is undefined.

- [ ] **Step 3: Implement `Tv.cs`**

```csharp
// Tv.cs - Faithful port of usim/tv.c's main (black-and-white) TV device.
// Scoped to tv_monitor case 0 (the "CPT" monitor, the real C's default) --
// see docs/superpowers/specs/2026-10-20-tv-device-design.md. Deliberately
// headless: no reference to Display.cs/WpfBackend.cs or any WPF type.

using System;
using System.IO;

namespace Usim;

public class Tv
{
    private readonly BusInterface _busInterface;

    public uint[] ScreenBuffer { get; } = new uint[0x8000];
    public uint Width { get; } = 768;
    public uint Height { get; } = 896;
    public uint Mode { get; private set; }

    private readonly byte[] _syncRam = new byte[4096];
    private uint _syncPtr;
    private uint _vertSpacing;

    public Tv(BusInterface busInterface)
    {
        _busInterface = busInterface;
    }

    public void ScreenRead(uint offset, out uint value) => value = ScreenBuffer[offset];
    public void ScreenWrite(uint offset, uint value) => ScreenBuffer[offset] = value;

    private bool SyncPromEnabled => (_vertSpacing & 0x80) != 0; // see Global Constraints: resolved discrepancy, bit 7 of vertSpacing, not Mode

    public void ControlRead(uint offset, out uint value)
    {
        switch (offset)
        {
            case 0: value = Mode; return;
            case 1: value = SyncPromEnabled ? 0u : _syncRam[_syncPtr]; return;
            default:
                _busInterface.SetXbusNxm();
                value = 0; // documented deviation -- real C leaves *pv untouched
                return;
        }
    }

    public void ControlWrite(uint offset, uint value)
    {
        switch (offset)
        {
            case 0: Mode = value; return; // no masking -- see Global Constraints
            case 1:
                if (!SyncPromEnabled) _syncRam[_syncPtr] = (byte)(value & 0xFF);
                return;
            case 2: _syncPtr = value & 0x0FFF; return;
            case 3: _vertSpacing = value; return;
            default:
                _busInterface.SetXbusNxm();
                return;
        }
    }

    public void Reset()
    {
        Mode = 0;
        Array.Clear(ScreenBuffer);
    }

    /// <summary>
    /// Faithful port of tv_save_screenshot (usim/tv.c) -- a plain-text PBM
    /// (P1 ASCII bitmap) image, bit-for-bit from ScreenBuffer's raw words.
    /// Iterates exactly Width*Height bits, not the full ScreenBuffer length
    /// -- see Global Constraints.
    /// </summary>
    public void SaveScreenshotPbm(string filename)
    {
        using var writer = new StreamWriter(filename);
        writer.Write("P1\n");
        writer.Write($"{Width} {Height}\n");
        for (uint i = 0; i < Width * Height; i++)
        {
            uint offset = i / 32;
            int bitpos = (int)(i % 32);
            bool bit = (ScreenBuffer[offset] & (1u << bitpos)) != 0;
            writer.Write(bit ? '1' : '0');
            if (i % 70 == 0 && i > 0) writer.Write('\n');
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

The `--test-tv` CLI switch doesn't exist yet (that's Task 2's Step 5) — verify by temporarily adding `TvTests.RunAllTests();` as the first line of `Program.Main` in `usim-cs/Program.cs`, then:

Run: `dotnet build usim-cs && dotnet run --project usim-cs`
Expected: all 8 tests in `TvTests.cs` pass (printed before the program's normal startup output).

Revert the temporary `Program.Main` edit before committing this task — it must not be part of this commit.

- [ ] **Step 5: Commit**

```bash
git add usim-cs/Tv.cs usim-cs/TvTests.cs
git commit -m "Add faithful Tv device port (usim/tv.c, main B&W TV only)"
```

---

### Task 2: Wire `Tv` into `BusAdaptor`, prove it through the real register interface

**Files:**
- Modify: `usim-cs/BusAdaptor.cs`
- Modify: `usim-cs/BusAdaptorTests.cs`
- Modify: `usim-cs/Program.cs` (add `--test-tv` CLI case, `PrintUsage` line, `RunAllTests()` call)

**Interfaces:**
- Consumes: Task 1's `Tv` class (constructor `Tv(BusInterface)`, `ScreenRead`/`ScreenWrite`/`ControlRead`/`ControlWrite`/`SaveScreenshotPbm`/`Width`/`Height`).
- Produces: nothing consumed by a later task (this plan has only 2 tasks).

- [ ] **Step 1: Write the failing tests**

Add to `usim-cs/BusAdaptorTests.cs` (following its existing `static class`/`RunAllTests()` pattern — add these calls to the existing `RunAllTests()` method body, and add the methods below):

```csharp
    private static bool TestTvScreenReadWriteThroughBusAdaptor()
    {
        Console.WriteLine("Test: TV screen read/write through BusAdaptor's real register interface round-trips, including the last valid word");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            bool promDisabled = false;

            // TvScreenLo = 0x3C0000 (BusAdaptor's own constant) -- offset 0 and offset 0x7FFF (last valid word).
            uint tvScreenLo = 0x3C0000;
            ucode.BusAdaptor.Write(tvScreenLo + 0, 0xDEADBEEF, ref promDisabled);
            uint v0 = ucode.BusAdaptor.Read(tvScreenLo + 0);
            Assert(v0 == 0xDEADBEEF, $"offset 0 round-trips through BusAdaptor, got 0x{v0:X}");

            ucode.BusAdaptor.Write(tvScreenLo + 0x7FFF, 0x12345678, ref promDisabled);
            uint vLast = ucode.BusAdaptor.Read(tvScreenLo + 0x7FFF);
            Assert(vLast == 0x12345678, $"last valid word (offset 0x7FFF) round-trips through BusAdaptor, got 0x{vLast:X}");

            Console.WriteLine("  TV-screen-read-write-through-BusAdaptor test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  TV-screen-read-write-through-BusAdaptor test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestTvModeRegisterThroughBusAdaptor()
    {
        Console.WriteLine("Test: TV mode register read/write through BusAdaptor's real register interface round-trips");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            bool promDisabled = false;

            // TvControlLo = 0x3DFFF0 (BusAdaptor's own constant), offset 0 is the mode register.
            uint tvControlLo = 0x3DFFF0;
            ucode.BusAdaptor.Write(tvControlLo + 0, 0xFFFFFFFF, ref promDisabled);
            uint v = ucode.BusAdaptor.Read(tvControlLo + 0);
            Assert(v == 0xFFFFFFFF, $"mode register round-trips through BusAdaptor with no masking, got 0x{v:X}");

            Console.WriteLine("  TV-mode-register-through-BusAdaptor test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  TV-mode-register-through-BusAdaptor test failed: {ex.Message}\n"); return false; }
    }

    private static bool TestTvCheckerboardScreenshotThroughBusAdaptor()
    {
        Console.WriteLine("Test: a checkerboard pattern written through BusAdaptor and saved via Tv.SaveScreenshotPbm matches an independently-computed PBM");
        string path = Path.GetTempFileName();
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            bool promDisabled = false;
            uint tvScreenLo = 0x3C0000;

            uint visibleWords = ucode.BusAdaptor.Tv.Width * ucode.BusAdaptor.Tv.Height / 32;
            for (uint i = 0; i < visibleWords; i++)
                ucode.BusAdaptor.Write(tvScreenLo + i, 0xAAAAAAAA, ref promDisabled); // checkerboard: alternating bits

            ucode.BusAdaptor.Tv.SaveScreenshotPbm(path);

            var expected = new System.Text.StringBuilder();
            uint width = ucode.BusAdaptor.Tv.Width, height = ucode.BusAdaptor.Tv.Height;
            expected.Append("P1\n").Append(width).Append(' ').Append(height).Append('\n');
            for (uint i = 0; i < width * height; i++)
            {
                int bitpos = (int)(i % 32);
                bool bit = (0xAAAAAAAAu & (1u << bitpos)) != 0;
                expected.Append(bit ? '1' : '0');
                if (i % 70 == 0 && i > 0) expected.Append('\n');
            }

            string actual = File.ReadAllText(path);
            Assert(actual == expected.ToString(), "checkerboard screenshot written through BusAdaptor matches independently-computed PBM exactly");

            Console.WriteLine("  TV-checkerboard-screenshot-through-BusAdaptor test passed\n");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"  TV-checkerboard-screenshot-through-BusAdaptor test failed: {ex.Message}\n"); return false; }
        finally { File.Delete(path); }
    }
```

Register all three in `BusAdaptorTests.RunAllTests()`.

**Note:** this requires `BusAdaptor` to expose the wired `Tv` instance for the screenshot call (`ucode.BusAdaptor.Tv`) — add a `public Tv Tv { get; }` property in Step 3 below.

- [ ] **Step 2: Run tests to verify they fail (Tv not wired into BusAdaptor yet — compile error or wrong behavior)**

Run: `dotnet build usim-cs`
Expected: FAIL — `BusAdaptor` has no `Tv` property yet, and the TV address writes currently hit the generic logged-no-op fallback (confirm by temporarily building without the `Tv` property reference removed, if needed, to see the round-trip assertions fail with "read back 0" rather than the written value).

- [ ] **Step 3: Wire `Tv` into `BusAdaptor.cs`**

Add a field and property, constructed directly in the constructor (confirmed no ordering dependency, unlike `DiskController`):

```csharp
    public Tv Tv { get; }
```

In the constructor:

```csharp
    public BusAdaptor(BusInterface busInterface)
    {
        _busInterface = busInterface;
        Tv = new Tv(busInterface);
    }
```

In `ReadXbusIo`, add branches for the TV ranges ahead of the `DiskControlLo`/`Hi` check (address order: `TvScreenLo`/`Hi` is `0x3C0000`-`0x3C7FFF`, `TvControlLo`/`Hi` is `0x3DFFF0`-`0x3DFFF7`, both below `DiskControlLo` at `0x3DFFFC`):

```csharp
        if (paddr >= TvScreenLo && paddr <= TvScreenHi)
        {
            Tv.ScreenRead(paddr - TvScreenLo, out uint v);
            return v;
        }
        if (paddr >= TvControlLo && paddr <= TvControlHi)
        {
            Tv.ControlRead(paddr - TvControlLo, out uint v);
            return v;
        }
```

In `WriteXbusIo`, the matching pair:

```csharp
        if (paddr >= TvScreenLo && paddr <= TvScreenHi)
        {
            Tv.ScreenWrite(paddr - TvScreenLo, v);
            return;
        }
        if (paddr >= TvControlLo && paddr <= TvControlHi)
        {
            Tv.ControlWrite(paddr - TvControlLo, v);
            return;
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build usim-cs && dotnet run --project usim-cs -- --test-bus-adaptor`
Expected: all `BusAdaptorTests` pass, including the three new TV tests.

- [ ] **Step 5: Wire `--test-tv` and `--test-all` in `Program.cs`**

Add immediately after the existing `case "--test-bus-adaptor":` block:

```csharp
                case "--test-tv":
                    TvTests.RunAllTests();
                    Environment.Exit(0);
                    break;
```

Add a `PrintUsage` line next to the other `--test-*` lines:

```csharp
        Console.WriteLine("  --test-tv               Run TV device tests only");
```

Add a call inside `RunAllTests()`, after the existing `BusAdaptorTests.RunAllTests();` call:

```csharp
        Console.WriteLine("Running TV Tests...\n");
        TvTests.RunAllTests();
        Console.WriteLine();
```

- [ ] **Step 6: Run the full suite**

Run: `dotnet build usim-cs && dotnet run --project usim-cs -- --test-all`
Expected: builds clean; every suite passes.

- [ ] **Step 7: Save and inspect a sample screenshot**

As a final, manual sanity check (not an automated test): write a short throwaway script or use `--debug-microcode`'s REPL-like capability (or a temporary `Main`-level call) to construct a `UCode`, write a checkerboard pattern through `BusAdaptor` across the visible screen range, call `ucode.BusAdaptor.Tv.SaveScreenshotPbm("/tmp/tv-checkerboard.pbm")` (or an equivalent path), and open/convert the resulting file to confirm it visibly shows a checkerboard (a `.pbm` viewer, or a quick conversion to `.png` for viewing — e.g. Python's `Pillow` via a one-off script, or any online/local PBM viewer). This step has no pass/fail assertion — its only purpose is letting a person actually see the pattern once, as the spec's stated milestone. Delete the throwaway script/file afterward if it isn't meant to be kept.

- [ ] **Step 8: Commit**

```bash
git add usim-cs/BusAdaptor.cs usim-cs/BusAdaptorTests.cs usim-cs/Program.cs
git commit -m "Wire Tv into BusAdaptor's TV address ranges, add through-register tests"
```
