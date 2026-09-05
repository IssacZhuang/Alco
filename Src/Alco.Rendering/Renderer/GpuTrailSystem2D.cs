using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Renders any number of 2D trail ribbons GPU-resident: the CPU appends one point
/// record per emission into the trail's ring slice of the shared point buffer, and
/// the vertex stage handles validity, aging and quad expansion (module
/// AlcoRendering_Trails2D). CPU work covers newly emitted points and bounded
/// trail-slot scans for lifecycle, uploads and material batching; live points
/// require no per-frame CPU simulation.
/// <br/>Materials: the trail's <see cref="TrailEffect2D.Material"/> asset composes
/// with the trail pass template (<see cref="RenderModule"/>, GpuTrail2D) through a
/// <see cref="MaterialCompiler"/> the system owns — the asset's surface module
/// (implementing ITrailSurface, module AlcoRendering_TrailSurface) shapes the look,
/// the system compiles, caches per (asset, blend, depth), applies the pass state
/// (premultiplied blend, depth-read, cull-none) and binds the shared buffers (the
/// camera block and the trailPoints/trailParams/trailGlobals resources the template
/// declares) — the same template-plus-surface design as the particle system's group
/// materials. A null material selects the engine's default surface.
/// <br/>Capacity model: one shared point buffer (sliced per trail in powers of two,
/// clamped to 32..1024 points) plus one parameter record per trail slot, both fixed
/// at construction — <see cref="TryCreateInstance"/> returns false when either is
/// exhausted instead of growing, so the worst-case GPU cost is bounded by the budget.
/// <br/>Draw batching: per frame (in <see cref="Render"/>) the live visible trails
/// are bucketed by material, one <see cref="IndexedIndirectData"/> record per trail
/// (firstInstance = trail slot, vertexOffset = sliceOffset * 4) is written
/// contiguously per material, and every material draws with a single
/// <see cref="RenderPassScope.MultiDrawIndexedIndirect(in Mesh, in GraphicsMaterial, GraphicsBuffer, uint, uint, in int)"/>
/// — draw calls scale with the material count, not the trail count. Dead trails
/// drop out of the plan entirely; dead segments collapse in the vertex stage.
/// <br/>Threading: main-thread contract — creation, emission, <see cref="Update"/>
/// and <see cref="Render"/> are all driven by the owning map service on the main
/// thread, like <see cref="DynamicMeshRenderer"/>.
/// </summary>
public sealed class GpuTrailSystem2D : AutoDisposable
{
    /// <summary>The module name of the built-in 2D trail render pass template.</summary>
    public const string RenderModule = "GpuTrail2D";

    /// <summary>
    /// The module name of the default trail surface (the color gradient with a soft
    /// across-ribbon edge), composed with the render pass template for trails whose
    /// material names no surface module of its own.
    /// </summary>
    public const string DefaultSurface = "TrailSurfaceDefault";

    /// <summary>One trail point record (GPU twin of TrailPoint2D in AlcoRendering_Trails2D, 32 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct TrailPoint2D
    {
        /// <summary>xy: world position, z: render depth.</summary>
        public Vector3 PosDepth;

        /// <summary>The renderer clock value at emission (seconds).</summary>
        public float EmitTime;

        /// <summary>The ribbon normal at the point.</summary>
        public Vector2 Normal;

        /// <summary>The arc length in world units.</summary>
        public float U;

#pragma warning disable CS0169 // never assigned to — GPU layout padding
        private readonly float _Pad;
#pragma warning restore CS0169
    }

    /// <summary>The per-trail parameter record (GPU twin of TrailParams2D in AlcoRendering_Trails2D, 96 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct TrailParams2D
    {
        /// <summary>x: life (s), y: width0, z: width1, w: opacity.</summary>
        public Vector4 Envelope;

        /// <summary>The ribbon color of a freshly emitted point.</summary>
        public Vector4 Color0;

        /// <summary>The ribbon color of a fully aged point.</summary>
        public Vector4 Color1;

        /// <summary>The material-defined custom data of <see cref="TrailEffect2D.UserData"/>.</summary>
        public Vector4 UserData;

        /// <summary>The slice's absolute first point slot in the shared buffer.</summary>
        public uint SliceOffset;

        /// <summary>The slice's capacity in points (power of two).</summary>
        public uint Capacity;

        /// <summary>The ring write cursor (the next slice-local slot to write).</summary>
        public uint Cursor;

        /// <summary>The total written point count, monotonic (the live window is min(Written, Capacity)).</summary>
        public uint Written;

        /// <summary>x: random seed, y: fadeIn (life fraction), z: fadeOut (life fraction).</summary>
        public Vector4 Misc;
    }

