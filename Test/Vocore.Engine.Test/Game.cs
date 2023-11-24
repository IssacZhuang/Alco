using System;
using System.Numerics;
using System.IO;
using Vocore;
using Vocore.Engine;
using Veldrid;

using Shader = Vocore.Engine.Shader;

public class Game : GameEngine
{
    private CameraPerspective _cameraP;
    private Shader _shaderBasic;
    private float _timer;

    private Transform _cubeTranform1 = Transform.Default;
    private Transform _cubeTranform2 = Transform.Default;
    private ActorFreeLook3D _actorFreeLook3D;
    override protected void OnStart()
    {
        Console.WriteLine("Hello World!");
        _cameraP = new CameraPerspective();
        _cameraP.tranform.position = new Vector3(0, 0, -5);
        Camera = _cameraP;

        ShaderComplier complier = new ShaderComplier(GraphicsDevice);
        var shaderBasic = File.ReadAllText(Path.Combine(WorkingDirectory, "Assets/Basic.glsl"));
        _shaderBasic = complier.Complie(shaderBasic, "Basic");

        _actorFreeLook3D = new ActorFreeLook3D();
        _actorFreeLook3D.sensitivity = 10f;
    }

    protected override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        _actorFreeLook3D.Update();
        _cameraP.tranform.rotation = _actorFreeLook3D.Rotation;
        if(Input.IsKeyDown(Key.Escape))
        {
            //Stop();
        }

        if (Input.IsKeyDown(Key.F11))
        {
            Window.WindowState = Window.WindowState == WindowState.BorderlessFullScreen ? WindowState.Normal : WindowState.BorderlessFullScreen;
        }

        _timer += delta;

        _cubeTranform1.position = new Vector3(1, 0.5f * math.sin(_timer), 0);
        _cubeTranform1.Rotate(Transform.Up, delta);

        _cubeTranform2.Rotate(Transform.Up, delta);

        
    }

    protected override void OnDraw(float delta)
    {
        Graphics.DrawMesh(BuiltInMeshs.Cube, _shaderBasic, _cubeTranform1);
        Graphics.DrawMesh(BuiltInMeshs.TestCube, _shaderBasic, _cubeTranform2);
    }
}