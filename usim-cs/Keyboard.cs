// Keyboard.cs - Faithful port of the real keyboard-to-IOB wire protocol
// (usim/iob.c's WITH_SDL3 iob_set_keyboard_ready path). The previous version
// of this class invented its own buffer/modifier-bit-packing scheme with no
// relation to the real hardware. See
// docs/superpowers/specs/2026-09-18-iob-design.md.

using System;

namespace Usim;

public class Keyboard
{
    private Iob? _iob;

    public ulong KeysPressed { get; private set; }

    public void WireIob(Iob iob)
    {
        _iob = iob;
    }

    public void Initialize()
    {
        KeysPressed = 0;
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

        // Faithful port of kbd_event's v = ((!keydown) << 8) | code (usim/kbd.c:79),
        // reused here as the real scancode encoding IOB expects: bit 8 clear = down.
        uint v = keysym & 0xFF;
        KeysPressed++;
        _iob?.SetKeyboardReady(v);

        TraceLog.Instance.Trace(TraceCategory.Keyboard, TraceLevel.Debug,
            $"KeyDown: keysym=0x{keysym:X}, modifiers=0x{modifiers:X}");
    }

    /// <summary>Handle a key-up event from WPF. Bit 8 set = up.</summary>
    public void KeyUp(uint keysym, int modifiers)
    {
        if (keysym == 0)
        {
            return;
        }

        uint v = (1u << 8) | (keysym & 0xFF);
        _iob?.SetKeyboardReady(v);

        TraceLog.Instance.Trace(TraceCategory.Keyboard, TraceLevel.Debug,
            $"KeyUp: keysym=0x{keysym:X}, modifiers=0x{modifiers:X}");
    }
}
