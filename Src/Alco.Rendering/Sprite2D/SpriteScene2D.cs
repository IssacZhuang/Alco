using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// GPU-driven bindless 2D scene renderer managing a single persistent instance pool,
/// workgroup-aggregated compute frustum culling, and multi-tier indirect draw submission.
/// </summary>
public sealed unsafe class SpriteScene2D : AutoDisposable
{
    private const int InitialBucketCapacity = 64;

    private sealed class BucketState
    {
        public required uint Id;
        public required SpriteSceneBucketDesc Desc;
        public required uint VertexCount;
        public required float LocalExtentScale;
        public required uint BucketTag;
        public uint VisibleBase;
    }

    private readonly record struct RetiredBuffer(Alco.Graphics.Buffer Buffer, RenderContext? Context, ulong FrameId);

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
    private struct SpriteSceneResetArgsRoot
    {
        public GPUBufferView BucketMeta;
        public GPUBufferView DrawArgs;
        public uint BucketCount;
        public uint Pad0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpriteSceneCullRoot
    {
        public Vector4 Viewport;
        public GPUBufferView Pool;
        public GPUBufferView SlotBuckets;
        public GPUBufferView BucketMeta;
        public GPUBufferView DrawArgs;
        public GPUBufferView VisibleSlots;
        public uint SlotCount;
        public uint BucketCount;
        public float CullMargin;
        public uint Pad0;
        public Vector2 OriginOffset;
    }

    private readonly RenderingSystem _system;
    private readonly GpuInstancePool<SpriteSceneInstance> _pool;
    private readonly TransientBufferPool _transientBuffers;
    private readonly Shader _scatterShader;
    private readonly Shader _resetArgsShader;
    private readonly Shader _cullShader;

    private readonly List<BucketState> _buckets = new();
    private readonly Dictionary<int, List<BucketState>> _tiers = new();
    private SpriteSceneBucketMeta[] _bucketMetaStaging = new SpriteSceneBucketMeta[InitialBucketCapacity];
    private uint[] _bucketSlotIndices = BuildSequentialIndices(InitialBucketCapacity);
    private readonly List<RetiredBuffer> _retiredBuffers = new();

    private Alco.Graphics.Buffer _bucketMetaBuffer;
    private Alco.Graphics.Buffer _drawArgsBuffer;
    private int _bucketCapacity = InitialBucketCapacity;
    private Alco.Graphics.Buffer _visibleSlotsBuffer;
    private int _visibleSlotsCapacity;
    private bool _bucketsDirty = true;
    private int _drawCallsSubmitted;
    private RenderContext? _lastContext;
    private ulong _lastFrameId;

    /// <summary>Creates a GPU-driven 2D sprite scene backed by <paramref name="system"/>.</summary>
    /// <param name="system">The rendering system owning GPU buffers and descriptor heaps.</param>
    /// <param name="shaders">Optional shader system override; defaults to <see cref="RenderingSystem.Shaders"/>.</param>
    public SpriteScene2D(RenderingSystem system, ShaderSystem? shaders = null)
    {
        _system = system ?? throw new ArgumentNullException(nameof(system));
        ShaderSystem resolvedShaders = shaders ?? system.Shaders;
        AlcoSprite2DShaders.EnsureBuiltInModulesLoaded(resolvedShaders);

        _pool = new GpuInstancePool<SpriteSceneInstance>(system, resolvedShaders);
        _transientBuffers = new TransientBufferPool(system);

        _scatterShader = resolvedShaders.GetShader(AlcoSprite2DShaders.GpuInstanceScatterModule, ShaderKind.Compute);
        _resetArgsShader = resolvedShaders.GetShader(AlcoSprite2DShaders.SpriteSceneResetArgsModule, ShaderKind.Compute);
        _cullShader = resolvedShaders.GetShader(AlcoSprite2DShaders.SpriteSceneCullModule, ShaderKind.Compute);

        VerifyLayouts();

        _bucketMetaBuffer = system.CreateBuffer((ulong)_bucketCapacity * (ulong)sizeof(SpriteSceneBucketMeta));
        _drawArgsBuffer = system.CreateBuffer((ulong)_bucketCapacity * (ulong)sizeof(GpuDrawIndirectArgs));
        _visibleSlotsCapacity = _pool.SlotCapacity;
        _visibleSlotsBuffer = system.CreateBuffer((ulong)_visibleSlotsCapacity * sizeof(uint));
    }

    /// <summary>Gets the GPU buffer containing per-bucket <see cref="GpuDrawIndirectArgs"/> records.</summary>
    public Alco.Graphics.Buffer DrawArgsBuffer => _drawArgsBuffer;

