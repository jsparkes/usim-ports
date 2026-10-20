// Tv.cs - Faithful port of usim/tv.c's main (black-and-white) TV device.
// Scoped to tv_monitor case 0 (the "CPT" monitor, the real C's default) --
// see docs/superpowers/specs/2026-10-20-tv-device-design.md. Deliberately
// headless: no reference to Display.cs/WpfBackend.cs or any WPF type.

using System;
using System.IO;

namespace Usim;

public class Tv
{
    private readonly BusInterface _busInterface;

    public uint[] ScreenBuffer { get; } = new uint[0x8000];
    public uint Width { get; } = 768;
    public uint Height { get; } = 896;
    public uint Mode { get; private set; }

    private readonly byte[] _syncRam = new byte[4096];
    private uint _syncPtr;
    private uint _vertSpacing;

    public Tv(BusInterface busInterface)
    {
        _busInterface = busInterface;
    }

    public void ScreenRead(uint offset, out uint value) => value = ScreenBuffer[offset];
    public void ScreenWrite(uint offset, uint value) => ScreenBuffer[offset] = value;

    private bool SyncPromEnabled => (_vertSpacing & 0x80) == 0; // see Global Constraints: resolved discrepancy, bit 7 of vertSpacing, not Mode -- real C negates: enabled when the bit is CLEAR (tv.c:103-108's leading `!`)

    public void ControlRead(uint offset, out uint value)
    {
        switch (offset)
        {
            case 0: value = Mode; return;
            case 1: value = SyncPromEnabled ? 0u : _syncRam[_syncPtr]; return;
            default:
                _busInterface.SetXbusNxm();
                value = 0; // documented deviation -- real C leaves *pv untouched
                return;
        }
    }

    public void ControlWrite(uint offset, uint value)
    {
        switch (offset)
        {
            case 0: Mode = value; return; // no masking -- see Global Constraints
            case 1:
                if (!SyncPromEnabled) _syncRam[_syncPtr] = (byte)(value & 0xFF);
                return;
            case 2: _syncPtr = value & 0x0FFF; return;
            case 3: _vertSpacing = value; return;
            default:
                _busInterface.SetXbusNxm();
                return;
        }
    }

    public void Reset()
    {
        Mode = 0;
        Array.Clear(ScreenBuffer);
    }

    /// <summary>
    /// Faithful port of tv_save_screenshot (usim/tv.c) -- a plain-text PBM
    /// (P1 ASCII bitmap) image, bit-for-bit from ScreenBuffer's raw words.
    /// Iterates exactly Width*Height bits, not the full ScreenBuffer length
    /// -- see Global Constraints.
    /// </summary>
    public void SaveScreenshotPbm(string filename)
    {
        using var writer = new StreamWriter(filename);
        writer.Write("P1\n");
        writer.Write($"{Width} {Height}\n");
        for (uint i = 0; i < Width * Height; i++)
        {
            uint offset = i / 32;
            int bitpos = (int)(i % 32);
            bool bit = (ScreenBuffer[offset] & (1u << bitpos)) != 0;
            writer.Write(bit ? '1' : '0');
            if (i % 70 == 0 && i > 0) writer.Write('\n');
        }
    }
}
