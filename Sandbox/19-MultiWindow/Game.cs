using System.Numerics;
using Alco.Engine;
using Alco.Audio;
using Alco;
using Alco.Rendering;
using Alco.GUI;
using Alco.Graphics;



public class Game : GameEngine
{
    private readonly View _window2;
    private readonly ViewPresenter _presenter2;
    private readonly RenderPipeline _pipeline2;
    private readonly RenderPipeline _mainPipeline;

    private readonly Camera2DBuffer _windowCamera1;
    private readonly Camera2DBuffer _windowCamera2;

    private readonly Shader _shader;
    private readonly SpriteRenderer _renderer1;
    private readonly SpriteRenderer _renderer2;

    //hdr
    private readonly RGNode_Tonemap _tonemapNode1;
    private readonly RGNode_Tonemap _tonemapNode2;

    //bloom
    private readonly RGNode_Bloom _bloomNode1;
    private readonly RGNode_Bloom _bloomNode2;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _mainPipeline = new RenderPipeline(RenderingSystem, RenderingSystem.PreferredHDRPass, BuiltInAssets.Shader_Blit, MainView.Size.X, MainView.Size.Y);
        MainPresenter.OnResize += size => _mainPipeline.Resize(size.X, size.Y);

        _shader = BuiltInAssets.Shader_Sprite;


        _window2 = CreateView(new ViewSetting()
        {
            Title = "window_2",
            Width = 720,
            Height = 405,
            VSync = false
        });


        _presenter2 = CreateViewPresenter(_window2);
        _pipeline2 = new RenderPipeline(RenderingSystem, RenderingSystem.PreferredHDRPass, BuiltInAssets.Shader_Blit, _window2.Size.X, _window2.Size.Y);
        _presenter2.OnResize += size => _pipeline2.Resize(size.X, size.Y);

        _windowCamera1 = RenderingSystem.CreateCamera2D(720, 405, 100);
        _windowCamera2 = RenderingSystem.CreateCamera2D(720, 405, 100);

        Material material1 = RenderingSystem.CreateMaterial(_shader);
        material1.SetBuffer(ShaderResourceId.Camera, _windowCamera1);
        _renderer1 = RenderingSystem.CreateSpriteRenderer(_mainPipeline.Graph.RenderContext, material1);

        Material material2 = RenderingSystem.CreateMaterial(_shader);
        material2.SetBuffer(ShaderResourceId.Camera, _windowCamera2);
        _renderer2 = RenderingSystem.CreateSpriteRenderer(_pipeline2.Graph.RenderContext, material2);

        _mainPipeline.Use(new SceneNode(this, _mainPipeline.Graph, _mainPipeline.Chain, _renderer1));
        _pipeline2.Use(new SceneNode(this, _pipeline2.Graph, _pipeline2.Chain, _renderer2));


        MainView.Position = new Vector2(276, 258);
        _window2.Position = new Vector2(889, 410);

        _bloomNode1 = new RGNode_Bloom(RenderingSystem, _mainPipeline.Graph, _mainPipeline.Chain, _mainPipeline.PostProcessLayout, RenderingSystem.CreateBloom(
            BuiltInAssets.Shader_BloomBlit,
            BuiltInAssets.Shader_BloomClamp,
            BuiltInAssets.Shader_BloomDownSample,
            BuiltInAssets.Shader_BloomUpSample,
            11), BuiltInAssets.Shader_Blit);
        _mainPipeline.Use(_bloomNode1);

        _bloomNode2 = new RGNode_Bloom(RenderingSystem, _pipeline2.Graph, _pipeline2.Chain, _pipeline2.PostProcessLayout, RenderingSystem.CreateBloom(
            BuiltInAssets.Shader_BloomBlit,
            BuiltInAssets.Shader_BloomClamp,
            BuiltInAssets.Shader_BloomDownSample,
            BuiltInAssets.Shader_BloomUpSample,
            11), BuiltInAssets.Shader_Blit);
        _pipeline2.Use(_bloomNode2);

        _tonemapNode1 = new RGNode_Tonemap(RenderingSystem, _mainPipeline.Graph, _mainPipeline.Chain, _mainPipeline.PostProcessLayout,
            BuiltInAssets.Shader_Blit,
            BuiltInAssets.Shader_ReinhardLuminanceTonemap,
            BuiltInAssets.Shader_Uncharted2Tonemap,
            BuiltInAssets.Shader_FilmicTonemap,
            BuiltInAssets.Shader_ACESTonemap,
            BuiltInAssets.Shader_NeutralTonemap,
            BuiltInAssets.Shader_AgXTonemap);
        _mainPipeline.Use(_tonemapNode1);

        _tonemapNode2 = new RGNode_Tonemap(RenderingSystem, _pipeline2.Graph, _pipeline2.Chain, _pipeline2.PostProcessLayout,
            BuiltInAssets.Shader_Blit,
            BuiltInAssets.Shader_ReinhardLuminanceTonemap,
            BuiltInAssets.Shader_Uncharted2Tonemap,
            BuiltInAssets.Shader_FilmicTonemap,
            BuiltInAssets.Shader_ACESTonemap,
            BuiltInAssets.Shader_NeutralTonemap,
            BuiltInAssets.Shader_AgXTonemap);
        _pipeline2.Use(_tonemapNode2);

        
    }

    override protected void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }



        _windowCamera1.ViewSize = new Vector2(MainView.Size.X, MainView.Size.Y);
        //window pos to game scene pos

        _windowCamera1.Position = ScreenToWorld(new Vector2(1920, 1080), MainView.Position, MainView.Size);
        _windowCamera1.UpdateBuffer();

        _windowCamera2.ViewSize = new Vector2(_window2.Size.X, _window2.Size.Y);
        _windowCamera2.Position = ScreenToWorld(new Vector2(1920, 1080), _window2.Position, _window2.Size);
        _windowCamera2.UpdateBuffer();

        _presenter2.BeginFrame();
        _pipeline2.Render(_presenter2.FrameBuffer);
        _presenter2.EndFrame();

        _mainPipeline.Render(MainPresenter.FrameBuffer);
    }

    protected override void OnStop()
    {
        _pipeline2.Dispose();
        _mainPipeline.Dispose();
        _presenter2.Dispose();
    }

    private Vector2 ScreenToWorld(Vector2 minotorSize, Vector2 windowPos, Vector2 windowSize)
    {
        float x = windowPos.X + windowSize.X * 0.5f - minotorSize.X * 0.5f;
        float y = minotorSize.Y * 0.5f - windowPos.Y - windowSize.Y * 0.5f;
        return new Vector2(x, y);
    }

    private sealed class SceneNode : RGNode_SceneContent
    {
        private readonly Game _game;
        private readonly SpriteRenderer _renderer;

        public SceneNode(Game game, RenderGraph graph, RenderChain chain, SpriteRenderer renderer) : base(graph, chain)
        {
            _game = game;
            _renderer = renderer;
        }

        protected override void OnRender(in RenderGraphContext context, GPUFrameBuffer target, GPUAttachmentLayout layout)
        {
            using (RenderPassScope pass = context.RenderContext.BeginPass(target))
            {
                _renderer.Draw(_game.RenderingSystem.TextureWhite, new Vector2(0, 0), Rotation2D.Identity, new Vector2(200, 200), new ColorFloat(2, 1.2f, 1.2f, 1));
            }
        }
    }
}