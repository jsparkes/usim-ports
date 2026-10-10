// Iob.cs - Faithful port of usim/iob.c's WITH_SDL3 code path (the CADR I/O
// board). Real Chaosnet networking (usim/uch11.c) is a separate, future
// port; the four Chaos registers real boot activity touches get a minimal,
// safe idle stub here. The keyboard-triggered cold/warm-reboot hotkey is
// dropped entirely -- see docs/superpowers/specs/2026-09-18-iob-design.md.

using System;
using System.Diagnostics;

namespace Usim;

public class Iob
{
    private const uint ChaosMyAddrPlaceholder = 0xFE20; // 0177040 octal = 0xFE20, uch11.c's uch11_myaddr default (LOCAL-CADR)

    private readonly UCode _ucode;

    public Action<int>? OnBeep { get; set; }

    private uint _csr;
    private uint _scancode;
    private uint _mouseX;
    private uint _mouseY;
    private ushort _the60CycleClock;
    private uint _intervalTimerTicksRemaining;
    private readonly Stopwatch _usecStopwatch = new();
    private bool _usecStarted;
    private uint _usecLatched;

    public Iob(UCode ucode)
    {
        _ucode = ucode;
    }

    /// <summary>Port of iob_is_keyboard_ready_set: CSR bit 5.</summary>
    public bool IsKeyboardReadySet => (_csr & (1u << 5)) != 0;

    private bool IsKeyboardInterruptEnabled() => (_csr & 0x4) != 0;
    private bool IsMouseInterruptEnabled() => (_csr & 0x2) != 0;

    /// <summary>
    /// Faithful port of iob_bus_reset (usim/iob.c:428-431), which has a
    /// genuinely empty body -- IOB state is never cleared by any reset in
    /// the real C. Kept as an explicit method purely for wiring symmetry
    /// with Tv/ColorTv.
    /// </summary>
    public void Reset()
    {
    }

    /// <summary>
    /// Faithful port of iob_set_keyboard_ready (usim/iob.c:88-107, WITH_SDL3
    /// branch), minus the cold/warm-boot sentinel check (that hotkey
    /// mechanism is dropped -- see the design's Decisions). Called by
    /// Keyboard's queue once the keyboard is free (IsKeyboardReadySet clear).
    /// </summary>
    public void SetKeyboardReady(uint scancode)
    {
        _scancode = scancode;
        _csr |= 1u << 5;

        if (IsKeyboardInterruptEnabled())
        {
            _ucode.AssertUnibusInterrupt(0xB0);
        }
    }

    /// <summary>
    /// Faithful port of iob_set_mouse_ready (usim/iob.c:112-132, WITH_SDL3
    /// branch). rawX/rawY are always 0 -- WPF delivers discrete positions,
    /// not raw quadrature-encoder ticks, so there is no realistic value to
    /// synthesize (see the design's Decisions).
    /// </summary>
    public void SetMouseReady(int x, int y, bool tailPressed, bool middlePressed, bool headPressed)
    {
        _mouseX = (uint)(x & 0x7777);
        _mouseY = ((tailPressed ? 1u : 0u) << 12)
            | ((middlePressed ? 1u : 0u) << 13)
            | ((headPressed ? 1u : 0u) << 14)
            | (uint)(y & 0x7777);
        _csr |= 1u << 4;

        if (IsMouseInterruptEnabled())
        {
            _ucode.AssertUnibusInterrupt(0xB4);
        }
    }

