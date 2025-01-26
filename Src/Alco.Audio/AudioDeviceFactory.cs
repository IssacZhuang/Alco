using Alco.Audio.OpenAL;

namespace Alco.Audio;

public static class AudioDeviceFactory
{
    public static AudioDevice CreateOpenALDevice(IAudioDeviceHost lifeCycleProvider)
    {
        return new OpenALDevice(lifeCycleProvider);
    }
}