using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;

using Random = Vocore.Random;
using Vocore.Graphics;
using Vocore.GUI;

public class Game : GameEngine
{
    //scence
    private readonly CameraPerspective _camera;

    private readonly Shader _shader;
    private readonly MaterialRenderer _renderer;
    private readonly UniversalMaterial _material;

    private readonly List<Entity> _entities = new List<Entity>();


    public Game(GameEngineSetting setting) : base(setting)
    {

        _shader = Assets.Load<Shader>("Rendering/Shader/3D/Unlit.hlsl");

        _camera = Rendering.CreateCameraPerspective(1.03f, 16f / 9, 0.1f, 1000);

        _camera.Tranform.position.Z = -10;
        _camera.UpdateData();

        _renderer = Rendering.CreateMaterialRenderer();
        _material = new UniversalMaterial(_shader);

        _material["_camera"] = _camera.Buffer;
        _material["_texture"] = Rendering.TextureWhite;

        _entities.Add(CreateCube(0xff7777));
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        _renderer.Begin(Rendering.DefaultFrameBuffer);
        for (int i = 0; i < _entities.Count; i++)
        {
            _entities[i].OnDraw(_renderer);
        }

        _renderer.End();

        Ray3D cameraRay = UtilsCameraMath.ScreenPointToRay(Input.MousePosition, Window.Size, _camera.Data.ViewProjectionMatrix, _camera.Tranform.position);
        ShapeBox3D box = new ShapeBox3D(new Vector3(0, 0, 0), new Vector3(1, 1, 1), Quaternion.Identity);

        bool hit = UtilsCollision3D.RayBox(cameraRay * 1000, box, out RaycastHit3D rayCastHit);

        //debug ui
        DebugGUI.Text("Camera Data");
        int fov = (int)(_camera.FieldOfView * 100);
        DebugGUI.Slider(60, 110, ref fov);
        _camera.FieldOfView = fov / 100f;
        DebugGUI.SameLine();
        DebugGUI.Text("Fov");

        if (hit)
        {
            DebugGUI.Text("Hit");
            DebugGUI.Text(rayCastHit.point.ToString());
        }
        else
        {
            DebugGUI.Text("No Hit");
        }

        _camera.UpdateData();
    }



    protected override void OnResize(int2 size)
    {
        _camera.AspectRatio = (float)size.x / size.y;
        _camera.UpdateData();
    }

    protected override void OnStop()
    {

    }

    private Entity CreateCube(ColorFloat color)
    {
        Entity ent = new Entity(Rendering.MeshCube, _material);
        ent.Color = color;
        return ent;
    }
}