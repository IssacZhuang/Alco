using System;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Windowing;

using Vocore;
using Vocore.Engine;

public class Game : GameEngine
{
    private CameraPerspective _cameraP;
    private Shader _shaderBasic;
    private float _timer;

    private Transform3D _cubeTranform1 = Transform3D.Default;
    private ShapeBox3D _cubeShape1 = new ShapeBox3D(Vector3.Zero, Vector3.One, Quaternion.Identity);
    private bool _isHit = false;
    private ActorFreeLook3D _actorFreeLook3D;
    public Game(GameEngineSetting setting) : base(setting)
    {
    }

    protected override void OnStart()
    {
        _shaderBasic = Shader.ComplieAndAdd(new ShaderComplieDescription(LoadAsset("Assets/Basic.hlsl"), "Basic.hlsl"));

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
        _cameraP.SetAspectRatio(Window.AspectRatio);

        _cubeTranform1.position = new Vector3(1, 0.5f * math.sin(_timer), 0);
        _cubeTranform1.Rotate(Vector3.UnitY, delta);

        ShapeBox3D transformed = _cubeShape1.TransformByParent(_cubeTranform1);
        Ray3D ray = new Ray3D(_cameraP.tranform.position, _cameraP.tranform.Direction * 100);
        _isHit = UtilsCollision3D.RayBox(ray, transformed, out RaycastHit3D hit);

    }

    protected override void OnDraw(float delta)
    {
        IMeshData mesh = _isHit ? BuiltInMeshs.Cube : BuiltInMeshs.TestCube;
        Graphics.DrawMesh(mesh, _shaderBasic, _cubeTranform1);
    }

    public static string LoadAsset(string path)
    {
        return File.ReadAllText(Path.Combine(WorkingDirectory, path));
    }
}