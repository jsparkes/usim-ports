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
    private const uint ChaosMyAddrPlaceholder = 0x7C20; // 0177040 octal, uch11.c's LOCAL-CADR fallback

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

    public Iob(UCode ucode)
    {
        _ucode = ucode;
    }

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
    /// mechanism is dropped -- see the design's Decisions). Called directly
    /// from Keyboard's key-down path -- no queue, no retry/backoff.
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

    /// <summary>Faithful port of iob_unibus_read (usim/iob.c:138-303, WITH_SDL3 branch + shared registers).</summary>
    public ushort Read(uint offset)
    {
        switch (offset)
        {
            case 0x20: // kbd low
                {
                    ushort v = (ushort)(_scancode & 0xFFFF);
                    _csr &= ~(1u << 5);
                    return v;
                }

            case 0x22: // kbd high
                return (ushort)((_scancode >> 16) & 0xFFFF);

            case 0x24: // mouse y
                {
                    ushort v = (ushort)_mouseY;
                    _csr &= ~(1u << 4);
                    return v;
                }

            case 0x26: // mouse x
                return (ushort)_mouseX;

            case 0x28: // beep
                OnBeep?.Invoke(744); // usim/iob.c:209-211's own fixed default: 744us half-wavelength (~672Hz)
                return 0;

            case 0x2A: // csr
                return (ushort)_csr;

            case 0x30: // usec clock low
                return (ushort)(ElapsedMicroseconds() & 0xFFFF);

            case 0x32: // usec clock high
                return (ushort)((ElapsedMicroseconds() >> 16) & 0xFFFF);

            case 0x34: // 60Hz clock
                return _the60CycleClock;

            case 0x36: // gpio
                TraceLog.Instance.Warning(TraceCategory.IOBus, "iob: read from gpio");
                return 0;

            case 0x40: // ch_csr
                return 0;

            case 0x42: // ch_myaddr
                return (ushort)ChaosMyAddrPlaceholder;

            case 0x44: // ch_rbf
            case 0x46: // ch_rbc
                return 0;

            case 0x48: // ch_nop (obsolete)
            case 0x4C:
            case 0x4E:
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: chaos read obsolete unibus address: 0x{offset:X}");
                return 0;

            case 0x4A: // ch_xmt
                return (ushort)ChaosMyAddrPlaceholder;

            case 0x60: // serial
            case 0x62:
            case 0x64:
            case 0x66:
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: read from serial interface: 0x{offset:X}");
                return 0;

            case 0x68: // undocumented
            case 0x6A:
            case 0x6C:
            case 0x6E:
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: read from unknown device: 0x{offset:X}");
                return 0;

            default:
                TraceLog.Instance.Warning(TraceCategory.IOBus, $"iob: read from unknown offset: 0x{offset:X}");
                return 0;
        }
    }

    /// <summary>Faithful port of iob_unibus_write (usim/iob.c:307-384).</summary>
    public void Write(uint offset, ushort v)
    {
        switch (offset)
        {
            case 0x20: // kbd low
            case 0x22: // kbd high
            case 0x24: // mouse y
            case 0x26: // mouse x
                break; // real C: DEBUG trace only, no state change

            case 0x28: // beep
                OnBeep?.Invoke(v);
                break;

            case 0x2A: // csr
                _csr = (_csr & ~0xFu) | (v & 0xFu);
                break;

            case 0x30: // usec clock
            case 0x32:
                break; // real C: DEBUG trace only, no state change (read-only in practice)

            case 0x34: // interval timer set
                {
                    const double tickMicroseconds = 16000.0; // ~16ms per Tick(), matching WpfBackend/the headless loop
                    _intervalTimerTicksRemaining = (uint)Math.Ceiling(16.0 * v / tickMicroseconds);
                }
                break;

            case 0x40: // ch_csr
            case 0x42: // ch_myaddr write (buffer write in the real C)
            case 0x44: // ch_rbf
            case 0x46: // ch_rbc
            case 0x48: // ch_nop
            case 0x4A: // ch_xmt
                break; // accepted, ignored -- real Chaosnet networking is out of scope

            case 0x60: // serial
            case 0x62:
            case 0x64:
            case 0x66:
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
