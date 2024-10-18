using System.Numerics;
using Vocore.Engine;
using Vocore.Audio;
using Vocore;
using Vocore.GUI;


public class Game : GameEngine
{
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

                _source.AudioClip = asset;
                _source.Play();
            });
        }
        
        if (DebugGUI.Button("play Shot.ogg"))
        {
            Assets.LoadAsync<AudioClip>("Shot.ogg", (asset, e) =>
            {
                _source.AudioClip = asset;
                _source.Play();
            });
        }

        if (DebugGUI.Button("play Song.ogg"))
        {
            Assets.LoadAsync<AudioClip>("Song.ogg", (asset, e) =>
            {
                _source.AudioClip = asset;
                _source.Play();
            });
        }

        if (DebugGUI.Button("play ShotPcm16.wav"))
        {
            Assets.LoadAsync<AudioClip>("ShotPcm16.wav", (asset, e) =>
            {
                _source.AudioClip = asset;
                _source.Play();
            });
        }

        if (DebugGUI.Button("play ShotPcm24.wav"))
        {
            Assets.LoadAsync<AudioClip>("ShotPcm24.wav", (asset, e) =>
            {
                _source.AudioClip = asset;
                _source.Play();
            });
        }

        if (DebugGUI.Button("play ShotPcm32.wav"))
        {
            Assets.LoadAsync<AudioClip>("ShotPcm32.wav", (asset, e) =>
            {
                _source.AudioClip = asset;
                _source.Play();
            });
        }

        if (DebugGUI.Button("play Song.wav"))
        {
            Assets.LoadAsync<AudioClip>("Song.wav", (asset, e) =>
            {
                _source.AudioClip = asset;
                _source.Play();
            });
        }

        bool isLooping = _source.IsLooping;
        if(DebugGUI.CheckBoxWithText("Is Looping", ref isLooping))
        {
            _source.IsLooping = isLooping;
        }
        
        float posX = _source.Position.X;
        if (DebugGUI.SliderWithText("Position X", ref posX, -100, 100))
        {
            _source.Position = new Vector3(posX, _source.Position.Y, _source.Position.Z);
        }

        float posY = _source.Position.Y;
        if (DebugGUI.SliderWithText("Position Y", ref posY, -100, 100))
        {
            _source.Position = new Vector3(_source.Position.X, posY, _source.Position.Z);
        }

        float posZ = _source.Position.Z;
        if (DebugGUI.SliderWithText("Position Z", ref posZ, -100, 100))
        {
            _source.Position = new Vector3(_source.Position.X, _source.Position.Y, posZ);
        }

        if (DebugGUI.Button("GC"))
        {
            GC.Collect(0);
            GC.Collect(1);
            GC.Collect(2);
            GC.WaitForPendingFinalizers();
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