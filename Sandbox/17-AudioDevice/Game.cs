using System.Numerics;
using Vocore.Engine;
using Vocore.Audio;
using Vocore;
using Vocore.Audio.OpenAL;

public class Game : GameEngine
{

    public Game(GameEngineSetting setting) : base(setting)
    {
        foreach (string name in OpenALAudioDevice.GetDeviceNames())
        {
            Log.Info(name);
        }

        Log.Info("Default Device: " + OpenALAudioDevice.GetDefaultDeviceName());
    }

    protected override void OnUpdate(float delta)
    {
        
    }

    protected override void OnStop()
    {
        
    }
}