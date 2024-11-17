using System.Numerics;
using Vocore.Engine;
using Vocore.Audio;
using Vocore;
using Vocore.Rendering;
using Vocore.GUI;
using Vocore.Graphics;



public class Game : GameEngine
{
    private readonly Window _window2;
    private readonly WindowRenderTarget _windowRenderTarget;

    private readonly Camera2D _windowCamera1;
    private readonly Camera2D _windowCamera2;

    private readonly Shader _shader;
    private readonly SpriteRenderer _renderer1;
    private readonly SpriteRenderer _renderer2;

    //hdr
    private readonly ReinhardLuminanceToneMap _toneMap1;
    private readonly ReinhardLuminanceToneMap _toneMap2;

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

        _renderer1 = Rendering.CreateSpriteRenderer(_windowCamera1, _shader);
        _renderer2 = Rendering.CreateSpriteRenderer(_windowCamera2, _shader);

        MainWindow.Position = new Vector2(276, 258);
        _window2.Position = new Vector2(889, 410);

        _toneMap1 = Rendering.CreateReinhardLuminanceToneMap(BuiltInAssets.Shader_ReinhardLuminanceTonemap);
        _toneMap2 = Rendering.CreateReinhardLuminanceToneMap(BuiltInAssets.Shader_ReinhardLuminanceTonemap);

        MainRenderTarget.SetRenderPass(Rendering.PrefferedHDRPass, _toneMap1);
        _windowRenderTarget.SetRenderPass(Rendering.PrefferedHDRPass, _toneMap2);

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



        _windowCamera1.ViewSize = new Vector2(MainWindow.Size.x, MainWindow.Size.y);
        //window pos to game scene pos

        _windowCamera1.Position = ScreenToWorld(new Vector2(1920, 1080), MainWindow.Position, MainWindow.Size);
        _windowCamera1.UpdateBuffer();

        _windowCamera2.ViewSize = new Vector2(_window2.Size.x, _window2.Size.y);
        _windowCamera2.Position = ScreenToWorld(new Vector2(1920, 1080), _window2.Position, _window2.Size);
        _windowCamera2.UpdateBuffer();

        DebugGUI.Text(MainWindow.Position.ToString());
        DebugGUI.Text(_window2.Position.ToString());

        _renderer1.Begin(MainFrameBuffer);
        _renderer1.Draw(Rendering.TextureWhite, new Vector2(0, 0), Rotation2D.Identity, new Vector2(200, 200), new ColorFloat(2, 1.2f, 1.2f, 1));
        _renderer1.End();

        _renderer2.Begin(_windowRenderTarget.FrameBuffer);
        _renderer2.Draw(Rendering.TextureWhite, new Vector2(0, 0), Rotation2D.Identity, new Vector2(200, 200), new ColorFloat(2, 1.2f, 1.2f, 1));
        _renderer2.End();


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