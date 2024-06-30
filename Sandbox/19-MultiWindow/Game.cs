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
    public Game(GameEngineSetting setting) : base(setting)
    {
        _window2 = CreateWindow(new WindowSetting()
        {
            Title = "window_2",
            Width = 720,
            Height = 405,
            VSync = false
        });


        _windowRenderTarget = CreateWindowRenderTarget(_window2, Rendering.PrefferedSDRPass, BuiltInAssets.Shader_Blit);
    }



    override protected void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }
    
        DebugGUI.Text(MainWindow.Position.ToString());


    }

    protected override void OnStop()
    {
        
    }
}