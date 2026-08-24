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
    private readonly RenderPipeline _mainPipeline;

    //scence
    private readonly Camera2DBuffer _camera;

    private readonly Texture2D _quad;
    private readonly Shader _spriteShader;
    private readonly SpriteRenderer _renderer;
    private ColorFloat _color = new ColorFloat(4, 2, 2, 1);
    private bool _enabled = true;

    private readonly RGNode_Bloom _bloomNode;
    private readonly RGNode_Tonemap _tonemapNode;
    private TonemapType _toneMapType;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _mainPipeline = new RenderPipeline(
            RenderingSystem,
            RenderingSystem.PreferredHDRPass,
            BuiltInAssets.Shader_Blit,
            MainView.Size.X,
            MainView.Size.Y);

        // The node chain: scene content first, then bloom, then tone mapping.
        _mainPipeline.Use(new SceneNode(this, _mainPipeline.Graph, _mainPipeline.Chain));

        _bloomNode = new RGNode_Bloom(
            RenderingSystem,
            _mainPipeline.Graph,
            _mainPipeline.Chain,
            _mainPipeline.PostProcessLayout,
            new RGNode_Bloom.Descriptor
            {
                BlitShader = BuiltInAssets.Shader_BloomBlit,
                ClampShader = BuiltInAssets.Shader_BloomClamp,
                DownsampleShader = BuiltInAssets.Shader_BloomDownsample,
                UpsampleShader = BuiltInAssets.Shader_BloomUpsample,
                TargetDownsampleHeight = 11,
                SceneCopyShader = BuiltInAssets.Shader_Blit,
            });
        _mainPipeline.Use(_bloomNode);

        _tonemapNode = new RGNode_Tonemap(
            RenderingSystem,
            _mainPipeline.Graph,
            _mainPipeline.Chain,
            _mainPipeline.PostProcessLayout,
            new RGNode_Tonemap.Descriptor
            {
                BlitShader = BuiltInAssets.Shader_Blit,
                ReinhardShader = BuiltInAssets.Shader_ReinhardLuminanceTonemap,
                Uncharted2Shader = BuiltInAssets.Shader_Uncharted2Tonemap,
                FilmicShader = BuiltInAssets.Shader_FilmicTonemap,
                AcesShader = BuiltInAssets.Shader_AcesTonemap,
                NeutralShader = BuiltInAssets.Shader_NeutralTonemap,
                AgxShader = BuiltInAssets.Shader_AgxTonemap,
            });
        _mainPipeline.Use(_tonemapNode);
        _toneMapType = _tonemapNode.Operator;

        MainPresenter.OnResize += size => _mainPipeline.Resize(size.X, size.Y);

        //scence
        _spriteShader = BuiltInAssets.Shader_Sprite;

        _quad = RenderingSystem.CreateTexture2D(4,4, 0xffffff);

        _camera = RenderingSystem.CreateCamera2D(640, 360, 100);

        GraphicsMaterial material = RenderingSystem.CreateGraphicsMaterial(_spriteShader, "sprite", "false");
        material.SetBuffer(ShaderResourceId.Camera, _camera);
        _renderer = RenderingSystem.CreateSpriteRenderer(_mainPipeline.Graph.RenderContext, material);
    }

    public override IEnumerable<IFileSource> CreateDefaultFileSources()
    {
        foreach (var fileSource in base.CreateDefaultFileSources())
        {
            yield return fileSource;
        }
        yield return new DirectoryWatcherFileSource(Utils.GetBuiltInAssetsPath(), AssetSystem);
        yield return new DirectoryWatcherFileSource(Utils.GetRenderingAssetsPath(), AssetSystem);
        yield return new DirectoryWatcherFileSource(Utils.GetProjectAssetsPath(), AssetSystem);
    }

    /// <summary>
    /// Called when the game starts; initializes references to systems/plugins used by the sample.
    /// </summary>
    protected override void OnStart()
    {
        AddSystem(new ImGUISystem(this));
    }

    /// <summary>
    /// Per-frame update. Handles input, updates scene state, and renders ImGui controls.
    /// </summary>
    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        // removed intensity hotkeys; color is controlled via ImGui

        // ImGUI Controls
        ImGui.Begin("Bloom HDR Controls");

        // Color control
        ImGui.ColorEdit4("Color", ref _color, ImGuiColorEditFlags.HDR);
        ImGui.Checkbox("Enabled", ref _enabled);

        // Bloom System Controls
        ImGui.Separator();
        ImGui.Text("Bloom System Controls");

        bool bloomEnabled = _bloomNode.IsEnabled;
        if (ImGui.Checkbox("Bloom Enabled", ref bloomEnabled))
        {
            _bloomNode.IsEnabled = bloomEnabled;
        }

        float threshold = _bloomNode.Threshold;
        if (ImGui.SliderFloat("Bloom Threshold", ref threshold, 0.0f, 3.0f))
        {
            _bloomNode.Threshold = threshold;
        }

        float spread = _bloomNode.Spread;
        if (ImGui.SliderFloat("Bloom Spread", ref spread, 0.0f, 5.0f))
        {
            _bloomNode.Spread = spread;
        }

        float bloomIntensity = _bloomNode.Intensity;
        if (ImGui.SliderFloat("Bloom Intensity", ref bloomIntensity, 0.0f, 5.0f))
        {
            _bloomNode.Intensity = bloomIntensity;
        }

        float gamma = _bloomNode.Gamma;
        if (ImGui.SliderFloat("Bloom Gamma", ref gamma, 0.5f, 4.0f))
        {
            _bloomNode.Gamma = gamma;
        }

        // Tone map controls
        ImGui.Separator();
        ImGui.Text("Tone Mapping");
        if (ImGui.Combo("Tone Map Type", ref _toneMapType))
        {
            _tonemapNode.Operator = _toneMapType;
        }

        // Optional parameter controls depending on type
        switch (_toneMapType)
        {
            case TonemapType.Reinhard:
                {
                    var d = _tonemapNode.ReinhardData;
                    if (ImGui.SliderFloat("Max Luminance", ref d.MaxLuminance, 0.1f, 10f) |
                        ImGui.SliderFloat("Gamma", ref d.Gamma, 0.5f, 3.0f))
                    {
                        _tonemapNode.ReinhardData = d;
                    }
                    break;
                }
            case TonemapType.Uncharted2:
                {
                    var d2 = _tonemapNode.Uncharted2Data;
                    if (ImGui.SliderFloat("Exposure", ref d2.Exposure, 0.1f, 4f) |
                        ImGui.SliderFloat("Gamma", ref d2.Gamma, 0.5f, 3.0f))
                    {
                        _tonemapNode.Uncharted2Data = d2;
                    }
                    break;
                }
            case TonemapType.Filmic:
                {
                    var df = _tonemapNode.FilmicData;
                    if (ImGui.SliderFloat("Exposure", ref df.Exposure, 0.1f, 4f) |
                        ImGui.SliderFloat("Gamma", ref df.Gamma, 0.5f, 3.0f))
                    {
                        _tonemapNode.FilmicData = df;
                    }
                    break;
                }
            case TonemapType.ACES:
                {
                    var da = _tonemapNode.ACESData;
                    if (ImGui.SliderFloat("Exposure", ref da.Exposure, 0.1f, 4f) |
                        ImGui.SliderFloat("Gamma", ref da.Gamma, 0.5f, 3.0f))
                    {
                        _tonemapNode.ACESData = da;
                    }
                    break;
                }
            case TonemapType.Neutral:
                {
                    var dn = _tonemapNode.NeutralData;
                    if (ImGui.SliderFloat("Exposure", ref dn.Exposure, 0.1f, 4f) |
                        ImGui.SliderFloat("Gamma", ref dn.Gamma, 0.5f, 3.0f) |
                        ImGui.SliderFloat("StartCompression", ref dn.StartCompression, 0.5f, 1f) |
                        ImGui.SliderFloat("Desaturation", ref dn.Desaturation, 0.0f, 4f))
                    {
                        _tonemapNode.NeutralData = dn;
                    }
                    break;
                }
        }

        ImGui.End();

        _mainPipeline.Render(MainPresenter.FrameBuffer);
    }

    protected override void OnStop()
    {
        _mainPipeline.Dispose();
    }

    /// <summary>
    /// Content node drawing the HDR sprite into the pipeline-assigned target.
    /// </summary>
    private sealed class SceneNode : RGNode_SceneContent
    {
        private readonly Game _game;

        public SceneNode(Game game, RenderGraph graph, RenderChain chain) : base(graph, chain)
        {
            _game = game;
        }

        protected override void OnRender(in RenderGraphContext context, GPUFrameBuffer target, GPUAttachmentLayout layout)
        {
            Vector2 normalizedMousePosition = _game.Input.MousePosition / new Vector2(1280, 720);
            Vector2 spritePosition = normalizedMousePosition * new Vector2(640, 360) - new Vector2(320, 180);
            spritePosition.Y = -spritePosition.Y;

            using (RenderPassScope pass = context.RenderContext.BeginPass(target))
            {
                if (_game._enabled)
                {
                    _game._renderer.Draw(_game._quad, Vector2.Zero, Rotation2D.Identity, Vector2.One * 24, _game._color);
                }
            }
        }
    }
}
