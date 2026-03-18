using Alco.Audio;

namespace Alco.Engine;

public partial class GameEngine
{
    private AudioDevice CreateAudioDevice(AudioSetting setting)
    {
        if (setting.Backend == AudioBackend.None)
        {
            return AudioDeviceFactory.GetNoAudioDevice();
        }

        return AudioDeviceFactory.CreateOpenALDevice(this);
    }
}
