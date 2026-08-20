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
    /// Translate a WPF Key into an X11 keysym-style code. Returns 0
    /// (XK_VoidSymbol) for keys with no mapping, matching
    /// SDL2Backend.TranslateKeycode's behavior.
    /// </summary>
    public static int TranslateKey(Key key, bool shift)
    {
        switch (key)
        {
            case Key.Escape: return 0xFF1B; // XK_Escape
            case Key.F1: return 0xFFBE;
            case Key.F2: return 0xFFBF;
            case Key.F3: return 0xFFC0;
            case Key.F4: return 0xFFC1;
            case Key.F5: return 0xFFC2;
            case Key.F6: return 0xFFC3;
            case Key.F7: return 0xFFC4;
            case Key.F8: return 0xFFC5;
            case Key.F9: return 0xFFC6;
            case Key.F10: return 0xFFC7;
            case Key.F11: return 0xFFC8;
            case Key.F12: return 0xFFC9;

            case Key.PageUp: return 0xFF55;
            case Key.PageDown: return 0xFF56;
            case Key.Home: return 0xFF50;
            case Key.End: return 0xFF57;
            case Key.Left: return 0xFF51;
            case Key.Right: return 0xFF53;
            case Key.Up: return 0xFF52;
            case Key.Down: return 0xFF54;

            case Key.Insert: return 0xFF63;
            case Key.Delete: return 0xFFFF;
            case Key.Back: return 0xFF08;
            case Key.Tab: return 0xFF09;
            case Key.Return: return 0xFF0D;
            case Key.Space: return 0x0020;

            case Key.LeftShift: return 0xFFE1;
            case Key.RightShift: return 0xFFE2;
            case Key.LeftCtrl: return 0xFFE3;
            case Key.RightCtrl: return 0xFFE4;
            case Key.LeftAlt: return 0xFFE9;
            case Key.RightAlt: return 0xFFEA;
            case Key.LWin: return 0xFFE7;
            case Key.RWin: return 0xFFE8;
            case Key.CapsLock: return 0xFFE5;

            case Key.OemTilde: return shift ? 0x007E : 0x0060;
            case Key.D1: return shift ? 0x0021 : 0x0031;
            case Key.D2: return shift ? 0x0040 : 0x0032;
            case Key.D3: return shift ? 0x0023 : 0x0033;
            case Key.D4: return shift ? 0x0024 : 0x0034;
            case Key.D5: return shift ? 0x0025 : 0x0035;
            case Key.D6: return shift ? 0x005E : 0x0036;
            case Key.D7: return shift ? 0x0026 : 0x0037;
            case Key.D8: return shift ? 0x002A : 0x0038;
            case Key.D9: return shift ? 0x0028 : 0x0039;
            case Key.D0: return shift ? 0x0029 : 0x0030;
            case Key.OemMinus: return shift ? 0x005F : 0x002D;
            case Key.OemPlus: return shift ? 0x002B : 0x003D;

            case Key.A: return shift ? 0x0041 : 0x0061;
            case Key.B: return shift ? 0x0042 : 0x0062;
            case Key.C: return shift ? 0x0043 : 0x0063;
            case Key.D: return shift ? 0x0044 : 0x0064;
            case Key.E: return shift ? 0x0045 : 0x0065;
            case Key.F: return shift ? 0x0046 : 0x0066;
            case Key.G: return shift ? 0x0047 : 0x0067;
            case Key.H: return shift ? 0x0048 : 0x0068;
            case Key.I: return shift ? 0x0049 : 0x0069;
            case Key.J: return shift ? 0x004A : 0x006A;
            case Key.K: return shift ? 0x004B : 0x006B;
            case Key.L: return shift ? 0x004C : 0x006C;
            case Key.M: return shift ? 0x004D : 0x006D;
            case Key.N: return shift ? 0x004E : 0x006E;
            case Key.O: return shift ? 0x004F : 0x006F;
            case Key.P: return shift ? 0x0050 : 0x0070;
            case Key.Q: return shift ? 0x0051 : 0x0071;
            case Key.R: return shift ? 0x0052 : 0x0072;
            case Key.S: return shift ? 0x0053 : 0x0073;
            case Key.T: return shift ? 0x0054 : 0x0074;
            case Key.U: return shift ? 0x0055 : 0x0075;
            case Key.V: return shift ? 0x0056 : 0x0076;
            case Key.W: return shift ? 0x0057 : 0x0077;
            case Key.X: return shift ? 0x0058 : 0x0078;
            case Key.Y: return shift ? 0x0059 : 0x0079;
            case Key.Z: return shift ? 0x005A : 0x007A;

            case Key.OemOpenBrackets: return shift ? 0x007B : 0x005B;
            case Key.OemCloseBrackets: return shift ? 0x007D : 0x005D;
            case Key.OemPipe: return shift ? 0x007C : 0x005C;      // US \| key (VK_OEM_5)
            case Key.OemBackslash: return shift ? 0x007C : 0x005C; // ISO 102-key extra (VK_OEM_102)
            case Key.OemSemicolon: return shift ? 0x003A : 0x003B;
            case Key.OemQuotes: return shift ? 0x0022 : 0x0027;
            case Key.OemComma: return shift ? 0x003C : 0x002C;
            case Key.OemPeriod: return shift ? 0x003E : 0x002E;
            case Key.OemQuestion: return shift ? 0x003F : 0x002F;

            default: return 0; // XK_VoidSymbol
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
