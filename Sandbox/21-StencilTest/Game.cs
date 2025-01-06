using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;

using Random = Vocore.Random;
using Vocore.Graphics;
using Vocore.GUI;

public class Game : GameEngine
{
    private static ColorFloat Color1 = new ColorFloat(0, 0, 0, 1f);
    private static ColorFloat Color2 = new ColorFloat(2.5f, 1.25f, 1.25f, 1f);
    private static ColorFloat Color3 = new ColorFloat(1.25f, 2.5f, 1.25f, 1f);
    //scence
    private Transform3D _camaraParent = Transform3D.Identity;
    private Transform3D _camaraChild = Transform3D.Identity;

    private readonly Shader _shader;
    private readonly MaterialRenderer _renderer;
    private readonly CameraPerspective _camera;
    private readonly GraphicsMaterial _materialStencilWrite;
    private readonly GraphicsMaterial _materialStencilTest;

    private readonly Cube _cubeStencilWrite;
    private readonly Cube _cubeStencilTest1;
    private readonly Cube _cubeStencilTest2;

    private readonly GPUCommandBuffer _commandClearScreen;

    public Game(GameEngineSetting setting) : base(setting)
    {

        _shader = Assets.Load<Shader>(BuiltInAssetsPath.Shader_Unlit);

        // _camera = new CameraDataPerspective(1.03f, 0.1f, 1000, 16f / 9);
        // _camaraChild.position.Z = -10;
        // _camera.tranform = math.transform(_camaraParent, _camaraChild);

        _camera = Rendering.CreateCameraPerspective(1.03f, 16f / 9, 0.1f, 1000);
        _camaraChild.position.Z = -10;
        _camera.Tranform = math.transform(_camaraParent, _camaraChild);

        _renderer = Rendering.CreateMaterialRenderer();
        _materialStencilWrite = Rendering.CreateGraphicsMaterial(_shader, "Unlit");
        _materialStencilWrite.SetBuffer("_camera", _camera);
        _materialStencilWrite.DepthStencilState = new DepthStencilState
        {
            DepthWriteEnabled = false,
            DepthCompare = CompareFunction.Always,
            FrontFace = StencilFaceState.Write,
            BackFace = StencilFaceState.Write,
            StencilReadMask = 0xFF,
            StencilWriteMask = 0xFF,
        };

        _materialStencilWrite.StencilReference = 250;

        _materialStencilTest = Rendering.CreateGraphicsMaterial(_shader, "Unlit");
        _materialStencilTest.SetBuffer("_camera", _camera);
        _materialStencilTest.DepthStencilState = new DepthStencilState
        {
            DepthWriteEnabled = false,
            DepthCompare = CompareFunction.LessEqual,
            FrontFace = StencilFaceState.CompareEqual,
            BackFace = StencilFaceState.CompareEqual,
            StencilReadMask = 0xFF,
            StencilWriteMask = 0xFF,
        };

        _materialStencilTest.StencilReference = 250;

        _cubeStencilWrite = new Cube(Rendering.MeshCube, _materialStencilWrite);
        _cubeStencilWrite.Color = Color1;
        _cubeStencilWrite.transform.position = new Vector3(0, 0, 0);
        _cubeStencilWrite.transform.scale = new Vector3(5f, 5f, 0.1f);

        _cubeStencilTest1 = new Cube(Rendering.MeshCube, _materialStencilTest);
        _cubeStencilTest1.Color = Color2;
        _cubeStencilTest1.transform.position = new Vector3(2, 0, 3f);

        _cubeStencilTest2 = new Cube(Rendering.MeshCube, _materialStencilTest);
        _cubeStencilTest2.Color = Color3;
        _cubeStencilTest2.transform.position = new Vector3(-1, -1, 3f);

        _commandClearScreen = GraphicsDevice.CreateCommandBuffer();

        MainWindow.OnResize += OnMainWindowResize;
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        _commandClearScreen.Begin();
        _commandClearScreen.SetFrameBuffer(MainFrameBuffer);
        _commandClearScreen.ClearColor(new ColorFloat(0.2f, 0.2f, 0.2f, 1), 0);
        _commandClearScreen.End();
        Rendering.ScheduleCommandBuffer(_commandClearScreen);


        _renderer.Begin(MainFrameBuffer);
        _cubeStencilWrite.OnDraw(_renderer);
        _cubeStencilTest1.OnDraw(_renderer);
        _cubeStencilTest2.OnDraw(_renderer);
        _renderer.End();

        _camaraParent.Rotate(Vector3.UnitY, Input.MouseDelta.X * 0.01f);
        _camaraParent.Rotate(Vector3.UnitX, Input.MouseDelta.Y * 0.01f);

        _camera.Tranform = math.transform(_camaraParent, _camaraChild);
        _camera.UpdateData();
    }



    protected void OnMainWindowResize(uint2 size)
    {
        _camera.AspectRatio = (float)size.x / size.y;
    }

    protected override void OnStop()
    {

    }

}