    /// <summary>The renderer's per-frame globals (the trailGlobals uniform block).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct TrailGlobals2D
    {
        /// <summary>The renderer clock in seconds; point age is time - emitTime.</summary>
        public float Time;
    }

    /// <summary>The per-slot CPU state of one trail: lifecycle, ring bookkeeping and the emission cursor.</summary>
    private struct TrailState
    {
        public TrailEffectInstance2D? Instance;
        public GraphicsMaterial? Material;

        /// <summary>Slot generation: bumped on recycle so a stale instance object turns inert.</summary>
        public uint Generation;

        public uint SliceOffset;
        public uint Capacity;
        public uint Cursor;
        public uint Written;
        public float Distance;
        public Vector2 LastEmitPosition;
        public Vector2 LastDirection;
        public float Spacing;
        public float Life;

        /// <summary>The clock value of the newest point (the fade-out deadline once emission stopped).</summary>
        public float LastPointTime;

        public bool Emitting;
        public bool Visible;

        public bool PointsDirty;
        public uint PointDirtyMin;
        public uint PointDirtyMax;
    }

    /// <summary>One material-homogeneous run of the per-frame draw plan.</summary>
    private readonly record struct DrawBatch(GraphicsMaterial Material, uint First, uint Count);

    /// <summary>A contiguous free range in the shared point buffer.</summary>
    private readonly record struct PointRange(uint Offset, uint Count);

    // The graphics state structs expose object-based equality. Compare their
    // values directly so cache hits do not box them on the creation path.
    private sealed class MaterialKeyComparer : IEqualityComparer<(MaterialAsset Asset, BlendState Blend, DepthStencilState Depth)>
    {
        /// <inheritdoc />
        public bool Equals(
            (MaterialAsset Asset, BlendState Blend, DepthStencilState Depth) x,
            (MaterialAsset Asset, BlendState Blend, DepthStencilState Depth) y)
        {
            return ReferenceEquals(x.Asset, y.Asset) && x.Blend == y.Blend && x.Depth == y.Depth
                && x.Depth.StencilReadMask == y.Depth.StencilReadMask
                && x.Depth.StencilWriteMask == y.Depth.StencilWriteMask;
        }

        /// <inheritdoc />
        public int GetHashCode((MaterialAsset Asset, BlendState Blend, DepthStencilState Depth) key)
        {
            return HashCode.Combine(key.Asset, key.Blend, key.Depth);
        }
    }

    /// <summary>The smallest trail slice, in points.</summary>
    private const uint MinSlicePoints = 32;

    /// <summary>The largest trail slice, in points (sizes the static index pattern).</summary>
    private const uint MaxSlicePoints = 1024;

    /// <summary>The indirect record stride in bytes (see <see cref="IndexedIndirectData"/>).</summary>
    private const uint DrawArgsStride = 20;

    private readonly RenderingSystem _rendering;
    private readonly MaterialCompiler _materialCompiler;
    private readonly ShaderLibrary _renderTemplate;
    private readonly GraphicsArrayBuffer<TrailPoint2D> _points;
    private readonly GraphicsArrayBuffer<TrailParams2D> _params;
    private readonly GraphicsValueBuffer<TrailGlobals2D> _globals;
    private readonly GraphicsBuffer _drawArgs;
    private readonly IndexedIndirectData[] _drawArgsData;
    private readonly PrimitiveMesh _indexMesh;
    private readonly TrailState[] _states;
    private readonly Stack<int> _freeSlots;
    // Sorted by offset; adjacent ranges merge on release. There can be at most
    // one more free range than live trails, so the list never needs to grow.
    private readonly List<PointRange> _freeSlices;
    // The compiled materials of the (asset, blend, depth) pairs the trails named so
    // far: one compile per pair, shared by every trail of the pair.
    private readonly Dictionary<(MaterialAsset Asset, BlendState Blend, DepthStencilState Depth), GraphicsMaterial> _materials = new(new MaterialKeyComparer());
    private readonly MaterialAsset _defaultAsset = new() { Name = "trails2d-default" };
    private GraphicsValueBuffer<Matrix4x4>? _camera;
    // The per-frame draw plan (rebuilt in Render): trails bucketed by material in
    // first-seen order, flattened into the draw-args records plus one batch per material.
    private readonly Dictionary<GraphicsMaterial, List<int>> _drawBuckets = [];
    private readonly List<GraphicsMaterial> _drawMaterials = [];
    private readonly List<DrawBatch> _drawBatches = [];
    private float _time;
    private uint _paramsDirtyMin = uint.MaxValue;
    private uint _paramsDirtyMax;

