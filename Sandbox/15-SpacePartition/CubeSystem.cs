

using System.Numerics;
using Vocore;
using Vocore.Graphics;
using Vocore.GUI;
using Vocore.Rendering;
using Random = Vocore.Random;

public class CubeSystem
{
    private static readonly ColorFloat DefaultColor = 0xffffff;
    private readonly SpriteRenderer _renderer;
    private readonly Texture2D _texture;
    private readonly List<Cube> _activeList = new List<Cube>();
    private readonly Stack<Cube> _despawnList = new Stack<Cube>();
    private readonly Pool<Cube> _pool = new Pool<Cube>(10000, () => new Cube());


    private Random _random = new Random(123);

    public CubeSystem(SpriteRenderer renderer, Texture2D texDroplet)
    {
        _renderer = renderer;
        _texture = texDroplet;
    }

    public void OnTick(float delta)
    {

        for (int i = 0; i < _activeList.Count; i++)
        {
            Cube entity = _activeList[i];

            if (entity.pendingDestroy)
            {
                _despawnList.Push(entity);
            }
        }

        while (_despawnList.Count > 0)
        {
            Cube entity = _despawnList.Pop();
            _activeList.Remove(entity);
            _pool.TryReturn(entity);
        }
    }

    public void Spawn(Vector3 position)
    {
        Log.Info("Spawn", position);
        if (_pool.TryGet(out Cube? entity))
        {
            entity.transform.position = position;
            entity.color = DefaultColor;
            entity.pendingDestroy = false;
            _activeList.Add(entity);
        }
    }

    public void PushCollisionCaster(CollisionWorld3D collisionWorld)
    {
        for (int i = 0; i < _activeList.Count; i++)
        {
            Cube entity = _activeList[i];
            collisionWorld.PushCaster(entity, entity.Shape);
        }
    }

    public void OnUpdate(GPUFrameBuffer frame, float delta)
    {
        _renderer.Begin(frame);
        for (int i = 0; i < _activeList.Count; i++)
        {
            Cube entity = _activeList[i];
            _renderer.Draw(_texture, entity.transform, entity.color);
        }
        _renderer.End();

    }
}