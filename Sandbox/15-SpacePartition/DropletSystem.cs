

using System.Buffers;
using System.Numerics;
using Vocore;
using Vocore.Graphics;
using Vocore.GUI;
using Vocore.Rendering;

using Random = Vocore.Random;

public class DropletSystem : IDisposable
{
    public const int BufferLength = 10000;
    private class JobCalcMatrix : IJobBatch
    {
        private readonly Transform2D[] _transformBuffer;
        private readonly Matrix4x4[] _matrixBuffer;

        public JobCalcMatrix(Transform2D[] transformBuffer, Matrix4x4[] matrixBuffer)
        {
            _transformBuffer = transformBuffer;
            _matrixBuffer = matrixBuffer;
        }

        public void Execute(int index)
        {
            _matrixBuffer[index] = _transformBuffer[index].Matrix;
        }
    }

    private static readonly ColorFloat DefaultColor = 0xCCCCCC;
    private readonly SpriteRenderer _renderer;
    private readonly Texture2D _texture;
    private readonly List<Droplet> _activeList = new List<Droplet>();
    private readonly Stack<Droplet> _despawnList = new Stack<Droplet>();
    private readonly Pool<Droplet> _pool = new Pool<Droplet>(10000, () => new Droplet());
    private readonly ParallelScheduler _scheduler = new ParallelScheduler(24);
    private readonly Transform2D[] _transformBuffer = new Transform2D[BufferLength];
    private readonly Matrix4x4[] _matrixBuffer = new Matrix4x4[BufferLength];
    private readonly JobCalcMatrix _jobCalcMatrix;
    private int _spawnRate = 100;
    private int _spawnHeight = 280;
    private int _despawnHeight = -280;
    private int _spwanRangeX = 480;
    private int _speed = 300;

    private Random _random = new Random(123);

    public DropletSystem(SpriteRenderer renderer, Texture2D texDroplet)
    {
        _renderer = renderer;
        _texture = texDroplet;
        _jobCalcMatrix = new JobCalcMatrix(_transformBuffer, _matrixBuffer);
    }

    public void OnTick(float delta)
    {
        

        for (int i = 0; i < _spawnRate; i++){
            Spawn();
        }

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
        int jobExecCount = _activeList.Count / BufferLength;
        int remain = _activeList.Count % BufferLength;

        _renderer.Begin(frame);
        for (int i = 0; i < _activeList.Count; i++)
        {
            var droplet = _activeList[i];
            _renderer.Draw(_texture, droplet.transform.Matrix, droplet.color);
        }
        // for (int i = 0; i < jobExecCount; i++)
        // {
        //     //copy transform to buffer
        //     for (int j = 0; j < BufferLength; j++)
        //     {
        //         _transformBuffer[j] = _activeList[i * BufferLength + j].transform;
        //     }

        //     _scheduler.Run(_jobCalcMatrix, BufferLength);

        //     for (int j = 0; j < BufferLength; j++)
        //     {
        //         _renderer.Draw(_texture, _matrixBuffer[j], DefaultColor);
        //     }
        // }

        // if (remain > 0)
        // {
        //     for (int i = 0; i < remain; i++)
        //     {
        //         _transformBuffer[i] = _activeList[jobExecCount * BufferLength + i].transform;
        //     }

        //     _scheduler.Run(_jobCalcMatrix, remain);

        //     for (int i = 0; i < remain; i++)
        //     {
        //         _renderer.Draw(_texture, _matrixBuffer[i], DefaultColor);
        //     }
        // }

        _renderer.End();

        DebugGUI.Text("Active: 0", _activeList.Count);

        DebugGUI.Slider(0, 400, ref _spawnRate);
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

    public void Dispose()
    {
        _scheduler.Dispose();
    }
}