using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Particles;

/// <summary>
/// The 2D GPU particle system: simulates and renders any number of
/// <see cref="ParticleEffect2DAsset"/> instances entirely on the GPU. Per frame it
/// records the compute work batched per behavior material — one wide emit and one
/// wide simulate/compact dispatch each, whose 64-thread blocks resolve their
/// emitter group through a CPU-uploaded work-block table (see
/// <see cref="GpuParticleWorkBlock"/>) — and, batched per render material, one
/// indexed multi-draw indirect into the scene pass; CPU work per frame is a small
/// parameter update per active emitter plus the per-frame draw-plan bookkeeping.
/// <br/>All instances share one buffer pool (see <see cref="ParticleBufferPool{,}"/>),
/// which makes creating/destroying effects cheap and keeps the draw state stable.
/// <br/>Draw batching: per frame the CPU buckets the visible active groups by
/// material and assigns each a drawIndex (its slot in the compacted draw-args
/// array, contiguous per material for the multi-draw) and a drawStart (the
/// firstInstance addressing its run in the instance-data buffer). The emit pass
/// writes the args record at drawIndex, the simulate pass appends one
/// GpuInstanceData record per surviving particle at drawStart; the render pass
/// binds the instance-data buffer as the instance-step vertex buffer and issues
/// one multi-draw per material, so draw calls scale with the material count, not
/// the emitter count. The per-draw identity travels through vertex fetch on
/// purpose: the instance-id builtin's firstInstance semantics differ between
/// Vulkan (absolute) and D3D12 (per-draw).
/// <br/>Note the batch list is recorded at simulation time; <c>Render</c> replays
/// it, so a visibility flip between the two phases lands the next frame (the
/// simulation itself already gates on the previous frame's visibility).
/// <br/>Rate limiting: with <see cref="SimulationRateLimitEnabled"/> on (the
/// default), the simulation steps at most once per <see cref="SimulationInterval"/>
/// and the frames in between skip every dispatch and replay the cached draw plan
/// — the pool buffers persist, so the last simulated state still draws. The GPU
/// simulation cost then scales with the configured rate instead of the frame rate.
/// <br/>Usage: insert the simulation into the pipeline with an
/// <see cref="RGNode_Callback"/> before the scene content node and call
/// <see cref="Render"/> from the scene content (or an <see cref="IRenderPassContent"/>).
/// <br/>Material modules (<see cref="AddMaterialModule"/>) bind further shared
/// shader resources into the render materials (e.g. a lighting middleware
/// binding a light map); slots resolve by name with opt-in semantics, so
/// surfaces that declare no matching slot are unaffected.
/// <br/>Threading: instance creation, release (instance disposal), the camera,
/// the frame simulation and rendering are serialized on one reentrant gate shared
/// with the pool, so creating/destroying effects from any thread is safe and never
/// corrupts the shared bookkeeping. First-use material compilation happens off the
/// gate (double-checked caches), so a worker-thread compile never stalls the frame
/// thread. Ongoing per-instance mutation (per-tick transforms, group parameter
/// edits) remains a main-thread contract; a one-time setup write from the creating
/// thread (e.g. facade depth right after creation) may race the frame simulation's
/// per-frame fields of the same slot — the worst case is losing one frame of that
/// group's emission, self-healed by the next step.
/// </summary>
public sealed class GpuParticleSystem2D : AutoDisposable
{
    /// <summary>One material-homogeneous run of the per-frame draw plan.</summary>
    private readonly record struct DrawBatch(GraphicsMaterial Material, uint First, uint Count);

    /// <summary>One behavior-material-homogeneous run of the per-frame batched compute plan.</summary>
    private readonly record struct WorkDispatch(ComputeMaterial Material, uint First, uint Count);

    /// <summary>
    /// Serializes all shared mutable state (instances, material caches, draw plan,
    /// pool bookkeeping via the pool's shared gate). Reentrant, so nested
    /// system↔pool calls and the pool's <c>Reallocated</c> handler cannot deadlock.
    /// </summary>
    private readonly Lock _gate = new();

