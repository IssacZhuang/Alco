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
    private readonly ViewRenderTarget _windowRenderTarget;

    private readonly Camera2DBuffer _windowCamera1;
    private readonly Camera2DBuffer _windowCamera2;

    private readonly Shader _shader;
    private readonly RenderContext _renderContext;
    private readonly SpriteRenderer _renderer;

    //hdr
    private ReinhardToneMapData _toneMapData;
    private readonly Material _toneMapMaterial;
    private readonly GraphicsValueBuffer<ReinhardToneMapData> _toneMapDataBuffer;

    //bloom
    private readonly BloomSystem _bloomSystem1;
    private readonly BloomSystem _bloomSystem2;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _shader = BuiltInAssets.Shader_Sprite;


        _window2 = CreateView(new ViewSetting()
        {
            Title = "window_2",
            Width = 720,
            Height = 405,
            VSync = false
        });


        _windowRenderTarget = CreateViewRenderTarget(_window2, RenderingSystem.PrefferedSDRPass, BuiltInAssets.Shader_Blit);
        AddSystem(_windowRenderTarget);

        _windowCamera1 = RenderingSystem.CreateCamera2D(720, 405, 100);
        _windowCamera2 = RenderingSystem.CreateCamera2D(720, 405, 100);

        Material material = RenderingSystem.CreateGraphicsMaterial(_shader);
        material.SetBuffer(ShaderResourceId.Camera, _windowCamera1);
        _renderContext = RenderingSystem.CreateRenderContext("renderer");
        _renderer = RenderingSystem.CreateSpriteRenderer(_renderContext, material);


        MainView.Position = new Vector2(276, 258);
        _window2.Position = new Vector2(889, 410);

        _toneMapData = ReinhardToneMapData.Default;
        _toneMapMaterial = RenderingSystem.CreateGraphicsMaterial(BuiltInAssets.Shader_ReinhardLuminanceTonemap);

        _toneMapDataBuffer = RenderingSystem.CreateGraphicsValueBuffer(_toneMapData, "tonemap_data_buffer");
        _toneMapMaterial.SetBuffer(ShaderResourceId.Data, _toneMapDataBuffer);

        MainRenderTarget.SetRenderPass(RenderingSystem.PrefferedHDRPass, _toneMapMaterial);
        _windowRenderTarget.SetRenderPass(RenderingSystem.PrefferedHDRPass, _toneMapMaterial);

        _bloomSystem1 = new BloomSystem(this, MainRenderTarget);
        AddSystem(_bloomSystem1);
        _bloomSystem2 = new BloomSystem(this, _windowRenderTarget);
        AddSystem(_bloomSystem2);

        
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

        DebugGUI.Text(MainView.Position.ToString());
        DebugGUI.Text(_window2.Position.ToString());

        _renderContext.Begin(MainFrameBuffer);
        _renderer.Draw(RenderingSystem.TextureWhite, new Vector2(0, 0), Rotation2D.Identity, new Vector2(200, 200), new ColorFloat(2, 1.2f, 1.2f, 1));
        _renderContext.End();

        _renderContext.Begin(_windowRenderTarget.FrameBuffer);
        _renderer.Draw(RenderingSystem.TextureWhite, new Vector2(0, 0), Rotation2D.Identity, new Vector2(200, 200), new ColorFloat(2, 1.2f, 1.2f, 1));
        _renderContext.End();


    }

    protected override void OnStop()
    {
        _toneMapDataBuffer?.Dispose();
    }

    private void Render(Camera2DBuffer camera, ViewRenderTarget renderTarget)
    {
        
    }

    private Vector2 ScreenToWorld(Vector2 minotorSize, Vector2 windowPos, Vector2 windowSize)
    {
        float x = windowPos.X + windowSize.X * 0.5f - minotorSize.X * 0.5f;
        float y = minotorSize.Y * 0.5f - windowPos.Y - windowSize.Y * 0.5f;
        return new Vector2(x, y);
    }
}