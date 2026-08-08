using System.Numerics;
using Alco.Engine;
using Alco.Audio;
using Alco;
using Alco.GUI;
using Alco.ImGUI;
using Alco.Rendering;


public class Game : GameEngine
{
    private readonly ForwardPipeline _mainPipeline;
    private readonly ImGUISystem _imGuiSystem;
    private readonly AudioSource _source;

    private float _gain = 1f;
    private float _pitch = 1f;

    private AudioStream? _stream;
    private float _streamGain = 1f;
    private float _streamPitch = 1f;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _mainPipeline = new ForwardPipeline(
            RenderingSystem,
            RenderingSystem.PreferredHDRPass,
            BuiltInAssets.Shader_Blit,
            MainView.Size.X,
            MainView.Size.Y);

        var tonemapNode = new RenderNode_Tonemap(
            RenderingSystem,
            BuiltInAssets.Shader_Blit,
            BuiltInAssets.Shader_ReinhardLuminanceTonemap,
            BuiltInAssets.Shader_Uncharted2Tonemap,
            BuiltInAssets.Shader_FilmicTonemap,
            BuiltInAssets.Shader_ACESTonemap,
            BuiltInAssets.Shader_NeutralTonemap,
            BuiltInAssets.Shader_AgXTonemap);
        _mainPipeline.Use(tonemapNode);

        MainPresenter.OnResize += size => _mainPipeline.Resize(size.X, size.Y);

        _imGuiSystem = new ImGUISystem(this);

        _source = AudioDevice.CreateAudioSource();
        _source.Gain = 1.5f;
    }

    protected override void OnUpdate(float delta)
    {
        _imGuiSystem.BeginFrame(delta);
        _imGuiSystem.UpdateInput();

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

        ImGui.Separator();
        ImGui.Text("Streaming BGM");

        if (ImGui.Button("stream Song.ogg"))
        {
            // Stream-based construction: the OGG is loaded on a background thread and decoded off the
            // main thread, so this click returns immediately and audio starts as soon as the load lands.
            Stream fileStream = new FileStream(Path.Combine("Assets", "Song.ogg"), FileMode.Open, FileAccess.Read, FileShare.Read);
            var provider = new VorbisStreamProvider(fileStream);
            _stream?.Dispose();
            _stream = AudioDevice.CreateAudioStream(provider);
            _stream.IsSpatial = false;
            _stream.IsLooping = true;
            _stream.Gain = _streamGain;
            _stream.Pitch = _streamPitch;
            _stream.Play();
        }

        if (ImGui.Button("stream Play"))
        {
            _stream?.Play();
        }

        if (ImGui.Button("stream Pause"))
        {
            _stream?.Pause();
        }

        if (ImGui.Button("stream Stop"))
        {
            _stream?.Stop();
        }

        if (ImGui.SliderFloat("stream Gain", ref _streamGain, 0, 2))
        {
            if (_stream != null) _stream.Gain = _streamGain;
        }

        if (ImGui.SliderFloat("stream Pitch", ref _streamPitch, 0, 2))
        {
            if (_stream != null) _stream.Pitch = _streamPitch;
        }

        bool streamLooping = _stream?.IsLooping ?? true;
        if (ImGui.Checkbox("stream Is Looping", ref streamLooping))
        {
            if (_stream != null) _stream.IsLooping = streamLooping;
        }

        bool streamSpatial = _stream?.IsSpatial ?? false;
        if (ImGui.Checkbox("stream Is Spatial", ref streamSpatial))
        {
            if (_stream != null) _stream.IsSpatial = streamSpatial;
        }

        ImGui.Text(_stream == null ? "stream: none" : $"stream: {_stream.State}");

        if (ImGui.Button("GC"))
        {
            GC.Collect(0);
            GC.Collect(1);
            GC.Collect(2);
            GC.WaitForPendingFinalizers();
        }

        ImGui.End();
    }

    protected override void OnEndFrame()
    {
        _mainPipeline.Render(MainPresenter.FrameBuffer);
        _imGuiSystem.RenderAndDraw(MainPresenter.FrameBuffer);
    }

    private async void LoadAudioClipAsync(string filename)
    {
        AudioClip audioClip = await AssetSystem.LoadAsync<AudioClip>(filename);
        _source.AudioClip = audioClip;
        _source.Play();
    }

    protected override void OnStop()
    {
        _mainPipeline.Dispose();
    }

    private static byte[] LoadFile(string path)
    {
        return File.ReadAllBytes(Path.Combine("Assets", path));
    }
}