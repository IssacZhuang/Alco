using System;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Windowing;
using Veldrid;
using Vocore;
using Vocore.Engine;

public class Game : GameEngine
{
    private CameraPerspective _cameraP;
    private Shader _shaderBasic;
    private float _timer;

    private Transform3D _cubeTranform1 = Transform3D.Default;
    private Transform3D _cubeTranform2 = Transform3D.Default;
    private ActorFreeLook3D _actorFreeLook3D;
    private DrawList _drawList;
    private GpuResourceGroup _bufferGroup;
    private UniformBuffer<Matrix4x4> _transformBuffer;
    private MeshBuffer _cube1;
    private MeshBuffer _cube2;
    public Game(GameEngineSetting setting) : base(setting)
    {
    }

    protected override void OnStart()
    {
        ShaderComplieDescription shaderInput = new ShaderComplieDescription(LoadAsset("Assets/Basic.hlsl"), "Basic.hlsl");
        _shaderBasic = Shader.ComplieAndAdd(shaderInput);
        Log.Info(_shaderBasic.GetReflectionInfo());

        _cameraP = new CameraPerspective();
        _cameraP.tranform.position = new Vector3(0, 0, -5);
        Camera = _cameraP;

        _actorFreeLook3D = new ActorFreeLook3D();
        _actorFreeLook3D.sensitivity = 10f;

        _drawList = new DrawList(GraphicsDevice);

        _transformBuffer = new UniformBuffer<Matrix4x4>(GraphicsDevice);

        OffscreenBuffer renderTarget = new OffscreenBuffer(GraphicsDevice, 
        (uint)Window.Size.x,
        (uint)Window.Size.y, 
        new PixelFormat[]{
            PixelFormat.R16_G16_B16_A16_UNorm
        }, 
            CompatibilityHelper.GetPlatformDepthTestingFormat()
        );

        _bufferGroup = new GpuResourceGroup(_shaderBasic);
        _bufferGroup.TrySet("type.GlobalBuffer", _frame.GlobalShaderData);
        _bufferGroup.TrySet("type.TransformBuffer", _transformBuffer);

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
        // Graphics.DrawMesh(BuiltInMeshs.Cube, _shaderBasic, _cubeTranform1);
        // Graphics.DrawMesh(BuiltInMeshs.TestCube, _shaderBasic, _cubeTranform2);

        _drawList.Begin();
        _transformBuffer.Value = _cubeTranform1.Matrix;
        _drawList.DrawMesh(_cube1, _shaderBasic.Pipeline, _bufferGroup);
        _transformBuffer.Value = _cubeTranform2.Matrix;
        _drawList.DrawMesh(_cube2, _shaderBasic.Pipeline, _bufferGroup);
        _drawList.End();
        //_drawList.PushToScreen();
    }

    public static string LoadAsset(string path)
    {
        return File.ReadAllText(Path.Combine(WorkingDirectory, path));
    }
}