using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;

namespace Alco.Rendering;

/// <summary>
/// Generic GPU-driven 3D instanced mesh scene managing a persistent instance pool,
/// dynamically growable <see cref="ModelGeometry"/> draw buckets (no fixed bucket limit),
/// multi-view compute frustum culling, and multi-tier indexed indirect draw submission.
/// </summary>
public sealed unsafe class MeshScene3D : AutoDisposable
{
    private const int InitialBucketCapacity = 64;
    private const uint IndexedArgsStride = 20u;

    private sealed class BucketState
    {
        public required uint Id;
        public required MeshScene3DBucketDesc Desc;
        public required uint FirstIndex;
        public required uint IndexCount;
        public required int VertexOffset;
        public required Vector3 LocalCenter;
        public required Vector3 LocalExtent;
        public GPUHeapOwner? IndexHeapOwner;
        public uint PageFirstIndex;
        public ulong PageEndBytes;
        public bool CanBatchInPage;
        public uint DrawSlot;
        public uint VisibleBase;
        public int SortHeapId;
        public int SortParamsId;
        public int SortMaterialId;
        public int SortDepthId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceScatterRoot
    {
        public GPUBufferView Patches;
        public GPUBufferView PatchSlots;
        public GPUBufferView Pool;
        public uint PatchCount;
        public uint WordsPerInstance;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MeshScene3DResetArgsRoot
    {
        public GPUBufferView BucketMeta;
        public GPUBufferView DrawArgs;
        public uint BucketCount;
        public uint Pad0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MeshScene3DCullRoot
    {
        public Vector4 Plane0;
        public Vector4 Plane1;
        public Vector4 Plane2;
        public Vector4 Plane3;
        public Vector4 Plane4;
        public Vector4 Plane5;
        public GPUBufferView Pool;
        public GPUBufferView SlotBuckets;
        public GPUBufferView BucketMeta;
        public GPUBufferView DrawArgs;
        public GPUBufferView VisibleSlots;
        public uint SlotCount;
        public uint BucketCount;
        public float CullMargin;
        public uint RequiredCullMask;
        public uint PlaneCount;
        public uint Pad0;
        public Vector3 OriginOffset;
        public uint Pad1;
    }

    private readonly RenderingSystem _system;
    private readonly GpuInstancePool<MeshScene3DInstance> _pool;
    private readonly TransientBufferPool _transientBuffers;
    private readonly Shader _scatterShader;
    private readonly Shader _resetArgsShader;
    private readonly Shader _cullShader;

    private readonly List<BucketState> _buckets = new();
    private readonly Dictionary<int, List<BucketState>> _tiers = new();
    private readonly List<int> _tierKeys = new();
    private readonly Dictionary<GPUHeapOwner, int> _heapSortIds = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Alco.Graphics.Buffer, int> _paramsBufferSortIds = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Material, int> _materialSortIds = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DepthStencilState, int> _depthSortIds = new();
    private readonly MeshScene3DCullOutput _defaultCullOutput;
    private readonly int _maxIndirectDrawCount;
    private readonly bool _multiDrawEnabled;

    private MeshScene3DBucketMeta[] _bucketMetaStaging = new MeshScene3DBucketMeta[InitialBucketCapacity];
    private MeshScene3DDrawEntry[] _drawEntriesStaging = new MeshScene3DDrawEntry[InitialBucketCapacity];
    private uint[] _bucketSlotIndices = BuildSequentialIndices(InitialBucketCapacity);
    private Alco.Graphics.Buffer _bucketMetaBuffer;
    private Alco.Graphics.Buffer _drawEntriesBuffer;
    private int _bucketCapacity = InitialBucketCapacity;
    private bool _bucketsDirty = true;
    private bool _layoutDirty = true;
    private int _drawCallsSubmitted;

    /// <summary>Creates a GPU-driven 3D mesh scene backed by <paramref name="system"/>.</summary>
    /// <param name="system">The rendering system owning GPU buffers and descriptor heaps.</param>
    /// <param name="shaders">Optional shader system override; defaults to <see cref="RenderingSystem.Shaders"/>.</param>
    public MeshScene3D(RenderingSystem system, ShaderSystem? shaders = null)
    {
        _system = system ?? throw new ArgumentNullException(nameof(system));
        ShaderSystem resolvedShaders = shaders ?? system.Shaders;
        AlcoMesh3DShaders.EnsureBuiltInModulesLoaded(resolvedShaders);

        _pool = new GpuInstancePool<MeshScene3DInstance>(system, resolvedShaders);
        _transientBuffers = new TransientBufferPool(system);

        _scatterShader = resolvedShaders.GetShader(AlcoSprite2DShaders.GpuInstanceScatterModule, ShaderKind.Compute);
        _resetArgsShader = resolvedShaders.GetShader(AlcoMesh3DShaders.MeshScene3DResetArgsModule, ShaderKind.Compute);
        _cullShader = resolvedShaders.GetShader(AlcoMesh3DShaders.MeshScene3DCullModule, ShaderKind.Compute);

        VerifyLayouts();

        _maxIndirectDrawCount = system.ActiveBackend is GraphicsBackend.Vulkan or GraphicsBackend.VulkanCompact
            ? (int)Math.Min(65535u, system.Caps.MaxIndirectDrawCount)
            : 0;
        _multiDrawEnabled = _maxIndirectDrawCount > 1;

        _bucketMetaBuffer = system.CreateBuffer((ulong)_bucketCapacity * (ulong)sizeof(MeshScene3DBucketMeta));
        _drawEntriesBuffer = system.CreateBuffer((ulong)_bucketCapacity * (ulong)sizeof(MeshScene3DDrawEntry));
        _defaultCullOutput = new MeshScene3DCullOutput(system, _bucketCapacity, _pool.SlotCapacity);
    }

    /// <summary>Gets whether multi-draw indirect batching is enabled for the active graphics backend.</summary>
    public bool IsMultiDrawEnabled => _multiDrawEnabled;

    /// <summary>Gets the persistent GPU buffer storing all <see cref="MeshScene3DInstance"/> slots.</summary>
    public Alco.Graphics.Buffer PoolBuffer => _pool.PoolBuffer;

    /// <summary>Gets the default cull output's indirect draw argument buffer.</summary>
    public Alco.Graphics.Buffer DrawArgsBuffer => _defaultCullOutput.DrawArgsBuffer;

    /// <summary>Gets the default cull output owning visible slot indices and indirect draw arguments.</summary>
    public MeshScene3DCullOutput DefaultCullOutput => _defaultCullOutput;

    /// <summary>Gets the current number of registered draw buckets.</summary>
    public int BucketCount => _buckets.Count;

    /// <summary>Gets the contiguous draw slot assigned to <paramref name="bucketId"/> inside draw argument and draw entry buffers.</summary>
    /// <param name="bucketId">Bucket identifier returned by <see cref="RegisterBucket"/>.</param>
    /// <returns>Zero-based contiguous draw slot index.</returns>
    public uint GetBucketDrawSlot(uint bucketId)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ValidateBucketId(bucketId);
        EnsureDrawLayout();
        return _buckets[(int)bucketId].DrawSlot;
    }

    /// <summary>Gets scene telemetry from the latest sync, cull, and render calls.</summary>
    public MeshScene3DStats Stats => new(
        _pool.TotalSlots,
        _pool.LiveCount,
        _buckets.Count,
        _pool.LastDirtyPatchCount,
        _drawCallsSubmitted,
        _pool.ActiveTextureCount);

    /// <summary>Pre-allocates GPU storage for at least <paramref name="minSlots"/> instances and optional <paramref name="minBuckets"/> buckets.</summary>
    /// <param name="minSlots">Minimum slot count to reserve.</param>
    /// <param name="minBuckets">Optional minimum bucket capacity to reserve.</param>
    public void EnsureCapacity(int minSlots, int minBuckets = 0)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (minSlots > 0)
        {
            _pool.EnsureSlotCapacity(minSlots);
        }

        if (minBuckets > _bucketCapacity)
        {
            GrowBucketStorage(minBuckets);
        }

        _defaultCullOutput.EnsureCapacity(_bucketCapacity, _pool.SlotCapacity);
    }

