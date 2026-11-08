// Sdl3KeyTranslator.cs - Translates SDL3 key events into the CADR
// keyboard's X11-keysym-style codes and modifier bitmask (same scheme
// SDL2Backend and WpfKeyTranslator used).
//
// This is the SDL3-CS port of WpfKeyTranslator.cs: same Cadet-scancode
// table and modifier bit assignments, translated from WPF's Key/
// ModifierKeys enums to SDL3-CS's Scancode/Keymod enums. SDL3 scancodes
// already represent the fixed physical-key value independent of shift
// state, so (unlike WPF's Key) there is no separate shift parameter here.

using SDL3;

namespace Usim;

public static class Sdl3KeyTranslator
{
    /// <summary>
    /// Translate an SDL3 Scancode into its real Cadet-keyboard scancode (a
    /// fixed 7-bit value per physical key, independent of shift state --
    /// shift is communicated separately via GetModifierState, matching the
    /// real hardware's actual model). Returns 0 for keys with no real Cadet
    /// mapping (matching usim/sdl3-keyboard-default-mapping.defs's own "no
    /// default mapping" list). Values independently computed by combining
    /// usim/sdl3-keyboard-cadet-scancodes.defs and
    /// usim/sdl3-keyboard-default-mapping.defs via a Python script -- see
    /// docs/superpowers/specs/2026-09-18-iob-design.md's Octal Literals table.
    /// </summary>
    public static int TranslateKey(SDL.Scancode scancode)
    {
        switch (scancode)
        {
            case SDL.Scancode.Escape: return 0x63;
            case SDL.Scancode.F1: return 0x61;
            case SDL.Scancode.F2: return 0x22;
            case SDL.Scancode.F3: return 0x26;
            case SDL.Scancode.F4: return 0x20;
            case SDL.Scancode.F5: return 0x4E;
            case SDL.Scancode.F6: return 0x48;
            case SDL.Scancode.F7: return 0x28;

            case SDL.Scancode.Home: return 0x77;
            case SDL.Scancode.End: return 0x6E;
            case SDL.Scancode.Pageup: return 0x37;
            case SDL.Scancode.Pagedown: return 0x27;
            case SDL.Scancode.Left: return 0x4F;
            case SDL.Scancode.Right: return 0x0F;
            case SDL.Scancode.Up: return 0x46;
            case SDL.Scancode.Down: return 0x7E;

            case SDL.Scancode.Backspace: return 0x13; // Backspace -> rubout
            case SDL.Scancode.Tab: return 0x12;
            case SDL.Scancode.Return: return 0x5E;
            case SDL.Scancode.Space: return 0x5C;

            case SDL.Scancode.LShift: return 0x14;
            case SDL.Scancode.RShift: return 0x14;
            case SDL.Scancode.LCtrl: return 0x10;
            case SDL.Scancode.RCtrl: return 0x10;
            case SDL.Scancode.LAlt: return 0x25; // meta
            case SDL.Scancode.RAlt: return 0x25; // meta
            case SDL.Scancode.LGUI: return 0x05; // super
            case SDL.Scancode.RGUI: return 0x05; // super
            case SDL.Scancode.Capslock: return 0x55;

            case SDL.Scancode.Grave: return 0x3F; // grave
            case SDL.Scancode.Alpha1: return 0x51;
            case SDL.Scancode.Alpha2: return 0x31;
            case SDL.Scancode.Alpha3: return 0x71;
            case SDL.Scancode.Alpha4: return 0x09;
            case SDL.Scancode.Alpha5: return 0x49;
            case SDL.Scancode.Alpha6: return 0x29;
            case SDL.Scancode.Alpha7: return 0x69;
            case SDL.Scancode.Alpha8: return 0x19;
            case SDL.Scancode.Alpha9: return 0x39;
            case SDL.Scancode.Alpha0: return 0x79;
            case SDL.Scancode.Minus: return 0x59;
            case SDL.Scancode.Equals: return 0x56; // equal

            case SDL.Scancode.A: return 0x53;
            case SDL.Scancode.B: return 0x4C;
            case SDL.Scancode.C: return 0x74;
            case SDL.Scancode.D: return 0x73;
            case SDL.Scancode.E: return 0x72;
            case SDL.Scancode.F: return 0x0B;
            case SDL.Scancode.G: return 0x4B;
            case SDL.Scancode.H: return 0x2B;
            case SDL.Scancode.I: return 0x1A;
            case SDL.Scancode.J: return 0x6B;
            case SDL.Scancode.K: return 0x1B;
            case SDL.Scancode.L: return 0x3B;
            case SDL.Scancode.M: return 0x6C;
            case SDL.Scancode.N: return 0x2C;
            case SDL.Scancode.O: return 0x3A;
            case SDL.Scancode.P: return 0x7A;
            case SDL.Scancode.Q: return 0x52;
            case SDL.Scancode.R: return 0x0A;
            case SDL.Scancode.S: return 0x33;
            case SDL.Scancode.T: return 0x4A;
            case SDL.Scancode.U: return 0x6A;
            case SDL.Scancode.V: return 0x0C;
            case SDL.Scancode.W: return 0x32;
            case SDL.Scancode.X: return 0x34;
            case SDL.Scancode.Y: return 0x2A;
            case SDL.Scancode.Z: return 0x54;

            case SDL.Scancode.Leftbracket: return 0x76; // brace_left
            case SDL.Scancode.Rightbracket: return 0x66; // brace_right
            case SDL.Scancode.Backslash: return 0x1F; // backslash
            case SDL.Scancode.NonUsBackSlash: return 0x1F; // backslash (ISO 102-key)
            case SDL.Scancode.Semicolon: return 0x7B;
            case SDL.Scancode.Apostrophe: return 0x5B; // apostrophe
            case SDL.Scancode.Comma: return 0x1C;
            case SDL.Scancode.Period: return 0x3C;
            case SDL.Scancode.Slash: return 0x7C; // slash

            default: return 0; // no real Cadet mapping for this key
        }
    }

    /// <summary>
    /// Build the same modifier bitmask SDL2Backend.GetModifierState /
    /// WpfKeyTranslator.GetModifierState produced.
    /// </summary>
    public static int GetModifierState(SDL.Keymod modifiers, bool capsLock)
    {
        int result = 0;

        if ((modifiers & SDL.Keymod.Shift) != 0)
            result |= 1 << 0; // ShiftMapIndex
        if (capsLock)
            result |= 1 << 1; // LockMapIndex
        if ((modifiers & SDL.Keymod.Ctrl) != 0)
            result |= 1 << 2; // ControlMapIndex
        if ((modifiers & SDL.Keymod.Alt) != 0)
            result |= 1 << 3; // Mod1MapIndex
        if ((modifiers & SDL.Keymod.GUI) != 0)
            result |= 1 << 6; // Mod4MapIndex

        return result;
    }
}
