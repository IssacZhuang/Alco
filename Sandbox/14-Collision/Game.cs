using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;

using Random = Vocore.Random;
using Vocore.Graphics;
using Vocore.GUI;

public class Game : GameEngine
{
    private static ColorFloat Color = new ColorFloat(1f, 0.5f, 0.5f, 1f);
    private static ColorFloat ColorHit = new ColorFloat(2.5f, 1.25f, 1.25f, 1f);
    //scence
    private readonly CameraPerspective _camera;

    private readonly Shader _shader;
    private readonly MaterialRenderer _renderer;
    private readonly GraphicsMaterial _material;

    private readonly Cube _entity;

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
        _material = Rendering.CreateGraphicsMaterial(_shader, "Unlit");

        _material.Set("_camera", _camera.Data.ViewProjectionMatrix);
        //_material["_texture"] = Rendering.TextureWhite;

        _plane = new Plane3D(new Vector3(0, 0, 1), 0);

        _entity = CreateCube(Color);
        _entity.transform.position = new Vector3(2, 0, 0);
        _entity.transform.rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 8);

        MainWindow.OnResize += OnMainWindowResize;
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }
        

        _renderer.Begin(MainFrameBuffer);
        _entity.OnDraw(_renderer);

        _renderer.End();

        Vector2 localMousePosition = MainWindow.MousePosition;

        Ray3D cameraRay = UtilsCameraMath.ScreenPointToRay(localMousePosition, MainWindow.Size, _camera.Data.ViewProjectionMatrix, _camera.Tranform.position);

        bool hit = UtilsCollision3D.RayBox(cameraRay * 10, _entity.Shape, out RaycastHit3D rayCastHit);

        _entity.Color = hit ? ColorHit : Color;

        _plane.IntersectRay(cameraRay, out Vector3 mouseWoldPosition);

        DebugGUI.Text(localMousePosition.ToString());
        DebugGUI.Text(mouseWoldPosition.ToString());
        if (Input.IsMouseDown(Mouse.Left) && hit)
        {
            offset = _entity.transform.position - mouseWoldPosition;
            _isDragging = true;
        }

        if (_isDragging)
        {
            _entity.transform.position = mouseWoldPosition + offset;
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
            _material.Set("_camera", _camera.Data.ViewProjectionMatrix);
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

    private Cube CreateCube(ColorFloat color)
    {
        Cube ent = new Cube(Rendering.MeshCube, _material);
        ent.Color = color;
        return ent;
    }
}