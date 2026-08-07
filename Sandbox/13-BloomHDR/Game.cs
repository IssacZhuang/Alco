using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;

using Random = Alco.FastRandom;
using Alco.Graphics;
using Alco.GUI;
using Alco.ImGUI;
using Alco.IO;

using SandboxUtils;

/// <summary>
/// Sandbox sample demonstrating Bloom and HDR with runtime ImGui controls,
/// including tone mapping type switching.
/// </summary>
public class Game : GameEngine
{
    private readonly ForwardPipeline _mainPipeline;
    private ImGUISystem? _imguiSystem;

    //scence
    private readonly Camera2DBuffer _camera;

    private readonly Texture2D _quad;
    private readonly Shader _spriteShader;
    private readonly RenderContext _renderContext;
    private readonly SpriteRenderer _renderer;
    private ColorFloat _color = new ColorFloat(4, 2, 2, 1);
    private bool _enabled = true;

    private BloomStage? _bloomSystem;
    private TonemapStage? _tonemapStage;
    private TonemapType _toneMapType;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _mainPipeline = new ForwardPipeline(
            RenderingSystem,
            RenderingSystem.PreferredHDRPass,
            BuiltInAssets.Shader_Blit,
            MainView.Size.X,
            MainView.Size.Y);

        var tonemapStage = new TonemapStage(
            RenderingSystem,
            BuiltInAssets.Shader_ReinhardLuminanceTonemap,
            BuiltInAssets.Shader_Uncharted2Tonemap,
            BuiltInAssets.Shader_FilmicTonemap,
            BuiltInAssets.Shader_ACESTonemap,
            BuiltInAssets.Shader_NeutralTonemap,
            BuiltInAssets.Shader_AgXTonemap);
        _mainPipeline.PostProcess.Add(tonemapStage);
        _tonemapStage = tonemapStage;

        Bloom bloom = RenderingSystem.CreateBloom(
            BuiltInAssets.Shader_BloomBlit,
            BuiltInAssets.Shader_BloomClamp,
            BuiltInAssets.Shader_BloomDownSample,
            BuiltInAssets.Shader_BloomUpSample,
            11);
        _mainPipeline.PostProcess.Add(new BloomStage(bloom));

        MainPresenter.OnResize += size => _mainPipeline.Resize(size.X, size.Y);

        //scence
        _spriteShader = BuiltInAssets.Shader_Sprite;
       
        _quad = RenderingSystem.CreateTexture2D(4,4, 0xffffff);

        _camera = RenderingSystem.CreateCamera2D(640, 360, 100);

