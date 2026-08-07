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
    private readonly ForwardPipeline _pipeline2;
    private readonly ForwardPipeline _mainPipeline;

    private readonly Camera2DBuffer _windowCamera1;
    private readonly Camera2DBuffer _windowCamera2;

    private readonly Shader _shader;
    private readonly RenderContext _renderContext;
    private readonly SpriteRenderer _renderer;

    //hdr
    private readonly TonemapStage _tonemapStage1;
    private readonly TonemapStage _tonemapStage2;

    //bloom
    private readonly BloomStage _bloom1;
    private readonly BloomStage _bloom2;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _mainPipeline = new ForwardPipeline(RenderingSystem, RenderingSystem.PreferredHDRPass, BuiltInAssets.Shader_Blit, MainView.Size.X, MainView.Size.Y);
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
        _pipeline2 = new ForwardPipeline(RenderingSystem, RenderingSystem.PreferredHDRPass, BuiltInAssets.Shader_Blit, _window2.Size.X, _window2.Size.Y);
        _presenter2.OnResize += size => _pipeline2.Resize(size.X, size.Y);

        _windowCamera1 = RenderingSystem.CreateCamera2D(720, 405, 100);
        _windowCamera2 = RenderingSystem.CreateCamera2D(720, 405, 100);

        Material material = RenderingSystem.CreateMaterial(_shader);
        material.SetBuffer(ShaderResourceId.Camera, _windowCamera1);
        _renderContext = RenderingSystem.CreateRenderContext("renderer");
        _renderer = RenderingSystem.CreateSpriteRenderer(_renderContext, material);


        MainView.Position = new Vector2(276, 258);
        _window2.Position = new Vector2(889, 410);

        _tonemapStage1 = new TonemapStage(RenderingSystem,
            BuiltInAssets.Shader_ReinhardLuminanceTonemap,
            BuiltInAssets.Shader_Uncharted2Tonemap,
            BuiltInAssets.Shader_FilmicTonemap,
            BuiltInAssets.Shader_ACESTonemap,
            BuiltInAssets.Shader_NeutralTonemap,
            BuiltInAssets.Shader_AgXTonemap);
        _mainPipeline.PostProcess.Add(_tonemapStage1);

        _tonemapStage2 = new TonemapStage(RenderingSystem,
            BuiltInAssets.Shader_ReinhardLuminanceTonemap,
            BuiltInAssets.Shader_Uncharted2Tonemap,
            BuiltInAssets.Shader_FilmicTonemap,
            BuiltInAssets.Shader_ACESTonemap,
            BuiltInAssets.Shader_NeutralTonemap,
            BuiltInAssets.Shader_AgXTonemap);
        _pipeline2.PostProcess.Add(_tonemapStage2);

        _bloom1 = new BloomStage(RenderingSystem.CreateBloom(
            BuiltInAssets.Shader_BloomBlit,
            BuiltInAssets.Shader_BloomClamp,
            BuiltInAssets.Shader_BloomDownSample,
            BuiltInAssets.Shader_BloomUpSample,
            11));
        _mainPipeline.PostProcess.Add(_bloom1);

        _bloom2 = new BloomStage(RenderingSystem.CreateBloom(
            BuiltInAssets.Shader_BloomBlit,
            BuiltInAssets.Shader_BloomClamp,
            BuiltInAssets.Shader_BloomDownSample,
            BuiltInAssets.Shader_BloomUpSample,
            11));
        _pipeline2.PostProcess.Add(_bloom2);

        
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

        _renderContext.Begin(_mainPipeline.SceneFrameBuffer);
        _renderer.Draw(RenderingSystem.TextureWhite, new Vector2(0, 0), Rotation2D.Identity, new Vector2(200, 200), new ColorFloat(2, 1.2f, 1.2f, 1));
        _renderContext.End();

        _presenter2.BeginFrame();
        _pipeline2.BeginFrame();

        _renderContext.Begin(_pipeline2.SceneFrameBuffer);
        _renderer.Draw(RenderingSystem.TextureWhite, new Vector2(0, 0), Rotation2D.Identity, new Vector2(200, 200), new ColorFloat(2, 1.2f, 1.2f, 1));
        _renderContext.End();

        _pipeline2.RenderFrame(_presenter2.FrameBuffer);
        _presenter2.EndFrame();


    }

    protected override void OnBeginFrame()
    {
        _mainPipeline.BeginFrame();
    }

    protected override void OnEndFrame()
    {
        _mainPipeline.RenderFrame(MainPresenter.FrameBuffer);
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
}