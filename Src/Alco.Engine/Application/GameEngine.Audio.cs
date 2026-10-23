using Alco.Audio;

namespace Alco.Engine;

/// <summary>
/// Audio device creation for the engine.
/// </summary>
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
