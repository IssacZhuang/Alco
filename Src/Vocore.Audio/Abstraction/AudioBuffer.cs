namespace Vocore.Audio;

public unsafe abstract class AudioBuffer : BaseAudioObject
{
    public AudioBuffer()
    {
    }

    public abstract void SetData(AudioBufferFormat format, void* data, int size, int frequency);

    public void SetData<T>(AudioBufferFormat format, T[] data, int frequency) where T : unmanaged
    {
        fixed (void* ptr = data)
        {
            SetData(format, ptr, data.Length * sizeof(T), frequency);
        }
    }
}