    private readonly RenderingSystem _rendering;
    private readonly ParticleBufferPool<GpuParticle2D, EmitterParams2D> _pool;
    private readonly MaterialCompiler _materialCompiler;
    private readonly ShaderLibrary _renderTemplate;
    private readonly ShaderLibrary _emitTemplate;
    private readonly ShaderLibrary _simulateTemplate;
    private readonly ShaderLibrary _defaultBehavior;
    private readonly ComputeMaterial _initMaterial;
    private readonly Dictionary<ShaderLibrary, (ComputeMaterial Emit, ComputeMaterial Simulate)> _behaviorMaterials = [];
    private readonly Dictionary<ParticleGroup2DAsset, GraphicsMaterial> _materials = [];
    private readonly List<Texture2D> _overLifeTextures = [];
    private readonly List<ParticleEffectInstance2D> _instances = [];
    // The per-frame draw plan (rebuilt in RecordSimulation, replayed in Render):
    // groups bucketed by material in first-seen order, flattened with their
    // drawIndex/drawStart assignment, plus one batch record per material.
    // GroupState is a struct: the bucket lists hold value snapshots taken after
    // the frame's AdvanceFrame pass, so nothing between that pass and the
    // dispatches recorded from these lists may mutate group state (and mutations
    // afterwards would not be reflected in the plan until the next rebuild).
    private readonly Dictionary<GraphicsMaterial, List<ParticleEffectInstance2D.GroupState>> _drawBuckets = [];
    private readonly List<GraphicsMaterial> _drawMaterials = [];
    private readonly List<ParticleEffectInstance2D.GroupState> _drawGroups = [];
    private readonly List<uint> _drawIndices = [];
    private readonly List<uint> _drawStarts = [];
    private readonly List<DrawBatch> _drawBatches = [];
    // The per-frame batched compute plan (rebuilt with the draw plan): the draw
    // plan's groups re-bucketed by behavior material in first-seen order, each
    // bucket flattened into one work-block record per 64-thread block and recorded
    // as one wide dispatch over its table run (see BuildWorkBlocks).
    private readonly Dictionary<ComputeMaterial, List<int>> _workBuckets = [];
    private readonly List<ComputeMaterial> _workMaterials = [];
    private readonly List<GpuParticleWorkBlock> _emitBlocks = [];
    private readonly List<GpuParticleWorkBlock> _simulateBlocks = [];
    private readonly List<WorkDispatch> _emitDispatches = [];
    private readonly List<WorkDispatch> _simulateDispatches = [];
    // The reusable sink of the frame's drained pending slice kills — allocation-free
    // even while effects churn every frame (see ParticleBufferPool.DrainPendingKills).
    private readonly List<(uint Offset, uint Count)> _kills = [];
    private GraphicsArrayBuffer<GpuParticleWorkBlock> _emitBlockBuffer;
    private GraphicsArrayBuffer<GpuParticleWorkBlock> _simulateBlockBuffer;
    // Outgrown work-block buffers retire two frames later (in-flight frames may
    // still reference them) — the same policy as the pool's growth.
    private readonly List<(GraphicsArrayBuffer<GpuParticleWorkBlock> Buffer, int FramesLeft)> _retiredWorkBuffers = [];
    private readonly MaterialAsset _defaultAsset = new() { Name = "particles2d-default" };
    private readonly ParticleMaterialModules _materialModules;
    private GraphicsValueBuffer<Matrix4x4>? _camera;
    // The rate-limiting state: the un-simulated frame time accumulated since the
    // last step, and whether the draw plan and pool buffers hold a replayable
    // simulated state (see RecordSimulation).
    private float _simulationAccumulator;
    private bool _hasValidPlan;

