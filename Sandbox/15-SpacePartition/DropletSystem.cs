

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
    private readonly Pool<Droplet> _pool = new Pool<Droplet>(10000, () => new Droplet());
    private float _spawnHeight = 100f;
    private float _spwanRangeX = 100f;
    private float _speed = 10f;

    private Random _random = new Random(123);

    public DropletSystem(SpriteRenderer renderer, Texture2D texDroplet)
    {
        _renderer = renderer;
        _texDroplet = texDroplet;
    }

    public void OnTick(float delta)
    {
        for (int i = 0; i < _activeList.Count; i++)
        {
            var droplet = _activeList[i];
            droplet.transform.position.Y -= _speed * delta;
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

        DebugGUI.Slider(0, 640, ref _spawnHeight);
        DebugGUI.SameLine();
        DebugGUI.Text("Spawn Height");

        DebugGUI.Slider(0, 640, ref _spwanRangeX);
        DebugGUI.SameLine();
        DebugGUI.Text("Spawn Range X");

        DebugGUI.Slider(0, 100, ref _speed);
        DebugGUI.SameLine();
        DebugGUI.Text("Speed");
    }
}