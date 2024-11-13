using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;

using Random = Vocore.Random;
using Vocore.Graphics;
using Vocore.GUI;

public class Game : GameEngine
{
    private static ColorFloat Color1 = new ColorFloat(0.2f, 0.2f, 0.2f, 1f);
    private static ColorFloat Color2 = new ColorFloat(2.5f, 1.25f, 1.25f, 1f);
    private static ColorFloat Color3 = new ColorFloat(1.25f, 2.5f, 1.25f, 1f);
    //scence
    private readonly CameraPerspective _camera;

    private readonly Shader _shader;
    private readonly MaterialRenderer _renderer;
    private readonly GraphicsMaterial _materialStencilWrite;
    private readonly GraphicsMaterial _materialStencilTest;

    private readonly Cube _cubeStencilWrite;
    private readonly Cube _cubeStencilTest1;
    private readonly Cube _cubeStencilTest2;

    private Plane3D _plane;
    private Vector3 offset;
    private bool _isDragging = false;

    public Game(GameEngineSetting setting) : base(setting)
    {

        _shader = Assets.Load<Shader>(BuiltInAssetsPath.Shader_Unlit);

        _camera = Rendering.CreateCameraPerspective(1.03f, 16f / 9, 0.1f, 1000);

        _camera.Tranform.position.Z = -10;
        _camera.UpdateData();

        _renderer = Rendering.CreateMaterialRenderer();
        _materialStencilWrite = Rendering.CreateGraphicsMaterial(_shader, "Unlit");
        _materialStencilWrite.SetValue("_camera", _camera.Data.ViewProjectionMatrix);
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
        _materialStencilTest.SetValue("_camera", _camera.Data.ViewProjectionMatrix);
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

        _plane = new Plane3D(new Vector3(0, 0, 1), 0);

        _cubeStencilWrite = new Cube(Rendering.MeshCube, _materialStencilWrite);
        _cubeStencilWrite.Color = Color1;
        _cubeStencilWrite.transform.position = new Vector3(0, 0, 0);
        _cubeStencilWrite.transform.scale = new Vector3(3f, 3f, 0.1f);

        _cubeStencilTest1 = new Cube(Rendering.MeshCube, _materialStencilTest);
        _cubeStencilTest1.Color = Color2;
        _cubeStencilTest1.transform.position = new Vector3(2, 0, 1.5f);

        _cubeStencilTest2 = new Cube(Rendering.MeshCube, _materialStencilTest);
        _cubeStencilTest2.Color = Color3;
        _cubeStencilTest2.transform.position = new Vector3(-1, -1, 3f);

        MainWindow.OnResize += OnMainWindowResize;
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }


        _renderer.Begin(MainFrameBuffer);
        _cubeStencilWrite.OnDraw(_renderer);
        _cubeStencilTest1.OnDraw(_renderer);
        _cubeStencilTest2.OnDraw(_renderer);
        _renderer.End();

        Vector2 localMousePosition = MainWindow.MousePosition;

        Ray3D cameraRay = UtilsCameraMath.ScreenPointToRay(localMousePosition, MainWindow.Size, _camera.Data.ViewProjectionMatrix, _camera.Tranform.position);

        bool hit = UtilsCollision3D.RayBox(cameraRay * 10, _cubeStencilWrite.Shape, out RaycastHit3D rayCastHit);

        //_cubeStencilWrite.Color = hit ? ColorHit : Color1;

        _plane.IntersectRay(cameraRay, out Vector3 mouseWoldPosition);

        DebugGUI.Text(localMousePosition.ToString());
        DebugGUI.Text(mouseWoldPosition.ToString());
        if (Input.IsMouseDown(Mouse.Left) && hit)
        {
            offset = _cubeStencilWrite.transform.position - mouseWoldPosition;
            _isDragging = true;
        }

        if (_isDragging)
        {
            _cubeStencilWrite.transform.position = mouseWoldPosition + offset;
        }

        if (Input.IsMouseUp(Mouse.Left))
        {
            _isDragging = false;
        }

        //debug ui
        DebugGUI.Text("Camera Data");
        int fov = (int)(_camera.FieldOfView * 100);
        if (DebugGUI.Slider(ref fov, 30, 110))
        {
            _camera.FieldOfView = fov / 100f;
            _materialStencilWrite.SetValue("_camera", _camera.Data.ViewProjectionMatrix);
        }

        DebugGUI.SameLine();
        DebugGUI.Text("Fov");
    }



    protected void OnMainWindowResize(uint2 size)
    {
        _camera.AspectRatio = (float)size.x / size.y;
        _camera.UpdateData();
    }

    protected override void OnStop()
    {

    }

}