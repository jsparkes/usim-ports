// Sdl3Smoke.cs - Minimal smoke test proving the SDL3-CS package (and its
// native runtime companion) resolves and runs under this project's TFM.
// Later tasks are free to delete this once Sdl3Video.cs exercises the
// package directly.

using SDL3;

namespace Usim;

internal static class Sdl3Smoke
{
    /// <summary>
    /// Returns the SDL version number reported by the native SDL3 library,
    /// proving the SDL3-CS managed binding and its native runtime both
    /// resolved and loaded correctly.
    /// </summary>
    public static int GetVersion()
    {
        return SDL.GetVersion();
    }
}
