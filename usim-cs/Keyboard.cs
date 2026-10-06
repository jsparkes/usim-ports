// Keyboard.cs - Faithful port of the real keyboard-to-IOB wire protocol
// (usim/iob.c's WITH_SDL3 iob_set_keyboard_ready path). The previous version
// of this class invented its own buffer/modifier-bit-packing scheme with no
// relation to the real hardware. See
// docs/superpowers/specs/2026-09-18-iob-design.md.

using System;

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

        // sdl3_keyboard_new_scancode: NKB header | (cadet scancode & 0x7F);
        // bit 8 clear = key down.
        uint v = NkbHeader | (keysym & 0x7Fu);
        KeysPressed++;
        _iob?.SetKeyboardReady(v);

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
        _iob?.SetKeyboardReady(v);

        TraceLog.Instance.Trace(TraceCategory.Keyboard, TraceLevel.Debug,
            $"KeyUp: keysym=0x{keysym:X}, modifiers=0x{modifiers:X}");
    }
}
