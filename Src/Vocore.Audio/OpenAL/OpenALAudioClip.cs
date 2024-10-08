using System.Numerics;
using Silk.NET.OpenAL;

namespace Vocore.Audio.OpenAL;

internal unsafe class OpenALAudioClip : AudioClip
{
    private static readonly ALContext ALC = ALContext.GetApi();
    private static readonly AL AL = AL.GetApi();

    private readonly uint _buffer;

    public override int Channel { get; }

    public override int SampleRate { get; }

    public override int SampleCount { get; }

    public uint Buffer => _buffer;

    public OpenALAudioClip(ReadOnlySpan<float> data, int channel, int sampleRate)
    {
        Channel = channel;
        SampleRate = sampleRate;
        SampleCount = data.Length;

        _buffer = AL.GenBuffer();

        fixed (float* ptr = data)
        {
            AL.BufferData(_buffer, UtilsOpenAL.GetBufferFormat(Channel), ptr, data.Length * sizeof(float), sampleRate);
        }
    }

    protected override void Dispose(bool disposing)
    {

    }
}