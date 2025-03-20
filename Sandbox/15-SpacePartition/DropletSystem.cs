

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
        private readonly RenderContext[] _renderContext;
        private readonly SpriteRenderer[] _renderers;
        private readonly RenderRange[] _renderRanges;
        private readonly UnorderedList<Droplet> _activeList;
        private readonly Texture2D _texture;
        private readonly GPUFrameBuffer _renderTarget;


        public JobParallelRender(GPUFrameBuffer renderTarget, RenderContext[] renderContext, SpriteRenderer[] renderers, RenderRange[] renderRanges, UnorderedList<Droplet> activeList, Texture2D texture)
        {
            _renderTarget = renderTarget;
            _renderContext = renderContext;
            _renderers = renderers;
            _renderRanges = renderRanges;
            _activeList = activeList;
            _texture = texture;
        }

        public void Execute(int index)
        {
            _renderContext[index].Begin(_renderTarget);
            for (int j = _renderRanges[index].start; j < _renderRanges[index].end; j++)
            {
                var droplet = _activeList[j];
                _renderers[index].Draw(_texture, droplet.transform.Matrix, droplet.color);
            }
            _renderContext[index].End();
        }
    }

    private static readonly ColorFloat DefaultColor = 0xCCCCCC;
    private readonly RenderContext[] _renderContext;
    private readonly SpriteRenderer[] _renderers;
    private readonly RenderRange[] _renderRanges;
    private readonly Texture2D _texture;
    private readonly UnorderedList<Droplet> _activeList = new UnorderedList<Droplet>();
    private readonly Pool<Droplet> _pool = new Pool<Droplet>(200000, () => new Droplet());
    private readonly ParallelScheduler _scheduler = new ParallelScheduler(24);
    private readonly WindowRenderTarget _renderTarget;
    private readonly JobParallelRender _jobParallelRender;
    private int _spawnRate = 100;
    private int _spawnHeight = 280;
    private int _despawnHeight = -280;
    private int _spwanRangeX = 480;
    private int _speed = 300;

    private Random _random = new Random(123);

    private readonly Profiler _profiler = new Profiler();

    public DropletSystem(WindowRenderTarget windowRenderTarget, RenderingSystem system, GraphicsBuffer camera, Shader shader, Texture2D texDroplet)
    {
        _renderContext = new RenderContext[RenderThreadCount];
        _renderers = new SpriteRenderer[RenderThreadCount];
        Material material = system.CreateGraphicsMaterial(shader, "Sprite");
        material.BlendState = BlendState.AlphaBlend;
        material.SetBuffer(ShaderResourceId.Camera, camera);
        for (int i = 0; i < RenderThreadCount; i++)
        {
            _renderContext[i] = system.CreateRenderContext();
            //a material instance per thread
            _renderers[i] = system.CreateSpriteRenderer(_renderContext[i], material.CreateInstance(), "Sprite");
        }

        _renderRanges = new RenderRange[RenderThreadCount];
        _texture = texDroplet;
        _renderTarget = windowRenderTarget;

        _jobParallelRender = new JobParallelRender(_renderTarget.RenderTexture.FrameBuffer, _renderContext, _renderers, _renderRanges, _activeList, _texture);
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
            entity.transform.Position.Y -= _speed * delta;

            if (entity.pendingDestroy || entity.transform.Position.Y < _despawnHeight)
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
            entity.transform.Position = new Vector2(_random.NextFloat(-_spwanRangeX, _spwanRangeX), _spawnHeight+ _random.NextFloat(-4,4));
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
        for (int i = 0; i < RenderThreadCount; i++)
        {
            _renderRanges[i].start = i * _activeList.Count / RenderThreadCount;
            _renderRanges[i].end = (i + 1) * _activeList.Count / RenderThreadCount;
            //if last
            if (i == RenderThreadCount - 1)
            {
                _renderRanges[i].end = _activeList.Count;
            }
        }

        _scheduler.Run(_jobParallelRender, RenderThreadCount);


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