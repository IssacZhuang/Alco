using Silk.NET.OpenAL;

namespace Vocore.Audio.OpenAL;

internal class OpenALAudioBuffer : AudioBuffer
{
    protected static readonly AL Al = AL.GetApi();
    private readonly uint _handle;

    public OpenALAudioBuffer(uint handle)
    {
        _handle = handle;
    }

    public override unsafe void SetData(AudioBufferFormat format, void* data, int size, int frequency)
    {
        Al.BufferData(_handle, UitlsOpenAL.BufferFormatToOpenAL(format), data, size, frequency);
    }

    protected override void Dispose(bool disposing)
    {
        Al.DeleteBuffer(_handle);
    }
}