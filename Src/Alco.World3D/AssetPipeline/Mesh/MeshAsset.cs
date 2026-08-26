using System.Diagnostics.CodeAnalysis;
using System.IO;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Descriptor of one LOD level of a <see cref="MeshAsset"/>.
/// </summary>
public readonly struct MeshAssetLod
{
    /// <summary>Number of vertices of the LOD.</summary>
    public uint VertexCount { get; }

    /// <summary>Number of indices of the LOD.</summary>
    public uint IndexCount { get; }

    /// <summary>Maximum geometric error of the LOD relative to the source.</summary>
    public float MaxError { get; }

    /// <summary>LOD bounds.</summary>
    public BoundingBox3D Bounds { get; }

    /// <summary>Content entry name of the vertex payload.</summary>
    public string VertexEntry { get; }

    /// <summary>Content entry name of the index payload.</summary>
    public string IndexEntry { get; }

    /// <summary>Creates a LOD descriptor.</summary>
    /// <param name="vertexCount">Vertex count.</param>
    /// <param name="indexCount">Index count.</param>
    /// <param name="maxError">Maximum geometric error.</param>
    /// <param name="bounds">LOD bounds.</param>
    /// <param name="vertexEntry">Vertex payload entry name.</param>
    /// <param name="indexEntry">Index payload entry name.</param>
    public MeshAssetLod(uint vertexCount, uint indexCount, float maxError, in BoundingBox3D bounds, string vertexEntry, string indexEntry)
    {
        VertexCount = vertexCount;
        IndexCount = indexCount;
        MaxError = maxError;
        Bounds = bounds;
        VertexEntry = vertexEntry;
        IndexEntry = indexEntry;
    }
}

/// <summary>
/// Descriptor of one submesh (material slot) of a <see cref="MeshAsset"/>: a named
/// index range. Materials bind to slot names in the composition layer (prefab) — the mesh
/// itself carries no material references.
/// </summary>
public readonly struct MeshAssetSubMesh
{
    /// <summary>The slot name (typically the source material name).</summary>
    public string Name { get; }

    /// <summary>First index in the owning LOD's index buffer.</summary>
    public uint FirstIndex { get; }

    /// <summary>Number of indices of the submesh.</summary>
    public uint IndexCount { get; }

    /// <summary>Creates a submesh descriptor.</summary>
    /// <param name="name">The slot name.</param>
    /// <param name="firstIndex">First index in the owning LOD's index buffer.</param>
    /// <param name="indexCount">Number of indices.</param>
    public MeshAssetSubMesh(string name, uint firstIndex, uint indexCount)
    {
        Name = name;
        FirstIndex = firstIndex;
        IndexCount = indexCount;
    }
}

/// <summary>
/// Lightweight handle to a mesh asset (.amsh): the parsed meta tables plus a positional
/// file reader. Holds no geometry in memory — geometry streams into GPU residency on demand via
/// <see cref="LoadLodAsync"/>. Bounds and structure are available before any payload load.
/// </summary>
public sealed class MeshAsset : AutoDisposable
{
    // Feature flags this runtime consumes; anything else on the load path is rejected.
    private const MeshAssetFlags SupportedFlags = MeshAssetFlags.Interleaved | MeshAssetFlags.HasLods;

    private readonly MeshAssetReader _reader;
    private readonly GPUDevice? _device;
    private readonly MeshVertexStream[] _streams;
    private readonly MeshAssetLod[] _lods;
    private readonly MeshAssetSubMesh[] _subMeshes;
    private readonly uint[] _lodSubMeshFirst;
    private readonly uint[] _lodSubMeshCount;
    private readonly StreamableMesh?[] _residencies;
    private readonly Task<StreamableMesh>?[] _loading;
    private readonly object _lock = new();

