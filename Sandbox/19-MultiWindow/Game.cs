using System.Numerics;
using Vocore.Engine;
using Vocore.Audio;
using Vocore;
using Vocore.Rendering;
using Vocore.GUI;



public class Game : GameEngine
{
    private readonly Window _window2;
    private readonly WindowRenderTarget _windowRenderTarget;

    private readonly Camera2D _windowCamera1;
    private readonly Camera2D _windowCamera2;

    private readonly Shader _shader;



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
    }

    override protected void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }
    
        DebugGUI.Text(MainWindow.Position.ToString());

        _windowCamera1.Size = new Vector2(_window2.Size.x, _window2.Size.y);
        _windowCamera1.Position = new Vector2(_window2.Position.x, _window2.Position.y);
        _windowCamera1.UpdateBuffer();

        _windowCamera2.Size = new Vector2(_window2.Size.x, _window2.Size.y);
        _windowCamera2.Position = new Vector2(_window2.Position.x, _windowCamera2.Position.Y);
        _windowCamera2.UpdateBuffer();


    }

    protected override void OnStop()
    {
        
    }

    private void Render(Camera2D camera, WindowRenderTarget renderTarget)
    {
        
    }
}