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

    private Transform3D _cubeTranform1 = Transform3D.Default;
    private Transform3D _cubeTranform2 = Transform3D.Default;
    private ActorFreeLook3D _actorFreeLook3D;
    override protected void OnStart()
    {
        Console.WriteLine("Hello World!");
        _cameraP = new CameraPerspective();
        _cameraP.tranform.position = new Vector3(0, 0, -5);
        Camera = _cameraP;

        ShaderComplier complier = new ShaderComplier(GraphicsDevice);
        var shaderBasic = File.ReadAllText(Path.Combine(WorkingDirectory, "Assets/Basic.glsl"));

        ShaderComplieDescription shaderInput = new ShaderComplieDescription(shaderBasic, "Basic");

        _shaderBasic = complier.Complie(shaderInput);

        _actorFreeLook3D = new ActorFreeLook3D();
        _actorFreeLook3D.sensitivity = 10f;
    }

    protected override void OnUpdate(float delta)
    {

        
    }

    protected override void OnDraw(float delta)
    {
        Graphics.DrawMesh(BuiltInMeshs.Cube, _shaderBasic, _cubeTranform1);
        Graphics.DrawMesh(BuiltInMeshs.TestCube, _shaderBasic, _cubeTranform2);
    }
}