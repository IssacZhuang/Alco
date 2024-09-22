using SteamAudio;

using static SteamAudio.IPL;

namespace Vocore.Audio.Steam;

internal class SteamAudioBuffer : AudioBuffer
{
    private readonly SteamAudioDevice _device;
    private IPL.AudioBuffer _native;
    private bool _allocated;

    public SteamAudioBuffer(SteamAudioDevice device)
    {
        _device = device;

    }

    public override unsafe void SetData(AudioBufferFormat format, void* data, int size, int frequency)
    {
        TryFreeBuffer();
    }

    protected override void Dispose(bool disposing)
    {
        TryFreeBuffer();
    }

    private void TryFreeBuffer()
    {
        if (!_allocated)
        {
            return;
        }

        AudioBufferFree(_device.Context, ref _native);
    }

    private void AllocateBuffer()
    {
        
    }
}