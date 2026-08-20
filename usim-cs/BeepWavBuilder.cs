// BeepWavBuilder.cs - Builds an in-memory WAV (PCM) buffer for the CADR's
// beep tone, for playback via System.Media.SoundPlayer under WPF.

using System;
using System.IO;
using System.Text;

namespace Usim;

public static class BeepWavBuilder
{
    public const int SampleRate = 44100;
    private const short Amplitude = 28000;

    /// <summary>
    /// Build a mono 16-bit PCM WAV buffer containing a sine wave at the
    /// frequency implied by halfWavelengthMicros, for durationMicros.
    /// Matches the waveform formula SDL2Backend.AudioCallback used.
    /// </summary>
    public static byte[] BuildWav(int halfWavelengthMicros, int durationMicros)
    {
        double frequency = 1_000_000.0 / (halfWavelengthMicros * 2);
        int sampleCount = (int)(SampleRate * (durationMicros / 1_000_000.0));

        var pcm = new short[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            double time = i / (double)SampleRate;
            pcm[i] = (short)(Amplitude * Math.Sin(2.0 * Math.PI * frequency * time));
        }

        return WrapPcmInWavContainer(pcm);
    }

    private static byte[] WrapPcmInWavContainer(short[] pcm)
    {
        int dataSize = pcm.Length * 2;
        int fileSize = 36 + dataSize;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(fileSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // fmt chunk size
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2); // byte rate = SampleRate * block align
        writer.Write((short)2); // block align (16-bit mono)
        writer.Write((short)16); // bits per sample
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        foreach (short sample in pcm)
            writer.Write(sample);

        writer.Flush();
        return stream.ToArray();
    }
}
