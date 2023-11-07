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
    private Shader _shaderBasic;
    private Shader _shaderInstanced;
    private Shader _shaderTexture;
    private CameraPerspective _cameraP;
    private float _timer;
    private int _fps;
    private ActorFreeLook3D _actorFreeLook3D;
    private Transform _cubeTranform1 = Transform.Default;
    private Transform _cubeTranform2 = Transform.Default;
    private Texture2D _texture;
    public struct TestJob : IJobBatch
    {
        public void Execute(int i)
        {
            int tmp = i;
        }
    }



    public App(GraphicsBackend backend, string name) : base(backend, name)
    {

    }

    public App(string name) : base(name)
    {
        NativeBuffer<int> buffer = new NativeBuffer<int>(1000);
    }

    protected override void OnStart()
    {
        _cameraP = new CameraPerspective();
         _cameraP.tranform.position = new Vector3(100, 100, -300);
        //_cameraP.tranform.position = new Vector3(0, 0, -5);
        Current.Camera = _cameraP;
        //this.Fullscreen = true;

        _cubeTranform2.rotation = math.euler(new Vector3(0, 1, 0));

        _actorFreeLook3D = new ActorFreeLook3D();
        _actorFreeLook3D.sensitivity = 10f;

        var shaderBasic = File.ReadAllText(Path.Combine(Application.Path, "Assets/Basic.glsl"));
        shaderBasic = ShaderComplier.ProcessInclude(shaderBasic, "Basic.glsl");
        _shaderBasic = new Shader(GraphicsDevice, shaderBasic, "Basic");

        var shaderInstanced = File.ReadAllText(Path.Combine(Application.Path, "Assets/Instanced.glsl"));
        shaderInstanced = ShaderComplier.ProcessInclude(shaderInstanced, "Instanced.glsl");
        _shaderInstanced = new Shader(GraphicsDevice, shaderInstanced, "Instanced");
        Log.Info(_shaderInstanced.GetReflectionInfo());

        Stream textureFS = File.OpenRead(Path.Combine(Application.Path, "Assets/Rectangle.png"));
        _texture =Texture2D.FromStream(GraphicsDevice, textureFS);
        var shaderTexture = File.ReadAllText(Path.Combine(Application.Path, "Assets/Texture.glsl"));
        shaderTexture = ShaderComplier.ProcessInclude(shaderTexture, "Texture.glsl");
        _shaderTexture = new Shader(GraphicsDevice, shaderTexture, "Texture");

        ThreadManager.AddAsyncTask(() =>
        {
            Log.Info("Async task");
        }, () =>
        {
            Log.Info("Async task finished");
        });
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

        // GraphicsCommand.UpdateGlobalData();
        // Graphics.DrawMesh(MeshPool.Cube, _shaderBasic, _cubeTranform1);
        //Graphics.DrawMesh(MeshPool.TestCube, _shaderBasic, _cubeTranform2);
        Graphics.DrawMeshIntanced(MeshPool.Cube, _shaderInstanced, Transform.Default, 40000);
        //Graphics.DrawMeshWithTexture(MeshPool.Cube, _shaderTexture, _cubeTranform1, _texture.ResourceSet);
        //Graphics.DrawMeshWithTexture(MeshPool.Cube, _shaderTexture, _cubeTranform2, _texture.ResourceSet);
        TestJob job = new TestJob();
        job.RunParallel(1000);
        job.RunParallel(1000);
        // ThreadManager.AddAsyncTask(() =>
        // {

        // }, () =>
        // {
        //     int tmp = 1;
        // });
    }

    protected override void OnTick(float delta)
    {
        if (_fps != Profiler.FPS)
        {
            _fps = Profiler.FPS;
            //Log.Info(_fps.ToString());
        }
    }

    protected override void OnWindowResize(int width, int height)
    {
        Log.Info($"Window resized to {width}x{height}");
    }


}