    /// <summary>Creates an independent cull output buffer pair so the scene can be culled for an additional view.</summary>
    /// <returns>A disposable <see cref="MeshScene3DCullOutput"/> owned by the caller.</returns>
    public MeshScene3DCullOutput CreateCullOutput()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return new MeshScene3DCullOutput(_system, Math.Max(_bucketCapacity, _buckets.Count), _pool.SlotCapacity);
    }

    /// <summary>Registers a new draw bucket and returns its stable zero-based bucket identifier.</summary>
    /// <param name="desc">Bucket tier, geometry, material, depth-stencil state, index slice, and optional local bounds.</param>
    /// <returns>The assigned zero-based bucket identifier.</returns>
    public uint RegisterBucket(in MeshScene3DBucketDesc desc)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(desc.Geometry);
        ArgumentNullException.ThrowIfNull(desc.Material);

        if (desc.FirstIndex >= desc.Geometry.IndexCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desc),
                $"Bucket FirstIndex ({desc.FirstIndex}) must be less than Geometry.IndexCount ({desc.Geometry.IndexCount}).");
        }

        uint resolvedIndexCount = desc.IndexCount == 0u
            ? desc.Geometry.IndexCount - desc.FirstIndex
            : desc.IndexCount;
        if (resolvedIndexCount == 0u || desc.FirstIndex + resolvedIndexCount > desc.Geometry.IndexCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desc),
                $"Bucket index range [{desc.FirstIndex}, {desc.FirstIndex + resolvedIndexCount}) exceeds Geometry.IndexCount ({desc.Geometry.IndexCount}).");
        }

        if (desc.VertexOffset < 0 || (uint)desc.VertexOffset >= desc.Geometry.VertexCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desc),
                $"Bucket VertexOffset ({desc.VertexOffset}) must be in [0, {desc.Geometry.VertexCount}).");
        }

        if (desc.Material.Shader.Kind != ShaderKind.Graphics ||
            desc.Material.Root.SizeBytes < (uint)sizeof(MeshScene3DPassHeader))
        {
            throw new ArgumentException(
                $"MeshScene3D bucket material '{desc.Material.Name}' must be graphics with root size >= {sizeof(MeshScene3DPassHeader)} bytes.",
                nameof(desc));
        }

        Vector3 bMin = desc.LocalBoundsMin ?? desc.Geometry.LocalBoundsMin;
        Vector3 bMax = desc.LocalBoundsMax ?? desc.Geometry.LocalBoundsMax;
        Vector3 center = (bMin + bMax) * 0.5f;
        Vector3 extent = Vector3.Abs((bMax - bMin) * 0.5f);

        GPURange idxRange = desc.Geometry.IndexBuffer.GetRange();
        ulong idxStride = desc.Geometry.IndexType == IndexType.UInt16 ? 2ul : 4ul;
        ulong relEndBytes = (ulong)(desc.FirstIndex + resolvedIndexCount) * idxStride;
        bool canBatch = _multiDrawEnabled
            && idxRange.Buffer != null
            && (idxRange.Offset & (idxStride - 1ul)) == 0ul
            && relEndBytes <= idxRange.Size;
        uint pageFirstIndex = desc.FirstIndex;
        ulong pageEndBytes = 0ul;
        if (canBatch)
        {
            ulong pageFirst = idxRange.Offset / idxStride + desc.FirstIndex;
            ulong pageEnd = idxRange.Offset + relEndBytes;
            if (pageFirst <= uint.MaxValue && pageEnd >= idxRange.Offset)
            {
                pageFirstIndex = (uint)pageFirst;
                pageEndBytes = pageEnd;
            }
            else
            {
                canBatch = false;
            }
        }

        uint bucketId = (uint)_buckets.Count;
        BucketState state = new()
        {
            Id = bucketId,
            Desc = desc,
            FirstIndex = desc.FirstIndex,
            IndexCount = resolvedIndexCount,
            VertexOffset = desc.VertexOffset,
            LocalCenter = center,
            LocalExtent = extent,
            IndexHeapOwner = canBatch ? idxRange.Buffer : null,
            PageFirstIndex = pageFirstIndex,
            PageEndBytes = pageEndBytes,
            CanBatchInPage = canBatch,
            DrawSlot = bucketId,
        };
        _buckets.Add(state);

        if (!_tiers.TryGetValue(desc.Tier, out List<BucketState>? tierList))
        {
            tierList = new List<BucketState>(8);
            _tiers[desc.Tier] = tierList;
        }

        tierList.Add(state);
        _layoutDirty = true;
        _bucketsDirty = true;
        return bucketId;
    }

    /// <summary>Gets the live instance count currently assigned to <paramref name="bucketId"/>.</summary>
    /// <param name="bucketId">Bucket identifier returned by <see cref="RegisterBucket"/>.</param>
    /// <returns>The live instance count.</returns>
    public int GetBucketLiveCount(uint bucketId) => _pool.GetBucketCount(bucketId);

    /// <summary>Queues a thread-safe deferred mesh instance registration returning a disposable slot handle.</summary>
    /// <param name="bucketId">Bucket identifier returned by <see cref="RegisterBucket"/>.</param>
    /// <param name="initialData">Initial 3D instance payload.</param>
    /// <param name="primaryTexture">Optional primary texture to retain while registered.</param>
    /// <param name="auxTexture">Optional auxiliary texture to retain while registered.</param>
    /// <returns>The registration handle.</returns>
    public GpuInstancePool<MeshScene3DInstance>.Registration RegisterDeferred(
        uint bucketId,
        in MeshScene3DInstance initialData,
        Texture? primaryTexture = null,
        Texture? auxTexture = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ValidateBucketId(bucketId);
        return _pool.RegisterDeferred(bucketId, in initialData, primaryTexture, auxTexture);
    }

    /// <summary>Allocates and writes a direct slot on the render thread.</summary>
    /// <param name="bucketId">Bucket identifier returned by <see cref="RegisterBucket"/>.</param>
    /// <param name="data">Initial 3D instance payload.</param>
    /// <param name="primaryTexture">Optional primary texture to retain while live.</param>
    /// <param name="auxTexture">Optional auxiliary texture to retain while live.</param>
    /// <returns>The allocated global slot index.</returns>
    public int AddDirect(
        uint bucketId,
        in MeshScene3DInstance data,
        Texture? primaryTexture = null,
        Texture? auxTexture = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ValidateBucketId(bucketId);
        return _pool.AddDirect(bucketId, in data, primaryTexture, auxTexture);
    }

    /// <summary>Updates a direct slot's payload and optional retained textures on the render thread.</summary>
    /// <param name="slot">Global slot index returned by <see cref="AddDirect"/>.</param>
    /// <param name="data">Updated 3D instance payload.</param>
    /// <param name="primaryTexture">Optional primary texture to retain.</param>
    /// <param name="auxTexture">Optional auxiliary texture to retain.</param>
    public void UpdateDirect(
        int slot,
        in MeshScene3DInstance data,
        Texture? primaryTexture = null,
        Texture? auxTexture = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _pool.UpdateDirect(slot, in data, primaryTexture, auxTexture);
    }

    /// <summary>Reassigns a live direct slot to another registered bucket on the render thread.</summary>
    /// <param name="slot">Global slot index returned by <see cref="AddDirect"/>.</param>
    /// <param name="newBucketId">Target bucket identifier returned by <see cref="RegisterBucket"/>.</param>
    public void UpdateBucketDirect(int slot, uint newBucketId)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ValidateBucketId(newBucketId);
        _pool.UpdateBucketDirect(slot, newBucketId);
    }

    /// <summary>Removes and recycles a direct slot on the render thread.</summary>
    /// <param name="slot">Global slot index returned by <see cref="AddDirect"/>.</param>
    public void RemoveDirect(int slot)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _pool.RemoveDirect(slot);
    }

    /// <summary>Applies pending deferred registrations, bucket reassignments, and detachments without dispatching GPU work.</summary>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_pool.Flush())
        {
            _bucketsDirty = true;
        }
    }

    /// <summary>
    /// Syncs dirty instance patches and bucket membership metadata to the GPU.
    /// Call once per frame before issuing one or more <see cref="Cull"/> passes.
    /// </summary>
    /// <param name="context">Active recording context outside any open scope.</param>
    public void SyncToGpu(RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (_buckets.Count == 0)
        {
            _pool.Flush();
            return;
        }

        context.Barrier(
            Stage.Compute | Stage.Vertex | Stage.Pixel,
            Access.ShaderRead | Access.ShaderWrite,
            Stage.Compute, Access.ShaderRead | Access.ShaderWrite);

        bool uploadedMeta;
        using (RenderContext.ComputeScope compute = context.BeginCompute())
        {
            uploadedMeta = SyncInternal(context, compute);
        }

        if (uploadedMeta || _pool.LastDirtyPatchCount > 0)
        {
            context.Barrier(
                Stage.Compute, Access.ShaderWrite,
                Stage.Compute | Stage.Vertex | Stage.Pixel, Access.ShaderRead);
        }
    }

    /// <summary>
    /// Resets indirect draw arguments and dispatches 3D frustum/mask culling for <paramref name="view"/>
    /// into <paramref name="output"/> (or <see cref="DefaultCullOutput"/> when null).
    /// Assumes <see cref="SyncToGpu"/> has already been called this frame.
    /// </summary>
    /// <param name="context">Active recording context outside any open scope.</param>
    /// <param name="view">3D culling planes and mask filter.</param>
    /// <param name="output">Optional target cull output; defaults to <see cref="DefaultCullOutput"/>.</param>
    public void Cull(RenderContext context, in MeshScene3DCullView view, MeshScene3DCullOutput? output = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (_buckets.Count == 0)
        {
            return;
        }

        MeshScene3DCullOutput target = output ?? _defaultCullOutput;
        target.EnsureCapacity(_buckets.Count, _pool.SlotCapacity);
        SynchronizeCullOutputReuse(context);

        using (RenderContext.ComputeScope compute = context.BeginCompute())
        {
            DispatchCullInternal(context, compute, in view, target);
        }

        context.Barrier(
            Stage.Compute, Access.ShaderWrite,
            Stage.Indirect | Stage.Vertex | Stage.Pixel,
            Access.IndirectRead | Access.ShaderRead);
    }

    /// <summary>
    /// Combines <see cref="SyncToGpu"/> and <see cref="Cull"/> into a single compute scope followed
    /// by an indirect/vertex/pixel memory barrier. Resets per-frame <see cref="Stats"/> draw counters.
    /// </summary>
    /// <param name="context">Active recording context outside any open scope.</param>
    /// <param name="view">Primary 3D culling view.</param>
    /// <param name="output">Optional target cull output; defaults to <see cref="DefaultCullOutput"/>.</param>
    public void PrepareAndCull(RenderContext context, in MeshScene3DCullView view, MeshScene3DCullOutput? output = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        _drawCallsSubmitted = 0;

        if (_buckets.Count == 0)
        {
            _pool.Flush();
            return;
        }

        MeshScene3DCullOutput target = output ?? _defaultCullOutput;
        target.EnsureCapacity(_buckets.Count, _pool.SlotCapacity);
        SynchronizeCullOutputReuse(context);

        using (RenderContext.ComputeScope compute = context.BeginCompute())
        {
            _ = SyncInternal(context, compute);
            DispatchCullInternal(context, compute, in view, target);
        }

        context.Barrier(
            Stage.Compute, Access.ShaderWrite,
            Stage.Indirect | Stage.Vertex | Stage.Pixel,
            Access.IndirectRead | Access.ShaderRead);
    }

    /// <summary>Resets the per-frame submitted draw call counter without mutating GPU state.</summary>
    public void ResetFrameStats()
    {
        _drawCallsSubmitted = 0;
    }

    /// <summary>Records indexed indirect draws for all non-empty buckets in <paramref name="tier"/>.</summary>
    /// <param name="context">Active recording context.</param>
    /// <param name="render">Open render pass scope.</param>
    /// <param name="tier">Tier identifier matching <see cref="MeshScene3DBucketDesc.Tier"/>.</param>
    /// <param name="camera">Camera providing the view-projection matrix.</param>
    /// <param name="time">Elapsed world time in seconds.</param>
    /// <param name="userParams">Optional vector forwarded to shaders via <see cref="MeshScene3DPassHeader.UserParams"/>.</param>
    /// <param name="passFlags">Optional caller flag bits forwarded via <see cref="MeshScene3DPassHeader.PassFlags"/>.</param>
    /// <param name="cullOutput">Optional cull output to source indirect arguments and visible slots from; defaults to <see cref="DefaultCullOutput"/>.</param>
    /// <param name="materialOverride">Optional material overriding each bucket's default material (must declare <see cref="MeshScene3DPassHeader"/> at root offset 0).</param>
    /// <param name="depthStencilOverride">Optional depth-stencil state overriding each bucket's default state.</param>
    /// <param name="originOffset">Origin offset subtracted from instance translations prior to vertex transformation.</param>
    /// <returns>The number of indexed indirect draw calls submitted for this tier.</returns>
    public int RenderTier(
        RenderContext context,
        in RenderContext.RenderScope render,
        int tier,
        ICamera camera,
        float time = 0f,
        Vector4 userParams = default,
        uint passFlags = 0u,
        MeshScene3DCullOutput? cullOutput = null,
        Material? materialOverride = null,
        DepthStencilState? depthStencilOverride = null,
        Vector3 originOffset = default)
    {
        ArgumentNullException.ThrowIfNull(camera);
        Matrix4x4 vp = camera.ViewProjectionMatrix;
        return RenderTier(
            context,
            in render,
            tier,
            in vp,
            time,
            userParams,
            passFlags,
            cullOutput,
            materialOverride,
            depthStencilOverride,
            originOffset);
    }

    /// <summary>Records indexed indirect draws for all non-empty buckets in <paramref name="tier"/>.</summary>
    /// <param name="context">Active recording context.</param>
    /// <param name="render">Open render pass scope.</param>
    /// <param name="tier">Tier identifier matching <see cref="MeshScene3DBucketDesc.Tier"/>.</param>
    /// <param name="viewProjection">Row-major view-projection matrix (origin-relative when <paramref name="originOffset"/> is non-zero).</param>
    /// <param name="time">Elapsed world time in seconds.</param>
    /// <param name="userParams">Optional vector forwarded to shaders via <see cref="MeshScene3DPassHeader.UserParams"/>.</param>
    /// <param name="passFlags">Optional caller flag bits forwarded via <see cref="MeshScene3DPassHeader.PassFlags"/>.</param>
    /// <param name="cullOutput">Optional cull output to source indirect arguments and visible slots from; defaults to <see cref="DefaultCullOutput"/>.</param>
    /// <param name="materialOverride">Optional material overriding each bucket's default material (must declare <see cref="MeshScene3DPassHeader"/> at root offset 0).</param>
    /// <param name="depthStencilOverride">Optional depth-stencil state overriding each bucket's default state.</param>
    /// <param name="originOffset">Origin offset subtracted from instance translations prior to vertex transformation.</param>
    /// <returns>The number of indexed indirect draw calls submitted for this tier.</returns>
    public int RenderTier(
        RenderContext context,
        in RenderContext.RenderScope render,
        int tier,
        in Matrix4x4 viewProjection,
        float time = 0f,
        Vector4 userParams = default,
        uint passFlags = 0u,
        MeshScene3DCullOutput? cullOutput = null,
        Material? materialOverride = null,
        DepthStencilState? depthStencilOverride = null,
        Vector3 originOffset = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (materialOverride != null &&
            (materialOverride.Shader.Kind != ShaderKind.Graphics ||
             materialOverride.Root.SizeBytes < (uint)sizeof(MeshScene3DPassHeader)))
        {
            throw new ArgumentException(
                $"Override material '{materialOverride.Name}' must be graphics with root size >= {sizeof(MeshScene3DPassHeader)} bytes.",
                nameof(materialOverride));
        }

        if (!_tiers.TryGetValue(tier, out List<BucketState>? tierBuckets) || tierBuckets.Count == 0)
        {
            return 0;
        }

        EnsureDrawLayout();
        MeshScene3DCullOutput activeOutput = cullOutput ?? _defaultCullOutput;
        int submitted = 0;
        GPUBufferView poolView = _pool.PoolBuffer.View;
        GPUBufferView visibleBaseView = activeOutput.VisibleSlotsBuffer.View;
        Alco.Graphics.Buffer drawArgsBuffer = activeOutput.DrawArgsBuffer;

        if (!_multiDrawEnabled)
        {
            for (int i = 0; i < tierBuckets.Count; ++i)
            {
                BucketState bucket = tierBuckets[i];
                if (_pool.GetBucketCount(bucket.Id) == 0)
                {
                    continue;
                }

                submitted += SubmitSingleBucket(
                    context,
                    in render,
                    bucket,
                    in viewProjection,
                    time,
                    userParams,
                    passFlags,
                    poolView,
                    visibleBaseView,
                    drawArgsBuffer,
                    materialOverride,
                    depthStencilOverride,
                    originOffset);
            }

            _drawCallsSubmitted += submitted;
            return submitted;
        }

        int index = 0;
        int bucketCount = tierBuckets.Count;
        while (index < bucketCount)
        {
            BucketState first = tierBuckets[index];
            if (!first.CanBatchInPage)
            {
                if (_pool.GetBucketCount(first.Id) > 0)
                {
                    submitted += SubmitSingleBucket(
                        context,
                        in render,
                        first,
                        in viewProjection,
                        time,
                        userParams,
                        passFlags,
                        poolView,
                        visibleBaseView,
                        drawArgsBuffer,
                        materialOverride,
                        depthStencilOverride,
                        originOffset);
                }

                ++index;
                continue;
            }

            Material runMaterial = materialOverride ?? first.Desc.Material;
            DepthStencilState runDepth = depthStencilOverride ?? first.Desc.DepthStencil;
            Alco.Graphics.Buffer? runMatParams = first.Desc.MaterialParamsBuffer;
            GPUHeapOwner runHeap = first.IndexHeapOwner!;
            IndexType runIndexType = first.Desc.Geometry.IndexType;

            int runEnd = index + 1;
            while (runEnd < bucketCount)
            {
                BucketState next = tierBuckets[runEnd];
                if (!next.CanBatchInPage)
                {
                    break;
                }

                Material nextMaterial = materialOverride ?? next.Desc.Material;
                DepthStencilState nextDepth = depthStencilOverride ?? next.Desc.DepthStencil;
                if (!ReferenceEquals(nextMaterial, runMaterial)
                    || !nextDepth.Equals(runDepth)
                    || !ReferenceEquals(next.Desc.MaterialParamsBuffer, runMatParams)
                    || !ReferenceEquals(next.IndexHeapOwner, runHeap)
                    || next.Desc.Geometry.IndexType != runIndexType)
                {
                    break;
                }

                ++runEnd;
            }

            int trimmedStart = index;
            while (trimmedStart < runEnd && _pool.GetBucketCount(tierBuckets[trimmedStart].Id) == 0)
            {
                ++trimmedStart;
            }

            int trimmedEnd = runEnd;
            while (trimmedEnd > trimmedStart && _pool.GetBucketCount(tierBuckets[trimmedEnd - 1].Id) == 0)
            {
                --trimmedEnd;
            }

            if (trimmedStart < trimmedEnd)
            {
                ulong pageEnd = 0ul;
                for (int k = trimmedStart; k < trimmedEnd; ++k)
                {
                    if (tierBuckets[k].PageEndBytes > pageEnd)
                    {
                        pageEnd = tierBuckets[k].PageEndBytes;
                    }
                }

                uint firstDrawSlot = tierBuckets[trimmedStart].DrawSlot;
                int totalDraws = trimmedEnd - trimmedStart;
                GPURange pageIndexRange = new() { Buffer = runHeap, Offset = 0ul, Size = pageEnd };

                for (int chunkOffset = 0; chunkOffset < totalDraws;)
                {
                    int chunkCount = Math.Min(totalDraws - chunkOffset, _maxIndirectDrawCount);
                    uint chunkFirstSlot = firstDrawSlot + (uint)chunkOffset;
                    BucketState chunkLead = tierBuckets[trimmedStart + chunkOffset];

                    BufferSlice rootSlice = BuildPassRootSlice(
                        context,
                        runMaterial,
                        in viewProjection,
                        poolView,
                        visibleBaseView,
                        runMatParams,
                        chunkFirstSlot,
                        chunkLead.Desc.BucketTag,
                        time,
                        userParams,
                        passFlags,
                        originOffset);

                    BufferSlice argsSlice = drawArgsBuffer.Slice(
                        (ulong)chunkFirstSlot * IndexedArgsStride,
                        (ulong)chunkCount * IndexedArgsStride);

                    render.DrawIndexedIndirect(
                        runMaterial,
                        rootSlice.GetRange(),
                        runDepth,
                        pageIndexRange,
                        runIndexType,
                        argsSlice.GetRange(),
                        drawCount: (uint)chunkCount,
                        stride: IndexedArgsStride);

                    ++submitted;
                    chunkOffset += chunkCount;
                }
            }

            index = runEnd;
        }

        _drawCallsSubmitted += submitted;
        return submitted;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        _defaultCullOutput.Dispose();
        _pool.Dispose();
        _transientBuffers.Dispose();

        if (!_bucketMetaBuffer.IsDisposed)
        {
            _bucketMetaBuffer.Dispose();
        }

        if (!_drawEntriesBuffer.IsDisposed)
        {
            _drawEntriesBuffer.Dispose();
        }

        _heapSortIds.Clear();
        _paramsBufferSortIds.Clear();
        _materialSortIds.Clear();
        _depthSortIds.Clear();
    }

    private static void SynchronizeCullOutputReuse(RenderContext context)
    {
        // Each output has one buffer pair, even when reused by multiple frames or views.
        context.Barrier(
            Stage.Compute | Stage.Indirect | Stage.Vertex | Stage.Pixel,
            Access.ShaderRead | Access.ShaderWrite | Access.IndirectRead,
            Stage.Compute, Access.ShaderRead | Access.ShaderWrite);
    }

    private bool SyncInternal(RenderContext context, in RenderContext.ComputeScope compute)
    {
        bool membershipChanged = _pool.SyncToGpu(context, compute);
        if (_buckets.Count > _bucketCapacity)
        {
            GrowBucketStorage(_buckets.Count);
        }

        bool uploadedMeta = false;
        if (membershipChanged || _bucketsDirty)
        {
            UploadBucketMeta(context, compute);
            _bucketsDirty = false;
            uploadedMeta = true;
        }

        return uploadedMeta;
    }

    private void DispatchCullInternal(
        RenderContext context,
        in RenderContext.ComputeScope compute,
        in MeshScene3DCullView view,
        MeshScene3DCullOutput output)
    {
        MeshScene3DResetArgsRoot resetRoot = new()
        {
            BucketMeta = _bucketMetaBuffer.View,
            DrawArgs = output.DrawArgsBuffer.View,
            BucketCount = (uint)_buckets.Count,
        };
        compute.Dispatch(_resetArgsShader, in resetRoot, new Extent3D((uint)((_buckets.Count + 63) / 64), 1u, 1u));

        if (_pool.TotalSlots > 0)
        {
            compute.Barrier(
                Stage.Compute, Access.ShaderWrite,
                Stage.Compute, Access.ShaderRead | Access.ShaderWrite);

            MeshScene3DCullRoot cullRoot = new()
            {
                Plane0 = view.Plane0,
                Plane1 = view.Plane1,
                Plane2 = view.Plane2,
                Plane3 = view.Plane3,
                Plane4 = view.Plane4,
                Plane5 = view.Plane5,
                Pool = _pool.PoolBuffer.View,
                SlotBuckets = _pool.SlotBucketBuffer.View,
                BucketMeta = _bucketMetaBuffer.View,
                DrawArgs = output.DrawArgsBuffer.View,
                VisibleSlots = output.VisibleSlotsBuffer.View,
                SlotCount = (uint)_pool.TotalSlots,
                BucketCount = (uint)_buckets.Count,
                CullMargin = view.CullMargin,
                RequiredCullMask = view.RequiredCullMask,
                PlaneCount = view.EnableFrustumCulling ? 6u : 0u,
                OriginOffset = view.OriginOffset,
            };
            compute.Dispatch(_cullShader, in cullRoot, new Extent3D((uint)((_pool.TotalSlots + 63) / 64), 1u, 1u));
        }
    }

    private void UploadBucketMeta(RenderContext context, in RenderContext.ComputeScope compute)
    {
        EnsureDrawLayout();
        int count = _buckets.Count;
        uint prefix = 0;
        for (int i = 0; i < count; ++i)
        {
            BucketState bucket = _buckets[i];
            bucket.VisibleBase = prefix;
            prefix += (uint)_pool.GetBucketCount(bucket.Id);
            _bucketMetaStaging[i] = new MeshScene3DBucketMeta
            {
                IndexCount = bucket.IndexCount,
                VisibleBase = bucket.VisibleBase,
                BucketTag = bucket.Desc.BucketTag,
                FirstIndex = bucket.CanBatchInPage ? bucket.PageFirstIndex : bucket.FirstIndex,
                LocalCenter = bucket.LocalCenter,
                VertexOffset = 0,
                LocalExtent = bucket.LocalExtent,
                DrawSlot = bucket.DrawSlot,
            };
            uint vertexByteOffset = checked((uint)bucket.VertexOffset * (uint)sizeof(VertexPBR));
            _drawEntriesStaging[(int)bucket.DrawSlot] = new MeshScene3DDrawEntry
            {
                Vertices = bucket.Desc.Geometry.VertexBuffer.View.OffsetBy(vertexByteOffset),
                VisibleBase = bucket.VisibleBase,
                BucketTag = bucket.Desc.BucketTag,
            };
        }

        Alco.Graphics.Buffer metaPatchBuf = _transientBuffers.Acquire(context, (ulong)count * (ulong)sizeof(MeshScene3DBucketMeta));
        Alco.Graphics.Buffer slotBuf = _transientBuffers.Acquire(context, (ulong)count * sizeof(uint));
        metaPatchBuf.SetData<MeshScene3DBucketMeta>(_bucketMetaStaging.AsSpan(0, count));
        slotBuf.SetData<uint>(_bucketSlotIndices.AsSpan(0, count));

        Extent3D scatterGroups = new((uint)((count + 63) / 64), 1u, 1u);
        InstanceScatterRoot metaScatterRoot = new()
        {
            Patches = metaPatchBuf.View,
            PatchSlots = slotBuf.View,
            Pool = _bucketMetaBuffer.View,
            PatchCount = (uint)count,
            WordsPerInstance = (uint)(sizeof(MeshScene3DBucketMeta) / 4),
        };
        compute.Dispatch(_scatterShader, in metaScatterRoot, scatterGroups);

        Alco.Graphics.Buffer entryPatchBuf = _transientBuffers.Acquire(context, (ulong)count * (ulong)sizeof(MeshScene3DDrawEntry));
        entryPatchBuf.SetData<MeshScene3DDrawEntry>(_drawEntriesStaging.AsSpan(0, count));

        InstanceScatterRoot entryScatterRoot = new()
        {
            Patches = entryPatchBuf.View,
            PatchSlots = slotBuf.View,
            Pool = _drawEntriesBuffer.View,
            PatchCount = (uint)count,
            WordsPerInstance = (uint)(sizeof(MeshScene3DDrawEntry) / 4),
        };
        compute.Dispatch(_scatterShader, in entryScatterRoot, scatterGroups);

        compute.Barrier(
            Stage.Compute, Access.ShaderWrite,
            Stage.Compute, Access.ShaderRead);
    }

    private void GrowBucketStorage(int requiredBuckets)
    {
        int nextCapacity = _bucketCapacity;
        while (nextCapacity < requiredBuckets)
        {
            nextCapacity *= 2;
        }

        Array.Resize(ref _bucketMetaStaging, nextCapacity);
        Array.Resize(ref _drawEntriesStaging, nextCapacity);
        _bucketSlotIndices = BuildSequentialIndices(nextCapacity);
        _bucketMetaBuffer.Dispose();
        _drawEntriesBuffer.Dispose();
        _bucketMetaBuffer = _system.CreateBuffer((ulong)nextCapacity * (ulong)sizeof(MeshScene3DBucketMeta));
        _drawEntriesBuffer = _system.CreateBuffer((ulong)nextCapacity * (ulong)sizeof(MeshScene3DDrawEntry));
        _bucketCapacity = nextCapacity;
        _bucketsDirty = true;
    }

    private BufferSlice BuildPassRootSlice(
        RenderContext context,
        Material material,
        in Matrix4x4 viewProjection,
        GPUBufferView poolView,
        GPUBufferView visibleBaseView,
        Alco.Graphics.Buffer? matParams,
        uint firstDrawSlot,
        uint bucketTag,
        float time,
        Vector4 userParams,
        uint passFlags,
        Vector3 originOffset)
    {
        MeshScene3DPassHeader header = new()
        {
            ViewProjection = viewProjection,
            DrawEntries = _drawEntriesBuffer.GetView((ulong)firstDrawSlot * (ulong)sizeof(MeshScene3DDrawEntry)),
            Pool = poolView,
            VisibleSlots = visibleBaseView,
            MaterialParams = matParams != null ? matParams.View : default,
            UserParams = userParams,
            Time = time,
            BucketTag = bucketTag,
            HasMaterialParams = matParams != null ? 1u : 0u,
            PassFlags = passFlags,
            OriginOffset = originOffset,
        };

        uint rootSize = material.Root.SizeBytes;
        Span<byte> dest = context.AllocateTransient<byte>(checked((int)rootSize), out BufferSlice rootSlice);
        material.CopyRoot(dest);
        MemoryMarshal.Write(dest, in header);
        return rootSlice;
    }

    private int SubmitSingleBucket(
        RenderContext context,
        in RenderContext.RenderScope render,
        BucketState bucket,
        in Matrix4x4 viewProjection,
        float time,
        Vector4 userParams,
        uint passFlags,
        GPUBufferView poolView,
        GPUBufferView visibleBaseView,
        Alco.Graphics.Buffer drawArgsBuffer,
        Material? materialOverride,
        DepthStencilState? depthStencilOverride,
        Vector3 originOffset)
    {
        Material material = materialOverride ?? bucket.Desc.Material;
        DepthStencilState depthStencil = depthStencilOverride ?? bucket.Desc.DepthStencil;
        BufferSlice rootSlice = BuildPassRootSlice(
            context,
            material,
            in viewProjection,
            poolView,
            visibleBaseView,
            bucket.Desc.MaterialParamsBuffer,
            bucket.DrawSlot,
            bucket.Desc.BucketTag,
            time,
            userParams,
            passFlags,
            originOffset);

        render.DrawIndexedIndirect(
            material,
            rootSlice,
            depthStencil,
            bucket.Desc.Geometry.IndexBuffer.Slice(),
            bucket.Desc.Geometry.IndexType,
            drawArgsBuffer.Slice((ulong)bucket.DrawSlot * IndexedArgsStride, IndexedArgsStride),
            drawCount: 1u,
            stride: 0u);
        return 1;
    }

    private void EnsureDrawLayout()
    {
        if (!_layoutDirty)
        {
            return;
        }

        _tierKeys.Clear();
        foreach (int tierKey in _tiers.Keys)
        {
            _tierKeys.Add(tierKey);
        }
        _tierKeys.Sort();

        uint nextSlot = 0u;
        for (int t = 0; t < _tierKeys.Count; ++t)
        {
            List<BucketState> tierBuckets = _tiers[_tierKeys[t]];
            for (int i = 0; i < tierBuckets.Count; ++i)
            {
                BucketState b = tierBuckets[i];
                b.SortHeapId = b.IndexHeapOwner == null ? -1 : GetOrAddSortId(_heapSortIds, b.IndexHeapOwner);
                b.SortParamsId = b.Desc.MaterialParamsBuffer == null ? -1 : GetOrAddSortId(_paramsBufferSortIds, b.Desc.MaterialParamsBuffer);
                b.SortMaterialId = GetOrAddSortId(_materialSortIds, b.Desc.Material);
                b.SortDepthId = GetOrAddSortId(_depthSortIds, b.Desc.DepthStencil);
            }

            tierBuckets.Sort(static (a, b) =>
            {
                int c = b.CanBatchInPage.CompareTo(a.CanBatchInPage);
                if (c != 0) return c;
                c = a.SortHeapId.CompareTo(b.SortHeapId);
                if (c != 0) return c;
                c = ((int)a.Desc.Geometry.IndexType).CompareTo((int)b.Desc.Geometry.IndexType);
                if (c != 0) return c;
                c = a.SortParamsId.CompareTo(b.SortParamsId);
                if (c != 0) return c;
                c = a.SortMaterialId.CompareTo(b.SortMaterialId);
                if (c != 0) return c;
                c = a.SortDepthId.CompareTo(b.SortDepthId);
                if (c != 0) return c;
                return a.Id.CompareTo(b.Id);
            });

            for (int i = 0; i < tierBuckets.Count; ++i)
            {
                tierBuckets[i].DrawSlot = nextSlot++;
            }
        }

        _layoutDirty = false;
    }

    private static int GetOrAddSortId<TKey>(Dictionary<TKey, int> map, TKey key) where TKey : notnull
    {
        if (!map.TryGetValue(key, out int id))
        {
            id = map.Count;
            map.Add(key, id);
        }
        return id;
    }

    private void ValidateBucketId(uint bucketId)
    {
        if (bucketId >= (uint)_buckets.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(bucketId), bucketId, "Bucket id has not been registered.");
        }
    }

    private void VerifyLayouts()
    {
        if ((uint)sizeof(GpuDrawIndexedIndirectArgs) != IndexedArgsStride)
        {
            throw new InvalidOperationException("GpuDrawIndexedIndirectArgs stride mismatch.");
        }

        if ((uint)sizeof(MeshScene3DResetArgsRoot) != _resetArgsShader.Root.SizeBytes ||
            !_resetArgsShader.Reflection.MatchesStructLayout<MeshScene3DResetArgsRoot>("MeshScene3DResetArgsRoot"))
        {
            throw new InvalidOperationException("MeshScene3DResetArgs shader layout mismatch.");
        }

        if ((uint)sizeof(MeshScene3DCullRoot) != _cullShader.Root.SizeBytes ||
            !_cullShader.Reflection.MatchesStructLayout<MeshScene3DCullRoot>("MeshScene3DCullRoot"))
        {
            throw new InvalidOperationException("MeshScene3DCull shader layout mismatch.");
        }
    }

    private static uint[] BuildSequentialIndices(int count)
    {
        uint[] indices = new uint[count];
        for (int i = 0; i < count; ++i)
        {
            indices[i] = (uint)i;
        }

        return indices;
    }
}
