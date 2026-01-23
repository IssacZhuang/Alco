

using System.Numerics;
using Alco;
using Alco.Graphics;
using Alco.GUI;
using Alco.Rendering;
using FastRandom = Alco.FastRandom;

public class CubeSystem : IDisposable
{
    private static readonly ColorFloat DefaultColor = 0xff4444;
    private readonly Texture2D _texture;
    private readonly List<Cube> _activeList = new List<Cube>();
    private readonly Stack<Cube> _despawnList = new Stack<Cube>();
    private readonly Pool<Cube> _pool = new Pool<Cube>(10000, () => new Cube());
    private readonly RenderContext _renderContext;
    private readonly SpriteRenderer _spriteRenderer;
    private readonly PerformCollisionTask _collisionTask;

    private FastRandom _random = new FastRandom(123);

    public CubeSystem(RenderingSystem rendering, Material material, Texture2D texCube)
    {
        _texture = texCube;
        _renderContext = rendering.CreateRenderContext();
        _spriteRenderer = rendering.CreateSpriteRenderer(_renderContext, material, "Sprite");
        _collisionTask = new PerformCollisionTask();
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
            entity.Transform.Rotation = new Rotation2D(_random.NextFloat(0,360));
            _activeList.Add(entity);
        }
    }

    public void PerformCollision(CollisionWorld2D collisionWorld)
    {
        if (_activeList.Count == 0) return;

        _collisionTask.activeList = _activeList;
        _collisionTask.collisionWorld = collisionWorld;
        _collisionTask.RunParallel(_activeList.Count, 16);
    }

    private class PerformCollisionTask : ReuseableBatchTask
    {
        public List<Cube>? activeList;
        public CollisionWorld2D? collisionWorld;

        protected override void ExecuteCore(int index)
        {
            Cube entity = activeList![index];
            var collector = new CubeCollisionCollector(entity);
            collisionWorld!.CastBox(ref collector, entity.Shape);
        }
    }

    private struct CubeCollisionCollector : ICollisionCastCollector
    {
        private readonly Cube _cube;
        public CubeCollisionCollector(Cube cube) => _cube = cube;
        public bool OnHit(object target)
        {
            _cube.OnHit(target);
            return true;
        }
    }

    public void OnUpdate(GPUFrameBuffer frame, float delta)
    {
        _renderContext.Begin(frame);
        for (int i = 0; i < _activeList.Count; i++)
        {
            Cube entity = _activeList[i];
            _spriteRenderer.Draw(_texture, entity.Transform, entity.Color);
        }
        _renderContext.End();

    }

    public void Dispose()
    {
        _collisionTask.Dispose();
        _renderContext.Dispose();
    }
}