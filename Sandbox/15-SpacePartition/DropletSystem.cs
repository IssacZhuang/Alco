

using System.Buffers;
using System.Numerics;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.GUI;
using Alco.Rendering;

using Random = Alco.Random;

public class DropletSystem : IDisposable
{
    public const int RenderThreadCount = 16;
    public const int BufferLength = 10000;

    private struct RenderRange
    {
        public int start;
        public int end;
    }

    private class JobParallelRender : IJobBatch
    {
        private readonly SpriteRenderer[] _renderers;
        private readonly RenderRange[] _renderRanges;
        private readonly UnorderedList<Droplet> _activeList;
        private readonly Texture2D _texture;
        private readonly GPUFrameBuffer _renderTarget;
        

        public JobParallelRender(GPUFrameBuffer renderTarget, SpriteRenderer[] renderers, RenderRange[] renderRanges, UnorderedList<Droplet> activeList, Texture2D texture)
        {
            _renderTarget = renderTarget;
            _renderers = renderers;
            _renderRanges = renderRanges;
            _activeList = activeList;
            _texture = texture;
        }

        public void Execute(int index)
        {
            _renderers[index].Begin(_renderTarget);
            for (int j = _renderRanges[index].start; j < _renderRanges[index].end; j++)
            {
                var droplet = _activeList[j];
                _renderers[index].Draw(_texture, droplet.transform.Matrix, droplet.color);
            }
            _renderers[index].End();
        }
    }

    private static readonly ColorFloat DefaultColor = 0xCCCCCC;
    private readonly SpriteRenderer _renderer;
    private readonly RenderRange[] _renderRanges;
    private readonly Texture2D _texture;
    private readonly UnorderedList<Droplet> _activeList = new UnorderedList<Droplet>();
    private readonly Pool<Droplet> _pool = new Pool<Droplet>(200000, () => new Droplet());
    private readonly ParallelScheduler _scheduler = new ParallelScheduler(24);
    private readonly WindowRenderTarget _renderTarget;
    //private readonly JobParallelRender _jobParallelRender;
    private int _spawnRate = 100;
    private int _spawnHeight = 280;
    private int _despawnHeight = -280;
    private int _spwanRangeX = 480;
    private int _speed = 300;

    private Random _random = new Random(123);

    private readonly Profiler _profiler = new Profiler();

    public DropletSystem(WindowRenderTarget windowRenderTarget, RenderingSystem system, GraphicsBuffer camera, Shader shader, Texture2D texDroplet)
    {
        // _renderer = new SpriteRenderer[RenderThreadCount];
        // for (int i = 0; i < RenderThreadCount; i++)
        // {
        //     _renderer[i] = system.CreateSpriteRenderer(camera, shader);
        // }
        _renderer = system.CreateSpriteRenderer(camera, shader);
        _renderRanges = new RenderRange[RenderThreadCount];
        _texture = texDroplet;
        _renderTarget = windowRenderTarget;

        //_jobParallelRender = new JobParallelRender(_renderTarget.RenderTexture.FrameBuffer, _renderer, _renderRanges, _activeList, _texture);
    }

    public void OnTick(float delta)
    {
        

        for (int i = 0; i < _spawnRate; i++){
            Spawn();
        }


        int length = _activeList.Count;
        for (int i = 0; i < _activeList.Count; i++)
        {
            Droplet entity = _activeList[i];
            entity.transform.position.Y -= _speed * delta;

            if (entity.pendingDestroy || entity.transform.position.Y < _despawnHeight)
            {
                _pool.TryReturn(entity);
                _activeList[i] = _activeList.RemoveLast();
                length--;
            }
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

    public void OnUpdate(float delta)
    {
        // int jobExecCount = _activeList.Count / BufferLength;
        // int remain = _activeList.Count % BufferLength;

        // _renderer.Begin(frame);
        // for (int i = 0; i < _activeList.Count; i++)
        // {
        //     var droplet = _activeList[i];
        //     _renderer.Draw(_texture, droplet.transform.Matrix, droplet.color);
        // }
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

        //_renderer.End();

        // for (int i = 0; i < RenderThreadCount; i++)
        // {
        //     _renderRanges[i].start = i * _activeList.Count / RenderThreadCount;
        //     _renderRanges[i].end = (i + 1) * _activeList.Count / RenderThreadCount;
        //     //if last
        //     if (i == RenderThreadCount - 1)
        //     {
        //         _renderRanges[i].end = _activeList.Count;
        //     }
        // }

        //_scheduler.Run(_jobParallelRender, RenderThreadCount);

        _profiler.Start("all");
        _profiler.Start("draw");
        _renderer.Begin(_renderTarget.FrameBuffer);
        for (int i = 0; i < _activeList.Count; i++)
        {
            var droplet = _activeList[i];
            _renderer.Draw(_texture, droplet.transform.Matrix, droplet.color);
        }
        DebugGUI.Text(_profiler.End().time);
        _renderer.End();
        DebugGUI.Text(_profiler.End().time);

        DebugGUI.Text("Active: 0", _activeList.Count);

        DebugGUI.Slider(ref _spawnRate, 0, 1000);
        DebugGUI.SameLine();
        DebugGUI.Text("Spawn Rate");

        DebugGUI.Slider(ref _spawnHeight, 0, 640);
        DebugGUI.SameLine();
        DebugGUI.Text("Spawn Height");

        DebugGUI.Slider(ref _despawnHeight, -640, 0);
        DebugGUI.SameLine();
        DebugGUI.Text("Despawn Height");

        DebugGUI.Slider(ref _spwanRangeX, 0, 640);
        DebugGUI.SameLine();
        DebugGUI.Text("Spawn Range X");

        DebugGUI.Slider(ref _speed, 0, 600);
        DebugGUI.SameLine();
        DebugGUI.Text("Speed");
    }

    public void Render(int i)
    {
        // _renderer[i].Begin(_renderTarget.RenderTexture.FrameBuffer);
        // for (int j = _renderRanges[i].start; j < _renderRanges[i].end; j++)
        // {
        //     var droplet = _activeList[j];
        //     _renderer[i].Draw(_texture, droplet.transform.Matrix, droplet.color);
        // }
        // _renderer[i].End();
    }

    public void Dispose()
    {
        _scheduler.Dispose();
    }
}