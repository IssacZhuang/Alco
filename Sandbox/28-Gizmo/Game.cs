using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;

using Random = Alco.Random;
using Alco.Graphics;
using Alco.GUI;
using System.Diagnostics;
using Alco.ImGUI;

public class Game : GameEngine
{
    private static ColorFloat Color1 = new ColorFloat(0, 0, 0, 1f);
    private static ColorFloat Color2 = new ColorFloat(2.5f, 1.25f, 1.25f, 1f);
    private static ColorFloat Color3 = new ColorFloat(1.25f, 2.5f, 1.25f, 1f);
    //scence
    private Transform3D _camaraParent = Transform3D.Identity;
    private Transform3D _camaraChild = Transform3D.Identity;

    private readonly Shader _shader;
    private readonly RenderContext _renderer;
    private readonly CameraPerspective _camera;
    private readonly GraphicsMaterial _material;

    private readonly Cube _cube;

    private readonly GPUCommandBuffer _commandClearScreen;

    private string[] _operationNames = new string[] { "Translate", "Rotate", "Scale" };
    private int _currentOperation = 0;
    private OPERATION _currentOperationEnum = OPERATION.TRANSLATE;

    public Game(GameEngineSetting setting) : base(setting)
    {

        _shader = Assets.Load<Shader>(BuiltInAssetsPath.Shader_Unlit);

        // _camera = new CameraDataPerspective(1.03f, 0.1f, 1000, 16f / 9);
        // _camaraChild.position.Z = -10;
        // _camera.tranform = math.transform(_camaraParent, _camaraChild);

        _camera = Rendering.CreateCameraPerspective(1.03f, 16f / 9, 0.1f, 1000);
        _camaraChild.Position.Z = -10;
        _camera.Tranform = math.transform(_camaraParent, _camaraChild);

        _renderer = Rendering.CreateRenderContext();

        _material = Rendering.CreateGraphicsMaterial(_shader, "Unlit");
        _material.SetBuffer("_camera", _camera);

        _cube = new Cube(Rendering.MeshCube, _material);
        _cube.Color = Color2;
        _cube.transform.Position = new Vector3(0, 0, 0);

        _commandClearScreen = GraphicsDevice.CreateCommandBuffer();

        MainView.OnResize += OnMainWindowResize;
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
        _cube.OnDraw(_renderer);
        _renderer.End();

        if (Input.IsMousePressing(Mouse.Middle))
        {
            _camaraParent.Rotate(Vector3.UnitY, Input.MouseDelta.X * 0.01f);
            _camaraParent.Rotate(Vector3.UnitX, Input.MouseDelta.Y * 0.01f);
        }

        _camera.Tranform = math.transform(_camaraParent, _camaraChild);
        _camera.UpdateMatrixToGPU();

        ImGui.Begin("Transform");
        ImGui.Text("Hold mouse middle button to rotate camera");
        if (ImGui.Combo("Operation", ref _currentOperation, _operationNames, 3))
        {
            switch (_currentOperation){
                case 0:
                    _currentOperationEnum = OPERATION.TRANSLATE;
                    break;
                case 1:
                    _currentOperationEnum = OPERATION.ROTATE;
                    break;
                case 2:
                    _currentOperationEnum = OPERATION.SCALE;
                    break;
            }
        }

        ImGui.End();

        ImGuizmo.Manipulate(_camera.Data.ViewMatrix, _camera.Data.ProjectionMatrix, _currentOperationEnum, MODE.LOCAL, ref _cube.transform);
    }



    protected void OnMainWindowResize(uint2 size)
    {
        _camera.AspectRatio = (float)size.X / size.Y;
    }

    protected override void OnStop()
    {

    }

}