using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Effects;

/// <summary>
/// Simulates and renders any number of <see cref="ParticleEffect2D"/> instances entirely on the GPU,
/// using bindless sprite/over-life textures and vertex pulling over <see cref="Alco.Graphics"/>.
/// Main-thread only.
/// </summary>
public sealed class GpuParticleSystem2D : AutoDisposable
{
    private readonly record struct CachedMaterial(Material Material, RootField<ParticlePassData2D> PassField);
    private readonly record struct DrawBatch(Material Material, RootField<ParticlePassData2D> PassField, DepthStencilState DepthStencil, uint InstanceBase);
    private readonly record struct WorkDispatch(Shader Shader, uint FirstBlock, uint BlockCount);

    private readonly struct PlannedBuffers(
        Alco.Graphics.Buffer particles,
        Alco.Graphics.Buffer paramsBuffer,
        Alco.Graphics.Buffer drawArgs,
        Alco.Graphics.Buffer instanceData)
    {
        public readonly Alco.Graphics.Buffer Particles = particles;
        public readonly Alco.Graphics.Buffer Params = paramsBuffer;
        public readonly Alco.Graphics.Buffer DrawArgs = drawArgs;
        public readonly Alco.Graphics.Buffer InstanceData = instanceData;
        public bool IsValid => Particles != null;
    }

    private sealed class GroupTextureEntry
    {
        public Texture Sprite = null!;
        public Texture ColorGradient = null!;
        public Texture SizeCurve = null!;
        public bool OwnsGradient;
        public bool OwnsSize;
        public int RefCount;
        public ulong IdleSinceFrame;
        public bool InIdleList;
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
    internal struct ParticlePassData2D
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
            => HashCode.Combine(RuntimeHelpers.GetHashCode(key.Asset), key.Blend);
    }

    private sealed class BatchKeyComparer : IEqualityComparer<(Material Material, DepthStencilState Depth)>
    {
        public bool Equals((Material Material, DepthStencilState Depth) x, (Material Material, DepthStencilState Depth) y)
            => ReferenceEquals(x.Material, y.Material) && x.Depth == y.Depth;

        public int GetHashCode((Material Material, DepthStencilState Depth) key)
            => HashCode.Combine(RuntimeHelpers.GetHashCode(key.Material), key.Depth);
    }

    private const ulong IdleTextureRetireFrames = 60;
    private const int MaxIdleTextureGroups = 128;

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
    private readonly Dictionary<ParticleGroup2D, GroupTextureEntry> _groupTextures = [];
    private readonly List<ParticleGroup2D> _idleTextureGroups = [];
    private readonly List<ParticleEffectInstance2D> _instances = [];

    private readonly Dictionary<(Material Material, DepthStencilState Depth), List<ParticleEffectInstance2D.GroupState>> _drawBuckets =
        new(new BatchKeyComparer());
    private readonly List<(Material Material, DepthStencilState Depth)> _drawBucketKeys = [];
    private readonly List<ParticleEffectInstance2D.GroupState> _drawGroups = [];
    private readonly List<uint> _drawIndices = [];
    private readonly List<uint> _drawStarts = [];
    private readonly List<DrawBatch> _drawBatches = [];
    private PlannedBuffers _plannedBuffers;

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
    private ulong _simulationStepCounter;
    private int _depthWriteGroupCount;
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

        _pool = new ParticleBufferPool<GpuParticle2D, EmitterParams2D>(rendering, particleCapacity, emitterSlots);
        _pool.Reallocated += OnPoolReallocated;
        _materialModules = new ParticleMaterialModules();
        _emitWorkPool = new TransientBufferPool(rendering);
        _simulateWorkPool = new TransientBufferPool(rendering);

