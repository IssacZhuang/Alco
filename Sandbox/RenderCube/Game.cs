using System;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Windowing;
using Veldrid.SPIRV;
using Vocore;
using Vocore.Engine;

public class Game : GameEngine
{
    private CameraPerspective _cameraP;
    private Shader _shaderBasic;
    private float _timer;

    private Transform _cubeTranform1 = Transform.Default;
    private Transform _cubeTranform2 = Transform.Default;
    private ActorFreeLook3D _actorFreeLook3D;
    public Game(GameEngineSetting setting) : base(setting)
    {
    }

    protected override void OnStart()
    {
        _shaderBasic = Shader.Complie(LoadAsset("Assets/Basic.hlsl"), "Basic.hlsl");
        Log.Info(_shaderBasic.GetReflectionInfo());
        _cameraP = new CameraPerspective();
        _cameraP.tranform.position = new Vector3(0, 0, -5);
        Camera = _cameraP;

        _actorFreeLook3D = new ActorFreeLook3D();
        _actorFreeLook3D.sensitivity = 10f;
 
    }
    protected override void OnUpdate(float delta)
    {
        
        if (Input.IsKeyDown(Key.Escape))
        {
            Stop();
        }

        if (Input.IsKeyDown(Key.F11))
        {
            Window.WindowState = Window.WindowState == WindowState.Fullscreen ? WindowState.Normal : WindowState.Fullscreen;
        }

        _timer += delta;

        _actorFreeLook3D.Update();
        _cameraP.tranform.rotation = _actorFreeLook3D.Rotation;

        _cubeTranform1.position = new Vector3(1, 0.5f * math.sin(_timer), 0);
        _cubeTranform1.Rotate(Transform.Up, delta);

        _cubeTranform2.Rotate(Transform.Up, delta);
    }

    protected override void OnDraw(float delta)
    {
        Graphics.DrawMesh(MeshPool.Cube, _shaderBasic, _cubeTranform1);
        Graphics.DrawMesh(MeshPool.TestCube, _shaderBasic, _cubeTranform2);
    }

    public static string LoadAsset(string path)
    {
        return File.ReadAllText(Path.Combine(WorkingDirectory, path));
    }
}