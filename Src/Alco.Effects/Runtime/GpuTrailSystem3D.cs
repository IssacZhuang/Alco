using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Effects;

/// <summary>
/// Renders camera-facing 3D trail ribbons GPU-resident using segment instancing and vertex pulling over <see cref="Alco.Graphics"/>.
/// Callers drive point emission, <see cref="Update"/>, <see cref="PrepareRender"/>, and <see cref="Render"/> on the main thread.
/// </summary>
public sealed class GpuTrailSystem3D : AutoDisposable
{
    /// <summary>The module name of the built-in 3D trail render pass template.</summary>
    public const string RenderModule = "GpuTrail3D";

    /// <summary>The module name of the default 3D trail surface.</summary>
    public const string DefaultSurface = "TrailSurfaceDefault";

    /// <summary>One trail point record (GPU twin of TrailPoint3D in AlcoRendering_Trails3D, 32 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct TrailPoint3D
    {
        public Vector3 Position;
        public float EmitTime;
        public Vector3 Tangent;
        public float U;
    }

    /// <summary>The per-trail parameter record (GPU twin of TrailParams3D in AlcoRendering_Trails3D, 96 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct TrailParams3D
    {
        public Vector4 Envelope;
        public Vector4 Color0;
        public Vector4 Color1;
        public Vector4 UserData;
        public uint SliceOffset;
        public uint Capacity;
        public uint Cursor;
        public uint Written;
        public Vector4 Misc;
    }

    /// <summary>One segment instance record (GPU twin of TrailSegment in AlcoRendering_Trails2D, 8 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TrailSegment
    {
        public uint TrailSlot;
        public uint Segment;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TrailPassData3D
    {
        public Matrix4x4 ViewProjection;
        public GPUBufferView TrailPoints;
        public GPUBufferView TrailParams;
        public GPUBufferView Segments;
        public Vector3 CameraPosition;
        public float Time;
    }

    private struct TrailState
    {
        public TrailEffectInstance3D? Instance;
        public CachedMaterial? CachedMat;
        public DepthStencilState? DepthOverride;
        public uint Generation;

        public uint SliceOffset;
        public uint Capacity;
        public uint Cursor;
        public uint Written;
        public float Distance;
        public Vector3 LastEmitPosition;
        public Vector3 LastDirection;
        public float Spacing;
        public float Life;
        public float LastPointTime;

        public bool Emitting;
        public bool Visible;

        public bool PointsDirty;
        public uint PointDirtyMin;
        public uint PointDirtyMax;
    }

    private sealed record CachedMaterial(Material Material, int PassRootOffset);
    private readonly record struct DrawBatch(Material Material, int PassRootOffset, DepthStencilState DepthStencil, uint FirstSegment, uint SegmentCount);
    private readonly record struct PointRange(uint Offset, uint Count);

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

    private const uint MinSlicePoints = 32;
    private const uint MaxSlicePoints = 1024;

    private readonly RenderingSystem _rendering;
    private readonly ShaderSystem _shaders;
    private readonly string _renderTemplateModule;
    private readonly Alco.Graphics.Buffer _gpuPoints;
    private readonly Alco.Graphics.Buffer _gpuParams;
    private readonly TrailPoint3D[] _cpuPoints;
    private readonly TrailParams3D[] _cpuParams;
    private readonly TransientBufferPool _stagingPool;
    private readonly TransientBufferPool _segmentPool;
    private readonly TrailState[] _states;
    private readonly Stack<int> _freeSlots;
    private readonly List<PointRange> _freeSlices;
    private readonly Dictionary<(MaterialAsset Asset, BlendState Blend), CachedMaterial> _materials = new(new MaterialKeyComparer());
    private readonly MaterialAsset _defaultAsset = new() { Name = "trails3d-default" };

    private readonly Dictionary<(Material Material, DepthStencilState Depth), List<int>> _drawBuckets = new(new BatchKeyComparer());
    private readonly List<(Material Material, DepthStencilState Depth)> _drawBucketKeys = [];
    private readonly List<DrawBatch> _drawBatches = [];
    private readonly List<TrailSegment> _segments = [];
    private Alco.Graphics.Buffer? _activeSegmentBuffer;
    private ulong _preparedFrameId;
    private DepthStencilState _depthStencilState = DepthStencilState.ReadReverseZ;
    private float _time;
    private uint _paramsDirtyMin = uint.MaxValue;
    private uint _paramsDirtyMax;

    /// <summary>Creates a 3D trail system using <paramref name="rendering"/>'s default <see cref="RenderingSystem.Shaders"/>.</summary>
    public GpuTrailSystem3D(
        RenderingSystem rendering,
        int pointCapacity = 65536,
        int trailSlots = 1024,
        string? renderTemplate = null)
        : this(rendering, (rendering ?? throw new ArgumentNullException(nameof(rendering))).Shaders, pointCapacity, trailSlots, renderTemplate)
    {
    }

    /// <summary>Creates a 3D trail system using an explicit <see cref="ShaderSystem"/> for custom surfaces.</summary>
    public unsafe GpuTrailSystem3D(
        RenderingSystem rendering,
        ShaderSystem shaders,
        int pointCapacity = 65536,
        int trailSlots = 1024,
        string? renderTemplate = null)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pointCapacity, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(trailSlots, 0);

        _rendering = rendering;
        _shaders = shaders;
        _renderTemplateModule = string.IsNullOrWhiteSpace(renderTemplate) ? RenderModule : renderTemplate.Trim();

        AlcoEffectsShaders.EnsureBuiltInModulesLoaded(shaders);

        _cpuPoints = new TrailPoint3D[pointCapacity];
        _cpuParams = new TrailParams3D[trailSlots];
        _gpuPoints = rendering.CreateBuffer((ulong)pointCapacity * (ulong)sizeof(TrailPoint3D));
        _gpuParams = rendering.CreateBuffer((ulong)trailSlots * (ulong)sizeof(TrailParams3D));
        _stagingPool = new TransientBufferPool(rendering, "trails3d_staging");
        _segmentPool = new TransientBufferPool(rendering, "trails3d_segments");

        _states = new TrailState[trailSlots];
        _freeSlots = new Stack<int>(trailSlots);
        _freeSlices = new List<PointRange>(Math.Min(trailSlots, pointCapacity / (int)MinSlicePoints) + 1)
        {
            new(0, (uint)pointCapacity),
        };
        for (int i = trailSlots - 1; i >= 0; i--)
            _freeSlots.Push(i);
    }