    /// <summary>Gets the current number of registered draw buckets.</summary>
    public int BucketCount => _buckets.Count;

    /// <summary>Gets the scene telemetry from the latest cull and render calls.</summary>
    public SpriteSceneStats Stats => new(
        _pool.TotalSlots,
        _pool.LiveCount,
        _buckets.Count,
        _pool.LastDirtyPatchCount,
        _drawCallsSubmitted,
        _pool.ActiveTextureCount);

    /// <summary>Reserves backing GPU storage for at least <paramref name="minSlots"/> instances and optional <paramref name="minBuckets"/> buckets.</summary>
    /// <param name="minSlots">Minimum slot count to pre-allocate.</param>
    /// <param name="minBuckets">Optional minimum bucket capacity to pre-allocate.</param>
    public void EnsureCapacity(int minSlots, int minBuckets = 0)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (minSlots > 0)
        {
            _pool.EnsureSlotCapacity(minSlots);
            if (_pool.SlotCapacity > _visibleSlotsCapacity)
            {
                ReplaceVisibleSlotsBuffer(_pool.SlotCapacity);
            }
        }

        if (minBuckets > _bucketCapacity)
        {
            GrowBucketStorage(minBuckets);
        }
    }

    /// <summary>Registers a new draw bucket and returns its stable bucket identifier.</summary>
    /// <param name="desc">Bucket tier, material, depth-stencil state, procedural vertex settings, and optional parameter buffer.</param>
    /// <returns>Zero-based bucket identifier.</returns>
    public uint RegisterBucket(in SpriteSceneBucketDesc desc)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(desc.Material);

        if (desc.VertexCount == 0u)
        {
            throw new ArgumentOutOfRangeException(nameof(desc), "Bucket vertex count must be greater than zero.");
        }

        if (desc.LocalExtentScale <= 0f || !float.IsFinite(desc.LocalExtentScale))
        {
            throw new ArgumentOutOfRangeException(nameof(desc), "Bucket local extent scale must be a positive finite number.");
        }

        if (_buckets.Count >= _bucketCapacity)
        {
            GrowBucketStorage(_buckets.Count + 1);
        }

        if (desc.Material.Shader.Kind != ShaderKind.Graphics ||
            desc.Material.Root.SizeBytes < (uint)sizeof(SpriteScenePassHeader))
        {
            throw new ArgumentException(
                $"SpriteScene bucket material '{desc.Material.Name}' must be graphics with root size >= {sizeof(SpriteScenePassHeader)} bytes.",
                nameof(desc));
        }

        uint bucketId = (uint)_buckets.Count;
        BucketState state = new()
        {
            Id = bucketId,
            Desc = desc,
            VertexCount = desc.VertexCount,
            LocalExtentScale = desc.LocalExtentScale,
            BucketTag = desc.BucketTag,
        };
        _buckets.Add(state);

        if (!_tiers.TryGetValue(desc.Tier, out List<BucketState>? tierList))
        {
            tierList = new List<BucketState>(4);
            _tiers[desc.Tier] = tierList;
        }