    /// <summary>Faithful port of iob_unibus_read (usim/iob.c:138-303, WITH_SDL3 branch + shared registers). Offsets are relative to IobLo (0764000 octal = 0x3E800).</summary>
    public ushort Read(uint offset)
    {
        switch (offset)
        {
            case 0x40: // kbd low (0764100)
                {
                    ushort v = (ushort)(_scancode & 0xFFFF);
                    _csr &= ~(1u << 5);
                    return v;
                }

            case 0x42: // kbd high (0764102)
                return (ushort)((_scancode >> 16) & 0xFFFF);

            case 0x44: // mouse y (0764104)
                {
                    ushort v = (ushort)_mouseY;
                    _csr &= ~(1u << 4);
                    return v;
                }

            case 0x46: // mouse x (0764106)
                return (ushort)_mouseX;

            case 0x48: // beep (0764110)
                OnBeep?.Invoke(744); // usim/iob.c:209-211's own fixed default: 744us half-wavelength (~672Hz)
                return 0;

            case 0x4A: // csr (0764112)
                return (ushort)_csr;

            case 0x50: // usec clock low (0764120): samples and latches the clock
                _usecLatched = (uint)ElapsedMicroseconds();
                return (ushort)(_usecLatched & 0xFFFF);

            case 0x52: // usec clock high (0764122): value latched by the last low read
                return (ushort)((_usecLatched >> 16) & 0xFFFF);

            case 0x54: // 60Hz clock (0764124)
                return _the60CycleClock;

            case 0x56: // gpio (0764126)
                TraceLog.Instance.Warning(TraceCategory.IOBus, "iob: read from gpio");
                return 0;

            case 0x60: // ch_csr (0764140)
                return 0;

            case 0x62: // ch_myaddr (0764142)
                return (ushort)ChaosMyAddrPlaceholder;

            case 0x64: // ch_rbf (0764144)
            case 0x66: // ch_rbc (0764146)
                return 0;

            case 0x68: // ch_nop (0764150, obsolete)
            case 0x6C: // obsolete chaos (0764154)
            case 0x6E: // obsolete chaos (0764156)
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: chaos read obsolete unibus address: 0x{offset:X}");
                return 0;

            case 0x6A: // ch_xmt (0764152)
                return (ushort)ChaosMyAddrPlaceholder;

            case 0x70: // serial (0764160)
            case 0x72: // serial (0764162)
            case 0x74: // serial (0764164)
            case 0x76: // serial (0764166)
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: read from serial interface: 0x{offset:X}");
                return 0;

            case 0x78: // undocumented (0764170)
            case 0x7A: // undocumented (0764172)
            case 0x7C: // undocumented (0764174)
            case 0x7E: // undocumented (0764176)
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: read from unknown device: 0x{offset:X}");
                return 0;

            default:
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: read from unknown offset: 0x{offset:X}");
                return 0;
        }
    }

    /// <summary>Faithful port of iob_unibus_write (usim/iob.c:307-384). Offsets are relative to IobLo (0764000 octal = 0x3E800).</summary>
    public void Write(uint offset, ushort v)
    {
        switch (offset)
        {
            case 0x40: // kbd low (0764100)
            case 0x42: // kbd high (0764102)
            case 0x44: // mouse y (0764104)
            case 0x46: // mouse x (0764106)
                break; // real C: DEBUG trace only, no state change

            case 0x48: // beep (0764110)
                OnBeep?.Invoke(v);
                break;

            case 0x4A: // csr (0764112)
                _csr = (_csr & ~0xFu) | (v & 0xFu);
                break;

            case 0x50: // usec clock low (0764120)
            case 0x52: // usec clock high (0764122)
                break; // real C: DEBUG trace only, no state change (read-only in practice)

            case 0x54: // interval timer set (0764124)
                {
                    const double tickMicroseconds = 16000.0; // ~16ms per Tick(), matching WpfBackend/the headless loop
                    _intervalTimerTicksRemaining = (uint)Math.Ceiling(16.0 * v / tickMicroseconds);
                }
                break;

            case 0x60: // ch_csr (0764140)
            case 0x62: // ch_myaddr write (0764142; buffer write in the real C)
            case 0x64: // ch_rbf (0764144)
            case 0x66: // ch_rbc (0764146)
            case 0x68: // ch_nop (0764150)
            case 0x6A: // ch_xmt (0764152)
                break; // accepted, ignored -- real Chaosnet networking is out of scope

            case 0x70: // serial (0764160)
            case 0x72: // serial (0764162)
            case 0x74: // serial (0764164)
            case 0x76: // serial (0764166)
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: uart write: 0x{offset:X} v=0x{v:X}");
                break;

            default:
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: write to unknown offset: 0x{offset:X} v=0x{v:X}");
                break;
        }
    }

    /// <summary>
    /// Faithful port of run_at_60hz's the_60_cycle_clock++ (usim/usim.c) plus
    /// the tick-counted interval-timer approximation (see the design).
    /// </summary>
    public void Tick()
    {
        _the60CycleClock++;

        if (_intervalTimerTicksRemaining > 0)
        {
            _intervalTimerTicksRemaining--;
            if (_intervalTimerTicksRemaining == 0)
            {
                _ucode.AssertUnibusInterrupt(0xBC);
            }
        }
    }

    private long ElapsedMicroseconds()
    {
        if (!_usecStarted)
        {
            _usecStopwatch.Start();
            _usecStarted = true;
            return 0;
        }

        return _usecStopwatch.Elapsed.Ticks / 10; // Stopwatch ticks are 100ns
    }
}