    /// <summary>
    /// Creates the system with its fixed budgets and the trail pass template. Both
    /// buffers are preallocated and never grow, so the steady state performs no
    /// per-frame GC allocations and the worst-case GPU cost is bounded by the budgets.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="pointCapacity">The shared point buffer size in points (all trails combined).</param>
    /// <param name="trailSlots">The maximum number of simultaneously-live trails.</param>
    /// <param name="renderModule">
    /// The pass-template module trail surfaces compose with; null uses the built-in
    /// <see cref="RenderModule"/>. A custom template must keep the built-in pass's
    /// vertex stage and resource contract.
    /// </param>
    public GpuTrailSystem2D(RenderingSystem rendering, int pointCapacity = 65536, int trailSlots = 1024, string? renderModule = null)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pointCapacity, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(trailSlots, 0);

        _rendering = rendering;
        ShaderSystem shaderSystem = rendering.ShaderSystem;
        _materialCompiler = new MaterialCompiler(rendering, shaderSystem.GetLibrary(DefaultSurface));
        _renderTemplate = shaderSystem.GetLibrary(renderModule ?? RenderModule);
        _points = rendering.CreateGraphicsArrayBuffer<TrailPoint2D>(pointCapacity, "trails2d_points");
        _params = rendering.CreateGraphicsArrayBuffer<TrailParams2D>(trailSlots, "trails2d_params");
        _globals = rendering.CreateGraphicsValueBuffer(new TrailGlobals2D(), "trails2d_globals");
        _drawArgs = rendering.CreateGraphicsBuffer((uint)(trailSlots * DrawArgsStride), "trails2d_draw_args");
        _drawArgsData = new IndexedIndirectData[trailSlots];
        _states = new TrailState[trailSlots];
        _freeSlots = new Stack<int>(trailSlots);
        _freeSlices = new List<PointRange>(Math.Min(trailSlots, pointCapacity / (int)MinSlicePoints) + 1)
        {
            new(0, (uint)pointCapacity),
        };
        for (int i = trailSlots - 1; i >= 0; i--)
        {
            _freeSlots.Push(i);
        }

