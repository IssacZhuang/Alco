using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Vocore.Audio;

public unsafe class AudioClip : BaseAudioObject
{
    private readonly float* _data;
    private readonly int _size;
    private readonly int _channel;
    private readonly int _sampleRate;

    public float* UnsafePointer
    {
        get => _data;
    }

    /// <summary>
    /// The data size in bytes
    /// </summary>
    /// <value></value>
    public int Size
    {
        get => _size;
    }

    public int Channel
    {
        get => _channel;
    }

    public int SampleRate
    {
        get => _sampleRate;
    }

    internal AudioClip(float* data, int size, int channel, int sampleRate)
    {
        _data = data;
        _size = size;
        _channel = channel;
        _sampleRate = sampleRate;
    }

    public static AudioClip Create(ReadOnlySpan<float> data, int channel, int frequency)
    {
        var ptr = (float*)Marshal.AllocHGlobal(data.Length * sizeof(float));
        fixed (float* p = data)
        {
            Unsafe.CopyBlock(ptr, p, (uint)(data.Length * sizeof(float)));
        }

        return new AudioClip(ptr, data.Length, channel, frequency);
    }

    /// <summary>
    /// Create a new PulseCodeModulation object by raw pointer, size and frequency. The pointer will be stored in the object and will be freed when the object is disposed.
    /// <br/> Therefore, the pointer can not be freed outside of the object.
    /// </summary>
    /// <param name="data">The pointer to the data</param>
    /// <param name="size">The size of the data</param>
    /// <param name="sampleRate">The frequency of the data</param>
    /// <returns></returns>
    public static AudioClip UnsafeCreate(float* data, int size, int channel, int sampleRate)
    {
        return new AudioClip(data, size, channel, sampleRate);
    }



    protected override void Dispose(bool disposing)
    {
        Marshal.FreeHGlobal((IntPtr)_data);
    }
}