using System.Numerics;
using Alco.Engine;
using Alco.Audio;
using Alco;
using Alco.GUI;
using Alco.ImGUI;


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
        ImGui.Begin("Audio Controls");

        if (ImGui.SliderFloat("Gain", ref _gain, -5, 5))
        {
            _source.Gain = _gain;
        }

        if (ImGui.SliderFloat("Pitch", ref _pitch, 0, 2))
        {
            _source.Pitch = _pitch;
        }

        if (ImGui.Button("play Shot.ogg"))
        {
            LoadAudioClipAsync("Shot.ogg");
        }

        if (ImGui.Button("play Song.ogg"))
        {
            LoadAudioClipAsync("Song.ogg");
        }

        if (ImGui.Button("play ShotPcm16.wav"))
        {
            LoadAudioClipAsync("ShotPcm16.wav");
        }

        if (ImGui.Button("play ShotPcm24.wav"))
        {
            LoadAudioClipAsync("ShotPcm24.wav");
        }

        if (ImGui.Button("play ShotPcm32.wav"))
        {
            LoadAudioClipAsync("ShotPcm32.wav");
        }

        if (ImGui.Button("play Song.wav"))
        {
            LoadAudioClipAsync("Song.wav");
        }

        if (ImGui.Button("play Song.flac"))
        {
            LoadAudioClipAsync("Song.flac");
        }

        bool isLooping = _source.IsLooping;
        if (ImGui.Checkbox("Is Looping", ref isLooping))
        {
            _source.IsLooping = isLooping;
        }

        bool isSpatial = _source.IsSpatial;
        if (ImGui.Checkbox("Is Spatial", ref isSpatial))
        {
            _source.IsSpatial = isSpatial;
        }
        
        float posX = _source.Position.X;
        if (ImGui.SliderFloat("Position X", ref posX, -100, 100))
        {
            _source.Position = new Vector3(posX, _source.Position.Y, _source.Position.Z);
        }

        float posY = _source.Position.Y;
        if (ImGui.SliderFloat("Position Y", ref posY, -100, 100))
        {
            _source.Position = new Vector3(_source.Position.X, posY, _source.Position.Z);
        }

        float posZ = _source.Position.Z;
        if (ImGui.SliderFloat("Position Z", ref posZ, -100, 100))
        {
            _source.Position = new Vector3(_source.Position.X, _source.Position.Y, posZ);
        }

        if (ImGui.Button("GC"))
        {
            GC.Collect(0);
            GC.Collect(1);
            GC.Collect(2);
            GC.WaitForPendingFinalizers();
        }

        ImGui.End();
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