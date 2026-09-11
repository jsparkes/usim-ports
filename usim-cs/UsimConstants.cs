// UsimConstants.cs - Core constants and system definitions
// Converted from usim.h

using System;

namespace Usim;

/// <summary>
/// Supported Lisp Machine system versions
/// </summary>
public static class LispMSystemVersions
{
    public const long LISPM_SYSTEM78 = 7800L;
    public const long LISPM_SYSTEM98 = 9800L;
    public const long LISPM_SYSTEM99 = 9900L;
    public const long LISPM_SYSTEM300 = 9999L;  // Yeah, I know...
    
    public const long LISPM_SYSTEM = LISPM_SYSTEM300;
}

/// <summary>
/// System configuration constants
/// </summary>
public static class UsimConstants
{
    public const int Q_POINTER_WIDTH = LispMSystemVersions.LISPM_SYSTEM >= 9800L ? 031 : 030;
    
    public const int O_BINARY = 0;  // Windows-specific flag
    public const int BLOCKSZ = 256 * 4;
}

/// <summary>
/// Global configuration state for USIM
/// </summary>
public static class UsimState
{
    public static string StateFilename { get; set; } = string.Empty;
    public static string SysDirectory { get; set; } = string.Empty;
    public static string FsRootDirectory { get; set; } = string.Empty;

    public static string WindowTitle { get; set; } = string.Empty;
    public static bool ApplyNewWindowTitle { get; set; }

    public static bool ColorTvEnabled { get; set; }
    public static bool VerboseDumpStateFlag { get; set; }
    public static bool WarmBootFlag { get; set; }
    public static string? WarmBootFilename { get; set; }
    public static bool Headless { get; set; }
    public static bool AutoBoot { get; set; }
    public static bool AutoPowerOff { get; set; }

    public static (uint Unit, string TypeName, string Filename)[] DiskUnits { get; set; } = Array.Empty<(uint, string, string)>();

    public static uint TvWidth { get; set; } = 768;
    public static uint TvHeight { get; set; } = 896;
}
