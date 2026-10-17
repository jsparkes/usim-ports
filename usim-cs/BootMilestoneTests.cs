// BootMilestoneTests.cs - End-to-end: boot the real promh.mcr PROM against
// the real disk.img T-300 pack and detect the real PROM->RAM handoff
// (Npc==6, PromDisabled) -- see docs/superpowers/specs/2026-10-17-end-to-end-boot-milestone-design.md.

using System;
using System.IO;

namespace Usim;

public static class BootMilestoneTests
{
    private const string RealPromPath = "sys/ubin/promh.mcr";
    private const string RealPromSymPath = "sys/ubin/promh.sym";
    private const string RealUcadrSymPath = "sys/ubin/ucadr.sym";
    private const string RealDiskImagePath = "usim/disk.img";
    private const int MaxBootCycles = 5_000_000;

    public static void RunAllTests()
    {
        Console.WriteLine("=== Boot Milestone Test Suite ===\n");
        int passed = 0, failed = 0;

        if (TestPromBootReachesRamHandoff()) passed++; else failed++;
        if (TestDiagnosticsOnCycleBudgetExceeded()) passed++; else failed++;
        if (TestDiagnosticsOnHalted()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestPromBootReachesRamHandoff()
    {
        Console.WriteLine("Test: booting the real promh.mcr against the real disk.img reaches the real PROM->RAM handoff (Npc==6, PromDisabled) (skipped if promh.mcr or disk.img is absent)");
        if (!File.Exists(RealPromPath) || !File.Exists(RealDiskImagePath))
        {
            Console.WriteLine($"  SKIPPED: {RealPromPath} and/or {RealDiskImagePath} not present\n");
            return true;
        }

        string diskPath = CopyRealDiskImage();
        try
        {
            var mem = new MainMemory();
            var ucode = new UCode(mem);
            ucode.Init();
            ucode.LoadPromFromFile(RealPromPath);
            var dc = new DiskController(mem, ucode);
            ucode.BusAdaptor.WireDiskController(dc);
            dc.ConfigureUnit(0, "T-300", diskPath);

            MaybeLoadSymbols();

            bool reached = TryRunUntilHandoff(ucode, MaxBootCycles, out string diagnostics);
            Assert(reached, $"PROM boot reached the real PROM->RAM handoff (Npc==6, PromDisabled) within {MaxBootCycles} cycles.\n{diagnostics}");

            Console.WriteLine("  Real-boot PROM->RAM handoff test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Real-boot PROM->RAM handoff test failed: {ex.Message}\n");
            return false;
        }
        finally
        {
            File.Delete(diskPath);
        }
    }

    // These two tests need no optional real files -- they deterministically
    // pin TryRunUntilHandoff's two non-success diagnostic paths so this
    // logic is actually exercised by --test-all even in an environment with
    // none of promh.mcr/ucadr.mcr/disk.img present (otherwise it would never
    // run at all in a typical dev/CI checkout).
    private static bool TestDiagnosticsOnCycleBudgetExceeded()
    {
        Console.WriteLine("Test: TryRunUntilHandoff reports a clear cycle-budget-exceeded diagnostic when the handoff is never reached (no real files needed)");
        try
        {
            var ucode = new UCode(new MainMemory());
            ucode.Init(); // Prom[]/IMem[] stay all-zero -- never produces Npc==6 && PromDisabled within a tiny budget.

            bool reached = TryRunUntilHandoff(ucode, maxCycles: 10, out string diagnostics);
            Assert(!reached, "handoff is not reached against an all-zero Prom/IMem within 10 cycles");
            Assert(diagnostics.Contains("Cycle budget"), $"diagnostics names the cycle-budget reason, got: {diagnostics}");
            Assert(diagnostics.Contains("Final Npc="), $"diagnostics includes the final Npc, got: {diagnostics}");
            Assert(diagnostics.Contains("Last instructions executed"), $"diagnostics includes recent disassembly, got: {diagnostics}");

            Console.WriteLine("  Cycle-budget-exceeded diagnostics test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Cycle-budget-exceeded diagnostics test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestDiagnosticsOnHalted()
    {
        Console.WriteLine("Test: TryRunUntilHandoff reports a clear Halted diagnostic when UCode.Halted becomes true before the handoff (no real files needed)");
        try
        {
            var ucode = new UCode(new MainMemory());
            ucode.Init();
            ucode.Halted = true; // simulate a real fatal PROM error path without needing to craft real microcode for it.

            bool reached = TryRunUntilHandoff(ucode, maxCycles: 1000, out string diagnostics);
            Assert(!reached, "handoff is not reported reached when Halted is already true");
            Assert(diagnostics.Contains("Halted"), $"diagnostics names the Halted reason, got: {diagnostics}");

            Console.WriteLine("  Halted diagnostics test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Halted diagnostics test failed: {ex.Message}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }

    private static string CopyRealDiskImage()
    {
        string path = Path.GetTempFileName();
        File.Copy(RealDiskImagePath, path, overwrite: true);
        return path;
    }

    private static void MaybeLoadSymbols()
    {
        var symbols = new SymbolTable();
        bool any = false;
        if (File.Exists(RealPromSymPath)) { symbols.LoadFromFile(RealPromSymPath); any = true; }
        if (File.Exists(RealUcadrSymPath)) { symbols.LoadFromFile(RealUcadrSymPath); any = true; }
        Disassembler.Symbols = any ? symbols : null;
    }

    private static bool TryRunUntilHandoff(UCode ucode, int maxCycles, out string diagnostics)
    {
        var recentPcs = new uint[5];
        var recentPromDisabled = new bool[5];
        int ring = 0;

        for (int i = 0; i < maxCycles; i++)
        {
            recentPcs[ring % 5] = ucode.Npc;
            recentPromDisabled[ring % 5] = ucode.PromDisabled;
            ring++;

            try
            {
                ucode.Step();
            }
            catch (Exception ex)
            {
                diagnostics = BuildDiagnostics(ucode, recentPcs, recentPromDisabled, ring, $"Exception during Step(): {ex.Message}");
                return false;
            }

            if (ucode.Npc == 6 && ucode.PromDisabled)
            {
                diagnostics = "";
                return true;
            }

            if (ucode.Halted)
            {
                diagnostics = BuildDiagnostics(ucode, recentPcs, recentPromDisabled, ring, "UCode.Halted became true");
                return false;
            }
        }

        diagnostics = BuildDiagnostics(ucode, recentPcs, recentPromDisabled, ring, $"Cycle budget ({maxCycles}) exhausted");
        return false;
    }

    private static string BuildDiagnostics(UCode ucode, uint[] recentPcs, bool[] recentPromDisabled, int ring, string reason)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Reason: {reason}");
        sb.AppendLine($"Final Npc=0x{ucode.Npc:X4} PromDisabled={ucode.PromDisabled} Halted={ucode.Halted}");

        string? label = ucode.PromDisabled
            ? Disassembler.Symbols?.FindByTypeValue(SymbolType.IMem, ucode.Npc)
            : null; // Prom-side addresses aren't resolved -- promh.sym's own entries are also typed I-MEM, indistinguishable from IMem[]'s, so only resolve when PromDisabled (i.e. genuinely executing from IMem).
        if (label != null) sb.AppendLine($"  (resolved label: {label})");

        sb.AppendLine("Last instructions executed (oldest first):");
        int count = Math.Min(ring, 5);
        for (int k = count - 1; k >= 0; k--)
        {
            int idx = (ring - 1 - k) % 5;
            sb.AppendLine($"  {Disassembler.DisassemblePC2(recentPcs[idx], recentPromDisabled[idx])}");
        }
        return sb.ToString();
    }
}
