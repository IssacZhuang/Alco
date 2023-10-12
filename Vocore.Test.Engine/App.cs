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
    private CameraPerspective _cameraP;
    private CameraOrthographic _cameraO;
    private float _timer;
    private int _fps;

    public App(string name) : base(name)
    {
        
    }

    protected override void OnStart()
    {
        base.OnStart();
        _cameraP = new CameraPerspective();
        _cameraP.tranform.position = new Vector3(1, 0, -5);
        _cameraP.tranform.LookAt(Vector3.Zero);
        _cameraP.tranform.position.Y = 1;

        float size = 5;
        float ratio = 16f / 9f;
        _cameraO = new CameraOrthographic(size * ratio, size);
        _cameraO.tranform.position = new Vector3(0, 1, -5);
        _cameraO.tranform.LookAt(Vector3.Zero);

        GraphicsCommand.CurrentCamera = _cameraP;

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

        if (Input.IsKeyPressing(Key.Plus))
        {
            _cameraP.FieldOfView += delta;
        }

        if (Input.IsKeyPressing(Key.Minus))
        {
            _cameraP.FieldOfView -= delta;
        }

        if (Input.IsKeyPressing(Key.Space))
        {
            _cameraP.tranform.position.Y += delta;
        }

        if (Input.IsKeyPressing(Key.C))
        {
            _cameraP.tranform.position.Y -= delta;
        }

        _timer += delta;

        GraphicsCommand.UpdateCameraBuffer();
        GraphicsCommand.DrawMesh(MeshPool.Cube, _shaderPipeline, Matrix4x4.CreateRotationY(_timer));
        GraphicsCommand.DrawMesh(MeshPool.TestCube, _shaderPipeline, Matrix4x4.CreateRotationY(_timer + 1) * Matrix4x4.CreateTranslation(1, 0.5f * math.sin(_timer), 0));

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