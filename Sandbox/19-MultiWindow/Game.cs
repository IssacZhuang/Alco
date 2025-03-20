using System.Numerics;
using Alco.Engine;
using Alco.Audio;
using Alco;
using Alco.Rendering;
using Alco.GUI;
using Alco.Graphics;



public class Game : GameEngine
{
    private readonly Window _window2;
    private readonly WindowRenderTarget _windowRenderTarget;

    private readonly Camera2D _windowCamera1;
    private readonly Camera2D _windowCamera2;

    private readonly Shader _shader;
    private readonly RenderContext _renderContext;
    private readonly SpriteRenderer _renderer;

    //hdr
    private ReinhardToneMapData _toneMapData;
    private readonly Material _toneMapMaterial;

    //bloom
    private readonly BloomSystem _bloomSystem1;
    private readonly BloomSystem _bloomSystem2;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _shader = BuiltInAssets.Shader_Sprite;


        _window2 = CreateWindow(new WindowSetting()
        {
            Title = "window_2",
            Width = 720,
            Height = 405,
            VSync = false
        });


        _windowRenderTarget = CreateWindowRenderTarget(_window2, Rendering.PrefferedSDRPass, BuiltInAssets.Shader_Blit);
        AddSystem(_windowRenderTarget);

        _windowCamera1 = Rendering.CreateCamera2D(720, 405, 100);
        _windowCamera2 = Rendering.CreateCamera2D(720, 405, 100);

        Material material = Rendering.CreateGraphicsMaterial(_shader);
        material.SetBuffer(ShaderResourceId.Camera, _windowCamera1);
        _renderContext = Rendering.CreateRenderContext("renderer");
        _renderer = Rendering.CreateSpriteRenderer(_renderContext, material);


        MainWindow.Position = new Vector2(276, 258);
        _window2.Position = new Vector2(889, 410);

        _toneMapData = ReinhardToneMapData.Default;
        _toneMapMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_ReinhardLuminanceTonemap);
        _toneMapMaterial.SetValue(ShaderResourceId.Data, _toneMapData);

        MainRenderTarget.SetRenderPass(Rendering.PrefferedHDRPass, _toneMapMaterial);
        _windowRenderTarget.SetRenderPass(Rendering.PrefferedHDRPass, _toneMapMaterial);

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



        _windowCamera1.ViewSize = new Vector2(MainWindow.Size.X, MainWindow.Size.Y);
        //window pos to game scene pos

        _windowCamera1.Position = ScreenToWorld(new Vector2(1920, 1080), MainWindow.Position, MainWindow.Size);
        _windowCamera1.UpdateBuffer();

        _windowCamera2.ViewSize = new Vector2(_window2.Size.X, _window2.Size.Y);
        _windowCamera2.Position = ScreenToWorld(new Vector2(1920, 1080), _window2.Position, _window2.Size);
        _windowCamera2.UpdateBuffer();

        DebugGUI.Text(MainWindow.Position.ToString());
        DebugGUI.Text(_window2.Position.ToString());

        _renderContext.Begin(MainFrameBuffer);
        _renderer.Draw(Rendering.TextureWhite, new Vector2(0, 0), Rotation2D.Identity, new Vector2(200, 200), new ColorFloat(2, 1.2f, 1.2f, 1));
        _renderContext.End();

        _renderContext.Begin(_windowRenderTarget.FrameBuffer);
        _renderer.Draw(Rendering.TextureWhite, new Vector2(0, 0), Rotation2D.Identity, new Vector2(200, 200), new ColorFloat(2, 1.2f, 1.2f, 1));
        _renderContext.End();


    }

    protected override void OnStop()
    {
        
    }

    private void Render(Camera2D camera, WindowRenderTarget renderTarget)
    {
        
    }

    private Vector2 ScreenToWorld(Vector2 minotorSize, Vector2 windowPos, Vector2 windowSize)
    {
        float x = windowPos.X + windowSize.X * 0.5f - minotorSize.X * 0.5f;
        float y = minotorSize.Y * 0.5f - windowPos.Y - windowSize.Y * 0.5f;
        return new Vector2(x, y);
    }
}