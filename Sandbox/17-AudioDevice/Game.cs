using System.Numerics;
using Vocore.Engine;
using Vocore.Audio;
using Vocore;
using Vocore.GUI;


public class Game : GameEngine
{
    private AudioClip? _clipMp3;
    private AudioClip? _clipOgg;
    private readonly AudioSource _source;

    private float _gain = 1f;
    private float _pitch = 1f;

    public Game(GameEngineSetting setting) : base(setting)
    {

        
        _source = AudioDevice.CreateAudioSource();
        _source.Gain = 1.5f;
    }

    protected override void OnUpdate(float delta)
    {
        if (DebugGUI.SliderWithText("Gain ", ref _gain, -5, 5))
        {
            _source.Gain = _gain;
        }

        if (DebugGUI.SliderWithText("Pitch ", ref _pitch, 0, 2))
        {
            _source.Pitch = _pitch;
        }

        if (DebugGUI.Button("play Sword.mp3"))
        {
            Assets.LoadAsync<AudioClip>("Sword.mp3", (asset, e) =>
            {
                _clipMp3 = asset;
                _source.AudioClip = _clipMp3;
                _source.Play();
            });
        }
        
        if (DebugGUI.Button("play Shot.ogg"))
        {
            Assets.LoadAsync<AudioClip>("Shot.ogg", (asset, e) =>
            {
                _clipOgg = asset;
                _source.AudioClip = _clipOgg;
                _source.Play();
            });
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