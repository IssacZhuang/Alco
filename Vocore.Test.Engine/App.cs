using System;
using System.Numerics;
using System.IO;
using System.Text;
using Veldrid;
using Vocore;
using Vocore.Engine;

public class App : Engine
{
    private Pipeline _shaderPipeline;

    public App(string name) : base(name)
    {
        
    }

    protected override void OnStart()
    {
        base.OnStart();
        var vertShader = File.ReadAllBytes(Path.Combine(Application.Path, "Assets/Basic.vert.glsl"));
        var fragShader = File.ReadAllBytes(Path.Combine(Application.Path, "Assets/Basic.frag.glsl"));
        _shaderPipeline = ShaderLoader.CreateShaderPipline(GraphicsDevice, vertShader, fragShader);
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

        GraphicsCommand.ClearFrame();
        GraphicsCommand.DrawMesh(MeshPool.TestQuad, _shaderPipeline);
        GraphicsCommand.SwapBuffer();
    }

    protected override void OnWindowResize(int width, int height)
    {
        Log.Info($"Window resized to {width}x{height}");
    }


}