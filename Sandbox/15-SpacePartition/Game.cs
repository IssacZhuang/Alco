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
    private readonly Camera2D _camera;

    private readonly Shader _shaderCube;
    private readonly MaterialRenderer _universalRenderer;
    private readonly UniversalMaterial _material;

    private readonly Shader _shaderSprite;
    private readonly SpriteRenderer _spriteRenderer;
    private readonly DropletSystem _dropletSystem;
    private readonly Texture2D _texDroplet;

    private readonly Cube _entity;

    private Plane3D _plane;
    private Vector3 offset;
    private bool _isDragging = false;

    public Game(GameEngineSetting setting) : base(setting)
    {

        _shaderCube = Assets.Load<Shader>("Rendering/Shader/3D/Unlit.hlsl");
        _shaderSprite = Assets.Load<Shader>("Rendering/Shader/2D/Sprite.hlsl");
        _texDroplet = Assets.Load<Texture2D>("Droplet.png");

        _camera = Rendering.CreateCamera2D(960, 540, 100f);

        _camera.UpdateData();

        _universalRenderer = Rendering.CreateMaterialRenderer();
        _material = new UniversalMaterial(_shaderCube);

        _material["_camera"] = _camera.Buffer;
        _material["_texture"] = Rendering.TextureWhite;

        _plane = new Plane3D(new Vector3(0, 0, 1), 0);

        _spriteRenderer  = Rendering.CreateSpriteRenderer(_camera, _shaderSprite);

        _dropletSystem = new DropletSystem(_spriteRenderer, _texDroplet);

        _entity = CreateCube(Color);
        _entity.transform.position = new Vector3(2, 0, 0);
    }

    protected override void OnTick(float delta)
    {
        base.OnTick(delta);
        _dropletSystem.OnTick(delta);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        _universalRenderer.Begin(Rendering.DefaultFrameBuffer);
        _entity.OnDraw(_universalRenderer);

        _universalRenderer.End();

        Ray3D cameraRay = UtilsCameraMath.ScreenPointToRay(Input.MousePosition, Window.Size, _camera.Data.ViewProjectionMatrix, Vector3.Zero);

        bool hit = UtilsCollision3D.RayBox(cameraRay * 1000, _entity.Shape, out RaycastHit3D rayCastHit);

        _entity.Color = hit ? ColorHit : Color;

        _plane.IntersectRay(cameraRay, out Vector3 mouseWoldPosition);

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
        DebugGUI.Text(FrameRate);

        _camera.UpdateData();
        _dropletSystem.OnUpdate(Rendering.DefaultFrameBuffer, delta);
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