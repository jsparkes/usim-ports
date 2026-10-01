// WpfKeyTranslator.cs - Translates WPF key events into the CADR keyboard's
// X11-keysym-style codes and modifier bitmask (same scheme SDL2Backend used).
//
// NOTE: System.Windows.Input.Keyboard (the static input-state helper) shares
// its simple name with Usim.Keyboard (this app's keyboard model). Any WPF
// caller of this class must fully qualify System.Windows.Input.Keyboard.

using System.Windows.Input;

namespace Usim;

public static class WpfKeyTranslator
{
    /// <summary>
    /// Translate a WPF Key into its real Cadet-keyboard scancode (a fixed
    /// 7-bit value per physical key, independent of shift state -- shift is
    /// communicated separately via GetModifierState, matching the real
    /// hardware's actual model). Returns 0 for keys with no real Cadet
    /// mapping (matching usim/sdl3-keyboard-default-mapping.defs's own "no
    /// default mapping" list). Values independently computed by combining
    /// usim/sdl3-keyboard-cadet-scancodes.defs and
    /// usim/sdl3-keyboard-default-mapping.defs via a Python script -- see
    /// docs/superpowers/specs/2026-09-18-iob-design.md's Octal Literals table.
    /// </summary>
    public static int TranslateKey(Key key, bool shift)
    {
        switch (key)
        {
            case Key.Escape: return 0x63;
            case Key.F1: return 0x61;
            case Key.F2: return 0x22;
            case Key.F3: return 0x26;
            case Key.F4: return 0x20;
            case Key.F5: return 0x4E;
            case Key.F6: return 0x48;
            case Key.F7: return 0x28;

            case Key.Home: return 0x77;
            case Key.End: return 0x6E;
            case Key.PageUp: return 0x37;
            case Key.PageDown: return 0x27;
            case Key.Left: return 0x4F;
            case Key.Right: return 0x0F;
            case Key.Up: return 0x46;
            case Key.Down: return 0x7E;

            case Key.Back: return 0x13; // Backspace -> rubout
            case Key.Tab: return 0x12;
            case Key.Return: return 0x5E;
            case Key.Space: return 0x5C;

            case Key.LeftShift: return 0x14;
            case Key.RightShift: return 0x14;
            case Key.LeftCtrl: return 0x10;
            case Key.RightCtrl: return 0x10;
            case Key.LeftAlt: return 0x25; // meta
            case Key.RightAlt: return 0x25; // meta
            case Key.LWin: return 0x05; // super
            case Key.RWin: return 0x05; // super
            case Key.CapsLock: return 0x55;

            case Key.OemTilde: return 0x3F; // grave
            case Key.D1: return 0x51;
            case Key.D2: return 0x31;
            case Key.D3: return 0x71;
            case Key.D4: return 0x09;
            case Key.D5: return 0x49;
            case Key.D6: return 0x29;
            case Key.D7: return 0x69;
            case Key.D8: return 0x19;
            case Key.D9: return 0x39;
            case Key.D0: return 0x79;
            case Key.OemMinus: return 0x59;
            case Key.OemPlus: return 0x56; // equal

            case Key.A: return 0x53;
            case Key.B: return 0x4C;
            case Key.C: return 0x74;
            case Key.D: return 0x73;
            case Key.E: return 0x72;
            case Key.F: return 0x0B;
            case Key.G: return 0x4B;
            case Key.H: return 0x2B;
            case Key.I: return 0x1A;
            case Key.J: return 0x6B;
            case Key.K: return 0x1B;
            case Key.L: return 0x3B;
            case Key.M: return 0x6C;
            case Key.N: return 0x2C;
            case Key.O: return 0x3A;
            case Key.P: return 0x7A;
            case Key.Q: return 0x52;
            case Key.R: return 0x0A;
            case Key.S: return 0x33;
            case Key.T: return 0x4A;
            case Key.U: return 0x6A;
            case Key.V: return 0x0C;
            case Key.W: return 0x32;
            case Key.X: return 0x34;
            case Key.Y: return 0x2A;
            case Key.Z: return 0x54;

            case Key.OemOpenBrackets: return 0x76; // brace_left
            case Key.OemCloseBrackets: return 0x66; // brace_right
            case Key.OemPipe: return 0x1F; // backslash
            case Key.OemBackslash: return 0x1F; // backslash (ISO 102-key)
            case Key.OemSemicolon: return 0x7B;
            case Key.OemQuotes: return 0x5B; // apostrophe
            case Key.OemComma: return 0x1C;
            case Key.OemPeriod: return 0x3C;
            case Key.OemQuestion: return 0x7C; // slash

            default: return 0; // no real Cadet mapping for this key
        }
    }

    /// <summary>
    /// Build the same modifier bitmask SDL2Backend.GetModifierState produced.
    /// </summary>
    public static int GetModifierState(ModifierKeys modifiers, bool capsLock)
    {
        int result = 0;

        if ((modifiers & ModifierKeys.Shift) != 0)
            result |= 1 << 0; // ShiftMapIndex
        if (capsLock)
            result |= 1 << 1; // LockMapIndex
        if ((modifiers & ModifierKeys.Control) != 0)
            result |= 1 << 2; // ControlMapIndex
        if ((modifiers & ModifierKeys.Alt) != 0)
            result |= 1 << 3; // Mod1MapIndex
        if ((modifiers & ModifierKeys.Windows) != 0)
            result |= 1 << 6; // Mod4MapIndex

        return result;
    }
}
