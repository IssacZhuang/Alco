using System;
using System.Numerics;
using System.IO;
using System.Text;
using Veldrid;
using Vocore;
using Vocore.Engine;
using Shader = Vocore.Engine.Shader;

public class App : Engine
{
    private Shader _shader;
    private CameraPerspective _cameraP;
    private float _timer;
    private int _fps;
    private ActorFreeLook3D _actorFreeLook3D;
    private Transform _cubeTranform1 = Transform.Default;
    private Transform _cubeTranform2 = Transform.Default;

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
        //this.Fullscreen = true;

        _cubeTranform2.rotation = math.EulerXYZ(new Vector3(0, 1, 0));

        _actorFreeLook3D = new ActorFreeLook3D();
        _actorFreeLook3D.sensitivity = 10f;

        var shaderAllInOne = File.ReadAllText(Path.Combine(Application.Path, "Assets/BasicAIO.glsl"));
        _shader = new Shader(GraphicsDevice, shaderAllInOne, "BasicAIO");
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
            _cameraP.tranform.Translate(Transform.Forward * delta);
        }

        if (Input.IsKeyPressing(Key.S))
        {
            _cameraP.tranform.Translate(Transform.Back * delta);
        }

        if (Input.IsKeyPressing(Key.A))
        {
            _cameraP.tranform.Translate(Transform.Left * delta);
        }

        if (Input.IsKeyPressing(Key.D))
        {
            _cameraP.tranform.Translate(Transform.Right * delta);
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


        _cubeTranform1.position = new Vector3(1, 0.5f * math.sin(_timer), 0);
        _cubeTranform1.Rotate(Transform.Up, delta);

        _cubeTranform2.Rotate(Transform.Up, delta);
        //Vector3 coloredCubePosition = new Vector3(1, 1, 0);

        _timer += delta;
        //_cameraP.tranform.LookAt(coloredCubePosition);

        GraphicsCommand.UpdateCameraBuffer();
        GraphicsCommand.DrawMesh(MeshPool.Cube, _shader, _cubeTranform1);
        GraphicsCommand.DrawMesh(MeshPool.TestCube, _shader, _cubeTranform2);
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