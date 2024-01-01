using System;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Windowing;

using Vocore;
using Vocore.Engine;

public class Game : GameEngine
{
    private CameraPerspective _cameraP;

    private float _timer;

    private Transform3D _cubeTranform1 = Transform3D.Default;
    private Transform3D _cubeTranform2 = Transform3D.Default;
    private ActorFreeLook3D _actorFreeLook3D;
    private DrawList _drawList;
    private Shader _shader;
    private MeshBuffer _cube1;
    private MeshBuffer _cube2;
    public Game(GameEngineSetting setting) : base(setting)
    {
    }

    protected override void OnStart()
    {
        Assets.AddFileSource(new DirectoryFileSource(WorkingDirectory));
        Assets.TryLoad<Shader>("Assets/Basic.hlsl", out _shader);
        Log.Info(_shader.GetReflectionInfo());
        
        _cameraP = new CameraPerspective();
        _cameraP.tranform.position = new Vector3(0, 0, -5);
        Camera = _cameraP;

        _actorFreeLook3D = new ActorFreeLook3D();
        _actorFreeLook3D.sensitivity = 10f;

        _drawList = new DrawList(this);

        _cube1 = new MeshBuffer(GraphicsDevice, BuiltInMeshs.Cube);
        _cube2 = new MeshBuffer(GraphicsDevice, BuiltInMeshs.TestCube);

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
        _cameraP.SetAspectRatio(Window.AspectRatio);

        _cubeTranform1.position = new Vector3(1, 0.5f * math.sin(_timer), 0);
        _cubeTranform1.Rotate(Vector3.UnitY, delta);
        _cubeTranform2.Rotate(Vector3.UnitY, delta);
    }

    protected override void OnDraw(float delta)
    {
        _drawList.Begin();
        _drawList.DrawMeshTranformed(_cube1, _shader, _cubeTranform1);
        _drawList.DrawMeshTranformed(_cube2, _shader, _cubeTranform2);
        _drawList.End();
    }

    public static string LoadAsset(string path)
    {
        return File.ReadAllText(Path.Combine(WorkingDirectory, path));
    }
}