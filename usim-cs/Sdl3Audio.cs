// Sdl3Audio.cs - SDL3-CS port of sdl3-audio.c's beep generation.
//
// Faithful port of sdl3_audio_init/sdl3_audio_internal_beep/
// sdl3_audio_fill_stream/sdl3_audio_quit, using an SDL3 audio stream opened
// with SDL_OpenAudioDeviceStream (here: SDL.OpenAudioDeviceStream) against
// the default playback device, mono 16-bit signed PCM at 8000 Hz. The CADR
// only ever uses this to generate short beep tones (see MachineControl.cs's
// Iob.OnBeep wiring), so this class exposes that single Beep(int, int)
// operation plus Init()/Dispose() for lifecycle management.
//
// The ASCII-beep fallback (sdl3_audio_use_ascii_beep in the C) is out of
// scope: real C defaults it to false and nothing in usim-cs turns it on.

using System;
using System.Runtime.InteropServices;
using SDL3;

namespace Usim;

public class Sdl3Audio : IDisposable
{
    private const int WantSampleRate = 8000; // WANT_SAMPLE_RATE
    private const double BeepAmplitudeFraction = 0.8; // sdl3_audio_beep_amplitude
    private const int BufferSize = 16 * 1024; // BUFFER_SIZE

    private IntPtr _stream = IntPtr.Zero;
    private readonly short[] _buffer = new short[BufferSize];

    // these are set only at init, mirroring the real C's max_amplitude/dt
    private double _maxAmplitude;
    private double _dt;

    // these change depending on the beep calls, mirroring the real C's f/t
    private double _f;
    private double _t;

    private int _lastWavelengthInUs;

    /// <summary>
    /// Opens the default playback device as an SDL3 audio stream (mono,
    /// 16-bit signed, 8000 Hz requested) and resumes it. Matches real C's
    /// sdl3_audio_init: failure to open or validate a device is logged as a
    /// warning (not thrown) and returns false, matching real C's three
    /// "return false" sites exactly -- callers are expected to treat false
    /// as non-fatal and continue without audio (see usim.c:567-570), but
    /// this function itself reports the failure via its return value.
    /// </summary>
    public bool Init()
    {
        if (!SDL.InitSubSystem(SDL.InitFlags.Audio))
        {
            TraceLog.Instance.Warning(TraceCategory.Keyboard,
                $"sdl3: failed to init audio subsystem: {SDL.GetError()}");
        }

        var want = new SDL.AudioSpec
        {
            Format = SDL.AudioFormat.AudioS16LE,
            Channels = 1,
            Freq = WantSampleRate
        };

        _stream = SDL.OpenAudioDeviceStream(SDL.AudioDeviceDefaultPlayback, in want, null, IntPtr.Zero);

        if (_stream == IntPtr.Zero)
        {
            // this is a warning because it is not so much critical for usim to function
            TraceLog.Instance.Warning(TraceCategory.Keyboard,
                $"sdl3: failed to open audio device stream: {SDL.GetError()}");
            return false;
        }

        if (!SDL.GetAudioStreamFormat(_stream, out var spec, out _))
        {
            TraceLog.Instance.Warning(TraceCategory.Keyboard,
                $"sdl3: failed to get audio stream format: {SDL.GetError()}");
            DestroyStream();
            return false;
        }

        // different spec.freq does not matter -- the audio functions below
        // use spec.freq, not WANT_SAMPLE_RATE
        if (spec.Channels != want.Channels || spec.Format != want.Format)
        {
            TraceLog.Instance.Warning(TraceCategory.Keyboard, "sdl3: failed to use required audio specs");
            DestroyStream();
            return false;
        }

        _maxAmplitude = 32767.0 * BeepAmplitudeFraction;
        _dt = 1.0 / spec.Freq;

        if (!SDL.ResumeAudioStreamDevice(_stream))
        {
            TraceLog.Instance.Warning(TraceCategory.Keyboard, "sdl3: failed to resume audio stream device");
        }

        return true;
    }

    /// <summary>
    /// Generates a beep tone. halfWavelengthMicros/durationMicros are both
    /// in microseconds, matching real C's sdl3_audio_beep's reading of
    /// M-C (half-wavelength) and M-1 (duration) -- the wavelength used for
    /// the sine wave's frequency is halfWavelengthMicros * 2, matching
    /// real C's sdl3_audio_internal_beep(half_wavelength_in_us * 2, ...)
    /// call and BeepWavBuilder.cs's frequency formula.
    /// </summary>
    public void Beep(int halfWavelengthMicros, int durationMicros)
    {
        InternalBeep(halfWavelengthMicros * 2, durationMicros);
    }

    private void InternalBeep(int wavelengthInUs, int durationInUs)
    {
        // might be disabled if the device failed to open in Init()
        if (_stream == IntPtr.Zero) return;

        // if wavelength thus f changes, flush stream to device
        // this may insert a small silent padding
        // so this should not be done for consecutive calls of the same wavelength
        if (wavelengthInUs != _lastWavelengthInUs)
        {
            SDL.FlushAudioStream(_stream);
            _f = 1_000_000.0 / wavelengthInUs;
            // should we reset t here or not
            // if we reset, the next sample will jump
            // if not, the playback of new freq will start not from 0
        }
        _lastWavelengthInUs = wavelengthInUs;

        double duration = durationInUs / 1_000_000.0;

        FillStream(duration);

        // do not wait for duration less than 100ms
        // this is a dirty hack
        // CADR calls beep with short durations, which breaks the audio buffer
        // this is to prevent it
        if (duration < 0.1) return;

        SDL.Delay((uint)(durationInUs / 1000));
    }

    private void FillStream(double duration)
    {
        double tStop = _t + duration;

        int idx = 0;

        while (_t < tStop)
        {
            _buffer[idx++] = (short)(_maxAmplitude * Math.Sin(2.0 * Math.PI * _f * _t));
            _t += _dt;
            if (idx == BufferSize)
            {
                PutBuffer(idx);
                idx = 0;
            }
        }
        if (idx > 0)
        {
            PutBuffer(idx);
        }
    }

    private void PutBuffer(int sampleCount)
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(((ReadOnlySpan<short>)_buffer)[..sampleCount]);
        SDL.PutAudioStreamData(_stream, bytes, bytes.Length);
    }

    private void DestroyStream()
    {
        if (_stream != IntPtr.Zero)
        {
            SDL.PauseAudioStreamDevice(_stream);
            SDL.DestroyAudioStream(_stream);
            _stream = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        DestroyStream();
        GC.SuppressFinalize(this);
    }
}
