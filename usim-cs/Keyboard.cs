// Keyboard.cs - Faithful port of the real keyboard-to-IOB wire protocol
// (usim/iob.c's WITH_SDL3 iob_set_keyboard_ready path). The previous version
// of this class invented its own buffer/modifier-bit-packing scheme with no
// relation to the real hardware. See
// docs/superpowers/specs/2026-09-18-iob-design.md.

using System;
using System.Collections.Generic;

namespace Usim;

public class Keyboard
{
    // NKB header the real SDL3 path ORs into every scancode
    // (usim/sdl3-keyboard.c sdl3_keyboard_new_scancode):
    // (0b11111 << 19) | (0b001 << 16) == 0xF90000. NKB<23:19> = all ones
    // (reserved), NKB<18:16> = 001 ("new keyboard"). Lisp's
    // KBD-CONVERT-TO-SOFTWARE-CHAR selects its converter from bits 18:16 and
    // drops the key if the header is missing.
    private const uint NkbHeader = (0b11111u << 19) | (0b001u << 16);

    // usim/sdl3-keyboard.c QUEUE_CAPACITY.
    private const int QueueCapacity = 100;

    // The real delivery thread waits for the IOB keyboard to be not busy for
    // 2+4+...+256 ms (sdl3_keyboard_thread_run) before dropping the scancode.
    private const long BusyTimeoutMs = 2 + 4 + 8 + 16 + 32 + 64 + 128 + 256;

    private readonly Queue<uint> _queue = new();
    private readonly Func<long> _nowMs;
    private long _busySinceMs = -1;
    private Iob? _iob;

    public Keyboard() : this(() => Environment.TickCount64)
    {
    }

    // The clock is injectable so the busy-timeout can be tested.
    public Keyboard(Func<long> nowMs)
    {
        _nowMs = nowMs;
    }

    public ulong KeysPressed { get; private set; }

    public void WireIob(Iob iob)
    {
        _iob = iob;
    }

    public void Initialize()
    {
        KeysPressed = 0;
        _queue.Clear();
        _busySinceMs = -1;
    }

    /// <summary>Scancodes waiting for the IOB keyboard to become free.</summary>
    public int QueuedCount => _queue.Count;

    /// <summary>
    /// Port of sdl3_keyboard_enqueue: drops the scancode (with a warning) if
    /// the queue is full, otherwise queues it and tries to deliver.
    /// </summary>
    private void Enqueue(uint scancode)
    {
        if (_queue.Count == QueueCapacity)
        {
            TraceLog.Instance.Trace(TraceCategory.Keyboard, TraceLevel.Warning,
                $"keyboard scancode: {scancode} is missed, queue exhausted");
            return;
        }
        _queue.Enqueue(scancode);
        Pump();
    }

    /// <summary>
    /// Replaces the real delivery thread (sdl3_keyboard_thread_run) with a
    /// poll: called per tick and on every enqueue. Delivers the head scancode
    /// once the IOB keyboard is not busy (CSR bit 5 clear); if it stays busy
    /// for the thread's full back-off (510 ms) the head scancode is dropped
    /// with a warning, as in the real code.
    /// </summary>
    public void Tick()
    {
        Pump();
    }

    private void Pump()
    {
        if (_iob == null)
        {
            return;
        }
        while (_queue.Count > 0)
        {
            if (!_iob.IsKeyboardReadySet)
            {
                _iob.SetKeyboardReady(_queue.Dequeue());
                _busySinceMs = -1;
                // Delivery sets the ready bit; the next scancode has to wait
                // until Lisp reads this one.
                return;
            }

            long now = _nowMs();
            if (_busySinceMs < 0)
            {
                _busySinceMs = now;
                return;
            }
            if (now - _busySinceMs < BusyTimeoutMs)
            {
                return;
            }
            uint dropped = _queue.Dequeue();
            TraceLog.Instance.Trace(TraceCategory.Keyboard, TraceLevel.Warning,
                $"keyboard scancode: {dropped} is missed, keyboard is busy exhausted");
            _busySinceMs = now;
        }
        _busySinceMs = -1;
    }

    /// <summary>
    /// Handle a key-down event from WPF. keysym is WpfKeyTranslator's real
    /// Cadet scancode (0 if the key has no real mapping, in which case this
    /// is a no-op -- matches real hardware silently ignoring an unmapped key).
    /// </summary>
    public void KeyDown(uint keysym, int modifiers)
    {
        if (keysym == 0)
        {
            return;
        }

        // sdl3_keyboard_new_scancode: NKB header | (cadet scancode & 0x7F);
        // bit 8 clear = key down.
        uint v = NkbHeader | (keysym & 0x7Fu);
        KeysPressed++;
        Enqueue(v);

        TraceLog.Instance.Trace(TraceCategory.Keyboard, TraceLevel.Debug,
            $"KeyDown: keysym=0x{keysym:X}, modifiers=0x{modifiers:X}");
    }

    /// <summary>Handle a key-up event from WPF. Header | bit 8 set = up.</summary>
    public void KeyUp(uint keysym, int modifiers)
    {
        if (keysym == 0)
        {
            return;
        }

        uint v = NkbHeader | (1u << 8) | (keysym & 0x7Fu);
        Enqueue(v);

        TraceLog.Instance.Trace(TraceCategory.Keyboard, TraceLevel.Debug,
            $"KeyUp: keysym=0x{keysym:X}, modifiers=0x{modifiers:X}");
    }
}
