using Vocore.Audio.OpenAL;

namespace Vocore.Audio;

public static class AudioDeviceFactory
{
    public static AudioDevice CreateOpenALDevice(IAudioDeviceHost lifeCycleProvider)
    {
        return new OpenALDevice(lifeCycleProvider);
    }
}