        // The static index pattern every trail draws with: quad-strip segments of 4
        // vertices and 6 indices, segment k covering vertices 4k..4k+3. A trail's
        // vertexOffset identifies its slice; the shader maps logical segment k
        // to consecutive ring points starting at the oldest written point.
        var indices = new uint[(MaxSlicePoints - 1) * 6];
        for (uint k = 0; k < MaxSlicePoints - 1; k++)
        {
            uint v = k * 4;
            uint i = k * 6;
            indices[i] = v;
            indices[i + 1] = v + 1;
            indices[i + 2] = v + 2;
            indices[i + 3] = v + 1;
            indices[i + 4] = v + 3;
            indices[i + 5] = v + 2;
        }
        // The vertex stage fetches by builtin ids only, so the vertex buffer is a
        // never-read placeholder the mesh contract requires.
        _indexMesh = rendering.CreatePrimitiveMesh<uint>([0u], indices, "trails2d_index_mesh");
    }

    /// <summary>
    /// The 2D camera the trails render with; bound to every compiled material.
    /// Accepts any view-projection matrix buffer (a <see cref="Camera2DBuffer"/>,
    /// or the shared <see cref="RenderingSystem.MainCameraViewProjectionBuffer"/> to
    /// track whatever camera is currently rendering). The compile path's factory
    /// already binds the main camera buffer, so this only needs setting to override
    /// it — materials compiled afterwards bind it at creation.
    /// </summary>
    public GraphicsValueBuffer<Matrix4x4>? Camera
    {
        get => _camera;
        set
        {
            _camera = value;
            if (value != null)
            {
                foreach (GraphicsMaterial material in _materials.Values)
                {
                    material.SetBuffer(ShaderResourceId.Camera, value);
                }
            }
        }
    }

    /// <summary>
    /// Creates a trail that starts emitting immediately, anchored at
    /// <paramref name="position"/> (the first <see cref="TrailEffectInstance2D.ExtendTo"/>
    /// walks from the anchor, so emission covers the anchor even when the head has
    /// already moved). Threading: main thread.
    /// </summary>
    /// <param name="effect">The trail description (snapshotted at creation).</param>
    /// <param name="position">The world position of the trail head at creation.</param>
    /// <param name="instance">The new instance, when creation succeeded.</param>
    /// <returns>False when the trail-slot or point budget is exhausted; the caller decides the eviction policy.</returns>
    public bool TryCreateInstance(TrailEffect2D effect, Vector2 position, out TrailEffectInstance2D instance)
    {
        instance = null!;
        ArgumentNullException.ThrowIfNull(effect);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(effect.Life, 0f);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(effect.Spacing, 0f);

        // Compile before allocating: a material that fails to compose must not leak
        // the slot or slice (the compile throws with no allocation to roll back).
        GraphicsMaterial material = GetOrCreateMaterial(effect);

        if (_freeSlots.Count == 0 || !TryAllocateSlice(effect.ExpectedPoints, out uint sliceOffset, out uint sliceCapacity))
        {
            return false;
        }
        int slot = _freeSlots.Pop();

        ref TrailState state = ref _states[slot];
        state.Material = material;
        state.SliceOffset = sliceOffset;
        state.Capacity = sliceCapacity;
        state.Cursor = 0;
        state.Written = 0;
        state.Distance = 0f;
        state.LastEmitPosition = position;
        state.LastDirection = Vector2.Zero;
        state.Spacing = effect.Spacing;
        state.Life = effect.Life;
        state.LastPointTime = _time;
        state.Emitting = true;
        state.Visible = true;
        state.PointsDirty = false;

        _params[slot] = new TrailParams2D
        {
            Envelope = new Vector4(effect.Life, effect.Width0, effect.Width1, effect.Opacity),
            Color0 = effect.Color0.value,
            Color1 = effect.Color1.value,
            UserData = effect.UserData,
            SliceOffset = sliceOffset,
            Capacity = sliceCapacity,
            Cursor = 0,
            Written = 0,
            Misc = new Vector4(Random.Shared.NextSingle() * 100f, effect.FadeIn, effect.FadeOut, 0f),
        };
        MarkParamsDirty(slot);

        var trail = new TrailEffectInstance2D(this, slot, state.Generation);
        state.Instance = trail;
        instance = trail;
        return true;
    }

    /// <summary>
    /// Advances the renderer clock and recycles the trails whose points have all
    /// outlived their age budget after emission stopped. Point uploads and the draw
    /// plan are not flushed here but in <see cref="Render"/>, so emissions between
    /// the two (the game's update phase) land in the same frame.
    /// </summary>
    /// <param name="delta">The elapsed time in seconds since the last update.</param>
    public void Update(float delta)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _time += delta;
        _globals.UpdateBuffer(new TrailGlobals2D { Time = _time });
        for (int slot = 0; slot < _states.Length; slot++)
        {
            ref TrailState state = ref _states[slot];
            if (state.Instance == null || state.Emitting || _time - state.LastPointTime <= state.Life)
            {
                continue;
            }
            RecycleSlot(slot, ref state);
        }
    }

    /// <summary>
    /// Flushes the dirty point/parameter ranges, rebuilds the draw plan and records
    /// the batched indirect draws into the current pass: one multi-draw per material.
    /// Call from the scene content (e.g. a map service's transparent-layer hook), the
    /// same entry shape as <see cref="GpuParticleSystem2D.Render"/> — not available
    /// while recording a render bundle (multi-draw indirect).
    /// </summary>
    /// <param name="pass">The render pass scope of the scene pass.</param>
    public void Render(RenderPassScope pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        FlushUploads();
        BuildDrawPlan();
        if (_drawBatches.Count == 0)
        {
            return;
        }
        int drawArgCount = 0;
        foreach (DrawBatch batch in _drawBatches)
        {
            drawArgCount += (int)batch.Count;
        }
        _drawArgs.UpdateBuffer(_drawArgsData.AsSpan(0, drawArgCount), 0);
        foreach (DrawBatch batch in _drawBatches)
        {
            pass.MultiDrawIndexedIndirect(_indexMesh, batch.Material, _drawArgs, batch.First * DrawArgsStride, batch.Count);
        }
    }

    /// <summary>Whether the trail of the given slot identity still emits.</summary>
    internal bool GetEmitting(int slot, uint generation)
    {
        return !IsDisposed && IsValid(slot, generation) && _states[slot].Emitting;
    }

    /// <summary>Whether the trail of the given slot identity still emits or has live points.</summary>
    internal bool GetAlive(int slot, uint generation)
    {
        if (IsDisposed || !IsValid(slot, generation))
        {
            return false;
        }
        ref TrailState state = ref _states[slot];
        return state.Emitting || _time - state.LastPointTime <= state.Life;
    }

    /// <summary>Whether the trail of the given slot identity draws.</summary>
    internal bool GetVisible(int slot, uint generation)
    {
        return !IsDisposed && IsValid(slot, generation) && _states[slot].Visible;
    }

    /// <summary>Shows or hides the trail of the given slot identity (a culling hook).</summary>
    internal void SetVisible(int slot, uint generation, bool visible)
    {
        if (IsDisposed || !IsValid(slot, generation))
        {
            return;
        }
        _states[slot].Visible = visible;
    }

    /// <summary>The <see cref="TrailEffectInstance2D.ExtendTo"/> implementation (slot identity already resolved).</summary>
    internal void ExtendTo(int slot, uint generation, Vector2 position, float depth)
    {
        if (IsDisposed || !IsValid(slot, generation))
        {
            return;
        }
        ref TrailState state = ref _states[slot];
        if (!state.Emitting)
        {
            return;
        }
        Vector2 segment = position - state.LastEmitPosition;
        float length = segment.Length();
        if (length < state.Spacing)
        {
            return;
        }
        Vector2 direction = segment / length;
        state.LastDirection = direction;
        Vector2 normal = new(-direction.Y, direction.X);
        Vector2 cursor = state.LastEmitPosition;
        float remaining = length;
        while (remaining >= state.Spacing)
        {
            cursor += direction * state.Spacing;
            remaining -= state.Spacing;
            state.Distance += state.Spacing;
            WritePoint(ref state, slot, cursor, normal, state.Distance, depth);
        }
        state.LastEmitPosition = cursor;
        SyncParams(slot, ref state);
    }

    /// <summary>The <see cref="TrailEffectInstance2D.Finish"/> implementation: the final segment lands only when it is ahead of the emitted path.</summary>
    internal void Finish(int slot, uint generation, Vector2 position, float depth)
    {
        if (IsDisposed || !IsValid(slot, generation))
        {
            return;
        }
        ref TrailState state = ref _states[slot];
        Vector2 segment = position - state.LastEmitPosition;
        float length = segment.Length();
        if (length >= 0.001f)
        {
            Vector2 direction = segment / length;
            if (Vector2.Dot(direction, state.LastDirection) > 0f)
            {
                Vector2 normal = new(-direction.Y, direction.X);
                Vector2 cursor = state.LastEmitPosition;
                float remaining = length;
                while (remaining >= state.Spacing)
                {
                    cursor += direction * state.Spacing;
                    remaining -= state.Spacing;
                    state.Distance += state.Spacing;
                    WritePoint(ref state, slot, cursor, normal, state.Distance, depth);
                }
                if (remaining > 0.001f)
                {
                    state.Distance += remaining;
                    WritePoint(ref state, slot, position, normal, state.Distance, depth);
                }
                state.LastEmitPosition = position;
                state.LastDirection = direction;
                SyncParams(slot, ref state);
            }
        }
        StopCore(ref state);
    }

    /// <summary>The <see cref="TrailEffectInstance2D.Stop"/> implementation.</summary>
    internal void Stop(int slot, uint generation)
    {
        if (IsDisposed || !IsValid(slot, generation))
        {
            return;
        }
        StopCore(ref _states[slot]);
    }

    /// <summary>
    /// The immediate-teardown path of <see cref="TrailEffectInstance2D.Dispose"/>: the slot
    /// and slice return to the pool now instead of after the fade-out.
    /// </summary>
    internal void ReleaseTrail(int slot, uint generation)
    {
        if (IsDisposed || !IsValid(slot, generation))
        {
            return;
        }
        RecycleSlot(slot, ref _states[slot]);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _points.Dispose();
            _params.Dispose();
            _globals.Dispose();
            _drawArgs.Dispose();
            _indexMesh.Dispose();
            foreach (GraphicsMaterial material in _materials.Values)
            {
                material.Dispose();
            }
            _materialCompiler.Dispose();
            // Live instances turn inert: their generation check fails against the
            // cleared states, and every renderer entry short-circuits on IsDisposed.
            for (int slot = 0; slot < _states.Length; slot++)
            {
                _states[slot].Instance = null;
                _states[slot].Material = null;
            }
        }
    }

    private bool IsValid(int slot, uint generation)
    {
        return _states[slot].Instance != null && _states[slot].Generation == generation;
    }

    // The effect's render material: one compile per (asset, blend, depth) pair —
    // the asset's surface composes with the trail pass template, the pair's state
    // overrides (or the pass defaults) apply, and the shared trail resources bind.
    // The compiler owns the composed shader cache; this system owns the materials.
    private GraphicsMaterial GetOrCreateMaterial(TrailEffect2D effect)
    {
        MaterialAsset asset = effect.Material ?? _defaultAsset;
        BlendState blend = effect.Blend ?? BlendState.PremultipliedAlpha;
        DepthStencilState depth = effect.Depth ?? DepthStencilState.Read;
        var key = (asset, blend, depth);
        if (_materials.TryGetValue(key, out GraphicsMaterial? cached))
        {
            return cached;
        }
        GraphicsMaterial material = _materialCompiler.Compile(
            asset,
            _renderTemplate,
            (materialAsset, shader) => _rendering.CreateGraphicsMaterial(shader, $"trails2d:{materialAsset.Name}"));
        material.BlendState = blend;
        material.DepthStencilState = depth;
        // Ribbon triangles are not consistently wound after surface displacement
        // (turbulence wander), so the pass never culls.
        material.RasterizerState = RasterizerState.CullNone;
        material.SetBuffer(ShaderResourceId.TrailPoints, _points);
        material.SetBuffer(ShaderResourceId.TrailParams, _params);
        material.SetBuffer(ShaderResourceId.TrailGlobals, _globals);
        if (_camera != null)
        {
            material.SetBuffer(ShaderResourceId.Camera, _camera);
        }
        _materials[key] = material;
        return material;
    }

    private void WritePoint(ref TrailState state, int slot, Vector2 position, Vector2 normal, float u, float depth)
    {
        uint local = state.Cursor;
        _points[(int)(state.SliceOffset + local)] = new TrailPoint2D
        {
            PosDepth = new Vector3(position, depth),
            EmitTime = _time,
            Normal = normal,
            U = u,
        };
        state.Cursor = (state.Cursor + 1) % state.Capacity;
        state.Written++;
        state.LastPointTime = _time;
        if (state.PointsDirty)
        {
            state.PointDirtyMin = Math.Min(state.PointDirtyMin, local);
            state.PointDirtyMax = Math.Max(state.PointDirtyMax, local);
        }
        else
        {
            state.PointsDirty = true;
            state.PointDirtyMin = local;
            state.PointDirtyMax = local;
        }
    }

    private void SyncParams(int slot, ref TrailState state)
    {
        TrailParams2D parameters = _params[slot];
        parameters.Cursor = state.Cursor;
        parameters.Written = state.Written;
        _params[slot] = parameters;
        MarkParamsDirty(slot);
    }

    private void MarkParamsDirty(int slot)
    {
        _paramsDirtyMin = Math.Min(_paramsDirtyMin, (uint)slot);
        _paramsDirtyMax = Math.Max(_paramsDirtyMax, (uint)slot);
    }

    private static void StopCore(ref TrailState state)
    {
        state.Emitting = false;
    }

    private void RecycleSlot(int slot, ref TrailState state)
    {
        state.Instance = null;
        state.Material = null;
        state.PointsDirty = false;
        state.Generation++;
        FreeSlice(state.SliceOffset, state.Capacity);
        _freeSlots.Push(slot);
    }

    private bool TryAllocateSlice(int expectedPoints, out uint offset, out uint capacity)
    {
        uint rounded = BitOperations.RoundUpToPowerOf2((uint)Math.Clamp(expectedPoints, (int)MinSlicePoints, (int)MaxSlicePoints));
        for (int i = 0; i < _freeSlices.Count; i++)
        {
            PointRange range = _freeSlices[i];
            if (range.Count < rounded)
            {
                continue;
            }
            offset = range.Offset;
            capacity = rounded;
            if (range.Count == rounded)
            {
                _freeSlices.RemoveAt(i);
            }
            else
            {
                _freeSlices[i] = new PointRange(range.Offset + rounded, range.Count - rounded);
            }
            return true;
        }
        offset = 0;
        capacity = 0;
        return false;
    }

    private void FreeSlice(uint offset, uint capacity)
    {
        int index = 0;
        while (index < _freeSlices.Count && _freeSlices[index].Offset < offset)
        {
            index++;
        }
        if (index > 0 && _freeSlices[index - 1].Offset + _freeSlices[index - 1].Count == offset)
        {
            PointRange previous = _freeSlices[--index];
            offset = previous.Offset;
            capacity += previous.Count;
            _freeSlices.RemoveAt(index);
        }
        if (index < _freeSlices.Count && offset + capacity == _freeSlices[index].Offset)
        {
            capacity += _freeSlices[index].Count;
            _freeSlices.RemoveAt(index);
        }
        _freeSlices.Insert(index, new PointRange(offset, capacity));
    }

    private void FlushUploads()
    {
        if (_paramsDirtyMin <= _paramsDirtyMax)
        {
            _params.UpdateBufferRanged(_paramsDirtyMin, _paramsDirtyMax - _paramsDirtyMin + 1);
            _paramsDirtyMin = uint.MaxValue;
            _paramsDirtyMax = 0;
        }
        for (int slot = 0; slot < _states.Length; slot++)
        {
            ref TrailState state = ref _states[slot];
            if (!state.PointsDirty)
            {
                continue;
            }
            state.PointsDirty = false;
            _points.UpdateBufferRanged(state.SliceOffset + state.PointDirtyMin, state.PointDirtyMax - state.PointDirtyMin + 1);
        }
    }

    private void BuildDrawPlan()
    {
        foreach (KeyValuePair<GraphicsMaterial, List<int>> bucket in _drawBuckets)
        {
            bucket.Value.Clear();
        }
        _drawMaterials.Clear();
        _drawBatches.Clear();
        for (int slot = 0; slot < _states.Length; slot++)
        {
            ref TrailState state = ref _states[slot];
            if (state.Instance == null || !state.Visible)
            {
                continue;
            }
            uint window = Math.Min(state.Written, state.Capacity);
            if (window < 2)
            {
                continue;
            }
            if (!_drawBuckets.TryGetValue(state.Material!, out List<int>? bucket))
            {
                bucket = [];
                _drawBuckets[state.Material!] = bucket;
            }
            // Buckets persist across frames (cleared above, never removed), so the
            // first add into an empty bucket is this frame's first sight of the
            // material — gating on bucket creation instead would only ever list the
            // material on the very first plan and drop every later frame's draws.
            if (bucket.Count == 0)
            {
                _drawMaterials.Add(state.Material!);
            }
            bucket.Add(slot);
        }
        int drawArgCount = 0;
        foreach (GraphicsMaterial material in _drawMaterials)
        {
            List<int> bucket = _drawBuckets[material];
            uint first = (uint)drawArgCount;
            foreach (int slot in bucket)
            {
                ref TrailState state = ref _states[slot];
                uint window = Math.Min(state.Written, state.Capacity);
                _drawArgsData[drawArgCount++] = new IndexedIndirectData(
                    (window - 1) * 6, 1, 0, state.SliceOffset * 4, (uint)slot);
            }
            _drawBatches.Add(new DrawBatch(material, first, (uint)(drawArgCount - first)));
        }
    }
}