        _resetArgsShader = shaders.GetShader(ParticleAssetPipeline.ResetArgsModule, ShaderKind.Compute);
        _initShader = shaders.GetShader(ParticleAssetPipeline.InitModule2D, ShaderKind.Compute);
    }

    /// <summary>Default 60 Hz fixed simulation interval.</summary>
    public const float DefaultSimulationInterval = 1f / 60f;

    /// <summary>Whether the fixed-interval simulation rate limiter is enabled.</summary>
    public bool SimulationRateLimitEnabled { get; set; } = true;

    /// <summary>The simulation step interval when <see cref="SimulationRateLimitEnabled"/> is enabled.</summary>
    public float SimulationInterval { get; set; } = DefaultSimulationInterval;

    /// <summary>Currently live effect instances.</summary>
    public IReadOnlyList<ParticleEffectInstance2D> Instances => _instances;

    internal ParticleBufferPool<GpuParticle2D, EmitterParams2D> Pool => _pool;
    internal int PlannedDrawGroupCount => _drawGroups.Count;
    internal int PlannedDrawBatchCount => _drawBatches.Count;
    internal int PlannedEmitDispatchCount => _emitDispatches.Count;
    internal int PlannedSimulateDispatchCount => _simulateDispatches.Count;
    internal int PlannedEmitBlockCount => _emitBlocks.Count;
    internal int PlannedSimulateBlockCount => _simulateBlocks.Count;
    internal bool HasDepthWritingEffects => _depthWriteGroupCount > 0;

    /// <summary>Current particle slot capacity of the shared pool.</summary>
    public int PoolParticleCapacity => _pool.ParticleCapacity;

    /// <summary>Current emitter slot capacity of the shared pool.</summary>
    public int PoolEmitterSlotCapacity => _pool.SlotCapacity;

    /// <summary>Destroys idle cached gradient/curve textures whose GPU frames have completed and removes unused materials.</summary>
    public void TrimUnusedCaches()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ProcessIdleAndRetiredTextures(forceIdleEvict: true);

        if (_materials.Count == 0)
            return;

        HashSet<Material> inUse = [];
        for (int i = 0; i < _instances.Count; i++)
        {
            ParticleEffectInstance2D.GroupState[] groups = _instances[i].Groups;
            for (int g = 0; g < groups.Length; g++)
                inUse.Add(groups[g].Material);
        }

        List<(MaterialAsset Asset, BlendState Blend)>? deadKeys = null;
        foreach (KeyValuePair<(MaterialAsset Asset, BlendState Blend), CachedMaterial> kv in _materials)
        {
            if (!inUse.Contains(kv.Value.Material))
            {
                deadKeys ??= [];
                deadKeys.Add(kv.Key);
            }
        }

        if (deadKeys != null)
        {
            for (int i = 0; i < deadKeys.Count; i++)
                _materials.Remove(deadKeys[i]);
        }
    }

    /// <summary>Registers a shared <see cref="IParticleMaterialModule"/> applied to all cached and future materials.</summary>
    public IDisposable AddMaterialModule(IParticleMaterialModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        IDisposable registration = _materialModules.Add(module);
        foreach (CachedMaterial entry in _materials.Values)
            module.ConfigureMaterial(entry.Material);
        return registration;
    }

    /// <summary>Re-applies all registered material modules onto cached materials.</summary>
    public void RefreshMaterialModules()
    {
        foreach (CachedMaterial entry in _materials.Values)
            _materialModules.Apply(entry.Material);
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

        int groupCount = effect.Groups.Count;
        var groups = new ParticleEffectInstance2D.GroupState[groupCount];
        ParticleGroup2D? pendingTexAsset = null;
        uint pendingSlot = 0;
        bool hasPendingSlot = false;
        ParticleSlice pendingSlice = default;
        int built = 0;

        try
        {
            for (int i = 0; i < groupCount; i++)
            {
                ParticleGroup2D groupAsset = effect.Groups[i];
                CachedMaterial cachedMat = GetOrCreateMaterial(groupAsset);
                (Shader emitShader, Shader simulateShader) = GetBehaviorShaders(groupAsset.Behavior);
                GroupTextureEntry texs = GetOrCreateGroupTextures(groupAsset);
                pendingTexAsset = groupAsset;

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

                DepthStencilState depthStencil = groupAsset.Depth ?? DepthStencilState.None;
                groups[i] = new ParticleEffectInstance2D.GroupState
                {
                    Asset = groupAsset,
                    Slot = pendingSlot,
                    Slice = pendingSlice,
                    Material = cachedMat.Material,
                    PassField = cachedMat.PassField,
                    DepthStencil = depthStencil,
                    EmitShader = emitShader,
                    SimulateShader = simulateShader,
                    EmissionRate = groupAsset.EmissionRate,
                    Lifetime = groupAsset.Lifetime,
                    Active = true,
                    Visible = true,
                };
                if (depthStencil.DepthWrite)
                    _depthWriteGroupCount++;
                built++;
                pendingTexAsset = null;
                hasPendingSlot = false;
                pendingSlice = default;
            }
        }
        catch
        {
            _pool.FreeSlice(pendingSlice);
            if (hasPendingSlot)
                _pool.FreeSlot(pendingSlot);
            if (pendingTexAsset != null)
                ReleaseGroupTextureRef(pendingTexAsset);
            for (int i = 0; i < built; i++)
            {
                if (groups[i].DepthStencil.DepthWrite)
                    _depthWriteGroupCount--;
                _pool.FreeSlice(groups[i].Slice);
                _pool.FreeSlot(groups[i].Slot);
                ReleaseGroupTextureRef(groups[i].Asset);
            }
            throw;
        }

        var instance = new ParticleEffectInstance2D(this, effect, transform, height, seed, groups);
        _instances.Add(instance);
        _hasValidPlan = false;
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

        _simulationStepCounter++;
        if (_idleTextureGroups.Count > 0)
            ProcessIdleAndRetiredTextures(forceIdleEvict: false);

        if (_pool.ReconcileFrame(context))
            _hasValidPlan = false;

        if (SimulationRateLimitEnabled && SimulationInterval > 0f && deltaTime > 0f)
        {
            if (_hasValidPlan)
            {
                _simulationAccumulator += deltaTime;
                if (_simulationAccumulator < SimulationInterval)
                {
                    if (_pool.RecordUploadsAndMigration(context))
                    {
                        context.Barrier(
                            Stage.Transfer, Access.TransferWrite,
                            Stage.Compute | Stage.Vertex | Stage.Indirect,
                            Access.ShaderRead | Access.ShaderWrite | Access.IndirectRead);
                    }
                    return;
                }
                deltaTime = Math.Min(SimulationInterval, ParticleEmission.MaxDeltaTime);
                _simulationAccumulator = Math.Min(_simulationAccumulator - deltaTime, SimulationInterval);
            }
            else
            {
                deltaTime = Math.Min(_simulationAccumulator + deltaTime, ParticleEmission.MaxDeltaTime);
                _simulationAccumulator = 0f;
            }
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
        PlannedBuffers plannedBuffers = new(_pool.Particles, _pool.Params, _pool.DrawArgs, _pool.InstanceData);
        _plannedBuffers = plannedBuffers;
        _hasValidPlan = true;

        _pool.DrainPendingKills(context, _kills);
        bool anyDrawGroups = _drawGroups.Count > 0;
        if (_kills.Count == 0 && !anyDrawGroups)
        {
            if (didTransfer)
            {
                context.Barrier(
                    Stage.Transfer, Access.TransferWrite,
                    Stage.Compute | Stage.Vertex | Stage.Indirect,
                    Access.ShaderRead | Access.ShaderWrite | Access.IndirectRead);
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
                Stage.Transfer | Stage.Compute | Stage.Vertex | Stage.Indirect,
                Access.TransferWrite | Access.ShaderRead | Access.ShaderWrite | Access.IndirectRead,
                Stage.Compute | Stage.Vertex,
                Access.ShaderRead | Access.ShaderWrite);
        }
        else
        {
            context.Barrier(
                Stage.Compute | Stage.Vertex | Stage.Indirect,
                Access.ShaderRead | Access.ShaderWrite | Access.IndirectRead,
                Stage.Compute,
                Access.ShaderRead | Access.ShaderWrite);
        }

        using (RenderContext.ComputeScope compute = context.BeginCompute())
        {
            bool didKills = false;
            bool didResetArgs = false;
            bool didEmit = false;
            GPUBufferView particlesView = plannedBuffers.Particles.View;
            GPUBufferView emittersView = plannedBuffers.Params.View;
            GPUBufferView drawArgsView = plannedBuffers.DrawArgs.View;
            GPUBufferView instanceView = plannedBuffers.InstanceData.View;

            for (int i = 0; i < _kills.Count; i++)
            {
                (uint offset, uint count) = _kills[i];
                var root = new InitRoot2D
                {
                    Particles = particlesView,
                    SliceOffset = offset,
                    Count = count,
                };
                DispatchRoot(in compute, _initShader, in root, (count + 63u) / 64u);
                didKills = true;
            }

            if (_drawBatches.Count > 0)
            {
                uint batchCount = (uint)_drawBatches.Count;
                var resetRoot = new ResetArgsRoot
                {
                    DrawArgs = drawArgsView,
                    DrawCount = batchCount,
                };
                DispatchRoot(in compute, _resetArgsShader, in resetRoot, (batchCount + 63u) / 64u);
                didResetArgs = true;
            }

            if (anyDrawGroups && emitWorkBuf != null && _emitDispatches.Count > 0)
            {
                if (didKills)
                {
                    compute.Barrier(
                        Stage.Compute, Access.ShaderWrite,
                        Stage.Compute, Access.ShaderRead | Access.ShaderWrite);
                }

                GPUBufferView workView = emitWorkBuf.View;
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
                    DispatchRoot(in compute, d.Shader, in root, d.BlockCount);
                }

                didEmit = true;
            }

            if (anyDrawGroups && simulateWorkBuf != null && _simulateDispatches.Count > 0)
            {
                if (didEmit || didKills || didResetArgs)
                {
                    compute.Barrier(
                        Stage.Compute, Access.ShaderWrite,
                        Stage.Compute, Access.ShaderRead | Access.ShaderWrite);
                }

                GPUBufferView workView = simulateWorkBuf.View;
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
                    DispatchRoot(in compute, d.Shader, in root, d.BlockCount);
                }
            }
        }

        context.Barrier(
            Stage.Compute, Access.ShaderWrite,
            Stage.Indirect | Stage.Vertex,
            Access.IndirectRead | Access.ShaderRead);
    }

    /// <summary>Records batched indexed indirect draws for all active visible particle batches using <paramref name="camera"/>.</summary>
    public void Render(RenderContext context, RenderContext.RenderScope render, ICamera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        Render(context, render, camera.ViewProjectionMatrix);
    }

    /// <summary>Records batched indexed indirect draws for all active visible particle batches.</summary>
    public unsafe void Render(RenderContext context, RenderContext.RenderScope render, in Matrix4x4 viewProjection)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (_drawBatches.Count == 0 || !_plannedBuffers.IsValid)
            return;

        PlannedBuffers buffers = _plannedBuffers;
        _pool.TouchBuffersUsed(context, buffers.Particles, buffers.Params, buffers.DrawArgs, buffers.InstanceData);

        GPUBufferView particlesView = buffers.Particles.View;
        GPUBufferView emittersView = buffers.Params.View;
        GPUBufferView instanceBaseView = buffers.InstanceData.View;

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

            render.DrawIndexedIndirect(
                batch.Material,
                batch.PassField,
                in passData,
                batch.DepthStencil,
                _pool.QuadIndices,
                IndexType.UInt16,
                buffers.DrawArgs.Slice((ulong)i * (ulong)sizeof(GpuDrawArgs)),
                drawCount: 1);
        }
    }

    private static void DispatchRoot<TRoot>(
        in RenderContext.ComputeScope compute,
        Shader shader,
        in TRoot rootData,
        uint groupCountX)
        where TRoot : unmanaged
    {
        if (groupCountX == 0)
            return;
        compute.Dispatch(shader, in rootData, new Extent3D(groupCountX, 1, 1));
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
            RootField<ParticlePassData2D> passField = bucket[0].PassField;

            for (int g = 0; g < bucket.Count; g++)
            {
                ParticleEffectInstance2D.GroupState group = bucket[g];
                _drawGroups.Add(group);
                _drawIndices.Add(batchDrawIndex);
                _drawStarts.Add(batchInstanceBase);
                instanceOffset += group.Slice.Capacity;
            }

            _drawBatches.Add(new DrawBatch(material, passField, depthStencil, batchInstanceBase));
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

    internal void InvalidatePlan() => _hasValidPlan = false;

    internal void ReleaseInstance(ParticleEffectInstance2D instance)
    {
        if (!_instances.Remove(instance))
            return;

        foreach (ParticleEffectInstance2D.GroupState group in instance.Groups)
        {
            if (group.DepthStencil.DepthWrite)
                _depthWriteGroupCount--;
            _pool.FreeSlice(group.Slice);
            _pool.FreeSlot(group.Slot);
            ReleaseGroupTextureRef(group.Asset);
        }
        if (instance.IsActive && instance.IsVisible)
            _hasValidPlan = false;
    }

    private (Shader Emit, Shader Simulate) GetBehaviorShaders(string? behavior)
    {
        string moduleName = string.IsNullOrWhiteSpace(behavior)
            ? ParticleAssetPipeline.DefaultBehavior2D
            : behavior.Trim().Replace('-', '_');

        if (_behaviorShaders.TryGetValue(moduleName, out (Shader Emit, Shader Simulate) cached))
            return cached;

        Shader emit = _shaders.Compose(ParticleAssetPipeline.EmitModule2D, moduleName, ShaderKind.Compute, $"particles2d_emit:{moduleName}");
        Shader simulate = _shaders.Compose(ParticleAssetPipeline.SimulateModule2D, moduleName, ShaderKind.Compute, $"particles2d_sim:{moduleName}");

        var pair = (emit, simulate);
        _behaviorShaders[moduleName] = pair;
        return pair;
    }

    private unsafe CachedMaterial GetOrCreateMaterial(ParticleGroup2D group)
    {
        MaterialAsset asset = group.Material ?? _defaultAsset;
        BlendState blend = group.Blend ?? BlendState.AlphaBlend;
        var key = (asset, blend);

        if (_materials.TryGetValue(key, out CachedMaterial cached))
            return cached;

        string surfaceModule = string.IsNullOrWhiteSpace(asset.Surface)
            ? ParticleAssetPipeline.DefaultSurface
            : asset.Surface.Trim().Replace('-', '_');

        Shader shader = _shaders.Compose(_renderTemplateModule, surfaceModule, ShaderKind.Graphics, $"particles2d:{surfaceModule}");
        Material material = ReferenceEquals(asset, _defaultAsset)
            ? shader.CreateMaterial($"particles2d:{asset.Name}")
            : asset.CreateMaterial(shader, $"particles2d:{asset.Name}", ParticleShaderKeys.Material + ".");
        material.Blend = blend;
        material.DepthStencilState = group.Depth ?? DepthStencilState.None;
        material.Rasterization = RasterizationState.CullNone;

        if (!material.TryGetRootField(ParticleShaderKeys.Pass, out RootField<ParticlePassData2D> passField))
            throw new InvalidDataException(
                $"Render template '{_renderTemplateModule}' does not declare expected '{ParticleShaderKeys.Pass}' block ({sizeof(ParticlePassData2D)}B).");

        var created = new CachedMaterial(material, passField);
        _materials[key] = created;
        _materialModules.Apply(material);
        return created;
    }

    private GroupTextureEntry GetOrCreateGroupTextures(ParticleGroup2D group)
    {
        if (_groupTextures.TryGetValue(group, out GroupTextureEntry? existing))
        {
            ActivateGroupTextureRef(group, existing);
            return existing;
        }

        Texture sprite = group.Texture?.Texture
            ?? (group.Material != null && group.Material.Textures.TryGetValue(ParticleShaderKeys.TextureSlot, out Image? matSprite) ? matSprite.Texture : null)
            ?? _rendering.TextureWhite;

        if (sprite.SampledIndex == Texture.NoIndex)
            throw new InvalidOperationException($"Sprite texture on particle group '{group.Name}' has no sampled descriptor slot.");

        Texture colorGradient = _rendering.TextureWhite;
        bool ownsGradient = false;
        Texture sizeCurve = _rendering.TextureWhite;
        bool ownsSize = false;

        try
        {
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
                ownsGradient = true;
                _rendering.UploadTexture(colorGradient, pixels);
            }

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
                ownsSize = true;
                _rendering.UploadTexture(sizeCurve, MemoryMarshal.AsBytes<Half>(texels));
            }
        }
        catch
        {
            if (ownsGradient) colorGradient.Dispose();
            if (ownsSize) sizeCurve.Dispose();
            throw;
        }

        var entry = new GroupTextureEntry
        {
            Sprite = sprite,
            ColorGradient = colorGradient,
            SizeCurve = sizeCurve,
            OwnsGradient = ownsGradient,
            OwnsSize = ownsSize,
            RefCount = 1,
        };
        _groupTextures[group] = entry;
        return entry;
    }

    private void ActivateGroupTextureRef(ParticleGroup2D group, GroupTextureEntry entry)
    {
        entry.RefCount++;
        if (entry.InIdleList)
        {
            entry.InIdleList = false;
            _idleTextureGroups.Remove(group);
        }
    }

    private void ReleaseGroupTextureRef(ParticleGroup2D group)
    {
        if (!_groupTextures.TryGetValue(group, out GroupTextureEntry? entry))
            return;

        entry.RefCount--;
        if (entry.RefCount <= 0 && !entry.InIdleList)
        {
            entry.RefCount = 0;
            entry.IdleSinceFrame = _simulationStepCounter;
            entry.InIdleList = true;
            _idleTextureGroups.Add(group);
        }
    }

    private void ProcessIdleAndRetiredTextures(bool forceIdleEvict)
    {
        for (int i = _idleTextureGroups.Count - 1; i >= 0; i--)
        {
            ParticleGroup2D group = _idleTextureGroups[i];
            if (!_groupTextures.TryGetValue(group, out GroupTextureEntry? entry))
            {
                RemoveIdleAt(i);
                continue;
            }
            if (entry.RefCount > 0)
            {
                entry.InIdleList = false;
                RemoveIdleAt(i);
                continue;
            }

            bool expired = forceIdleEvict
                || _idleTextureGroups.Count > MaxIdleTextureGroups
                || (_simulationStepCounter - entry.IdleSinceFrame) >= IdleTextureRetireFrames;
            if (!expired)
                continue;

            _groupTextures.Remove(group);
            RemoveIdleAt(i);
            if (entry.OwnsGradient)
                entry.ColorGradient.Dispose();
            if (entry.OwnsSize)
                entry.SizeCurve.Dispose();
        }
    }

    private void RemoveIdleAt(int index)
    {
        int last = _idleTextureGroups.Count - 1;
        if (index != last)
            _idleTextureGroups[index] = _idleTextureGroups[last];
        _idleTextureGroups.RemoveAt(last);
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

        for (int i = _instances.Count - 1; i >= 0; i--)
            _instances[i].Dispose();

        foreach (GroupTextureEntry entry in _groupTextures.Values)
        {
            if (entry.OwnsGradient) entry.ColorGradient.Dispose();
            if (entry.OwnsSize) entry.SizeCurve.Dispose();
        }
        _groupTextures.Clear();
        _idleTextureGroups.Clear();

        _materials.Clear();
        _behaviorShaders.Clear();
        _drawBatches.Clear();
        _plannedBuffers = default;

        _emitWorkPool.Dispose();
        _simulateWorkPool.Dispose();
        _pool.Reallocated -= OnPoolReallocated;
        _pool.Dispose();
    }
}
