

using System.Numerics;
using Alco;
using Alco.Graphics;
using Alco.GUI;
using Alco.Rendering;
using Random = Alco.Random;

public class CubeSystem
{
    private static readonly ColorFloat DefaultColor = 0xff4444;
    private readonly OldSpriteRenderer _renderer;
    private readonly Texture2D _texture;
    private readonly List<Cube> _activeList = new List<Cube>();
    private readonly Stack<Cube> _despawnList = new Stack<Cube>();
    private readonly Pool<Cube> _pool = new Pool<Cube>(10000, () => new Cube());

    private Random _random = new Random(123);

    public CubeSystem(OldSpriteRenderer renderer, Texture2D texDroplet)
    {
        _renderer = renderer;
        _texture = texDroplet;
    }

    public void OnTick(float delta)
    {

        for (int i = 0; i < _activeList.Count; i++)
        {
            Cube entity = _activeList[i];

            if (entity.PendingDestroy)
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
            entity.Transform.Position = new Vector2(position.X, position.Y);
            entity.Color = DefaultColor;
            entity.PendingDestroy = false;
            entity.Transform.Rotation = Rotation2D.FromDegree(_random.NextFloat(0,360));
            _activeList.Add(entity);
        }
    }

    public void PushCollisionCaster(CollisionWorld2D collisionWorld)
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
            _renderer.Draw(_texture, entity.Transform, entity.Color);
        }
        _renderer.End();

    }
}