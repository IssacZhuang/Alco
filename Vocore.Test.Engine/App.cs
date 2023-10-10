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
    private Camera _camera;
    private float _timer;
    private int _fps;

    public App(string name) : base(name)
    {
        
    }

    protected override void OnStart()
    {
        base.OnStart();
        _camera = new Camera();
        _camera.ViewMatrix = Matrix4x4.CreateLookAt(Vector3.UnitZ * 2.5f, Vector3.Zero, Vector3.UnitY);
        GraphicsCommand.CurrentCamera = _camera;

        var shaderAllInOne = File.ReadAllText(Path.Combine(Application.Path, "Assets/BasicAIO.glsl"));
        _shaderPipeline = ShaderComplier.CreateShaderPiplineFromGLSL(GraphicsDevice, shaderAllInOne, "BasicAIO");
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

        _timer += delta;

        GraphicsCommand.ClearFrame();
        GraphicsCommand.UpdateCameraBuffer();
        GraphicsCommand.DrawMesh(MeshPool.TestCube, _shaderPipeline, Matrix4x4.CreateRotationY(_timer));
        GraphicsCommand.DrawMesh(MeshPool.TestCube, _shaderPipeline, Matrix4x4.CreateRotationY(_timer+1)*Matrix4x4.CreateTranslation(1, 0, 0));
        GraphicsCommand.SwapBuffer();
    }

    protected override void OnTick(float delta)
    {
        if (_fps != Profiler.FPS)
        {
            _fps = Profiler.FPS;
            Log.Info(String.Concat("FPS: ", _fps));
        }

    }

    protected override void OnWindowResize(int width, int height)
    {
        Log.Info($"Window resized to {width}x{height}");
    }


}