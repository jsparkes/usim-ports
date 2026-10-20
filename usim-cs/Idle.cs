// Idle.cs - Port of usim/idle.c's WITH_SDL3 path: sleep when the machine has
// seen no keyboard, mouse or disk activity for a while.
//
// Upstream quirk, ported as is: usim's `idle_enabled` is a zero-initialised
// global that nothing ever sets to true (usim/ucfg.c only ever sets it to
// false, for a negative [idle] cycles value), so idle_check() is never called
// in the real C. Enabled therefore defaults to false here too, and the
// [idle] config cannot turn it on; it can be set programmatically.

using System;
using System.Threading;

namespace Usim;

public class Idle
{
    // usim/ucfg.defs: X(idle, cycles, "655350"), quantum "65535", timeout "1000".
    public const ulong DefaultCycles = 655350;
    public const ulong DefaultQuantum = 65535;
    public const ulong DefaultTimeoutMicros = 1000;

    private readonly UCode _ucode;
    private readonly Action<int> _sleepMs;

    private bool _isIdle;
    private ulong _lastWork;

    public bool Enabled { get; set; }
    public ulong Cycles { get; set; } = DefaultCycles;
    public ulong Quantum { get; set; } = DefaultQuantum;

    /// <summary>Length of one idle sleep, in microseconds (idle_timeout).</summary>
    public ulong TimeoutMicros { get; set; } = DefaultTimeoutMicros;

    public ulong IdleCount { get; private set; }

    public Idle(UCode ucode) : this(ucode, ms => Thread.Sleep(ms))
    {
    }

    // The sleep is injectable so tests do not really sleep.
    public Idle(UCode ucode, Action<int> sleepMs)
    {
        _ucode = ucode;
        _sleepMs = sleepMs;
    }

    /// <summary>Port of idle_init (SDL3 branch).</summary>
    public void Init()
    {
        _isIdle = false;
        _lastWork = 0;
        IdleCount = 0;
        TraceLog.Instance.Trace(TraceCategory.Idle,
            Enabled ? TraceLevel.Debug : TraceLevel.Info,
            Enabled
                ? $"idle: init cycles:{Cycles} quantum:{Quantum} timeout:{TimeoutMicros}"
                : "idle: is disabled");
    }

    public bool IsIdle => _isIdle;

    /// <summary>Port of idle_activity: records "work" at the current machine cycle.</summary>
    public void Activity()
    {
        _lastWork = _ucode.MachineCycles;
    }

    /// <summary>
    /// Port of idle_check (SDL3 branch): once no activity has been seen for
    /// more than Cycles cycles the machine is idle, and every (cycles &amp;
    /// Quantum) == 0 it sleeps for TimeoutMicros.
    /// </summary>
    public void Check(ulong cycles)
    {
        if (cycles - _lastWork > Cycles)
        {
            _isIdle = true;
            if ((cycles & Quantum) == 0)
            {
                IdleCount++;
                _sleepMs((int)((TimeoutMicros + 999) / 1000));
            }
        }
        else
        {
            _isIdle = false;
        }
    }
}
