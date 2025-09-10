using System.Numerics;
using Silk.NET.OpenAL;
using Alco;
using Alco.Audio;

namespace Alco.Audio.OpenAL;

internal unsafe class OpenALAudioClip : AudioClip
{
    private static readonly ALContext ALC = ALContext.GetApi();
    private static readonly AL AL = AL.GetApi();

    private readonly uint _buffer;
    private readonly string _name;

    public override int Channel { get; }

    public override int SampleRate { get; }

    public override int SampleCount { get; }

    public uint Buffer => _buffer;

    public override string Name => _name;

    private readonly OpenALDevice _device;

    public OpenALAudioClip(OpenALDevice device, ReadOnlySpan<float> data, int channel, int sampleRate, string? name = null)
    {
        _name = name ?? string.Empty;
        _device = device;
        float* ptrMono = null;
        try
        {
            ReadOnlySpan<float> workingData = data;
            int workingChannel = channel;

            // If stereo but AL_SOFT_source_spatialize is not available, downmix to mono
            if (channel == 2 && !AL.IsExtensionPresent("AL_SOFT_source_spatialize"))
            {
                ptrMono = (float*)UtilsMemory.Alloc(data.Length * sizeof(float) / 2);
                Span<float> monoSpan = new(ptrMono, data.Length / 2);
                UtilsAudioDecode.StereoToMono(data, monoSpan);
                workingData = monoSpan;
                workingChannel = 1;

                _device.LogWarning("AL_SOFT_source_spatialize is not supported, downmix stereo to mono for spatialization");
            }

            Channel = workingChannel;
            SampleRate = sampleRate;
            SampleCount = workingData.Length;

            _buffer = AL.GenBuffer();

            fixed (float* ptr = workingData)
            {
                AL.BufferData(_buffer, UtilsOpenAL.GetBufferFormat(Channel), ptr, workingData.Length * sizeof(float), sampleRate);
            }
        }
        finally
        {
            if (ptrMono != null)
            {
                UtilsMemory.Free(ptrMono);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        AL.DeleteBuffer(_buffer);
    }
}