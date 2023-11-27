using System;
using Silk.NET.Input;
using Silk.NET.Windowing;
using Vocore.Engine;

public class Game : GameEngine
{
    public Game(GameEngineSetting setting) : base(setting)
    {
        
    }
    protected override void OnUpdate(float delta)
    {
        if(Input.IsKeyDown(Key.Escape))
        {
            Stop();
        }

        if (Input.IsKeyDown(Key.F11))
        {
            Window.WindowState = Window.WindowState == WindowState.Fullscreen ? WindowState.Normal : WindowState.Fullscreen;
        }
    }
}