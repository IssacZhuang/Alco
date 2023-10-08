using System;
using Veldrid;
using Vocore;
using Vocore.Engine;

public class App:Engine{
    public App(string name):base(name){
        
    }

    protected override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (Input.IsMouseKeyDown(MouseButton.Left))
        {
            Log.Info("Left mouse button pressed");
        }

        if (Input.IsMouseKeyUp(MouseButton.Left))
        {
            Log.Info("Left mouse button released");
        }

        if (Input.IsKeyDown(Key.Escape))
        {
            Application.Quit();
        }

        if (Input.IsKeyPressing(Key.W))
        {
            Log.Info("W key pressing");
        }
    }

    protected override void OnWindowResize(int width, int height)
    {
        Log.Info($"Window resized to {width}x{height}");
    }
}