    /// <summary>Default depth state applied to trails without an explicit <see cref="TrailEffect3D.Depth"/> override.</summary>
    public DepthStencilState DepthStencilState
    {
        get => _depthStencilState;
        set => _depthStencilState = value;
    }

    internal int PlannedBatchCount => _drawBatches.Count;
    internal int PlannedSegmentCount => _segments.Count;
    internal ReadOnlySpan<TrailPoint3D> PointsSpan => _cpuPoints;
    internal ReadOnlySpan<TrailParams3D> ParamsSpan => _cpuParams;
    internal ReadOnlySpan<TrailSegment> SegmentsSpan => CollectionsMarshal.AsSpan(_segments);

    internal void SetSeed(int slot, float seed)
    {
        _cpuParams[slot].Misc.X = seed;
        MarkParamsDirty(slot);
    }

    /// <summary>Creates a trail that starts emitting immediately, anchored at <paramref name="position"/>.</summary>
    public bool TryCreateInstance(TrailEffect3D effect, Vector3 position, out TrailEffectInstance3D instance)
    {
        instance = null!;
        ArgumentNullException.ThrowIfNull(effect);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(effect.Life, 0f);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(effect.Spacing, 0f);

        CachedMaterial cachedMat = GetOrCreateMaterial(effect);
        if (_freeSlots.Count == 0 || !TryAllocateSlice(effect.ExpectedPoints, out uint sliceOffset, out uint sliceCapacity))
            return false;

        int slot = _freeSlots.Pop();
        ref TrailState state = ref _states[slot];
        state.CachedMat = cachedMat;
        state.DepthOverride = effect.Depth;
        state.SliceOffset = sliceOffset;
        state.Capacity = sliceCapacity;
        state.Cursor = 0;
        state.Written = 0;
        state.Distance = 0f;
        state.LastEmitPosition = position;
        state.LastDirection = Vector3.Zero;
        state.Spacing = effect.Spacing;
        state.Life = effect.Life;
        state.LastPointTime = _time;
        state.Emitting = true;
        state.Visible = true;
        state.PointsDirty = false;

        _cpuParams[slot] = new TrailParams3D
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

        var trail = new TrailEffectInstance3D(this, slot, state.Generation);
        state.Instance = trail;
        instance = trail;
        return true;
    }