        Material material = RenderingSystem.CreateMaterial(_spriteShader);
        material.SetBuffer(ShaderResourceId.Camera, _camera);
        _renderContext = RenderingSystem.CreateRenderContext("renderer");
        _renderer = RenderingSystem.CreateSpriteRenderer(_renderContext, material);
    }

    public override IEnumerable<IFileSource> CreateDefaultFileSources()
    {
        foreach (var fileSource in base.CreateDefaultFileSources())
        {
            yield return fileSource;
        }
        yield return new DirectoryWatcherFileSource(Utils.GetBuiltInAssetsPath(), AssetSystem);
        yield return new DirectoryWatcherFileSource(Utils.GetProjectAssetsPath(), AssetSystem);
    }

    /// <summary>
    /// Called when the game starts; initializes references to systems/plugins used by the sample.
    /// </summary>
    protected override void OnStart()
    {
        _imguiSystem = new ImGUISystem(this);

        // Get BloomStage reference after the pipeline is initialized
        _bloomSystem = _mainPipeline.PostProcess.Get<BloomStage>();

        // Get TonemapStage reference for tone map control
        if (_tonemapStage != null)
        {
            _toneMapType = _tonemapStage.Operator;
        }
    }

    protected override void OnBeginFrame()
    {
        _mainPipeline.BeginFrame();
    }

    /// <summary>
    /// Per-frame update. Handles input, draws scene, and renders ImGui controls.
    /// </summary>
    protected override void OnUpdate(float delta)
    {
        // ImGui frame start + input (previously driven by PluginImGUI).
        _imguiSystem?.BeginFrame(delta);
        _imguiSystem?.UpdateInput();

        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        // removed intensity hotkeys; color is controlled via ImGui

        Vector2 normalizedMousePosition = Input.MousePosition / new Vector2(1280, 720);
        Vector2 spritePosition = normalizedMousePosition * new Vector2(640, 360) - new Vector2(320, 180);
        spritePosition.Y = -spritePosition.Y;

        _renderContext.Begin(_mainPipeline.SceneFrameBuffer);
        //_spriteRenderer.Draw(_star, new Vector2(0, 0), Rotation2D.Identity, Vector2.One * 20, new Vector4(1, 1, 1, 1));

        if(_enabled){
            _renderer.Draw(_quad, Vector2.Zero, Rotation2D.Identity, Vector2.One * 24, _color);
        }
       

        _renderContext.End();

        // ImGUI Controls
        ImGui.Begin("Bloom HDR Controls");

        // Color control
        ImGui.ColorEdit4("Color", ref _color, ImGuiColorEditFlags.HDR);
        ImGui.Checkbox("Enabled", ref _enabled);

        // Bloom System Controls
        if (_bloomSystem != null)
        {
            ImGui.Separator();
            ImGui.Text("Bloom System Controls");


            bool bloomEnabled = _bloomSystem.IsEnabled;
            if (ImGui.Checkbox("Bloom Enabled", ref bloomEnabled))
            {
                _bloomSystem.IsEnabled = bloomEnabled;
            }

            float threshold = _bloomSystem.Threshold;
            if (ImGui.SliderFloat("Bloom Threshold", ref threshold, 0.0f, 3.0f))
            {
                _bloomSystem.Threshold = threshold;
            }

            float spread = _bloomSystem.Spread;
            if (ImGui.SliderFloat("Bloom Spread", ref spread, 0.0f, 5.0f))
            {
                _bloomSystem.Spread = spread;
            }

            float bloomIntensity = _bloomSystem.Intensity;
            if (ImGui.SliderFloat("Bloom Intensity", ref bloomIntensity, 0.0f, 5.0f))
            {
                _bloomSystem.Intensity = bloomIntensity;
            }

            float gamma = _bloomSystem.Gamma;
            if (ImGui.SliderFloat("Bloom Gamma", ref gamma, 0.5f, 4.0f))
            {
                _bloomSystem.Gamma = gamma;
            }
        }

        // Tone map controls (TonemapStage)
        if (_tonemapStage != null)
        {
            ImGui.Separator();
            ImGui.Text("Tone Mapping");
            if (ImGui.Combo("Tone Map Type", ref _toneMapType))
            {
                _tonemapStage.Operator = _toneMapType;
            }

            // Optional parameter controls depending on type
            switch (_toneMapType)
            {
                case TonemapType.Reinhard:
                    {
                        var d = _tonemapStage.ReinhardData;
                        if (ImGui.SliderFloat("Max Luminance", ref d.MaxLuminance, 0.1f, 10f) |
                            ImGui.SliderFloat("Gamma", ref d.Gamma, 0.5f, 3.0f))
                        {
                            _tonemapStage.ReinhardData = d;
                        }
                        break;
                    }
                case TonemapType.Uncharted2:
                    {
                        var d2 = _tonemapStage.Uncharted2Data;
                        if (ImGui.SliderFloat("Exposure", ref d2.Exposure, 0.1f, 4f) |
                            ImGui.SliderFloat("Gamma", ref d2.Gamma, 0.5f, 3.0f))
                        {
                            _tonemapStage.Uncharted2Data = d2;
                        }
                        break;
                    }
                case TonemapType.Filmic:
                    {
                        var df = _tonemapStage.FilmicData;
                        if (ImGui.SliderFloat("Exposure", ref df.Exposure, 0.1f, 4f) |
                            ImGui.SliderFloat("Gamma", ref df.Gamma, 0.5f, 3.0f))
                        {
                            _tonemapStage.FilmicData = df;
                        }
                        break;
                    }
                case TonemapType.ACES:
                    {
                        var da = _tonemapStage.ACESData;
                        if (ImGui.SliderFloat("Exposure", ref da.Exposure, 0.1f, 4f) |
                            ImGui.SliderFloat("Gamma", ref da.Gamma, 0.5f, 3.0f))
                        {
                            _tonemapStage.ACESData = da;
                        }
                        break;
                    }
                case TonemapType.Neutral:
                    {
                        var dn = _tonemapStage.NeutralData;
                        if (ImGui.SliderFloat("Exposure", ref dn.Exposure, 0.1f, 4f) |
                            ImGui.SliderFloat("Gamma", ref dn.Gamma, 0.5f, 3.0f) |
                            ImGui.SliderFloat("StartCompression", ref dn.StartCompression, 0.5f, 1f) |
                            ImGui.SliderFloat("Desaturation", ref dn.Desaturation, 0.0f, 4f))
                        {
                            _tonemapStage.NeutralData = dn;
                        }
                        break;
                    }
            }
        }

        ImGui.End();
    }

    protected override void OnEndFrame()
    {
        _mainPipeline.RenderFrame(MainPresenter.FrameBuffer);
        _imguiSystem?.RenderAndDraw(MainPresenter.FrameBuffer);
    }

    protected override void OnStop()
    {
        _imguiSystem?.Dispose();
    }
}