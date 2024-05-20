using System.Numerics;
using Vocore.Engine;
using Vocore.Audio;
using Vocore;

public class Game : GameEngine
{

    public Game(GameEngineSetting setting) : base(setting)
    {
        foreach(string name in AudioDevice.GetDeviceNames())
        {
            Log.Info(name);
        }

        Log.Info("Default Device: " + AudioDevice.GetDefaultDeviceName());
    }

    protected override void OnUpdate(float delta)
    {
        
    }

    protected override void OnStop()
    {
        
    }
}