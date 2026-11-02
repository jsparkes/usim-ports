// Sdl3BackendTests.cs - Tests for Sdl3Backend, the SDL3-CS integration layer
// composing Sdl3Video/Sdl3Audio/Sdl3KeyTranslator with Tv/ColorTv/Iob/
// Keyboard/Mouse/Idle.

using System;
using SDL3;

namespace Usim;

public static class Sdl3BackendTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== Sdl3Backend Test Suite ===\n");

        // Run headlessly on any machine, including CI with no real
        // display/audio device -- "dummy" is a real SDL driver that
        // discards all drawing/audio.
        Environment.SetEnvironmentVariable("SDL_VIDEODRIVER", "dummy");
        Environment.SetEnvironmentVariable("SDL_AUDIODRIVER", "dummy");

        int passed = 0;
        int failed = 0;

        if (TestBootScancodeConstants()) passed++; else failed++;
        if (TestBootScancodeOrdering()) passed++; else failed++;
        if (TestConstructionAndDefaults()) passed++; else failed++;
        if (TestUnscaleMotion()) passed++; else failed++;
        if (TestAreAllCtrlAndMetaDownForBoot()) passed++; else failed++;
        if (TestInitializeAndDispose()) passed++; else failed++;

        Console.WriteLine($"\n=== Test Summary ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");
    }

    private static bool TestBootScancodeConstants()
    {
        Console.WriteLine("Test: boot-chord constants are the corrected (non-zeroed) values");
        try
        {
            Assert(Sdl3Backend.ColdBootScancode ==
                    ((0b11111u << 19) | (0b111u << 16) | (0b111111u << 10) | 0x26u /* 046 octal */),
                "cold boot scancode uses the corrected shift, not real C's zeroed typo");
            Assert(Sdl3Backend.WarmBootScancode ==
                    ((0b11111u << 19) | (0b111u << 16) | (0b111111u << 10) | 0x32u /* 062 octal */),
                "warm boot scancode uses the corrected shift, not real C's zeroed typo");

            Console.WriteLine("  Boot scancode constants test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Boot scancode constants test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestBootScancodeOrdering()
    {
        Console.WriteLine("Test: Keyboard.EnqueueScancode lands the boot scancode directly behind its");
        Console.WriteLine("      triggering key, strictly before a later-arriving key event (sdl3-keyboard.c:377-389)");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var iob = new Iob(ucode);
            var keyboard = new Keyboard();
            keyboard.WireIob(iob);

            // The chord-completing key's own scancode (e.g. rubout/backspace,
            // cadet scancode 0x13). Queue is empty and IOB is free, so this
            // delivers immediately, claiming the IOB's ready bit.
            keyboard.KeyDown(0x13, modifiers: 0);
            Assert(iob.IsKeyboardReadySet, "the triggering key's own scancode was delivered immediately");
            Assert(keyboard.QueuedCount == 0, "nothing queued yet -- the triggering key's scancode went straight through");

            // Immediately after (same call order Sdl3Backend.HandleKeyDown
            // uses), the boot scancode is pushed onto the SAME real FIFO.
            // The IOB is currently busy (not yet read), so this queues
            // behind nothing -- it is the only thing in the queue so far.
            keyboard.EnqueueScancode(Sdl3Backend.ColdBootScancode);
            Assert(keyboard.QueuedCount == 1, "boot scancode is queued while the IOB is still busy with the triggering key's scancode");

            // A later-arriving key event (e.g. the next keystroke) must not
            // be able to jump ahead of the already-queued boot scancode.
            keyboard.KeyDown(0x53, modifiers: 0); // cadet scancode for 'a'
            Assert(keyboard.QueuedCount == 2, "the later key queues behind the boot scancode, not ahead of it");

            // "Lisp" reads the triggering key's own scancode off the IOB,
            // freeing it for the next delivery.
            uint delivered1 = ReadIobScancode(iob);
            uint expectedTriggerScancode = ((0b11111u << 19) | (0b001u << 16)) | 0x13u;
            Assert(delivered1 == expectedTriggerScancode, "first delivery is the triggering key's own up-down scancode");

            // The next Tick() must deliver the boot scancode next, not the
            // later key -- this is the ordering guarantee itself.
            keyboard.Tick();
            Assert(iob.IsKeyboardReadySet, "boot scancode was delivered once the IOB freed up");
            uint delivered2 = ReadIobScancode(iob);
            Assert(delivered2 == Sdl3Backend.ColdBootScancode,
                "second delivery is the boot scancode, strictly before the later-arriving key event");

            // Finally, the later key's own scancode is delivered last.
            keyboard.Tick();
            Assert(iob.IsKeyboardReadySet, "later key's scancode was delivered last");
            uint delivered3 = ReadIobScancode(iob);
            uint expectedLaterKeyScancode = ((0b11111u << 19) | (0b001u << 16)) | 0x53u;
            Assert(delivered3 == expectedLaterKeyScancode, "third delivery is the later key's own scancode");

            Console.WriteLine("  Boot scancode ordering test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Boot scancode ordering test failed: {ex}\n");
            return false;
        }
    }

    private static uint ReadIobScancode(Iob iob)
    {
        uint low = iob.Read(0x40);
        uint high = iob.Read(0x42);
        return low | (high << 16);
    }

    private static bool TestConstructionAndDefaults()
    {
        Console.WriteLine("Test: construction and shape parity with WpfBackend");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var tv = new Tv(ucode, 768, 963);
            var colorTv = new ColorTv(ucode);
            var iob = new Iob(ucode);
            var keyboard = new Keyboard();
            var mouse = new Mouse();
            var idle = new Idle(ucode);

            var backend = new Sdl3Backend(tv, colorTv, iob, keyboard, mouse, idle, onTick: () => { });

            Assert(backend.Scale == 1.0 && backend.AllowResize == false && backend.UseLinearFiltering == true,
                "defaults match WpfBackend's defaults");
            Assert(backend.WindowDisplay == -1 && backend.WindowPositionX == -1 && backend.WindowPositionY == -1,
                "window-placement defaults match ConfigManager's 'unset' sentinel (-1)");
            Assert(backend.WindowAlwaysOnTop == false, "WindowAlwaysOnTop defaults to false");
            Assert(backend.IsRunning == false, "IsRunning is false before Initialize()");

            Console.WriteLine("  Construction and defaults test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Construction and defaults test failed: {ex}\n");
            return false;
        }
    }

    private static bool TestUnscaleMotion()
    {
        Console.WriteLine("Test: mouse motion unscaling (Review Focus: Scale != 1.0)");
        try
        {
            var (x, y) = Sdl3Backend.UnscaleMotion(rawX: 100, rawY: 200, scale: 2.0);
            Assert(x == 50 && y == 100, "motion coordinates at scale=2.0 are halved before reaching Mouse.UpdatePosition");

            var (x1, y1) = Sdl3Backend.UnscaleMotion(rawX: 100, rawY: 200, scale: 1.0);
            Assert(x1 == 100 && y1 == 200, "motion coordinates at scale=1.0 (the default) pass through unchanged");

            // scale == 0 is usim.ini's "use native scaling" sentinel
            // (ConfigManager's [Display] scale default); it must normalize
            // to 1.0 (matching Sdl3Video.Init's own treatment), not divide
            // by zero and produce NaN/garbage coordinates.
            var (x2, y2) = Sdl3Backend.UnscaleMotion(rawX: 100, rawY: 200, scale: 0.0);
            Assert(x2 == 100 && y2 == 200, "motion coordinates at scale=0.0 (native-scaling sentinel) pass through unchanged, same as scale=1.0");

            Console.WriteLine("  UnscaleMotion test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  UnscaleMotion test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestAreAllCtrlAndMetaDownForBoot()
    {
        Console.WriteLine("Test: AreAllCtrlAndMetaDownForBoot requires both ctrl keys and both meta (alt) keys down");
        try
        {
            int count = (int)SDL.Scancode.Count;

            bool[] none = new bool[count];
            Assert(!Sdl3Backend.AreAllCtrlAndMetaDownForBoot(none), "nothing down -> false");

            bool[] onlyCtrl = new bool[count];
            onlyCtrl[(int)SDL.Scancode.LCtrl] = true;
            onlyCtrl[(int)SDL.Scancode.RCtrl] = true;
            Assert(!Sdl3Backend.AreAllCtrlAndMetaDownForBoot(onlyCtrl), "only both ctrl keys down -> false (meta missing)");

            bool[] all = new bool[count];
            all[(int)SDL.Scancode.LCtrl] = true;
            all[(int)SDL.Scancode.RCtrl] = true;
            all[(int)SDL.Scancode.LAlt] = true;
            all[(int)SDL.Scancode.RAlt] = true;
            Assert(Sdl3Backend.AreAllCtrlAndMetaDownForBoot(all), "both ctrl and both meta (alt) keys down -> true");

            bool[] oneCtrlMissing = new bool[count];
            oneCtrlMissing[(int)SDL.Scancode.LCtrl] = true;
            oneCtrlMissing[(int)SDL.Scancode.LAlt] = true;
            oneCtrlMissing[(int)SDL.Scancode.RAlt] = true;
            Assert(!Sdl3Backend.AreAllCtrlAndMetaDownForBoot(oneCtrlMissing), "only one ctrl key down -> false");

            Console.WriteLine("  AreAllCtrlAndMetaDownForBoot test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  AreAllCtrlAndMetaDownForBoot test failed: {ex.Message}\n");
            return false;
        }
    }

    private static bool TestInitializeAndDispose()
    {
        Console.WriteLine("Test: Initialize()/Dispose() succeed under SDL_VIDEODRIVER=dummy/SDL_AUDIODRIVER=dummy");
        try
        {
            var mainMemory = new MainMemory();
            var ucode = new UCode(mainMemory);
            var tv = new Tv(ucode, 768, 963);
            var colorTv = new ColorTv(ucode);
            var iob = new Iob(ucode);
            var keyboard = new Keyboard();
            var mouse = new Mouse();
            var idle = new Idle(ucode);

            using var backend = new Sdl3Backend(tv, colorTv, iob, keyboard, mouse, idle, onTick: () => { });

            backend.Initialize();
            Assert(backend.IsRunning, "IsRunning is true after a successful Initialize()");

            backend.SetWindowTitle("a new title"); // must not throw
            backend.RequestExit();

            Console.WriteLine("  Initialize/Dispose test passed\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Initialize/Dispose test failed: {ex}\n");
            return false;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"Assertion failed: {message}");
    }
}
