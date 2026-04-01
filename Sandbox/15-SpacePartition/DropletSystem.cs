using System.Buffers;
using System.Numerics;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.GUI;
using Alco.Rendering;
using Alco.ImGUI;

using FastRandom = Alco.FastRandom;

public class DropletSystem : IDisposable
{
    public static readonly int RenderThreadCount = 24;
    public const int BufferLength = 10000;

    private struct RenderRange
    {
        public int start;
        public int end;
    }

    private class JobParallelRender : ReuseableBatchTask
    {
        private readonly SubRenderContext[] _renderContext;
        private readonly InstanceRenderer<SpriteConstant>[] _renderers;
        private readonly RenderRange[] _renderRanges;
        private readonly UnorderedList<Droplet> _activeList;
        private readonly ViewRenderTarget _renderTarget;
        private readonly Mesh _mesh;


        public JobParallelRender(ViewRenderTarget renderTarget, SubRenderContext[] renderContext, InstanceRenderer<SpriteConstant>[] renderers, RenderRange[] renderRanges, UnorderedList<Droplet> activeList, Mesh mesh)
        {
            _renderTarget = renderTarget;
            _renderContext = renderContext;
            _renderers = renderers;
            _renderRanges = renderRanges;
            _activeList = activeList;
            _mesh = mesh;
        }

        protected override void ExecuteCore(int index)
        {
            _renderContext[index].Begin(_renderTarget.FrameBuffer.AttachmentLayout);
            RenderRange range = _renderRanges[index];
            int i = 0;
            Span<SpriteConstant> instances = stackalloc SpriteConstant[range.end - range.start];
            for (int j = range.start; j < range.end; j++)
            {
                var droplet = _activeList[j];
                instances[i++] = new SpriteConstant
                {
                    Model = droplet.transform.Matrix,
                    Color = ColorFloat.White,
                    UvRect = Rect.One
                };
            }

            _renderers[index].Draw(_mesh, instances);
            _renderContext[index].End();
        }
    }

    private static readonly ColorFloat DefaultColor = 0xCCCCCC;
    private readonly RenderContext _renderContext;
    private readonly SubRenderContext[] _subRenderContexts;
    private readonly InstanceRenderer<SpriteConstant>[] _renderers;
    private readonly RenderRange[] _renderRanges;
    private readonly UnorderedList<Droplet> _activeList = new UnorderedList<Droplet>();
    private readonly Pool<Droplet> _pool = new Pool<Droplet>(200000, () => new Droplet());
    private readonly ViewRenderTarget _renderTarget;
    private readonly JobParallelRender _jobParallelRender;
    private int _spawnRate = 100;
    private int _spawnHeight = 280;
    private int _despawnHeight = -280;
    private int _spwanRangeX = 480;
    private int _speed = 300;

    private FastRandom _random = new FastRandom(123);

    public DropletSystem(ViewRenderTarget windowRenderTarget, RenderingSystem system, GraphicsBuffer camera, Shader shader, Texture2D texDroplet)
    {
        _renderContext = system.CreateRenderContext();
        _subRenderContexts = new SubRenderContext[RenderThreadCount];
        _renderers = new InstanceRenderer<SpriteConstant>[RenderThreadCount];
        Material material = system.CreateMaterial(shader, "Sprite");
        material.SetTexture(ShaderResourceId.Texture, texDroplet);
        material.BlendState = BlendState.AlphaBlend;
        material.SetBuffer(ShaderResourceId.Camera, camera);
        for (int i = 0; i < RenderThreadCount; i++)
        {
            _subRenderContexts[i] = system.CreateSubRenderContext();
            //a material instance per thread
            _renderers[i] = system.CreateInstanceRenderer<SpriteConstant>(_subRenderContexts[i], material.CreateInstance());
        }

        _renderRanges = new RenderRange[RenderThreadCount];
        _renderTarget = windowRenderTarget;

        _jobParallelRender = new JobParallelRender(_renderTarget, _subRenderContexts, _renderers, _renderRanges, _activeList, system.MeshCenteredSprite);
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
            collisionWorld.PushCollisionTarget(entity, entity.Shape);
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

        _jobParallelRender.RunParallel(RenderThreadCount);
        _renderContext.Begin(_renderTarget.FrameBuffer);
        for (int i = 0; i < RenderThreadCount; i++)
        {
            _renderContext.ExecuteSubContext(_subRenderContexts[i]);
        }
        _renderContext.End();

        // ImGUI Controls
        ImGui.Begin("Droplet System Controls");

        // Display active count
        FixedString32 activeText = new FixedString32();
        activeText.Append("Active: ");
        activeText.Append(_activeList.Count);
        ImGui.Text(activeText);

        ImGui.SliderInt("Spawn Rate", ref _spawnRate, 0, 1000);
        ImGui.SliderInt("Spawn Height", ref _spawnHeight, 0, 640);
        ImGui.SliderInt("Despawn Height", ref _despawnHeight, -640, 0);
        ImGui.SliderInt("Spawn Range X", ref _spwanRangeX, 0, 640);
        ImGui.SliderInt("Speed", ref _speed, 0, 600);

        ImGui.End();
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
        _jobParallelRender.Dispose();
        foreach (var renderer in _renderers)
        {
            renderer.Dispose();
        }
        _renderContext.Dispose();
    }
}