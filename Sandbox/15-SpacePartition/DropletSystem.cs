

using System.Buffers;
using System.Numerics;
using Vocore;
using Vocore.Graphics;
using Vocore.GUI;
using Vocore.Rendering;

using Random = Vocore.Random;

public class DropletSystem
{
    private static readonly ColorFloat DefaultColor = 0xffffff;
    private readonly SpriteRenderer _renderer;
    private readonly Texture2D _texDroplet;
    private readonly List<Droplet> _activeList = new List<Droplet>();
    private readonly Stack<Droplet> _despawnList = new Stack<Droplet>();
    private readonly Pool<Droplet> _pool = new Pool<Droplet>(10000, () => new Droplet());
    private int _spawnRate = 20;
    private int _spawnHeight = 280;
    private int _despawnHeight = -280;
    private int _spwanRangeX = 480;
    private int _speed = 300;
    

    private Random _random = new Random(123);

    public DropletSystem(SpriteRenderer renderer, Texture2D texDroplet)
    {
        _renderer = renderer;
        _texDroplet = texDroplet;
    }

    public void OnTick(float delta)
    {
        for (int i = 0; i < _spawnRate; i++){
            SpawnDroplet();
        }
        
        for (int i = 0; i < _activeList.Count; i++)
        {
            var droplet = _activeList[i];
            droplet.transform.position.Y -= _speed * delta;


            if (droplet.pendingDestroy || droplet.transform.position.Y < _despawnHeight)
            {
                _despawnList.Push(droplet);
            }
        }

        while (_despawnList.Count > 0)
        {
            var droplet = _despawnList.Pop();
            _activeList.Remove(droplet);
            _pool.TryReturn(droplet);
        }
    }

    public void SpawnDroplet()
    {
        if (_pool.TryGet(out var droplet))
        {
            droplet.transform.position = new Vector3(_random.NextFloat(-_spwanRangeX, _spwanRangeX), _spawnHeight, 0);
            droplet.color = DefaultColor;
            _activeList.Add(droplet);
        }
    }

    public void OnUpdate(GPUFrameBuffer frame, float delta)
    {
        _renderer.Begin(frame);
        for (int i = 0; i < _activeList.Count; i++)
        {
            var droplet = _activeList[i];
            _renderer.Draw(_texDroplet, droplet.transform, droplet.color);
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