using System;
using System.Numerics;
using System.IO;
using Vocore;
using Vocore.Engine;

public class Game : GameEngine
{
    private CameraPerspective _cameraP;
    private Shader _shaderBasic;
    private float _timer;

    private Transform _cubeTranform1 = Transform.Default;
    private Transform _cubeTranform2 = Transform.Default;
    override protected void OnStart()
    {
        Console.WriteLine("Hello World!");
        _cameraP = new CameraPerspective();
        _cameraP.tranform.position = new Vector3(0, 0, -5);

        var shaderBasic = File.ReadAllText(Path.Combine(WorkingDirectory, "Assets/Basic.glsl"));
        shaderBasic = ShaderComplier.ProcessInclude(shaderBasic, "Basic.glsl");
        _shaderBasic = new Shader(GraphicsDevice, shaderBasic, "Basic");
    }

    protected override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if(Input.IsKeyDown(Key.Escape))
        {
            Log.Info("Escape key pressed");
        }

        _timer += delta;

        _cubeTranform1.position = new Vector3(1, 0.5f * math.sin(_timer), 0);
        _cubeTranform1.Rotate(Transform.Up, delta);

        _cubeTranform2.Rotate(Transform.Up, delta);

        Graphics.DrawMesh(MeshPool.Cube, _shaderBasic, _cubeTranform1);
        Graphics.DrawMesh(MeshPool.TestCube, _shaderBasic, _cubeTranform2);
    }
}