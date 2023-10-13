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
    private float _timer;
    private int _fps;
    ActorFreeLook3D _actorFreeLook3D;

    public App(GraphicsBackend backend, string name) : base(backend, name)
    {

    }

    public App(string name) : base(name)
    {
        
    }

    protected override void OnStart()
    {
        _cameraP = new CameraPerspective();
        _cameraP.tranform.position = new Vector3(0, 1, -5);

        Current.Camera = _cameraP;
        _actorFreeLook3D = new ActorFreeLook3D();
        _actorFreeLook3D.sensitivity = 10f;

        var shaderAllInOne = File.ReadAllText(Path.Combine(Application.Path, "Assets/BasicAIO.glsl"));
        _shaderPipeline = ShaderComplier.CreateShaderPiplineFromGLSL(GraphicsDevice, shaderAllInOne, "BasicAIO");
    }

    protected override void OnUpdate(float delta)
    {
        _cameraP.tranform.rotation = _actorFreeLook3D.Rotation;
        
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
            _cameraP.tranform.Translate(Tranform.Forward * delta);
        }

        if (Input.IsKeyPressing(Key.S))
        {
            _cameraP.tranform.Translate(Tranform.Back * delta);
        }

        if (Input.IsKeyPressing(Key.A))
        {
            _cameraP.tranform.Translate(Tranform.Left * delta);
        }

        if (Input.IsKeyPressing(Key.D))
        {
            _cameraP.tranform.Translate(Tranform.Right * delta);
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

        _actorFreeLook3D.Update();
        

        Vector3 coloredCubePosition = new Vector3(1, 0.5f * math.sin(_timer), 0);
        //Vector3 coloredCubePosition = new Vector3(1, 1, 0);

        _timer += delta;
        //_cameraP.tranform.LookAt(coloredCubePosition);

        GraphicsCommand.UpdateCameraBuffer();
        GraphicsCommand.DrawMesh(MeshPool.Cube, _shaderPipeline, Matrix4x4.CreateRotationY(_timer));
        GraphicsCommand.DrawMesh(MeshPool.TestCube, _shaderPipeline, Matrix4x4.CreateRotationY(_timer + 1) * Matrix4x4.CreateTranslation(coloredCubePosition));
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