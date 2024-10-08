using System.Numerics;
using Vocore.Engine;
using Vocore.Audio;
using Vocore;
using Vocore.GUI;


public class Game : GameEngine
{
    private AudioDevice _device;
    private AudioClip _clip;
    private AudioSource _source;

    private float _gain = 1f;
    private float _pitch = 1f;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _device = AudioDeviceFactory.CreateOpenALDevice();
        _clip = _device.CreateAudioClipFromOgg(LoadFile("Shot.ogg"));
        _source = _device.CreateSource();
        _source.Gain = 1.5f;
        _source.AudioClip = _clip;
    }

    protected override void OnUpdate(float delta)
    {
        if (DebugGUI.SliderWithText("Gain ", ref _gain, -5, 5))
        {
            _source.Gain = _gain;
        }

        if (DebugGUI.SliderWithText("Pitch ", ref _pitch, 0, 1))
        {
            _source.Pitch = _pitch;
        }

        if (DebugGUI.Button("play"))
        {
            _source.Play();
        }
    }

    protected override void OnStop()
    {
        
    }

    private static byte[] LoadFile(string path)
    {
        return File.ReadAllBytes(Path.Combine("Assets", path));
    }
}