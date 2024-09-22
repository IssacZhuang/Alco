using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Vocore.Audio;

public unsafe class PulseCodeModulation : BaseAudioObject
{
    private readonly float* _data;
    private readonly uint _size;
    private readonly int _frequency;

    internal PulseCodeModulation(float* data, uint size, int frequency)
    {
        _data = data;
        _size = size;
        _frequency = frequency;
    }

    public static PulseCodeModulation CreateByRaw(ReadOnlySpan<float> data, int frequency)
    {
        var ptr = (float*)Marshal.AllocHGlobal(data.Length * sizeof(float));
        fixed (float* p = data)
        {
            Unsafe.CopyBlock(ptr, p, (uint)(data.Length * sizeof(float)));
        }

        return new PulseCodeModulation(ptr, (uint)data.Length, frequency);
    }

   

    protected override void Dispose(bool disposing)
    {
        Marshal.FreeHGlobal((IntPtr)_data);
    }
}