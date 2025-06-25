using System.Numerics;
using Alco.Engine;
using Alco.Audio;
using Alco;
using Alco.GUI;


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
        if (DebugStats.SliderWithText("Gain ", ref _gain, -5, 5))
        {
            _source.Gain = _gain;
        }

        if (DebugStats.SliderWithText("Pitch ", ref _pitch, 0, 2))
        {
            _source.Pitch = _pitch;
        }

        if (DebugStats.Button("play Shot.ogg"))
        {
            LoadAudioClipAsync("Shot.ogg");
        }

        if (DebugStats.Button("play Song.ogg"))
        {
            LoadAudioClipAsync("Song.ogg");
        }

        if (DebugStats.Button("play ShotPcm16.wav"))
        {
            LoadAudioClipAsync("ShotPcm16.wav");
        }

        if (DebugStats.Button("play ShotPcm24.wav"))
        {
            LoadAudioClipAsync("ShotPcm24.wav");
        }

        if (DebugStats.Button("play ShotPcm32.wav"))
        {
            LoadAudioClipAsync("ShotPcm32.wav");
        }

        if (DebugStats.Button("play Song.wav"))
        {
            LoadAudioClipAsync("Song.wav");
        }

        if (DebugStats.Button("play Song.flac"))
        {
            LoadAudioClipAsync("Song.flac");
        }

        bool isLooping = _source.IsLooping;
        if (DebugStats.CheckBoxWithText("Is Looping", ref isLooping))
        {
            _source.IsLooping = isLooping;
        }
        
        float posX = _source.Position.X;
        if (DebugStats.SliderWithText("Position X", ref posX, -100, 100))
        {
            _source.Position = new Vector3(posX, _source.Position.Y, _source.Position.Z);
        }

        float posY = _source.Position.Y;
        if (DebugStats.SliderWithText("Position Y", ref posY, -100, 100))
        {
            _source.Position = new Vector3(_source.Position.X, posY, _source.Position.Z);
        }

        float posZ = _source.Position.Z;
        if (DebugStats.SliderWithText("Position Z", ref posZ, -100, 100))
        {
            _source.Position = new Vector3(_source.Position.X, _source.Position.Y, posZ);
        }

        if (DebugStats.Button("GC"))
        {
            GC.Collect(0);
            GC.Collect(1);
            GC.Collect(2);
            GC.WaitForPendingFinalizers();
        }
    }

    private async void LoadAudioClipAsync(string filename)
    {
        AudioClip audioClip = await AssetSystem.LoadAsync<AudioClip>(filename);
        _source.AudioClip = audioClip;
        _source.Play();
    }

    protected override void OnStop()
    {
        
    }

    private static byte[] LoadFile(string path)
    {
        return File.ReadAllBytes(Path.Combine("Assets", path));
    }
}