    /// <summary>Advances the renderer clock and recycles trails whose points have all expired.</summary>
    public void Update(float delta)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _time += delta;
        for (int slot = 0; slot < _states.Length; slot++)
        {
            ref TrailState state = ref _states[slot];
            if (state.Instance == null || state.Emitting || _time - state.LastPointTime <= state.Life)
                continue;
            RecycleSlot(slot, ref state);
        }
    }

    /// <summary>
    /// Uploads dirty point/parameter ranges and segment instances onto <paramref name="context"/> outside any render pass.
    /// Must be called each frame before <see cref="Render(RenderContext, RenderContext.RenderScope, ICamera)"/>.
    /// </summary>
    public unsafe void PrepareRender(RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        bool hasDirtyParams = _paramsDirtyMax >= _paramsDirtyMin;
        ulong paramElemSize = (ulong)sizeof(TrailParams3D);
        ulong pointElemSize = (ulong)sizeof(TrailPoint3D);
        ulong totalStagingBytes = 0;

        if (hasDirtyParams)
        {
            uint pCount = _paramsDirtyMax - _paramsDirtyMin + 1;
            totalStagingBytes += (ulong)pCount * paramElemSize;
        }

        for (int slot = 0; slot < _states.Length; slot++)
        {
            ref TrailState state = ref _states[slot];
            if (!state.PointsDirty)
                continue;
            uint count = state.PointDirtyMax - state.PointDirtyMin + 1;
            totalStagingBytes += (ulong)count * pointElemSize;
        }

        bool copied = false;
        if (totalStagingBytes > 0)
        {
            Alco.Graphics.Buffer staging = _stagingPool.Acquire(context, totalStagingBytes);
            ulong writeOffset = 0;

            if (hasDirtyParams)
            {
                uint pMin = _paramsDirtyMin;
                uint pCount = _paramsDirtyMax - pMin + 1;
                ulong byteSize = (ulong)pCount * paramElemSize;
                staging.SetData(_cpuParams.AsSpan((int)pMin, (int)pCount), writeOffset);
                context.CopyMemory(
                    staging.Range.Slice(writeOffset, byteSize),
                    _gpuParams.Range.Slice((ulong)pMin * paramElemSize, byteSize));
                writeOffset += byteSize;
                _paramsDirtyMin = uint.MaxValue;
                _paramsDirtyMax = 0;
                copied = true;
            }

            for (int slot = 0; slot < _states.Length; slot++)
            {
                ref TrailState state = ref _states[slot];
                if (!state.PointsDirty)
                    continue;
                state.PointsDirty = false;
                uint first = state.SliceOffset + state.PointDirtyMin;
                uint count = state.PointDirtyMax - state.PointDirtyMin + 1;
                ulong byteSize = (ulong)count * pointElemSize;
                staging.SetData(_cpuPoints.AsSpan((int)first, (int)count), writeOffset);
                context.CopyMemory(
                    staging.Range.Slice(writeOffset, byteSize),
                    _gpuPoints.Range.Slice((ulong)first * pointElemSize, byteSize));
                writeOffset += byteSize;
                copied = true;
            }
        }

        BuildDrawPlan();

        if (_segments.Count > 0)
        {
            _activeSegmentBuffer = _segmentPool.Acquire(context, (ulong)_segments.Count * (ulong)sizeof(TrailSegment));
            _activeSegmentBuffer.SetData(CollectionsMarshal.AsSpan(_segments));
        }
        else
        {
            _activeSegmentBuffer = null;
        }

        if (copied)
        {
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Vertex, Access.ShaderRead);
        }

        _preparedFrameId = context.CurrentFrameId;
    }

    /// <summary>Records batched camera-facing segment draws extracting view-projection and eye position from <paramref name="camera"/>.</summary>
    public void Render(RenderContext context, RenderContext.RenderScope render, ICamera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        Vector3 eyePos;
        if (camera is CameraPerspective perspective)
        {
            eyePos = perspective.Transform.Position;
        }
        else
        {
            Matrix4x4 view = camera.ViewMatrix;
            Matrix4x4.Invert(view, out Matrix4x4 invView);
            eyePos = invView.Translation;
        }
        Render(context, render, camera.ViewProjectionMatrix, eyePos);
    }

    /// <summary>Records batched camera-facing segment draws using explicit view-projection and camera position.</summary>
    public unsafe void Render(RenderContext context, RenderContext.RenderScope render, in Matrix4x4 viewProjection, Vector3 cameraPosition)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (_drawBatches.Count == 0)
            return;

        if (_preparedFrameId != context.CurrentFrameId || _activeSegmentBuffer == null)
            throw new InvalidOperationException("GpuTrailSystem3D.PrepareRender(context) must be called outside any pass before Render() each frame.");

        BumpAllocator frameData = context.FrameData;
        GPUBufferView pointsView = _gpuPoints.View;
        GPUBufferView paramsView = _gpuParams.View;
        GPUBufferView segmentsBaseView = _activeSegmentBuffer.View;

        for (int i = 0; i < _drawBatches.Count; i++)
        {
            DrawBatch batch = _drawBatches[i];
            var passData = new TrailPassData3D
            {
                ViewProjection = viewProjection,
                TrailPoints = pointsView,
                TrailParams = paramsView,
                Segments = segmentsBaseView.OffsetBy(batch.FirstSegment * (uint)sizeof(TrailSegment)),
                CameraPosition = cameraPosition,
                Time = _time,
            };

            GPUAllocation<byte> alloc = frameData.Allocate(batch.Material.Root.SizeBytes);
            batch.Material.CopyRoot(alloc.CpuSpan);
            MemoryMarshal.Write(alloc.CpuSpan.Slice(batch.PassRootOffset, sizeof(TrailPassData3D)), in passData);

            render.Draw(batch.Material, alloc.GPURange, batch.DepthStencil, vertexCount: 6u, instanceCount: batch.SegmentCount);
        }
    }

    internal bool GetEmitting(int slot, uint generation)
        => !IsDisposed && IsValid(slot, generation) && _states[slot].Emitting;

    internal bool GetAlive(int slot, uint generation)
    {
        if (IsDisposed || !IsValid(slot, generation))
            return false;
        ref TrailState state = ref _states[slot];
        return state.Emitting || _time - state.LastPointTime <= state.Life;
    }

    internal bool GetVisible(int slot, uint generation)
        => !IsDisposed && IsValid(slot, generation) && _states[slot].Visible;

    internal void SetVisible(int slot, uint generation, bool visible)
    {
        if (!IsDisposed && IsValid(slot, generation))
            _states[slot].Visible = visible;
    }

    internal void ExtendTo(int slot, uint generation, Vector3 position)
    {
        if (IsDisposed || !IsValid(slot, generation))
            return;
        ref TrailState state = ref _states[slot];
        if (!state.Emitting)
            return;

        Vector3 segment = position - state.LastEmitPosition;
        float length = segment.Length();
        if (length < state.Spacing)
            return;

        Vector3 direction = segment / length;
        state.LastDirection = direction;
        Vector3 cursor = state.LastEmitPosition;
        float remaining = length;
        while (remaining >= state.Spacing)
        {
            cursor += direction * state.Spacing;
            remaining -= state.Spacing;
            state.Distance += state.Spacing;
            WritePoint(ref state, cursor, direction, state.Distance);
        }
        state.LastEmitPosition = cursor;
        SyncParams(slot, ref state);
    }

    internal void Finish(int slot, uint generation, Vector3 position)
    {
        if (IsDisposed || !IsValid(slot, generation))
            return;
        ref TrailState state = ref _states[slot];
        Vector3 segment = position - state.LastEmitPosition;
        float length = segment.Length();
        if (length >= 0.001f)
        {
            Vector3 direction = segment / length;
            if (Vector3.Dot(direction, state.LastDirection) > 0f)
            {
                Vector3 cursor = state.LastEmitPosition;
                float remaining = length;
                while (remaining >= state.Spacing)
                {
                    cursor += direction * state.Spacing;
                    remaining -= state.Spacing;
                    state.Distance += state.Spacing;
                    WritePoint(ref state, cursor, direction, state.Distance);
                }
                if (remaining > 0.001f)
                {
                    state.Distance += remaining;
                    WritePoint(ref state, position, direction, state.Distance);
                }
                state.LastEmitPosition = position;
                state.LastDirection = direction;
                SyncParams(slot, ref state);
            }
        }
        StopCore(ref state);
    }

    internal void Stop(int slot, uint generation)
    {
        if (!IsDisposed && IsValid(slot, generation))
            StopCore(ref _states[slot]);
    }

    internal void ReleaseTrail(int slot, uint generation)
    {
        if (!IsDisposed && IsValid(slot, generation))
            RecycleSlot(slot, ref _states[slot]);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        _gpuPoints.Destroy();
        _gpuParams.Destroy();
        _stagingPool.Dispose();
        _segmentPool.Dispose();
        for (int slot = 0; slot < _states.Length; slot++)
        {
            _states[slot].Instance = null;
            _states[slot].CachedMat = null;
        }
    }

    private bool IsValid(int slot, uint generation)
        => _states[slot].Instance != null && _states[slot].Generation == generation;

    private unsafe CachedMaterial GetOrCreateMaterial(TrailEffect3D effect)
    {
        MaterialAsset asset = effect.Material ?? _defaultAsset;
        BlendState blend = effect.Blend ?? BlendState.PremultipliedAlpha;
        var key = (asset, blend);
        if (_materials.TryGetValue(key, out CachedMaterial? cached))
            return cached;

        string surfaceModule = string.IsNullOrWhiteSpace(asset.Surface)
            ? DefaultSurface
            : asset.Surface.Trim().Replace('-', '_');

        Shader shader = _shaders.Compose(_renderTemplateModule, surfaceModule, ShaderKind.Graphics, $"trails3d:{surfaceModule}");
        Material material = shader.CreateMaterial($"trails3d:{asset.Name}");
        material.Blend = blend;
        material.DepthStencilState = effect.Depth ?? _depthStencilState;
        material.Rasterization = RasterizationState.CullNone;

        if (!ReferenceEquals(asset, _defaultAsset))
            asset.ApplyTo(material, ParticleShaderKeys.Material + ".");

        RootField? passField = material.Root.Find(ParticleShaderKeys.Pass);
        if (passField == null || passField.SizeBytes != (uint)sizeof(TrailPassData3D))
            throw new InvalidDataException(
                $"Trail render template '{_renderTemplateModule}' does not declare expected '{ParticleShaderKeys.Pass}' block ({sizeof(TrailPassData3D)}B).");

        var created = new CachedMaterial(material, (int)passField.Offset);
        _materials[key] = created;
        return created;
    }

    private void WritePoint(ref TrailState state, Vector3 position, Vector3 tangent, float u)
    {
        uint local = state.Cursor;
        _cpuPoints[state.SliceOffset + local] = new TrailPoint3D
        {
            Position = position,
            EmitTime = _time,
            Tangent = tangent,
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
        ref TrailParams3D parameters = ref _cpuParams[slot];
        parameters.Cursor = state.Cursor;
        parameters.Written = state.Written;
        MarkParamsDirty(slot);
    }

    private void MarkParamsDirty(int slot)
    {
        _paramsDirtyMin = Math.Min(_paramsDirtyMin, (uint)slot);
        _paramsDirtyMax = Math.Max(_paramsDirtyMax, (uint)slot);
    }

    private static void StopCore(ref TrailState state) => state.Emitting = false;

    private void RecycleSlot(int slot, ref TrailState state)
    {
        state.Instance = null;
        state.CachedMat = null;
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
                continue;
            offset = range.Offset;
            capacity = rounded;
            if (range.Count == rounded)
                _freeSlices.RemoveAt(i);
            else
                _freeSlices[i] = new PointRange(range.Offset + rounded, range.Count - rounded);
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
            index++;
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

    private void BuildDrawPlan()
    {
        foreach (List<int> bucket in _drawBuckets.Values)
            bucket.Clear();
        _drawBucketKeys.Clear();
        _drawBatches.Clear();
        _segments.Clear();

        for (int slot = 0; slot < _states.Length; slot++)
        {
            ref TrailState state = ref _states[slot];
            if (state.Instance == null || !state.Visible || state.CachedMat == null)
                continue;
            uint window = Math.Min(state.Written, state.Capacity);
            if (window < 2)
                continue;

            DepthStencilState depth = state.DepthOverride ?? _depthStencilState;
            var key = (state.CachedMat.Material, depth);
            if (!_drawBuckets.TryGetValue(key, out List<int>? bucket))
            {
                bucket = [];
                _drawBuckets[key] = bucket;
            }
            if (bucket.Count == 0)
                _drawBucketKeys.Add(key);
            bucket.Add(slot);
        }

        for (int k = 0; k < _drawBucketKeys.Count; k++)
        {
            (Material material, DepthStencilState depthStencil) = _drawBucketKeys[k];
            List<int> bucket = _drawBuckets[(material, depthStencil)];
            uint firstSegment = (uint)_segments.Count;
            int passOffset = _states[bucket[0]].CachedMat!.PassRootOffset;

            for (int b = 0; b < bucket.Count; b++)
            {
                int slot = bucket[b];
                ref TrailState state = ref _states[slot];
                uint window = Math.Min(state.Written, state.Capacity);
                uint segCount = window - 1;
                for (uint s = 0; s < segCount; s++)
                {
                    _segments.Add(new TrailSegment
                    {
                        TrailSlot = (uint)slot,
                        Segment = s,
                    });
                }
            }

            uint batchSegments = (uint)_segments.Count - firstSegment;
            _drawBatches.Add(new DrawBatch(material, passOffset, depthStencil, firstSegment, batchSegments));
        }
    }
}