    /// <summary>
    /// Creates a handle over an opened reader. Pass a device to enable GPU residency loads;
    /// structure queries work without one.
    /// </summary>
    /// <param name="reader">The reader; ownership transfers to this asset.</param>
    /// <param name="device">The GPU device for residency uploads, null for header-only usage.</param>
    internal MeshAsset(MeshAssetReader reader, GPUDevice? device)
    {
        _reader = reader;
        _device = device;

        MeshAssetMeta meta = reader.Meta;

        Name = meta.Name;
        Bounds = meta.Bounds;
        Flags = meta.Flags;
        LodCount = meta.Lods.Count;
        HasClusters = Flags.HasFlag(MeshAssetFlags.HasClusters);

        _streams = new MeshVertexStream[meta.Streams.Count];
        for (int i = 0; i < _streams.Length; i++)
        {
            VertexStreamMeta stream = meta.Streams[i];
            _streams[i] = new MeshVertexStream
            {
                Semantic = stream.Semantic,
                Format = stream.Format,
                Offset = stream.Offset,
                Stride = stream.Stride,
                QuantBounds = stream.QuantBounds,
            };
        }

        _lods = new MeshAssetLod[meta.Lods.Count];
        _lodSubMeshFirst = new uint[meta.Lods.Count];
        _lodSubMeshCount = new uint[meta.Lods.Count];
        for (int i = 0; i < _lods.Length; i++)
        {
            MeshLodMeta lod = meta.Lods[i];
            _lods[i] = new MeshAssetLod(lod.VertexCount, lod.IndexCount, lod.MaxError, lod.Bounds, lod.VertexEntry, lod.IndexEntry);
            _lodSubMeshFirst[i] = lod.SubMeshFirst;
            _lodSubMeshCount[i] = lod.SubMeshCount;

            if (lod.SubMeshFirst + lod.SubMeshCount > (uint)meta.SubMeshes.Count)
            {
                throw new InvalidDataException($"Mesh asset '{meta.Name}' LOD {i} submesh range [{lod.SubMeshFirst}, {lod.SubMeshFirst + lod.SubMeshCount}) exceeds the submesh table ({meta.SubMeshes.Count} entries).");
            }
        }

        _subMeshes = new MeshAssetSubMesh[meta.SubMeshes.Count];
        for (int i = 0; i < _subMeshes.Length; i++)
        {
            MeshSubMeshMeta subMesh = meta.SubMeshes[i];
            _subMeshes[i] = new MeshAssetSubMesh(subMesh.Name, subMesh.FirstIndex, subMesh.IndexCount);
        }

        // Residency/loading slots are indexed by LOD; the count is fixed by the meta.
        _residencies = new StreamableMesh?[meta.Lods.Count];
        _loading = new Task<StreamableMesh>?[meta.Lods.Count];
    }

    /// <summary>Gets the mesh name.</summary>
    public string Name { get; }

    /// <summary>Whole-mesh bounds, available before any payload load.</summary>
    public BoundingBox3D Bounds { get; }

    /// <summary>Feature flags of the file.</summary>
    public MeshAssetFlags Flags { get; }

    /// <summary>Number of LOD levels in the file.</summary>
    public int LodCount { get; }

    /// <summary>Whether the file carries a cluster table (M3 virtual geometry capability).</summary>
    public bool HasClusters { get; }

    /// <summary>Vertex stream descriptors of the interleaved payload.</summary>
    public ReadOnlySpan<MeshVertexStream> Streams => _streams;

    /// <summary>
    /// Get the submesh (material slot) descriptors of one LOD. Materials bind to slot names
    /// externally (prefab layer), never inside the mesh; slot order is stable across LODs, so
    /// <c>GetSubMeshes(0)</c> is the canonical slot list before any payload load.
    /// </summary>
    /// <param name="lodIndex">The LOD index.</param>
    /// <returns>The LOD's submesh descriptors.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the index is out of range.</exception>
    public ReadOnlySpan<MeshAssetSubMesh> GetSubMeshes(int lodIndex)
    {
        if ((uint)lodIndex >= (uint)_lods.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(lodIndex), lodIndex, "LOD index out of range.");
        }

