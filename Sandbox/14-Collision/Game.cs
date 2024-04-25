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

        _camera = Rendering.CreateCameraPerspective(1.03f, 16 / 9, 0.1f, 1000);

        _camera.Tranform.position.Z = -10;
        

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
    }

    protected override void OnResize(int2 size)
    {
        _camera.AspectRatio = (float)size.x / size.y;
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