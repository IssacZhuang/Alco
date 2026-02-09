using Alco.Audio.NoAudio;
using Alco.Audio.OpenAL;

namespace Alco.Audio;

public static class AudioDeviceFactory
{
    public static AudioDevice GetNoAudioDevice()
    {
        return new NoAudioDevice();
    }

    public static AudioDevice CreateOpenALDevice(IAudioDeviceHost host)
    {
        return new OpenALDevice(host);
    }
}