    /// <summary>
    /// Creates the system with its shared pool's initial capacities.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="particleCapacity">The initial particle pool size (grows geometrically when exhausted).</param>
    /// <param name="emitterSlots">The initial emitter-slot count (one per emitter group instance).</param>
    /// <param name="renderModule">The pass-template module group surfaces compose with;
    /// null uses the built-in <see cref="ParticleAssetPipeline.RenderModule2D"/>. A custom
    /// template must keep the built-in pass's vertex stage and resource contract.</param>
    public GpuParticleSystem2D(RenderingSystem rendering, int particleCapacity = 65536, int emitterSlots = 256, string? renderModule = null)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        _rendering = rendering;
        _pool = new ParticleBufferPool<GpuParticle2D, EmitterParams2D>(rendering, particleCapacity, emitterSlots, "particles2d", _gate);
        _pool.Reallocated += OnPoolReallocated;
        _materialModules = new ParticleMaterialModules(_gate);
        ShaderSystem shaderSystem = rendering.ShaderSystem;
        _materialCompiler = new MaterialCompiler(rendering, shaderSystem.GetLibrary(ParticleAssetPipeline.DefaultSurface));
        _renderTemplate = shaderSystem.GetLibrary(renderModule ?? ParticleAssetPipeline.RenderModule2D);
        _emitTemplate = shaderSystem.GetLibrary("GpuParticleEmit2D");
        _simulateTemplate = shaderSystem.GetLibrary("GpuParticleSimulate2D");
        _defaultBehavior = shaderSystem.GetLibrary(ParticleAssetPipeline.DefaultBehavior2D);
        _initMaterial = rendering.CreateComputeMaterial(shaderSystem.GetShader("GpuParticleInit2D"));
        // Bind the pool up front: OnPoolReallocated refreshes this, but the first
        // slice-recycle kill dispatch can precede any reallocation.
        _initMaterial.TrySetBuffer(ShaderResourceId.Particles, _pool.Particles);
        _emitBlockBuffer = rendering.CreateGraphicsArrayBuffer<GpuParticleWorkBlock>(InitialWorkBlockCapacity, "particles2d_emit_work");
        _simulateBlockBuffer = rendering.CreateGraphicsArrayBuffer<GpuParticleWorkBlock>(InitialWorkBlockCapacity, "particles2d_simulate_work");
        QuadMesh = rendering.MeshCenteredSprite;
    }

    /// <summary>The initial capacity of the work-block tables in records (grows geometrically when exhausted).</summary>
    private const int InitialWorkBlockCapacity = 512;

    /// <summary>
    /// The 2D camera the groups render with; bound to every group's material.
    /// Accepts any view-projection matrix buffer (a <see cref="Camera2DBuffer"/>,
    /// or the shared <see cref="RenderingSystem.MainCameraViewProjectionBuffer"/> to
    /// track whatever camera is currently rendering). Must be set before
    /// <see cref="Render"/> is called.
    /// </summary>
    public GraphicsValueBuffer<Matrix4x4>? Camera
    {
        get => _camera;
        set
        {
            _camera = value;
            lock (_gate)
            {
                if (value != null)
                {
                    foreach (GraphicsMaterial material in _materials.Values)
                    {
                        material.SetBuffer(ShaderResourceId.Camera, value);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Registers a material module (see <see cref="IParticleMaterialModule"/>):
    /// its bindings apply immediately to every material already cached and to
    /// every render material the system creates afterwards. The typical module
    /// is a lighting middleware binding a shared light-map texture and its
    /// sampling parameters into slots the particle surfaces declare.
    /// </summary>
    /// <param name="module">The module to register.</param>
    /// <returns>The unregistration handle; dispose it to remove the module.</returns>
    public IDisposable AddMaterialModule(IParticleMaterialModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        lock (_gate)
        {
            IDisposable registration = _materialModules.Add(module);
            foreach (GraphicsMaterial material in _materials.Values)
            {
                module.ConfigureMaterial(material);
            }
            return registration;
        }
    }

    /// <summary>
    /// Re-applies every registered material module to every cached render
    /// material (materials created afterwards configure at creation anyway).
    /// For a module that replaced one of its resource objects (e.g. its light
    /// map was recreated) and needs the new object bound everywhere; unchanged
    /// bindings are overwritten idempotently.
    /// </summary>
    public void RefreshMaterialModules()
    {
        lock (_gate)
        {
            foreach (GraphicsMaterial material in _materials.Values)
            {
                _materialModules.Apply(material);
            }
        }
    }

    /// <summary>The quad mesh the particles draw with (centered, position.xy in [-0.5, 0.5]).</summary>
    public Mesh QuadMesh { get; set; }

    /// <summary>The default <see cref="SimulationInterval"/>: a 60 Hz simulation rate.</summary>
    public const float DefaultSimulationInterval = 1f / 60f;

    /// <summary>
    /// Whether the simulation rate limiter is on (default on). When off, every
    /// <see cref="RecordSimulation"/> call simulates — the unthrottled behavior.
    /// </summary>
    public bool SimulationRateLimitEnabled { get; set; } = true;

    /// <summary>
    /// The fixed time in seconds between simulation steps while
    /// <see cref="SimulationRateLimitEnabled"/> is on. Frames below it record no
    /// emit/simulate dispatches at all — <see cref="Render"/> replays the cached
    /// draw plan, which still draws the last simulated state — and their deltas
    /// accumulate; each step simulates exactly one interval and carries the
    /// remainder, so the cadence is even and the average pacing matches the wall
    /// clock. Backlog beyond one interval (a hitch, or a frame rate below the
    /// simulation rate) is discarded — effects play through stalls slightly
    /// slower instead of fast-forwarding. Must be positive to have an effect; a
    /// non-positive value simulates every frame.
    /// </summary>
    public float SimulationInterval { get; set; } = DefaultSimulationInterval;

    /// <summary>The live effect instances (a snapshot; safe from any thread).</summary>
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

    /// <summary>The shared buffer pool (diagnostics).</summary>
    internal ParticleBufferPool<GpuParticle2D, EmitterParams2D> Pool => _pool;

    /// <summary>The number of groups in the draw plan built by the last <see cref="RecordSimulation"/> (tests).</summary>
    internal int PlannedDrawGroupCount => _drawGroups.Count;

    /// <summary>The number of batched emit dispatches of the last simulated frame (tests).</summary>
    internal int PlannedEmitDispatchCount => _emitDispatches.Count;

    /// <summary>The number of batched simulate dispatches of the last simulated frame (tests).</summary>
    internal int PlannedSimulateDispatchCount => _simulateDispatches.Count;

    /// <summary>The number of emit work blocks of the last simulated frame (tests).</summary>
    internal int PlannedEmitBlockCount => _emitBlocks.Count;

    /// <summary>The number of simulate work blocks of the last simulated frame (tests).</summary>
    internal int PlannedSimulateBlockCount => _simulateBlocks.Count;

    /// <summary>The shared pool's current particle capacity (grows geometrically when exhausted).</summary>
    public int PoolParticleCapacity => _pool.ParticleCapacity;

    /// <summary>The shared pool's current emitter-slot count.</summary>
    public int PoolEmitterSlotCapacity => _pool.SlotCapacity;

    /// <summary>
    /// Creates an effect instance that starts playing immediately.
    /// </summary>
    /// <param name="effect">The effect asset.</param>
    /// <param name="transform">The emitter transform.</param>
    /// <param name="seed">The deterministic RNG seed of the instance; 0 seeds from the environment tick.</param>
    /// <returns>The new instance; dispose it to destroy the effect.</returns>
    public ParticleEffectInstance2D CreateInstance(ParticleEffect2DAsset effect, in Transform2D transform, int seed = 0)
    {
        return CreateInstance(effect, transform, 0f, seed);
    }

    /// <summary>
    /// Creates an effect instance with a ground-plane transform and an independent
    /// 2.5D emitter height. The instance starts playing immediately. Thread-safe:
    /// pool allocation, parameter writes and the instance publication are serialized
    /// on the shared gate; a first-use material compiles outside it (see
    /// <see cref="GetOrCreateMaterial"/>).
    /// </summary>
    /// <param name="effect">The effect asset.</param>
    /// <param name="transform">The emitter's ground-plane transform.</param>
    /// <param name="height">The emitter height above the ground plane.</param>
    /// <param name="seed">The deterministic RNG seed of the instance; 0 seeds from the environment tick.</param>
    /// <returns>The new instance; dispose it to destroy the effect.</returns>
    public ParticleEffectInstance2D CreateInstance(ParticleEffect2DAsset effect, in Transform2D transform, float height, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (effect.Groups.Count == 0)
        {
            throw new InvalidDataException($"Particle effect '{effect.Name}' has no groups.");
        }

        uint indexCount = QuadMesh.GetSubMesh(0).IndexCount;
        var groups = new ParticleEffectInstance2D.GroupState[effect.Groups.Count];
        uint pendingSlot = 0;
        ParticleSlice pendingSlice = default;
        uint uploadMin = uint.MaxValue;
        uint uploadMax = 0;
        int built = 0;
        try
        {
            for (int i = 0; i < groups.Length; i++)
            {
                ParticleGroup2DAsset groupAsset = effect.Groups[i];
                pendingSlot = _pool.AllocateSlot();
                pendingSlice = _pool.AllocateSlice(Math.Max(groupAsset.MaxParticles, 1));
                EmitterParams2D parameters = EmitterParams2D.FromAsset(groupAsset, indexCount);
                parameters.Capacity = pendingSlice.Capacity;
                parameters.SliceOffset = pendingSlice.Offset;
                _pool.SetParams(pendingSlot, parameters);
                uploadMin = Math.Min(uploadMin, pendingSlot);
                uploadMax = Math.Max(uploadMax, pendingSlot);
                (ComputeMaterial emit, ComputeMaterial simulate) = GetBehaviorMaterials(groupAsset.Behavior);
                groups[i] = new ParticleEffectInstance2D.GroupState
                {
                    Asset = groupAsset,
                    Slot = pendingSlot,
                    Slice = pendingSlice,
                    Material = GetOrCreateMaterial(groupAsset),
                    EmitMaterial = emit,
                    SimulateMaterial = simulate,
                    EmissionRate = groupAsset.EmissionRate,
                    Lifetime = groupAsset.Lifetime,
                    Active = true,
                    Visible = true,
                };
                built++;
                pendingSlice = default; // ownership moved to the group state
            }
        }
        catch
        {
            // A mid-construction failure (e.g. an invalid group material) must not
            // leak pool resources: free the in-flight group's slot/slice plus every
            // completed group's (the struct array has no null tail to detect them,
            // so the count of completed assignments carries the boundary).
            _pool.FreeSlice(pendingSlice);
            if (pendingSlice.Capacity > 0)
            {
                _pool.FreeSlot(pendingSlot);
            }
            for (int i = 0; i < built; i++)
            {
                _pool.FreeSlice(groups[i].Slice);
                _pool.FreeSlot(groups[i].Slot);
            }
            throw;
        }
        _pool.UpdateParams(uploadMin, uploadMax - uploadMin + 1);
        var instance = new ParticleEffectInstance2D(this, effect, transform, height, seed, groups);
        lock (_gate)
        {
            // Publish last: the frame simulation iterates the list, so a
            // half-initialized instance must never be visible to it.
            _instances.Add(instance);
        }
        return instance;
    }

    /// <summary>
    /// Records the simulation of all active instances into
    /// <paramref name="commandBuffer"/>: pending pool migrations, the parameter
    /// upload, the per-frame draw plan, pending slice kills, then the emit and
    /// simulate/compact dispatches batched per behavior material (see
    /// <see cref="BuildWorkBlocks"/>). The caller owns the ordering: record
    /// before the scene pass the particles draw into (e.g. from an
    /// <see cref="RGNode_Callback"/>). Pass
    /// <paramref name="deltaTime"/> = 0 to freeze the simulation while still
    /// processing pool migrations and pending kills. The step clamps to
    /// <see cref="ParticleEmission.MaxDeltaTime"/>: a hitch (first-use shader
    /// compilation, save load, OS stall) must not fast-forward one-shot effects
    /// past their emission timeline and kill every live particle in a single
    /// dispatch — effects play through hitches slightly slower instead.
    /// <br/>While <see cref="SimulationRateLimitEnabled"/> is on, a frame whose
    /// accumulated delta stays below <see cref="SimulationInterval"/> skips the
    /// whole simulation: pool migrations still record (growth and buffer disposal
    /// must not stall), but the parameter upload, the draw-plan rebuild and every
    /// dispatch wait for the next step, and <see cref="Render"/> replays the cached
    /// plan. Each step simulates one fixed interval with the remainder carried
    /// over; backlog beyond one interval is discarded (the same never-fast-forward
    /// policy as the hitch clamp). Pending slice kills ride the next simulated
    /// frame — they only gate a slice's reuse, which itself happens on simulated
    /// frames. A zero <paramref name="deltaTime"/> (paused) never accumulates and
    /// always simulates, so culling flips and kills stay live while the game is
    /// frozen.
    /// </summary>
    /// <param name="commandBuffer">The frame command buffer to record into.</param>
    /// <param name="deltaTime">The simulation time step in seconds.</param>
    public void RecordSimulation(GPUCommandBuffer commandBuffer, float deltaTime)
    {
        // The whole step runs under the gate: the per-frame draw plan and every
        // per-instance parameter write must be atomic against concurrent effect
        // creation/release from other threads.
        lock (_gate)
        {
            if (SimulationRateLimitEnabled && _hasValidPlan && deltaTime > 0f)
            {
                _simulationAccumulator += deltaTime;
                if (_simulationAccumulator < SimulationInterval)
                {
                    _pool.RecordMigration(commandBuffer);
                    TickRetiredWorkBuffers();
                    return;
                }
                // A fixed step per interval keeps the cadence even and the trajectory
                // independent of the frame rate; the remainder carries over, with any
                // backlog beyond one interval discarded — the hitch policy of
                // ParticleEmission.MaxDeltaTime (play through stalls slightly slower,
                // never fast-forward).
                deltaTime = Math.Min(SimulationInterval, ParticleEmission.MaxDeltaTime);
                _simulationAccumulator = Math.Min(_simulationAccumulator - deltaTime, SimulationInterval);
            }
            deltaTime = Math.Min(deltaTime, ParticleEmission.MaxDeltaTime);
            _pool.RecordMigration(commandBuffer);
            TickRetiredWorkBuffers();

            uint dirtyMin = uint.MaxValue;
            uint dirtyMax = 0;
            for (int i = 0; i < _instances.Count; i++)
            {
                _instances[i].AdvanceFrame(deltaTime, ref dirtyMin, ref dirtyMax);
            }
            if (dirtyMin <= dirtyMax)
            {
                _pool.Params.UpdateBufferRanged(dirtyMin, dirtyMax - dirtyMin + 1);
            }
            BuildDrawPlan();
            _hasValidPlan = true;

            _pool.DrainPendingKills(_kills);
            bool anyActive = AnyActiveGroups();
            if (_kills.Count == 0 && !anyActive)
            {
                return;
            }
            if (anyActive)
            {
                BuildWorkBlocks();
            }

            using (GPUCommandBuffer.ComputePass computePass = commandBuffer.BeginCompute())
            {
                for (int i = 0; i < _kills.Count; i++)
                {
                    (uint offset, uint count) = _kills[i];
                    _initMaterial.DispatchByGroupWithConstant(
                        computePass, (count + 63) / 64, 1, 1,
                        new GpuParticleInitConstant { SliceOffset = offset, Count = count });
                }

                if (!anyActive)
                {
                    return;
                }
                // One wide dispatch per behavior material instead of one per group:
                // each 64-thread block resolves its group through the work-block
                // table (the emit pass also resets the draw-args record, so every
                // active group contributes at least one emit block — even with zero
                // spawns). All emits record before all simulates: a group's spawn
                // still lands before its own step, cross-group slices are disjoint,
                // and the backend makes a dispatch's writes visible to the next.
                for (int i = 0; i < _emitDispatches.Count; i++)
                {
                    WorkDispatch dispatch = _emitDispatches[i];
                    dispatch.Material.DispatchByGroupWithConstant(
                        computePass, dispatch.Count, 1, 1,
                        new GpuParticleWorkConstant { WorkBlockBase = dispatch.First });
                }
                for (int i = 0; i < _simulateDispatches.Count; i++)
                {
                    WorkDispatch dispatch = _simulateDispatches[i];
                    dispatch.Material.DispatchByGroupWithConstant(
                        computePass, dispatch.Count, 1, 1,
                        new GpuParticleWorkConstant { WorkBlockBase = dispatch.First });
                }
            }
        }
    }

    /// <summary>
    /// Records the batched indirect draws of the frame's draw plan into the
    /// current pass: one multi-draw per material (or, without multi-draw support,
    /// one indexed-indirect draw per record — see
    /// <see cref="RenderPassScope.MultiDrawIndexedIndirect"/>). Call from the
    /// scene content node (<see cref="RGNode_SceneContent"/> or an
    /// <see cref="IRenderPassContent"/>).
    /// </summary>
    /// <param name="pass">The render pass scope of the scene pass.</param>
    public void Render(RenderPassScope pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (_camera == null)
        {
            throw new InvalidOperationException($"The {nameof(Camera)} of the particle system was not set.");
        }
        // Under the gate: the draw plan lists are rebuilt by RecordSimulation, and
        // the replay must not observe a half-rebuilt plan from a concurrent step.
        lock (_gate)
        {
            for (int i = 0; i < _drawBatches.Count; i++)
            {
                DrawBatch batch = _drawBatches[i];
                pass.MultiDrawIndexedIndirect(
                    QuadMesh,
                    batch.Material,
                    _pool.DrawArgs,
                    batch.First * 20,
                    batch.Count,
                    _pool.InstanceData);
            }
        }
    }

    /// <summary>
    /// Rebuilds the per-frame draw plan: the visible active groups bucketed by
    /// material (first-seen order), each assigned a drawIndex into the compacted
    /// draw-args array and a drawStart (the draw's firstInstance and base into
    /// the instance-data buffer, spaced by the group's slice capacity).
    /// </summary>
    private void BuildDrawPlan()
    {
        foreach (KeyValuePair<GraphicsMaterial, List<ParticleEffectInstance2D.GroupState>> bucket in _drawBuckets)
        {
            bucket.Value.Clear();
        }
        _drawMaterials.Clear();
        for (int i = 0; i < _instances.Count; i++)
        {
            ParticleEffectInstance2D instance = _instances[i];
            if (!instance.IsActive || !instance.IsVisible)
            {
                continue;
            }
            foreach (ParticleEffectInstance2D.GroupState group in instance.Groups)
            {
                if (!group.Active || !group.Visible)
                {
                    continue;
                }
                if (!_drawBuckets.TryGetValue(group.Material, out List<ParticleEffectInstance2D.GroupState>? bucket))
                {
                    bucket = [];
                    _drawBuckets[group.Material] = bucket;
                    // A brand-new material enters the first-seen order on its
                    // very first frame — otherwise the effect's debut frame
                    // neither dispatches nor draws.
                    _drawMaterials.Add(group.Material);
                }
                else if (bucket.Count == 0)
                {
                    // The bucket survives across frames; an empty one means this is
                    // the material's first group of the frame, so it (re)enters the
                    // first-seen order.
                    _drawMaterials.Add(group.Material);
                }
                bucket.Add(group);
            }
        }

        _drawGroups.Clear();
        _drawIndices.Clear();
        _drawStarts.Clear();
        _drawBatches.Clear();
        uint drawIndex = 0;
        uint drawStart = 0;
        for (int m = 0; m < _drawMaterials.Count; m++)
        {
            List<ParticleEffectInstance2D.GroupState> bucket = _drawBuckets[_drawMaterials[m]];
            uint first = drawIndex;
            for (int g = 0; g < bucket.Count; g++)
            {
                ParticleEffectInstance2D.GroupState group = bucket[g];
                _drawGroups.Add(group);
                _drawIndices.Add(drawIndex);
                _drawStarts.Add(drawStart);
                drawIndex++;
                drawStart += group.Slice.Capacity;
            }
            _drawBatches.Add(new DrawBatch(_drawMaterials[m], first, (uint)bucket.Count));
        }
    }

    /// <summary>
    /// Rebuilds the batched compute plan of the current draw plan: the groups
    /// re-bucketed by behavior material in first-seen order (a group's emit and
    /// simulate materials pair by behavior, but the two passes bucket separately),
    /// each bucket flattened into one work-block record per 64-thread block and
    /// uploaded to its GPU table, then recorded as one wide dispatch over the
    /// bucket's table run.
    /// </summary>
    private void BuildWorkBlocks()
    {
        _emitBlocks.Clear();
        _simulateBlocks.Clear();
        _emitDispatches.Clear();
        _simulateDispatches.Clear();
        AppendWorkBlocks(emit: true, _emitBlocks, _emitDispatches);
        AppendWorkBlocks(emit: false, _simulateBlocks, _simulateDispatches);
        UploadWorkBlocks(ref _emitBlockBuffer, _emitBlocks, emit: true, "particles2d_emit_work");
        UploadWorkBlocks(ref _simulateBlockBuffer, _simulateBlocks, emit: false, "particles2d_simulate_work");
    }

    private void AppendWorkBlocks(bool emit, List<GpuParticleWorkBlock> blocks, List<WorkDispatch> dispatches)
    {
        foreach (List<int> bucket in _workBuckets.Values)
        {
            bucket.Clear();
        }
        _workMaterials.Clear();
        for (int i = 0; i < _drawGroups.Count; i++)
        {
            // The behavior materials were resolved into the group state at
            // creation, so the frame loop takes no cache lookup.
            ComputeMaterial material = emit ? _drawGroups[i].EmitMaterial : _drawGroups[i].SimulateMaterial;
            if (!_workBuckets.TryGetValue(material, out List<int>? bucket))
            {
                bucket = [];
                _workBuckets[material] = bucket;
            }
            if (bucket.Count == 0)
            {
                // Buckets persist across frames; an empty one means this is the
                // material's first group of the frame — it (re)enters the
                // first-seen order.
                _workMaterials.Add(material);
            }
            bucket.Add(i);
        }
        for (int m = 0; m < _workMaterials.Count; m++)
        {
            List<int> bucket = _workBuckets[_workMaterials[m]];
            uint first = (uint)blocks.Count;
            for (int g = 0; g < bucket.Count; g++)
            {
                int groupIndex = bucket[g];
                ParticleEffectInstance2D.GroupState group = _drawGroups[groupIndex];
                uint threads = emit ? Math.Max(group.SpawnCount, 1u) : group.Slice.Capacity;
                uint blockCount = (threads + 63) / 64;
                for (uint b = 0; b < blockCount; b++)
                {
                    blocks.Add(new GpuParticleWorkBlock
                    {
                        EmitterSlot = group.Slot,
                        DrawIndex = _drawIndices[groupIndex],
                        DrawStart = _drawStarts[groupIndex],
                        ThreadBase = b * 64,
                    });
                }
            }
            dispatches.Add(new WorkDispatch(_workMaterials[m], first, (uint)blocks.Count - first));
        }
    }

    private void UploadWorkBlocks(
        ref GraphicsArrayBuffer<GpuParticleWorkBlock> buffer, List<GpuParticleWorkBlock> blocks, bool emit, string name)
    {
        if (blocks.Count > buffer.Length)
        {
            // Geometric growth; the table is per-frame transient, so the outgrown
            // buffer swaps without a copy and retires two frames later — the same
            // policy as the pool's instance-data buffer.
            int capacity = Math.Max(buffer.Length * 2, (int)BitOperations.RoundUpToPowerOf2((uint)blocks.Count));
            GraphicsArrayBuffer<GpuParticleWorkBlock> grown = _rendering.CreateGraphicsArrayBuffer<GpuParticleWorkBlock>(capacity, name);
            _retiredWorkBuffers.Add((buffer, 2));
            buffer = grown;
            foreach ((ComputeMaterial emitMaterial, ComputeMaterial simulateMaterial) in _behaviorMaterials.Values)
            {
                (emit ? emitMaterial : simulateMaterial).TrySetBuffer(ParticleShaderKeys.WorkBlocks, buffer);
            }
        }
        if (blocks.Count == 0)
        {
            return;
        }
        CollectionsMarshal.AsSpan(blocks).CopyTo(buffer.AsSpan());
        buffer.UpdateBufferRanged(0, (uint)blocks.Count);
    }

    private void TickRetiredWorkBuffers()
    {
        for (int i = _retiredWorkBuffers.Count - 1; i >= 0; i--)
        {
            (GraphicsArrayBuffer<GpuParticleWorkBlock> buffer, int framesLeft) = _retiredWorkBuffers[i];
            framesLeft--;
            if (framesLeft <= 0)
            {
                buffer.Dispose();
                _retiredWorkBuffers.RemoveAt(i);
            }
            else
            {
                _retiredWorkBuffers[i] = (buffer, framesLeft);
            }
        }
    }

    internal ref EmitterParams2D ParamsRef(uint slot) => ref _pool.Params.AsSpan()[(int)slot];

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
            {
                // An actively drawing instance leaves the plan: without a resimulation
                // its stale draw would replay for up to one simulation interval. A
                // deactivated instance (a finished one-shot) draws nothing, so it must
                // NOT force one — reaping one-shots is constant during play.
                _hasValidPlan = false;
            }
        }
    }

    private bool AnyActiveGroups()
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            if (_instances[i].IsActive)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The behavior's compute materials, double-checked: the compose runs off the
    /// gate (a first-use compile can take milliseconds and must not stall the frame
    /// thread), and a concurrent duplicate compile loses the race and is disposed.
    /// Called once per group at instance creation — on the frame thread for
    /// synchronous creations, on a worker thread for the async cold path — and the
    /// resolved materials are cached in the group state, so the frame simulation
    /// loop takes no per-group cache lookup or lock.
    /// </summary>
    private (ComputeMaterial Emit, ComputeMaterial Simulate) GetBehaviorMaterials(ShaderLibrary? behavior)
    {
        behavior ??= _defaultBehavior;
        lock (_gate)
        {
            if (_behaviorMaterials.TryGetValue(behavior, out (ComputeMaterial, ComputeMaterial) materials))
            {
                return materials;
            }
        }
        ComputeMaterial emit = _rendering.CreateComputeMaterial(_materialCompiler.ComposeCompute(_emitTemplate, behavior));
        ComputeMaterial simulate = _rendering.CreateComputeMaterial(_materialCompiler.ComposeCompute(_simulateTemplate, behavior));
        BindPool(emit, _emitBlockBuffer);
        BindPool(simulate, _simulateBlockBuffer);
        lock (_gate)
        {
            if (_behaviorMaterials.TryGetValue(behavior, out (ComputeMaterial, ComputeMaterial) existing))
            {
                emit.Dispose();
                simulate.Dispose();
                return existing;
            }
            _behaviorMaterials[behavior] = (emit, simulate);
            // Close the growth race: a work-table growth between the off-gate
            // BindPool above and this insert would leave the new materials bound
            // to a retired table (the growth sweep only covers cached materials).
            emit.TrySetBuffer(ParticleShaderKeys.WorkBlocks, _emitBlockBuffer);
            simulate.TrySetBuffer(ParticleShaderKeys.WorkBlocks, _simulateBlockBuffer);
            return (emit, simulate);
        }
    }

    /// <summary>
    /// The group's render material, double-checked like
    /// <see cref="GetBehaviorMaterials"/>: the compile and its over-life texture
    /// bakes run off the gate, so a worker-thread first-use compile never stalls
    /// the frame simulation; a concurrent duplicate loses the race and is disposed.
    /// </summary>
    private GraphicsMaterial GetOrCreateMaterial(ParticleGroup2DAsset group)
    {
        GraphicsValueBuffer<Matrix4x4>? camera;
        lock (_gate)
        {
            if (_materials.TryGetValue(group, out GraphicsMaterial? cached))
            {
                return cached;
            }
            camera = _camera;
        }
        // The group's material instance: the .amat compiles against the 2D
        // pass template (its surface shades the fragments and may adjust the
        // vertices, its textures and [MaterialParams] values bind), then the
        // group's own texture derives over the surface's "texture" slot.
        MaterialAsset asset = group.Material ?? _defaultAsset;
        GraphicsMaterial material = _materialCompiler.Compile(
            asset,
            _renderTemplate,
            (_, shader) => _rendering.CreateGraphicsMaterial(shader, $"particles2d:{group.Name}"));
        material.BlendState = group.Blend ?? BlendState.AlphaBlend;
        if (group.Depth is { } depth)
        {
            // Groups authoring world z in a custom render module (e.g. facade
            // sprites) depth-test (never write) the scene depth with "Read".
            material.DepthStencilState = depth;
        }
        if (group.Texture != null && !material.TrySetTexture(ShaderResourceId.Texture, group.Texture))
        {
            material.Dispose();
            throw new InvalidDataException(
                $"Particle group '{group.Name}' sets a texture, but the surface of material '{asset.Name}' " +
                $"declares no '{ShaderResourceId.Texture}' slot to override.");
        }
        List<Texture2D> overLifeTextures = [];
        BindOverLifeTextures(group, material, overLifeTextures);
        if (camera != null)
        {
            material.SetBuffer(ShaderResourceId.Camera, camera);
        }
        BindPool(material);
        lock (_gate)
        {
            if (_materials.TryGetValue(group, out GraphicsMaterial? existing))
            {
                foreach (Texture2D texture in overLifeTextures)
                {
                    texture.Dispose();
                }
                material.Dispose();
                return existing;
            }
            _overLifeTextures.AddRange(overLifeTextures);
            _materials[group] = material;
            // Publication and module application share one critical section, so
            // a concurrent AddMaterialModule either sweeps this material or is
            // already in the list this Apply reads — never a gap between them.
            _materialModules.Apply(material);
            return material;
        }
    }

    private void BindPool(ComputeMaterial material, GraphicsArrayBuffer<GpuParticleWorkBlock> workBlocks)
    {
        material.TrySetBuffer(ShaderResourceId.Particles, _pool.Particles);
        material.TrySetBuffer(ParticleShaderKeys.Emitters, _pool.Params);
        material.TrySetBuffer(ParticleShaderKeys.RenderList, _pool.RenderList);
        material.TrySetBuffer(ParticleShaderKeys.DrawArgs, _pool.DrawArgs);
        material.TrySetBuffer(ParticleShaderKeys.InstanceData, _pool.InstanceData);
        material.TrySetBuffer(ParticleShaderKeys.WorkBlocks, workBlocks);
    }

    // The group's over-life lookup textures: the authored gradient/curve bake
    // into a 256x1 row each (see ParticleOverLifeBake); groups without them bind
    // the shared 1x1 white texture (the identity sample) — the EmitterParams2D
    // flag bits gate the fetch, so no shader permutation is needed. Baked
    // textures are owned by the system (appended to _overLifeTextures once the
    // material wins the double-check) and disposed with it.
    private void BindOverLifeTextures(ParticleGroup2DAsset group, GraphicsMaterial material, List<Texture2D> created)
    {
        if (group.ColorGradient is { Count: > 0 } gradientKeys)
        {
            byte[] pixels = new byte[ParticleOverLifeBake.TextureWidth * 4];
            ParticleOverLifeBake.BakeGradient(gradientKeys, pixels);
            Texture2D texture = _rendering.CreateTexture2D(
                pixels, ParticleOverLifeBake.TextureWidth, 1,
                new ImageLoadOption(name: $"particles2d:{group.Name}:colorGradient"));
            created.Add(texture);
            material.SetTexture(ParticleShaderKeys.ColorGradient, texture);
        }
        else
        {
            material.SetTexture(ParticleShaderKeys.ColorGradient, _rendering.TextureWhite);
        }
        if (group.SizeCurve is { Count: > 0 } curveKeys)
        {
            Half[] texels = new Half[ParticleOverLifeBake.TextureWidth];
            ParticleOverLifeBake.BakeCurve(curveKeys, texels);
            Texture2D texture = _rendering.CreateTexture2D(
                MemoryMarshal.AsBytes<Half>(texels), ParticleOverLifeBake.TextureWidth, 1,
                new ImageLoadOption(format: PixelFormat.R16Float, name: $"particles2d:{group.Name}:sizeCurve"));
            created.Add(texture);
            material.SetTexture(ParticleShaderKeys.SizeCurve, texture);
        }
        else
        {
            material.SetTexture(ParticleShaderKeys.SizeCurve, _rendering.TextureWhite);
        }
    }

    private void BindPool(GraphicsMaterial material)
    {
        material.TrySetBuffer(ShaderResourceId.Particles, _pool.Particles);
        material.TrySetBuffer(ParticleShaderKeys.Emitters, _pool.Params);
    }

    private void OnPoolReallocated()
    {
        // Pool growth swaps the instance-data buffer without copying it (per-frame
        // transient), so the cached draw plan would draw garbage if replayed:
        // force the next RecordSimulation to resimulate and rebuild the plan.
        _hasValidPlan = false;
        foreach ((ComputeMaterial emit, ComputeMaterial simulate) in _behaviorMaterials.Values)
        {
            BindPool(emit, _emitBlockBuffer);
            BindPool(simulate, _simulateBlockBuffer);
        }
        _initMaterial.TrySetBuffer(ShaderResourceId.Particles, _pool.Particles);
        foreach (GraphicsMaterial material in _materials.Values)
        {
            BindPool(material);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }
        lock (_gate)
        {
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                _instances[i].Dispose();
            }
            foreach ((ComputeMaterial emit, ComputeMaterial simulate) in _behaviorMaterials.Values)
            {
                emit.Dispose();
                simulate.Dispose();
            }
            foreach (GraphicsMaterial material in _materials.Values)
            {
                material.Dispose();
            }
            foreach (Texture2D texture in _overLifeTextures)
            {
                texture.Dispose();
            }
            _materialCompiler.Dispose();
            _initMaterial.Dispose();
            _emitBlockBuffer.Dispose();
            _simulateBlockBuffer.Dispose();
            foreach ((GraphicsArrayBuffer<GpuParticleWorkBlock> retired, _) in _retiredWorkBuffers)
            {
                retired.Dispose();
            }
            _retiredWorkBuffers.Clear();
            _pool.Reallocated -= OnPoolReallocated;
            _pool.Dispose();
        }
    }
}

/// <summary>
/// One 64-thread block's work record of the batched emit/simulate dispatches
/// (16 bytes; GPU layout twin of GpuWorkBlock in AlcoParticles_Core2D.slang).
/// The CPU uploads one record per block of the wide per-material dispatches; a
/// block's shader thread index within its group's dispatch span is
/// ThreadBase + SV_GroupThreadID.x.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuParticleWorkBlock
{
    /// <summary>The emitter slot of the block's group.</summary>
    public uint EmitterSlot;

    /// <summary>The record's index in this frame's compacted draw-args array.</summary>
    public uint DrawIndex;

    /// <summary>The draw's firstInstance: its base into the instance-data buffer.</summary>
    public uint DrawStart;

    /// <summary>The block's first thread index within the group's dispatch span.</summary>
    public uint ThreadBase;
}

/// <summary>The push constant of a batched emit/simulate dispatch (16 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuParticleWorkConstant
{
    /// <summary>The dispatch bucket's first record in the work-block table.</summary>
    public uint WorkBlockBase;

    /// <summary>Reserved.</summary>
    public uint Pad0;

    /// <summary>Reserved.</summary>
    public uint Pad1;

    /// <summary>Reserved.</summary>
    public uint Pad2;
}

/// <summary>The push constant of the slice-init compute pass (16 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuParticleInitConstant
{
    /// <summary>The first slot of the slice to kill.</summary>
    public uint SliceOffset;

    /// <summary>The number of slots to kill.</summary>
    public uint Count;

    /// <summary>Reserved.</summary>
    public uint Pad0;

    /// <summary>Reserved.</summary>
    public uint Pad1;
}