        return _subMeshes.AsSpan((int)_lodSubMeshFirst[lodIndex], (int)_lodSubMeshCount[lodIndex]);
    }

    /// <summary>
    /// Open a handle over a seekable stream. The stream ownership transfers to the returned asset.
    /// </summary>
    /// <param name="stream">The seekable mesh asset stream.</param>
    /// <param name="device">The GPU device for residency uploads, null for header-only usage.</param>
    /// <returns>The asset handle.</returns>
    internal static MeshAsset FromStream(Stream stream, GPUDevice? device)
    {
        return new MeshAsset(MeshAssetReader.Open(stream, string.Empty), device);
    }

    /// <summary>
    /// Open a handle over mesh asset bytes held in memory.
    /// </summary>
    /// <param name="data">The mesh asset bytes.</param>
    /// <param name="device">The GPU device for residency uploads, null for header-only usage.</param>
    /// <returns>The asset handle.</returns>
    internal static MeshAsset FromMemory(ReadOnlySpan<byte> data, GPUDevice? device)
    {
        return new MeshAsset(MeshAssetReader.OpenMemory(data), device);
    }

    /// <summary>
    /// Gets a LOD descriptor.
    /// </summary>
    /// <param name="lodIndex">The LOD index.</param>
    /// <returns>The LOD descriptor.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the index is out of range.</exception>
    public ref readonly MeshAssetLod GetLod(int lodIndex)
    {
        if ((uint)lodIndex >= (uint)_lods.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(lodIndex), lodIndex, "LOD index out of range.");
        }

        return ref _lods[lodIndex];
    }

    /// <summary>
    /// Stream one LOD into GPU residency: reads its chunks on worker threads, uploads on the
    /// thread that owns the GPU device (the synchronization context captured at call time, or
    /// the calling thread when none), and completes with the resident mesh. Idempotent per
    /// (asset, lod): concurrent calls share one task.
    /// </summary>
    /// <param name="lodIndex">The LOD index to load.</param>
    /// <returns>A task completing with the resident <see cref="StreamableMesh"/>.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the stream has been disposed.</exception>
    /// <exception cref="NotSupportedException">Thrown when the file uses unsupported features.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no GPU device is bound or the LOD is released mid-load.</exception>
    public Task<StreamableMesh> LoadLodAsync(int lodIndex)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if ((uint)lodIndex >= (uint)_lods.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(lodIndex), lodIndex, "LOD index out of range.");
        }

        if (_device == null)
        {
            throw new InvalidOperationException("The streamable mesh has no GPU device bound; open it through a rendering system to load LODs.");
        }

        lock (_lock)
        {
            StreamableMesh? resident = _residencies[lodIndex];
            if (resident != null)
            {
                return Task.FromResult(resident);
            }

            Task<StreamableMesh>? existing = _loading[lodIndex];
            if (existing != null)
            {
                return existing;
            }

            Task<StreamableMesh> task = LoadLodCoreAsync(lodIndex, SynchronizationContext.Current);
            _loading[lodIndex] = task;
            return task;
        }
    }

    /// <summary>
    /// Resident LOD lookup. Out-of-range indices report not resident.
    /// </summary>
    /// <param name="lodIndex">The LOD index.</param>
    /// <param name="mesh">The resident mesh when loaded.</param>
    /// <returns>True while resident; false while loading, after failure, after release or after disposal.</returns>
    public bool TryGetLoadedLod(int lodIndex, [NotNullWhen(true)] out StreamableMesh? mesh)
    {
        if (IsDisposed || (uint)lodIndex >= (uint)_residencies.Length)
        {
            mesh = null;
            return false;
        }

        lock (_lock)
        {
            mesh = _residencies[lodIndex];
            return mesh != null;
        }
    }

    /// <summary>
    /// Dispose the GPU residency of one LOD. Call from the thread that owns the GPU device.
    /// Out-of-range indices are a no-op.
    /// </summary>
    /// <param name="lodIndex">The LOD index.</param>
    public void ReleaseLod(int lodIndex)
    {
        StreamableMesh? mesh;
        lock (_lock)
        {
            if ((uint)lodIndex >= (uint)_residencies.Length)
            {
                return;
            }

            mesh = _residencies[lodIndex];
            _residencies[lodIndex] = null;
        }

        mesh?.Dispose();
    }

    private async Task<StreamableMesh> LoadLodCoreAsync(int lodIndex, SynchronizationContext? synchronizationContext)
    {
        try
        {
            MeshAssetLod lod = _lods[lodIndex];
            MeshChunkMeta vertexChunk = _reader.GetChunk(lod.VertexEntry);
            MeshChunkMeta indexChunk = _reader.GetChunk(lod.IndexEntry);
            ValidateChunk(vertexChunk, lod.VertexEntry);
            ValidateChunk(indexChunk, lod.IndexEntry);

            // Read payloads on a worker; the reader supports concurrent positional reads.
            using SafeMemoryHandle vertexData = new((int)(lod.VertexCount * (long)_streams[0].Stride));
            using SafeMemoryHandle indexData = new((int)(lod.IndexCount * (long)sizeof(uint)));
            await Task.Run(() =>
            {
                _reader.ReadChunk(vertexChunk, vertexData);
                _reader.ReadChunk(indexChunk, indexData);
            }).ConfigureAwait(false);

            // GPU work must run on the device-owning thread: hop to the captured context when
            // the continuation landed elsewhere (e.g. on the worker).
            Task<StreamableMesh> upload;
            if (synchronizationContext != null && SynchronizationContext.Current != synchronizationContext)
            {
                TaskCompletionSource<StreamableMesh> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                synchronizationContext.Post(_ =>
                {
                    try
                    {
                        completion.SetResult(CreateResidency(lodIndex, vertexData, indexData));
                    }
                    catch (Exception exception)
                    {
                        completion.SetException(exception);
                    }
                }, null);
                upload = completion.Task;
            }
            else
            {
                upload = Task.FromResult(CreateResidency(lodIndex, vertexData, indexData));
            }

            StreamableMesh mesh = await upload.ConfigureAwait(false);

            lock (_lock)
            {
                _loading[lodIndex] = null;

                if (IsDisposed)
                {
                    // The asset was disposed mid-load: drop the residency instead of caching it into a
                    // dead asset. Its GPU buffers reclaim through their own finalizer-safe path; calling
                    // Dispose here would run on a worker thread, off the device-owning thread.
                    throw new ObjectDisposedException(nameof(MeshAsset), $"Mesh asset '{Name}' was disposed while LOD {lodIndex} was loading.");
                }

                _residencies[lodIndex] = mesh;
            }

            return mesh;
        }
        catch
        {
            // A failed load must not pin the slot: clear it so the next LoadLodAsync can retry.
            lock (_lock)
            {
                _loading[lodIndex] = null;
            }

            throw;
        }
    }

    private StreamableMesh CreateResidency(int lodIndex, SafeMemoryHandle vertexData, SafeMemoryHandle indexData)
    {
        MeshAssetLod lod = _lods[lodIndex];
        uint stride = _streams.Length > 0 ? _streams[0].Stride : 0;

        StreamableMesh mesh = new(_device!, (uint)vertexData.AsReadOnlySpan().Length, (uint)indexData.AsReadOnlySpan().Length, stride, lodIndex, Name);
        mesh.UploadVertex(vertexData.AsReadOnlySpan());
        mesh.UploadIndices(indexData.AsReadOnlySpan());

        ReadOnlySpan<MeshAssetSubMesh> subMeshTable = GetSubMeshes(lodIndex);
        Span<SubMeshData> subMeshes = stackalloc SubMeshData[subMeshTable.Length];
        for (int i = 0; i < subMeshTable.Length; i++)
        {
            MeshAssetSubMesh subMesh = subMeshTable[i];
            subMeshes[i] = new SubMeshData
            {
                Index = i,
                VertexOffset = 0,
                VertexSize = mesh.VertexBuffer.Size,
                IndexOffset = (ulong)(subMesh.FirstIndex * sizeof(uint)),
                IndexSize = (ulong)(subMesh.IndexCount * sizeof(uint)),
                IndexCount = subMesh.IndexCount,
                IndexFormat = Alco.Graphics.IndexFormat.UInt32,
            };
        }

        mesh.SetSubMeshes(subMeshes);
        mesh.MarkReady();
        return mesh;
    }

    private void ValidateChunk(MeshChunkMeta chunk, string entryName)
    {
        if (chunk.Codec != MeshChunkCodec.None)
        {
            throw new NotSupportedException($"Mesh asset entry '{entryName}' uses unsupported codec {chunk.Codec}.");
        }

        if ((_reader.Meta.Flags & ~SupportedFlags) != 0)
        {
            // Quantization/paging features affect payload interpretation; reject per LOD load.
            MeshAssetFlags unsupported = _reader.Meta.Flags & ~SupportedFlags;
            throw new NotSupportedException($"Mesh asset '{Name}' uses unsupported features: {unsupported}.");
        }
    }

    /// <summary>
    /// Disposes the file reader and all GPU residencies. Residencies must be disposed from the
    /// thread that owns the GPU device — dispose the asset on the main thread. The finalizer path
    /// (Dispose never called) releases only the file reader; the residencies' GPU buffers reclaim
    /// through their own finalizer-safe path, never through the device's deferred destroy queue.
    /// </summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>, false from the finalizer.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StreamableMesh?[] residencies;
            lock (_lock)
            {
                residencies = (StreamableMesh?[])_residencies.Clone();
                Array.Clear(_residencies);
            }

            foreach (StreamableMesh? mesh in residencies)
            {
                mesh?.Dispose();
            }
        }

        // Pure IO, safe from the finalizer thread.
        _reader.Dispose();
    }
}
