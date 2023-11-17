using System;
using Vocore.Engine;

public class Game : GameEngine
{
    private Shader _shaderBasic;
    public Game(GameEngineSetting setting) : base(setting)
    {
        _shaderBasic = Shader.Complie(LoadAsset("Shader/Basic.hlsl"), "Basic.hlsl");
    }
    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(Key.Escape))
        {
            Stop();
        }

        if (Input.IsKeyDown(Key.F11))
        {
            Window.WindowState = Window.WindowState == WindowState.BorderlessFullScreen ? WindowState.Normal : WindowState.BorderlessFullScreen;
        }
    }

    public string LoadAsset(string path)
    {
        return File.ReadAllText(Path.Combine(WorkingDirectory, path));
    }
}