        tierList.Add(state);
        _bucketsDirty = true;
        return bucketId;
    }

    /// <summary>Gets the live instance count currently registered in <paramref name="bucketId"/>.</summary>
    /// <param name="bucketId">Bucket identifier returned by <see cref="RegisterBucket"/>.</param>
    /// <returns>The live instance count.</returns>
    public int GetBucketLiveCount(uint bucketId) => _pool.GetBucketCount(bucketId);

    /// <summary>Queues a thread-safe deferred sprite registration returning a disposable slot handle.</summary>
    /// <param name="bucketId">Bucket identifier returned by <see cref="RegisterBucket"/>.</param>
    /// <param name="initialData">Initial sprite instance data.</param>
    /// <param name="primaryTexture">Optional primary texture to retain while registered.</param>
    /// <param name="auxTexture">Optional auxiliary texture (mask/noise) to retain while registered.</param>
    /// <returns>The registration handle.</returns>
    public GpuInstancePool<SpriteSceneInstance>.Registration RegisterDeferred(
        uint bucketId,
        in SpriteSceneInstance initialData,
        Texture? primaryTexture = null,
        Texture? auxTexture = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ValidateBucketId(bucketId);
        return _pool.RegisterDeferred(bucketId, in initialData, primaryTexture, auxTexture);
    }

    /// <summary>Adds a direct slot on the render thread (for bulk terrain/decal/filth data).</summary>
    /// <param name="bucketId">Bucket identifier returned by <see cref="RegisterBucket"/>.</param>
    /// <param name="data">Initial sprite instance data.</param>
    /// <param name="primaryTexture">Optional primary texture to retain while live.</param>
    /// <param name="auxTexture">Optional auxiliary texture to retain while live.</param>
    /// <returns>The allocated global slot index.</returns>
    public int AddDirect(
        uint bucketId,
        in SpriteSceneInstance data,
        Texture? primaryTexture = null,
        Texture? auxTexture = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ValidateBucketId(bucketId);
        return _pool.AddDirect(bucketId, in data, primaryTexture, auxTexture);
    }

    /// <summary>Updates a direct slot on the render thread.</summary>
    /// <param name="slot">Global slot index returned by <see cref="AddDirect"/>.</param>
    /// <param name="data">Updated sprite instance data.</param>
    /// <param name="primaryTexture">Optional primary texture to retain.</param>
    /// <param name="auxTexture">Optional auxiliary texture to retain.</param>
    public void UpdateDirect(
        int slot,
        in SpriteSceneInstance data,
        Texture? primaryTexture = null,
        Texture? auxTexture = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _pool.UpdateDirect(slot, in data, primaryTexture, auxTexture);
    }

    /// <summary>Removes and recycles a direct slot on the render thread.</summary>
    /// <param name="slot">Global slot index returned by <see cref="AddDirect"/>.</param>
    public void RemoveDirect(int slot)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _pool.RemoveDirect(slot);
    }

    /// <summary>Applies pending deferred registrations/detachments without dispatching GPU work.</summary>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_pool.Flush())
        {
            _bucketsDirty = true;
        }
    }

    /// <summary>
    /// Syncs dirty CPU mirror slots to the GPU, updates bucket layout metadata when membership
    /// changed, resets indirect draw args, and dispatches frustum culling.
    /// </summary>
    /// <param name="context">Active recording context outside any open scope.</param>
    /// <param name="viewport">Camera culling rectangle in origin-relative space (world space when <paramref name="originOffset"/> is zero).</param>
    /// <param name="cullMargin">Extra world-space margin added around each instance's AABB.</param>
    /// <param name="originOffset">Origin offset subtracted from instance positions before culling to preserve precision at large coordinates.</param>
    public void PrepareAndCull(RenderContext context, Rect viewport, float cullMargin = 2f, Vector2 originOffset = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        _drawCallsSubmitted = 0;
        ReclaimRetiredBuffers();

        if (_buckets.Count == 0)
        {
            _pool.Flush();
            return;
        }

        // Scene buffers are shared by all in-flight frames, unlike the frame's root storage.
        context.Barrier(
            Stage.Compute | Stage.Indirect | Stage.Vertex,
            Access.ShaderRead | Access.ShaderWrite | Access.IndirectRead,
            Stage.Compute, Access.ShaderRead | Access.ShaderWrite);

        using (RenderContext.ComputeScope compute = context.BeginCompute())
        {
            bool membershipChanged = _pool.SyncToGpu(context, compute);
            if (_pool.SlotCapacity > _visibleSlotsCapacity)
            {
                ReplaceVisibleSlotsBuffer(_pool.SlotCapacity);
            }

            if (membershipChanged || _bucketsDirty)
            {
                UploadBucketMeta(context, compute);
                _bucketsDirty = false;
            }

            GPURange resetRoot = context.AllocateRoot(new SpriteSceneResetArgsRoot
            {
                BucketMeta = _bucketMetaBuffer.View,
                DrawArgs = _drawArgsBuffer.View,
                BucketCount = (uint)_buckets.Count,
            });
            compute.Dispatch(_resetArgsShader, resetRoot, new Extent3D((uint)((_buckets.Count + 63) / 64), 1u, 1u));

            if (_pool.TotalSlots > 0)
            {
                compute.Barrier(
                    Stage.Compute, Access.ShaderWrite,
                    Stage.Compute, Access.ShaderRead | Access.ShaderWrite);

                Vector2 vpMin = viewport.Min;
                Vector2 vpMax = viewport.Max;
                GPURange cullRoot = context.AllocateRoot(new SpriteSceneCullRoot
                {
                    Viewport = new Vector4(vpMin.X, vpMin.Y, vpMax.X, vpMax.Y),
                    Pool = _pool.PoolBuffer.View,
                    SlotBuckets = _pool.SlotBucketBuffer.View,
                    BucketMeta = _bucketMetaBuffer.View,
                    DrawArgs = _drawArgsBuffer.View,
                    VisibleSlots = _visibleSlotsBuffer.View,
                    SlotCount = (uint)_pool.TotalSlots,
                    BucketCount = (uint)_buckets.Count,
                    CullMargin = cullMargin,
                    OriginOffset = originOffset,
                });
                compute.Dispatch(_cullShader, cullRoot, new Extent3D((uint)((_pool.TotalSlots + 63) / 64), 1u, 1u));
            }
        }

        context.Barrier(
            Stage.Compute, Access.ShaderWrite,
            Stage.Indirect | Stage.Vertex,
            Access.IndirectRead | Access.ShaderRead);

        _lastContext = context;
        _lastFrameId = context.CurrentFrameId;
    }

    /// <summary>Records indirect draws for all non-empty buckets belonging to <paramref name="tier"/>.</summary>
    /// <param name="context">Active recording context.</param>
    /// <param name="render">Open render pass scope.</param>
    /// <param name="tier">Tier identifier matching <see cref="SpriteSceneBucketDesc.Tier"/>.</param>
    /// <param name="camera">Camera providing the view-projection matrix.</param>
    /// <param name="time">Elapsed world time in seconds.</param>
    /// <param name="userParams">Optional vector forwarded to surfaces.</param>
    /// <param name="originOffset">Origin offset subtracted from instance positions before vertex transformation.</param>
    /// <returns>The number of indirect draw calls submitted for this tier.</returns>
    public int RenderTier(
        RenderContext context,
        in RenderContext.RenderScope render,
        int tier,
        ICamera camera,
        float time = 0f,
        Vector4 userParams = default,
        Vector2 originOffset = default)
    {
        ArgumentNullException.ThrowIfNull(camera);
        return RenderTier(context, in render, tier, camera.ViewProjectionMatrix, time, userParams, originOffset);
    }

    /// <summary>Records indirect draws for all non-empty buckets belonging to <paramref name="tier"/>.</summary>
    /// <param name="context">Active recording context.</param>
    /// <param name="render">Open render pass scope.</param>
    /// <param name="tier">Tier identifier matching <see cref="SpriteSceneBucketDesc.Tier"/>.</param>
    /// <param name="viewProjection">Row-major camera view-projection matrix (origin-relative when <paramref name="originOffset"/> is non-zero).</param>
    /// <param name="time">Elapsed world time in seconds.</param>
    /// <param name="userParams">Optional vector forwarded to surfaces.</param>
    /// <param name="originOffset">Origin offset subtracted from instance positions before vertex transformation.</param>
    /// <returns>The number of indirect draw calls submitted for this tier.</returns>
    public int RenderTier(
        RenderContext context,
        in RenderContext.RenderScope render,
        int tier,
        in Matrix4x4 viewProjection,
        float time = 0f,
        Vector4 userParams = default,
        Vector2 originOffset = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (!_tiers.TryGetValue(tier, out List<BucketState>? tierBuckets) || tierBuckets.Count == 0)
        {
            return 0;
        }

        int submitted = 0;
        GPUBufferView poolView = _pool.PoolBuffer.View;
        GPUBufferView visibleBaseView = _visibleSlotsBuffer.View;

        for (int i = 0; i < tierBuckets.Count; ++i)
        {
            BucketState bucket = tierBuckets[i];
            if (_pool.GetBucketCount(bucket.Id) == 0)
            {
                continue;
            }

            Material material = bucket.Desc.Material;
            Alco.Graphics.Buffer? matParams = bucket.Desc.MaterialParamsBuffer;
            SpriteScenePassHeader header = new()
            {
                ViewProjection = viewProjection,
                Pool = poolView,
                VisibleSlots = visibleBaseView.OffsetBy(bucket.VisibleBase * sizeof(uint)),
                MaterialParams = matParams != null ? matParams.View : default,
                Time = time,
                BucketTag = bucket.BucketTag,
                UserParams = userParams,
                HasMaterialParams = matParams != null ? 1u : 0u,
                OriginOffset = originOffset,
            };

            uint rootSize = material.Root.SizeBytes;
            GPUAllocation<byte> rootAlloc = context.FrameData.Allocate(rootSize);
            if (rootAlloc.SizeBytes == 0)
            {
                throw new InvalidOperationException("The recording frame's root storage is exhausted.");
            }

            Span<byte> dest = rootAlloc.CpuSpan;
            material.CopyRoot(dest);
            MemoryMarshal.Write(dest, in header);

            render.DrawIndirect(
                material,
                rootAlloc.GPURange,
                bucket.Desc.DepthStencil,
                _drawArgsBuffer,
                drawCount: 1u,
                stride: 0u,
                argumentByteOffset: (ulong)bucket.Id * (ulong)sizeof(GpuDrawIndirectArgs));
            ++submitted;
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

        _pool.Dispose();
        _transientBuffers.Dispose();

        for (int i = 0; i < _retiredBuffers.Count; ++i)
        {
            if (!_retiredBuffers[i].Buffer.IsDestroyed)
            {
                _retiredBuffers[i].Buffer.Destroy();
            }
        }

        _retiredBuffers.Clear();
        if (!_bucketMetaBuffer.IsDestroyed)
        {
            _bucketMetaBuffer.Destroy();
        }

        if (!_drawArgsBuffer.IsDestroyed)
        {
            _drawArgsBuffer.Destroy();
        }

        if (!_visibleSlotsBuffer.IsDestroyed)
        {
            _visibleSlotsBuffer.Destroy();
        }
    }

    private void UploadBucketMeta(RenderContext context, in RenderContext.ComputeScope compute)
    {
        int count = _buckets.Count;
        uint prefix = 0;
        for (int i = 0; i < count; ++i)
        {
            BucketState bucket = _buckets[i];
            bucket.VisibleBase = prefix;
            prefix += (uint)_pool.GetBucketCount(bucket.Id);
            _bucketMetaStaging[i] = new SpriteSceneBucketMeta
            {
                VertexCount = bucket.VertexCount,
                VisibleBase = bucket.VisibleBase,
                LocalExtentScale = bucket.LocalExtentScale,
                BucketTag = bucket.BucketTag,
            };
        }

        Alco.Graphics.Buffer patchBuf = _transientBuffers.Acquire(context, (ulong)count * (ulong)sizeof(SpriteSceneBucketMeta));
        Alco.Graphics.Buffer slotBuf = _transientBuffers.Acquire(context, (ulong)count * sizeof(uint));
        patchBuf.SetData<SpriteSceneBucketMeta>(_bucketMetaStaging.AsSpan(0, count));
        slotBuf.SetData<uint>(_bucketSlotIndices.AsSpan(0, count));

        GPURange scatterRoot = context.AllocateRoot(new InstanceScatterRoot
        {
            Patches = patchBuf.View,
            PatchSlots = slotBuf.View,
            Pool = _bucketMetaBuffer.View,
            PatchCount = (uint)count,
            WordsPerInstance = (uint)(sizeof(SpriteSceneBucketMeta) / 4),
        });
        compute.Dispatch(_scatterShader, scatterRoot, new Extent3D((uint)((count + 63) / 64), 1u, 1u));
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
        _bucketSlotIndices = BuildSequentialIndices(nextCapacity);
        _retiredBuffers.Add(new RetiredBuffer(_bucketMetaBuffer, _lastContext, _lastFrameId));
        _retiredBuffers.Add(new RetiredBuffer(_drawArgsBuffer, _lastContext, _lastFrameId));
        _bucketMetaBuffer = _system.CreateBuffer((ulong)nextCapacity * (ulong)sizeof(SpriteSceneBucketMeta));
        _drawArgsBuffer = _system.CreateBuffer((ulong)nextCapacity * (ulong)sizeof(GpuDrawIndirectArgs));
        _bucketCapacity = nextCapacity;
        _bucketsDirty = true;
    }

    private void ReplaceVisibleSlotsBuffer(int nextCapacity)
    {
        _retiredBuffers.Add(new RetiredBuffer(_visibleSlotsBuffer, _lastContext, _lastFrameId));
        _visibleSlotsCapacity = nextCapacity;
        _visibleSlotsBuffer = _system.CreateBuffer((ulong)nextCapacity * sizeof(uint));
    }

    private void ReclaimRetiredBuffers()
    {
        for (int i = _retiredBuffers.Count - 1; i >= 0; --i)
        {
            RetiredBuffer entry = _retiredBuffers[i];
            if (entry.Context != null && !entry.Context.IsFrameCompleted(entry.FrameId))
            {
                continue;
            }

            if (!entry.Buffer.IsDestroyed)
            {
                entry.Buffer.Destroy();
            }

            _retiredBuffers.RemoveAt(i);
        }
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
        if ((uint)sizeof(SpriteSceneResetArgsRoot) != _resetArgsShader.Root.SizeBytes ||
            !_resetArgsShader.Reflection.MatchesStructLayout<SpriteSceneResetArgsRoot>("SpriteSceneResetArgsRoot"))
        {
            throw new InvalidOperationException("SpriteSceneResetArgs shader layout mismatch.");
        }

        if ((uint)sizeof(SpriteSceneCullRoot) != _cullShader.Root.SizeBytes ||
            !_cullShader.Reflection.MatchesStructLayout<SpriteSceneCullRoot>("SpriteSceneCullRoot"))
        {
            throw new InvalidOperationException("SpriteSceneCull shader layout mismatch.");
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
