using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Effects;

/// <summary>
/// Simulates and renders any number of <see cref="ParticleEffect2D"/> instances entirely on the GPU,
/// using bindless sprite/over-life textures and vertex pulling over <see cref="Alco.Graphics"/>.
/// Creation, release, simulation, and rendering are serialized on one reentrant gate.
/// </summary>
public sealed class GpuParticleSystem2D : AutoDisposable
{
    private readonly record struct CachedMaterial(Material Material, int PassRootOffset);
    private readonly record struct DrawBatch(Material Material, int PassRootOffset, DepthStencilState DepthStencil, uint InstanceBase);
    private readonly record struct WorkDispatch(Shader Shader, uint FirstBlock, uint BlockCount);

    private readonly struct GroupTextures(Texture sprite, Texture colorGradient, Texture sizeCurve)
    {
        public readonly Texture Sprite = sprite;
        public readonly Texture ColorGradient = colorGradient;
        public readonly Texture SizeCurve = sizeCurve;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ResetArgsRoot
    {
        public GPUBufferView DrawArgs;
        public uint DrawCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InitRoot2D
    {
        public GPUBufferView Particles;
        public uint SliceOffset;
        public uint Count;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EmitRoot2D
    {
        public GPUBufferView Particles;
        public GPUBufferView Emitters;
        public GPUBufferView WorkBlocks;
        public uint WorkBlockBase;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SimulateRoot2D
    {
        public GPUBufferView Particles;
        public GPUBufferView Emitters;
        public GPUBufferView DrawArgs;
        public GPUBufferView InstanceData;
        public GPUBufferView WorkBlocks;
        public uint WorkBlockBase;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ParticlePassData2D
    {
        public Matrix4x4 ViewProjection;
        public GPUBufferView Particles;
        public GPUBufferView Emitters;
        public GPUBufferView InstanceData;
    }

    private sealed class MaterialKeyComparer : IEqualityComparer<(MaterialAsset Asset, BlendState Blend)>
    {
        public bool Equals((MaterialAsset Asset, BlendState Blend) x, (MaterialAsset Asset, BlendState Blend) y)
            => ReferenceEquals(x.Asset, y.Asset) && x.Blend == y.Blend;

        public int GetHashCode((MaterialAsset Asset, BlendState Blend) key)
            => HashCode.Combine(key.Asset, key.Blend);
    }

    private sealed class BatchKeyComparer : IEqualityComparer<(Material Material, DepthStencilState Depth)>
    {
        public bool Equals((Material Material, DepthStencilState Depth) x, (Material Material, DepthStencilState Depth) y)
            => ReferenceEquals(x.Material, y.Material) && x.Depth == y.Depth;

        public int GetHashCode((Material Material, DepthStencilState Depth) key)
            => HashCode.Combine(key.Material, key.Depth);
    }

    private readonly Lock _gate = new();
    private readonly RenderingSystem _rendering;
    private readonly ShaderSystem _shaders;
    private readonly string _renderTemplateModule;
    private readonly ParticleBufferPool<GpuParticle2D, EmitterParams2D> _pool;
    private readonly Shader _resetArgsShader;
    private readonly Shader _initShader;
    private readonly TransientBufferPool _emitWorkPool;
    private readonly TransientBufferPool _simulateWorkPool;

    private readonly Dictionary<string, (Shader Emit, Shader Simulate)> _behaviorShaders = new(StringComparer.Ordinal);
    private readonly Dictionary<(MaterialAsset Asset, BlendState Blend), CachedMaterial> _materials = new(new MaterialKeyComparer());
    private readonly Dictionary<ParticleGroup2D, GroupTextures> _groupTextures = [];
    private readonly List<Texture> _ownedTextures = [];
    private readonly List<ParticleEffectInstance2D> _instances = [];

    private readonly Dictionary<(Material Material, DepthStencilState Depth), List<ParticleEffectInstance2D.GroupState>> _drawBuckets =
        new(new BatchKeyComparer());
    private readonly List<(Material Material, DepthStencilState Depth)> _drawBucketKeys = [];
    private readonly List<ParticleEffectInstance2D.GroupState> _drawGroups = [];
    private readonly List<uint> _drawIndices = [];
    private readonly List<uint> _drawStarts = [];
    private readonly List<DrawBatch> _drawBatches = [];

    private readonly Dictionary<Shader, List<int>> _workBuckets = [];
    private readonly List<Shader> _workShaders = [];
    private readonly List<GpuParticleWorkBlock> _emitBlocks = [];
    private readonly List<GpuParticleWorkBlock> _simulateBlocks = [];
    private readonly List<WorkDispatch> _emitDispatches = [];
    private readonly List<WorkDispatch> _simulateDispatches = [];
    private readonly List<(uint Offset, uint Count)> _kills = [];

    private readonly MaterialAsset _defaultAsset = new() { Name = "particles2d-default" };
    private readonly ParticleMaterialModules _materialModules;

    private float _simulationAccumulator;
    private bool _hasValidPlan;

    /// <summary>Creates a 2D particle system using <paramref name="rendering"/>'s default <see cref="RenderingSystem.Shaders"/>.</summary>
    public GpuParticleSystem2D(
        RenderingSystem rendering,
        int particleCapacity = 65536,
        int emitterSlots = 256,
        string? renderTemplate = null)
        : this(rendering, (rendering ?? throw new ArgumentNullException(nameof(rendering))).Shaders, particleCapacity, emitterSlots, renderTemplate)
    {
    }

    /// <summary>Creates a 2D particle system using an explicit <see cref="ShaderSystem"/> for custom surfaces and behaviors.</summary>
    public GpuParticleSystem2D(
        RenderingSystem rendering,
        ShaderSystem shaders,
        int particleCapacity = 65536,
        int emitterSlots = 256,
        string? renderTemplate = null)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentNullException.ThrowIfNull(shaders);

        _rendering = rendering;
        _shaders = shaders;
        _renderTemplateModule = string.IsNullOrWhiteSpace(renderTemplate)
            ? ParticleAssetPipeline.RenderModule2D
            : renderTemplate.Trim();

        AlcoEffectsShaders.EnsureBuiltInModulesLoaded(shaders);

        _pool = new ParticleBufferPool<GpuParticle2D, EmitterParams2D>(rendering, particleCapacity, emitterSlots, "particles2d", _gate);
        _pool.Reallocated += OnPoolReallocated;
        _materialModules = new ParticleMaterialModules(_gate);
        _emitWorkPool = new TransientBufferPool(rendering, "particles2d_emit_work");
        _simulateWorkPool = new TransientBufferPool(rendering, "particles2d_simulate_work");

        _resetArgsShader = shaders.GetShader(ParticleAssetPipeline.ResetArgsModule, ShaderKind.Compute);
        _initShader = shaders.GetShader(ParticleAssetPipeline.InitModule2D, ShaderKind.Compute);
    }

    /// <summary>Default 60 Hz fixed simulation interval.</summary>
    public const float DefaultSimulationInterval = 1f / 60f;

    /// <summary>Whether the fixed-interval simulation rate limiter is enabled.</summary>
    public bool SimulationRateLimitEnabled { get; set; } = true;

    /// <summary>The simulation step interval when <see cref="SimulationRateLimitEnabled"/> is enabled.</summary>
    public float SimulationInterval { get; set; } = DefaultSimulationInterval;

    /// <summary>Snapshot of currently live effect instances.</summary>
    public IReadOnlyList<ParticleEffectInstance2D> Instances
    {
        get
        {
            lock (_gate)
            {
                return _instances.ToArray();
            }
        }
    }

    internal ParticleBufferPool<GpuParticle2D, EmitterParams2D> Pool => _pool;
    internal int PlannedDrawGroupCount => _drawGroups.Count;
    internal int PlannedDrawBatchCount => _drawBatches.Count;
    internal int PlannedEmitDispatchCount => _emitDispatches.Count;
    internal int PlannedSimulateDispatchCount => _simulateDispatches.Count;
    internal int PlannedEmitBlockCount => _emitBlocks.Count;
    internal int PlannedSimulateBlockCount => _simulateBlocks.Count;

    /// <summary>Current particle slot capacity of the shared pool.</summary>
    public int PoolParticleCapacity => _pool.ParticleCapacity;

    /// <summary>Current emitter slot capacity of the shared pool.</summary>
    public int PoolEmitterSlotCapacity => _pool.SlotCapacity;

    /// <summary>Registers a shared <see cref="IParticleMaterialModule"/> applied to all cached and future materials.</summary>
    public IDisposable AddMaterialModule(IParticleMaterialModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        lock (_gate)
        {
            IDisposable registration = _materialModules.Add(module);
            foreach (CachedMaterial entry in _materials.Values)
                module.ConfigureMaterial(entry.Material);
            return registration;
        }
    }

    /// <summary>Re-applies all registered material modules onto cached materials.</summary>
    public void RefreshMaterialModules()
    {
        lock (_gate)
        {
            foreach (CachedMaterial entry in _materials.Values)
                _materialModules.Apply(entry.Material);
        }
    }

    /// <summary>Creates an effect instance at ground height 0 that starts playing immediately.</summary>
    public ParticleEffectInstance2D CreateInstance(ParticleEffect2D effect, in Transform2D transform, int seed = 0)
        => CreateInstance(effect, transform, 0f, seed);

    /// <summary>Creates an effect instance with ground-plane transform and 2.5D emitter height.</summary>
    public ParticleEffectInstance2D CreateInstance(ParticleEffect2D effect, in Transform2D transform, float height, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (effect.Groups.Count == 0)
            throw new InvalidDataException($"Particle effect '{effect.Name}' has no groups.");

        var groups = new ParticleEffectInstance2D.GroupState[effect.Groups.Count];
        uint pendingSlot = 0;
        bool hasPendingSlot = false;
        ParticleSlice pendingSlice = default;
        uint uploadMin = uint.MaxValue;
        uint uploadMax = 0;
        int built = 0;

        try
        {
            for (int i = 0; i < groups.Length; i++)
            {
                ParticleGroup2D groupAsset = effect.Groups[i];
                CachedMaterial cachedMat = GetOrCreateMaterial(groupAsset);
                GroupTextures texs = GetOrCreateGroupTextures(groupAsset);
                (Shader emitShader, Shader simulateShader) = GetBehaviorShaders(groupAsset.Behavior);

                pendingSlot = _pool.AllocateSlot();
                hasPendingSlot = true;
                pendingSlice = _pool.AllocateSlice(Math.Max(groupAsset.MaxParticles, 1));

                EmitterParams2D parameters = EmitterParams2D.FromAsset(groupAsset);
                parameters.Capacity = pendingSlice.Capacity;
                parameters.SliceOffset = pendingSlice.Offset;
                parameters.SpriteTextureId = texs.Sprite.SampledIndex;
                parameters.ColorGradientTextureId = texs.ColorGradient.SampledIndex;
                parameters.SizeCurveTextureId = texs.SizeCurve.SampledIndex;
                _pool.SetParams(pendingSlot, parameters);

                uploadMin = Math.Min(uploadMin, pendingSlot);
                uploadMax = Math.Max(uploadMax, pendingSlot);

                groups[i] = new ParticleEffectInstance2D.GroupState
                {
                    Asset = groupAsset,
                    Slot = pendingSlot,
                    Slice = pendingSlice,
                    Material = cachedMat.Material,
                    PassRootOffset = cachedMat.PassRootOffset,
                    DepthStencil = groupAsset.Depth ?? DepthStencilState.None,
                    EmitShader = emitShader,
                    SimulateShader = simulateShader,
                    EmissionRate = groupAsset.EmissionRate,
                    Lifetime = groupAsset.Lifetime,
                    Active = true,
                    Visible = true,
                };
                built++;
                hasPendingSlot = false;
                pendingSlice = default;
            }
        }
        catch
        {
            _pool.FreeSlice(pendingSlice);
            if (hasPendingSlot)
                _pool.FreeSlot(pendingSlot);
            for (int i = 0; i < built; i++)
            {
                _pool.FreeSlice(groups[i].Slice);
                _pool.FreeSlot(groups[i].Slot);
            }
            throw;
        }

        if (uploadMin <= uploadMax)
            _pool.UpdateParams(uploadMin, uploadMax - uploadMin + 1);

        var instance = new ParticleEffectInstance2D(this, effect, transform, height, seed, groups);
        lock (_gate)
        {
            _instances.Add(instance);
        }
        return instance;
    }

    /// <summary>
    /// Uploads dirty parameters/migrations and records slice kills, arg resets, emits, and
    /// simulations onto <paramref name="context"/> outside any scope.
    /// </summary>
    public void RecordSimulation(RenderContext context, float deltaTime)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        lock (_gate)
        {
            if (SimulationRateLimitEnabled && _hasValidPlan && deltaTime > 0f)
            {
                _simulationAccumulator += deltaTime;
                if (_simulationAccumulator < SimulationInterval)
                {
                    if (_pool.RecordUploadsAndMigration(context))
                    {
                        context.Barrier(
                            Stage.Transfer, Access.TransferWrite,
                            Stage.Compute | Stage.Vertex | Stage.Indirect,
                            Access.ShaderRead | Access.IndirectRead);
                    }
                    return;
                }
                deltaTime = Math.Min(SimulationInterval, ParticleEmission.MaxDeltaTime);
                _simulationAccumulator = Math.Min(_simulationAccumulator - deltaTime, SimulationInterval);
            }

            deltaTime = Math.Min(deltaTime, ParticleEmission.MaxDeltaTime);

            uint dirtyMin = uint.MaxValue;
            uint dirtyMax = 0;
            for (int i = 0; i < _instances.Count; i++)
                _instances[i].AdvanceFrame(deltaTime, ref dirtyMin, ref dirtyMax);

            if (dirtyMin <= dirtyMax)
                _pool.UpdateParams(dirtyMin, dirtyMax - dirtyMin + 1);

            bool didTransfer = _pool.RecordUploadsAndMigration(context);

            BuildDrawPlan();
            _hasValidPlan = true;

            _pool.DrainPendingKills(_kills);
            bool anyDrawGroups = _drawGroups.Count > 0;
            if (_kills.Count == 0 && !anyDrawGroups)
            {
                if (didTransfer)
                {
                    context.Barrier(
                        Stage.Transfer, Access.TransferWrite,
                        Stage.Compute | Stage.Vertex | Stage.Indirect,
                        Access.ShaderRead | Access.IndirectRead);
                }
                return;
            }

            Alco.Graphics.Buffer? emitWorkBuf = null;
            Alco.Graphics.Buffer? simulateWorkBuf = null;
            if (anyDrawGroups)
                BuildWorkBlocks(context, out emitWorkBuf, out simulateWorkBuf);

            if (didTransfer)
            {
                context.Barrier(
                    Stage.Transfer, Access.TransferWrite,
                    Stage.Compute | Stage.Vertex,
                    Access.ShaderRead);
            }

            BumpAllocator frameData = context.FrameData;
            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                bool didPreWork = false;
                for (int i = 0; i < _kills.Count; i++)
                {
                    (uint offset, uint count) = _kills[i];
                    var root = new InitRoot2D
                    {
                        Particles = _pool.Particles.View,
                        SliceOffset = offset,
                        Count = count,
                    };
                    DispatchRoot(compute, frameData, _initShader, in root, (count + 63u) / 64u);
                    didPreWork = true;
                }

                if (_drawBatches.Count > 0)
                {
                    uint batchCount = (uint)_drawBatches.Count;
                    var resetRoot = new ResetArgsRoot
                    {
                        DrawArgs = _pool.DrawArgs.View,
                        DrawCount = batchCount,
                    };
                    DispatchRoot(compute, frameData, _resetArgsShader, in resetRoot, (batchCount + 63u) / 64u);
                    didPreWork = true;
                }

                if (anyDrawGroups && emitWorkBuf != null && _emitDispatches.Count > 0)
                {
                    if (didPreWork)
                    {
                        compute.Barrier(
                            Stage.Compute, Access.ShaderWrite,
                            Stage.Compute, Access.ShaderRead | Access.ShaderWrite);
                    }

                    GPUBufferView workView = emitWorkBuf.View;
                    GPUBufferView particlesView = _pool.Particles.View;
                    GPUBufferView emittersView = _pool.Params.View;
                    for (int i = 0; i < _emitDispatches.Count; i++)
                    {
                        WorkDispatch d = _emitDispatches[i];
                        var root = new EmitRoot2D
                        {
                            Particles = particlesView,
                            Emitters = emittersView,
                            WorkBlocks = workView,
                            WorkBlockBase = d.FirstBlock,
                        };
                        DispatchRoot(compute, frameData, d.Shader, in root, d.BlockCount);
                    }

                    compute.Barrier(
                        Stage.Compute, Access.ShaderWrite,
                        Stage.Compute, Access.ShaderRead | Access.ShaderWrite);
                }
                else if (didPreWork && anyDrawGroups)
                {
                    compute.Barrier(
                        Stage.Compute, Access.ShaderWrite,
                        Stage.Compute, Access.ShaderRead | Access.ShaderWrite);
                }

                if (anyDrawGroups && simulateWorkBuf != null)
                {
                    GPUBufferView workView = simulateWorkBuf.View;
                    GPUBufferView particlesView = _pool.Particles.View;
                    GPUBufferView emittersView = _pool.Params.View;
                    GPUBufferView drawArgsView = _pool.DrawArgs.View;
                    GPUBufferView instanceView = _pool.InstanceData.View;

                    for (int i = 0; i < _simulateDispatches.Count; i++)
                    {
                        WorkDispatch d = _simulateDispatches[i];
                        var root = new SimulateRoot2D
                        {
                            Particles = particlesView,
                            Emitters = emittersView,
                            DrawArgs = drawArgsView,
                            InstanceData = instanceView,
                            WorkBlocks = workView,
                            WorkBlockBase = d.FirstBlock,
                        };
                        DispatchRoot(compute, frameData, d.Shader, in root, d.BlockCount);
                    }
                }
            }

            context.Barrier(
                Stage.Compute, Access.ShaderWrite,
                Stage.Indirect | Stage.Vertex,
                Access.IndirectRead | Access.ShaderRead);
        }
    }

    /// <summary>Records batched non-indexed indirect draws for all active visible particle batches using <paramref name="camera"/>.</summary>
    public void Render(RenderContext context, RenderContext.RenderScope render, ICamera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        Render(context, render, camera.ViewProjectionMatrix);
    }

    /// <summary>Records batched non-indexed indirect draws for all active visible particle batches.</summary>
    public unsafe void Render(RenderContext context, RenderContext.RenderScope render, in Matrix4x4 viewProjection)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        lock (_gate)
        {
            if (_drawBatches.Count == 0)
                return;

            BumpAllocator frameData = context.FrameData;
            GPUBufferView particlesView = _pool.Particles.View;
            GPUBufferView emittersView = _pool.Params.View;
            GPUBufferView instanceBaseView = _pool.InstanceData.View;

            for (int i = 0; i < _drawBatches.Count; i++)
            {
                DrawBatch batch = _drawBatches[i];
                var passData = new ParticlePassData2D
                {
                    ViewProjection = viewProjection,
                    Particles = particlesView,
                    Emitters = emittersView,
                    InstanceData = instanceBaseView.OffsetBy(batch.InstanceBase * (uint)sizeof(GpuInstanceData)),
                };

                GPUAllocation<byte> alloc = frameData.Allocate(batch.Material.Root.SizeBytes);
                batch.Material.CopyRoot(alloc.CpuSpan);
                MemoryMarshal.Write(alloc.CpuSpan.Slice(batch.PassRootOffset, sizeof(ParticlePassData2D)), in passData);

                render.DrawIndirect(
                    batch.Material,
                    alloc.GPURange,
                    batch.DepthStencil,
                    _pool.DrawArgs,
                    drawCount: 1,
                    stride: 0,
                    argumentByteOffset: (ulong)i * (ulong)sizeof(GpuDrawArgs));
            }
        }
    }

    private static unsafe void DispatchRoot<TRoot>(
        RenderContext.ComputeScope compute,
        BumpAllocator frameData,
        Shader shader,
        in TRoot rootData,
        uint groupCountX)
        where TRoot : unmanaged
    {
        if (groupCountX == 0)
            return;
        Debug.Assert((uint)sizeof(TRoot) == shader.Root.SizeBytes);
        GPUAllocation<byte> alloc = frameData.Allocate((ulong)sizeof(TRoot));
        MemoryMarshal.Write(alloc.CpuSpan, in rootData);
        compute.Dispatch(shader, alloc.GPURange, new Extent3D(groupCountX, 1, 1));
    }

    private void BuildDrawPlan()
    {
        foreach (List<ParticleEffectInstance2D.GroupState> bucket in _drawBuckets.Values)
            bucket.Clear();
        _drawBucketKeys.Clear();

        for (int i = 0; i < _instances.Count; i++)
        {
            ParticleEffectInstance2D instance = _instances[i];
            if (!instance.IsActive || !instance.IsVisible)
                continue;
            ParticleEffectInstance2D.GroupState[] groups = instance.Groups;
            for (int g = 0; g < groups.Length; g++)
            {
                ParticleEffectInstance2D.GroupState group = groups[g];
                if (!group.Active || !group.Visible)
                    continue;

                var key = (group.Material, group.DepthStencil);
                if (!_drawBuckets.TryGetValue(key, out List<ParticleEffectInstance2D.GroupState>? bucket))
                {
                    bucket = [];
                    _drawBuckets[key] = bucket;
                    _drawBucketKeys.Add(key);
                }
                else if (bucket.Count == 0)
                {
                    _drawBucketKeys.Add(key);
                }
                bucket.Add(group);
            }
        }

        _drawGroups.Clear();
        _drawIndices.Clear();
        _drawStarts.Clear();
        _drawBatches.Clear();

        uint instanceOffset = 0;
        for (int b = 0; b < _drawBucketKeys.Count; b++)
        {
            (Material material, DepthStencilState depthStencil) = _drawBucketKeys[b];
            List<ParticleEffectInstance2D.GroupState> bucket = _drawBuckets[(material, depthStencil)];
            uint batchDrawIndex = (uint)b;
            uint batchInstanceBase = instanceOffset;
            int passOffset = bucket[0].PassRootOffset;

            for (int g = 0; g < bucket.Count; g++)
            {
                ParticleEffectInstance2D.GroupState group = bucket[g];
                _drawGroups.Add(group);
                _drawIndices.Add(batchDrawIndex);
                _drawStarts.Add(batchInstanceBase);
                instanceOffset += group.Slice.Capacity;
            }

            _drawBatches.Add(new DrawBatch(material, passOffset, depthStencil, batchInstanceBase));
        }
    }

    private unsafe void BuildWorkBlocks(
        RenderContext context,
        out Alco.Graphics.Buffer? emitWorkBuf,
        out Alco.Graphics.Buffer? simulateWorkBuf)
    {
        _emitBlocks.Clear();
        _simulateBlocks.Clear();
        _emitDispatches.Clear();
        _simulateDispatches.Clear();

        AppendWorkBlocks(emit: true, _emitBlocks, _emitDispatches);
        AppendWorkBlocks(emit: false, _simulateBlocks, _simulateDispatches);

        emitWorkBuf = UploadWorkBlocks(context, _emitWorkPool, _emitBlocks);
        simulateWorkBuf = UploadWorkBlocks(context, _simulateWorkPool, _simulateBlocks);
    }

    private void AppendWorkBlocks(bool emit, List<GpuParticleWorkBlock> blocks, List<WorkDispatch> dispatches)
    {
        foreach (List<int> bucket in _workBuckets.Values)
            bucket.Clear();
        _workShaders.Clear();

        for (int i = 0; i < _drawGroups.Count; i++)
        {
            if (emit && _drawGroups[i].SpawnCount == 0)
                continue;

            Shader shader = emit ? _drawGroups[i].EmitShader : _drawGroups[i].SimulateShader;
            if (!_workBuckets.TryGetValue(shader, out List<int>? bucket))
            {
                bucket = [];
                _workBuckets[shader] = bucket;
            }
            if (bucket.Count == 0)
                _workShaders.Add(shader);
            bucket.Add(i);
        }

        for (int m = 0; m < _workShaders.Count; m++)
        {
            Shader shader = _workShaders[m];
            List<int> bucket = _workBuckets[shader];
            uint first = (uint)blocks.Count;
            for (int g = 0; g < bucket.Count; g++)
            {
                int groupIndex = bucket[g];
                ParticleEffectInstance2D.GroupState group = _drawGroups[groupIndex];
                uint threads = emit ? group.SpawnCount : group.Slice.Capacity;
                uint blockCount = (threads + 63u) / 64u;
                for (uint b = 0; b < blockCount; b++)
                {
                    blocks.Add(new GpuParticleWorkBlock
                    {
                        EmitterSlot = group.Slot,
                        DrawIndex = _drawIndices[groupIndex],
                        DrawStart = _drawStarts[groupIndex],
                        ThreadBase = b * 64u,
                    });
                }
            }
            dispatches.Add(new WorkDispatch(shader, first, (uint)blocks.Count - first));
        }
    }

    private static unsafe Alco.Graphics.Buffer? UploadWorkBlocks(
        RenderContext context,
        TransientBufferPool pool,
        List<GpuParticleWorkBlock> blocks)
    {
        if (blocks.Count == 0)
            return null;
        Alco.Graphics.Buffer buf = pool.Acquire(context, (ulong)blocks.Count * (ulong)sizeof(GpuParticleWorkBlock));
        buf.SetData(CollectionsMarshal.AsSpan(blocks));
        return buf;
    }

    internal ref EmitterParams2D ParamsRef(uint slot) => ref _pool.ParamsSpan[(int)slot];

    internal void ReleaseInstance(ParticleEffectInstance2D instance)
    {
        lock (_gate)
        {
            foreach (ParticleEffectInstance2D.GroupState group in instance.Groups)
            {
                _pool.FreeSlice(group.Slice);
                _pool.FreeSlot(group.Slot);
            }
            _instances.Remove(instance);
            if (instance.IsActive)
                _hasValidPlan = false;
        }
    }

    private (Shader Emit, Shader Simulate) GetBehaviorShaders(string? behavior)
    {
        string moduleName = string.IsNullOrWhiteSpace(behavior)
            ? ParticleAssetPipeline.DefaultBehavior2D
            : behavior.Trim().Replace('-', '_');

        lock (_gate)
        {
            if (_behaviorShaders.TryGetValue(moduleName, out (Shader Emit, Shader Simulate) cached))
                return cached;
        }

        Shader emit = _shaders.Compose(ParticleAssetPipeline.EmitModule2D, moduleName, ShaderKind.Compute, $"particles2d_emit:{moduleName}");
        Shader simulate = _shaders.Compose(ParticleAssetPipeline.SimulateModule2D, moduleName, ShaderKind.Compute, $"particles2d_sim:{moduleName}");

        lock (_gate)
        {
            if (_behaviorShaders.TryGetValue(moduleName, out (Shader Emit, Shader Simulate) existing))
                return existing;
            var pair = (emit, simulate);
            _behaviorShaders[moduleName] = pair;
            return pair;
        }
    }

    private unsafe CachedMaterial GetOrCreateMaterial(ParticleGroup2D group)
    {
        MaterialAsset asset = group.Material ?? _defaultAsset;
        BlendState blend = group.Blend ?? BlendState.AlphaBlend;
        var key = (asset, blend);

        lock (_gate)
        {
            if (_materials.TryGetValue(key, out CachedMaterial cached))
                return cached;
        }

        string surfaceModule = string.IsNullOrWhiteSpace(asset.Surface)
            ? ParticleAssetPipeline.DefaultSurface
            : asset.Surface.Trim().Replace('-', '_');

        Shader shader = _shaders.Compose(_renderTemplateModule, surfaceModule, ShaderKind.Graphics, $"particles2d:{surfaceModule}");
        Material material = shader.CreateMaterial($"particles2d:{asset.Name}");
        material.Blend = blend;
        material.DepthStencilState = group.Depth ?? DepthStencilState.None;
        material.Rasterization = RasterizationState.CullNone;

        if (!ReferenceEquals(asset, _defaultAsset))
            asset.ApplyTo(material, ParticleShaderKeys.Material + ".");

        RootField? passField = material.Root.Find(ParticleShaderKeys.Pass);
        if (passField == null || passField.SizeBytes != (uint)sizeof(ParticlePassData2D))
            throw new InvalidDataException(
                $"Render template '{_renderTemplateModule}' does not declare expected '{ParticleShaderKeys.Pass}' block ({sizeof(ParticlePassData2D)}B).");

        var created = new CachedMaterial(material, (int)passField.Offset);
        lock (_gate)
        {
            if (_materials.TryGetValue(key, out CachedMaterial existing))
                return existing;
            _materials[key] = created;
            _materialModules.Apply(material);
            return created;
        }
    }

    private GroupTextures GetOrCreateGroupTextures(ParticleGroup2D group)
    {
        lock (_gate)
        {
            if (_groupTextures.TryGetValue(group, out GroupTextures existing))
                return existing;
        }

        Texture sprite = group.Texture?.Texture
            ?? (group.Material != null && group.Material.Textures.TryGetValue("texture", out Image? matSprite) ? matSprite.Texture : null)
            ?? _rendering.TextureWhite;

        Texture colorGradient = _rendering.TextureWhite;
        bool ownsGradient = false;
        if (group.ColorGradient is { Count: > 0 } gradientKeys)
        {
            byte[] pixels = new byte[ParticleOverLifeBake.TextureWidth * 4];
            ParticleOverLifeBake.BakeGradient(gradientKeys, pixels);
            colorGradient = _rendering.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D((uint)ParticleOverLifeBake.TextureWidth, 1, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            });
            _rendering.UploadTexture(colorGradient, pixels);
            ownsGradient = true;
        }

        Texture sizeCurve = _rendering.TextureWhite;
        bool ownsSize = false;
        if (group.SizeCurve is { Count: > 0 } curveKeys)
        {
            Half[] texels = new Half[ParticleOverLifeBake.TextureWidth];
            ParticleOverLifeBake.BakeCurve(curveKeys, texels);
            sizeCurve = _rendering.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D((uint)ParticleOverLifeBake.TextureWidth, 1, 1),
                Format = Format.R16Float,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            });
            _rendering.UploadTexture(sizeCurve, MemoryMarshal.AsBytes<Half>(texels));
            ownsSize = true;
        }

        lock (_gate)
        {
            if (_groupTextures.TryGetValue(group, out GroupTextures winner))
            {
                if (ownsGradient) colorGradient.Destroy();
                if (ownsSize) sizeCurve.Destroy();
                return winner;
            }

            if (ownsGradient) _ownedTextures.Add(colorGradient);
            if (ownsSize) _ownedTextures.Add(sizeCurve);

            var entry = new GroupTextures(sprite, colorGradient, sizeCurve);
            _groupTextures[group] = entry;
            return entry;
        }
    }

    private void OnPoolReallocated()
    {
        _hasValidPlan = false;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        lock (_gate)
        {
            for (int i = _instances.Count - 1; i >= 0; i--)
                _instances[i].Dispose();

            for (int i = 0; i < _ownedTextures.Count; i++)
                _ownedTextures[i].Destroy();
            _ownedTextures.Clear();
            _groupTextures.Clear();

            _emitWorkPool.Dispose();
            _simulateWorkPool.Dispose();
            _pool.Reallocated -= OnPoolReallocated;
            _pool.Dispose();
        }
    }
}
