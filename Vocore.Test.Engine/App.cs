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
    private float timer;

    public App(string name) : base(name)
    {
        
    }

    protected override void OnStart()
    {
        base.OnStart();
        //GLSL
        var vertShader = File.ReadAllBytes(Path.Combine(Application.Path, "Assets/Basic.vert.glsl"));
        var fragShader = File.ReadAllBytes(Path.Combine(Application.Path, "Assets/Basic.frag.glsl"));
        _shaderPipeline = ShaderLoader.CreateShaderPiplineFromGLSL(GraphicsDevice, vertShader, fragShader);
        // HLSL
        // var vertShader = File.ReadAllBytes(Path.Combine(Application.Path, "Assets/Basic.vert.hlsl"));
        // var fragShader = File.ReadAllBytes(Path.Combine(Application.Path, "Assets/Basic.frag.hlsl"));
        // _shaderPipeline = ShaderLoader.CreateShaderPiplineFromHLSL(GraphicsDevice, vertShader, fragShader);
        _camera = new Camera();
        // _camera.Position = new Vector3(-3, 0, 0);
        // _camera.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY,0.2f);
        _camera.ViewMatrix = Matrix4x4.CreateLookAt(Vector3.UnitZ * 2.5f, Vector3.Zero, Vector3.UnitY);
        GraphicsCommand.CurrentCamera = _camera;
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

        timer += delta;

        GraphicsCommand.ClearFrame();
        GraphicsCommand.UpdateCameraBuffer();   
        GraphicsCommand.DrawMesh(MeshPool.TestCube, _shaderPipeline, Matrix4x4.CreateRotationY(timer));
        GraphicsCommand.DrawMesh(MeshPool.TestCube, _shaderPipeline, Matrix4x4.CreateRotationY(timer+1)*Matrix4x4.CreateTranslation(1, 0, 0));
        GraphicsCommand.SwapBuffer();
    }

    protected override void OnWindowResize(int width, int height)
    {
        Log.Info($"Window resized to {width}x{height}");
    }


}