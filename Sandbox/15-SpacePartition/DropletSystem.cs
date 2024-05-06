

using System.Buffers;
using System.Numerics;
using Vocore;
using Vocore.Graphics;
using Vocore.GUI;
using Vocore.Rendering;

using Random = Vocore.Random;

public class DropletSystem
{
    private class JobMoveDroplet : IJobBatch
    {
        public List<Droplet> activeList = null!;
        public int speed;
        public float delta;
        public float despawnHeight;
        public void Execute(int i)
        {
            Droplet entity = activeList[i];
            entity.transform.position.Y -= speed * delta;
            if (entity.transform.position.Y < despawnHeight)
            {
                entity.pendingDestroy = true;
            }
        }
    }
    private static readonly ColorFloat DefaultColor = 0xCCCCCC;
    private readonly SpriteRenderer _renderer;
    private readonly Texture2D _texture;
    private readonly List<Droplet> _activeList = new List<Droplet>();
    private readonly Stack<Droplet> _despawnList = new Stack<Droplet>();
    private readonly Pool<Droplet> _pool = new Pool<Droplet>(10000, () => new Droplet());
    private readonly ParallelScheduler _scheduler = new ParallelScheduler();
    private readonly JobMoveDroplet _jobMoveDroplet;
    private int _spawnRate = 20;
    private int _spawnHeight = 280;
    private int _despawnHeight = -280;
    private int _spwanRangeX = 480;
    private int _speed = 300;
    

    private Random _random = new Random(123);

    public DropletSystem(SpriteRenderer renderer, Texture2D texDroplet)
    {
        _renderer = renderer;
        _texture = texDroplet;

        _jobMoveDroplet = new JobMoveDroplet(){
            activeList = _activeList,
            speed = _speed,
            delta = 0,
            despawnHeight = _despawnHeight
        };
    }

    public void OnTick(float delta)
    {
        

        for (int i = 0; i < _spawnRate; i++){
            Spawn();
        }

        // _jobMoveDroplet.delta = delta;
        // _jobMoveDroplet.speed = _speed;
        //_scheduler.Run(_jobMoveDroplet, _activeList.Count);

        //use single thread

        for (int i = 0; i < _activeList.Count; i++)
        {
            Droplet entity = _activeList[i];
            entity.transform.position.Y -= _speed * delta;

            if (entity.pendingDestroy || entity.transform.position.Y < _despawnHeight)
            {
                _despawnList.Push(entity);
            }
        }

        while (_despawnList.Count > 0)
        {
            Droplet entity = _despawnList.Pop();
            _activeList.Remove(entity);
            _pool.TryReturn(entity);
        }
    }

    public void Spawn()
    {
        if (_pool.TryGet(out Droplet? entity))
        {
            entity.transform.position = new Vector2(_random.NextFloat(-_spwanRangeX, _spwanRangeX), _spawnHeight+ _random.NextFloat(-4,4));
            entity.color = DefaultColor;
            entity.pendingDestroy = false;
            _activeList.Add(entity);
        }
    }

    public void PushCollisionTarget(CollisionWorld2D collisionWorld)
    {
        for (int i = 0; i < _activeList.Count; i++)
        {
            Droplet entity = _activeList[i];
            collisionWorld.PushTarget(entity, entity.Shape);
        }
    }

    public void OnUpdate(GPUFrameBuffer frame, float delta)
    {
        _renderer.Begin(frame);
        for (int i = 0; i < _activeList.Count; i++)
        {
            var droplet = _activeList[i];
            _renderer.Draw(_texture, droplet.transform, droplet.color);
        }
        _renderer.End();

        DebugGUI.Text("Active: 0", _activeList.Count);

        DebugGUI.Slider(0, 100, ref _spawnRate);
        DebugGUI.SameLine();
        DebugGUI.Text("Spawn Rate");

        DebugGUI.Slider(0, 640, ref _spawnHeight);
        DebugGUI.SameLine();
        DebugGUI.Text("Spawn Height");

        DebugGUI.Slider(-640, 0, ref _despawnHeight);
        DebugGUI.SameLine();
        DebugGUI.Text("Despawn Height");

        DebugGUI.Slider(0, 640, ref _spwanRangeX);
        DebugGUI.SameLine();
        DebugGUI.Text("Spawn Range X");

        DebugGUI.Slider(0, 600, ref _speed);
        DebugGUI.SameLine();
        DebugGUI.Text("Speed");
    }
}