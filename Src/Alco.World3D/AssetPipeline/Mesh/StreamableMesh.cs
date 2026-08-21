using System.Diagnostics.CodeAnalysis;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Descriptor of one LOD level of a <see cref="StreamableMesh"/>.
/// </summary>
public readonly struct StreamableMeshLod
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
    public StreamableMeshLod(uint vertexCount, uint indexCount, float maxError, in BoundingBox3D bounds, string vertexEntry, string indexEntry)
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
/// Descriptor of one submesh (material slot) of a <see cref="StreamableMesh"/>: a named
/// index range. Materials bind to slot names in the composition layer (prefab) — the mesh
/// itself carries no material references.
/// </summary>
public readonly struct StreamableMeshSubMesh
{
    /// <summary>The slot name (typically the source material name).</summary>
    public string Name { get; }

    /// <summary>First index in the LOD0 index buffer.</summary>
    public uint FirstIndex { get; }

    /// <summary>Number of indices of the submesh.</summary>
    public uint IndexCount { get; }

    /// <summary>Creates a submesh descriptor.</summary>
    /// <param name="name">The slot name.</param>
    /// <param name="firstIndex">First index in the LOD0 index buffer.</param>
    /// <param name="indexCount">Number of indices.</param>
    public StreamableMeshSubMesh(string name, uint firstIndex, uint indexCount)
    {
        Name = name;
        FirstIndex = firstIndex;
        IndexCount = indexCount;
    }
}

/// <summary>
/// Lightweight handle to a cooked mesh asset (.amsh): the parsed meta tables plus a positional
/// file reader. Holds no geometry in memory — geometry streams into GPU residency on demand via
/// <see cref="LoadLodAsync"/>. Bounds and structure are available before any payload load.
/// </summary>
public sealed class StreamableMesh : IDisposable
{
    // Feature flags this runtime consumes; anything else on the load path is rejected.
    private const CookedMeshFlags SupportedFlags = CookedMeshFlags.Interleaved | CookedMeshFlags.HasLods;

    private readonly MeshFileReader _reader;
    private readonly GPUDevice? _device;
    private readonly CookedVertexStream[] _streams;
    private readonly StreamableMeshLod[] _lods;
    private readonly StreamableMeshSubMesh[] _subMeshes;
    private readonly Dictionary<int, CookedMesh> _residencies = new();
    private readonly Dictionary<int, Task<CookedMesh>> _loading = new();
    private readonly object _lock = new();

    /// <summary>
    /// Creates a handle over an opened reader. Pass a device to enable GPU residency loads;
    /// structure queries work without one.
    /// </summary>
    /// <param name="reader">The reader; ownership transfers to this asset.</param>
    /// <param name="device">The GPU device for residency uploads, null for header-only usage.</param>
    internal StreamableMesh(MeshFileReader reader, GPUDevice? device)
    {
        _reader = reader;
        _device = device;

        CookedMeshMeta meta = reader.Meta;

        Name = meta.Name;
        Bounds = meta.Bounds;
        Flags = (CookedMeshFlags)meta.Flags;
        LodCount = meta.Lods.Count;
        HasClusters = Flags.HasFlag(CookedMeshFlags.HasClusters);

        _streams = new CookedVertexStream[meta.Streams.Count];
        for (int i = 0; i < _streams.Length; i++)
        {
            VertexStreamMeta stream = meta.Streams[i];
            _streams[i] = new CookedVertexStream
            {
                Semantic = (MeshStreamSemantic)stream.Semantic,
                Format = (Alco.Graphics.VertexFormat)stream.Format,
                Offset = stream.Offset,
                Stride = stream.Stride,
                QuantBounds = stream.QuantBounds,
            };
        }

        _lods = new StreamableMeshLod[meta.Lods.Count];
        for (int i = 0; i < _lods.Length; i++)
        {
            MeshLodMeta lod = meta.Lods[i];
            _lods[i] = new StreamableMeshLod(lod.VertexCount, lod.IndexCount, lod.MaxError, lod.Bounds, lod.VertexEntry, lod.IndexEntry);
        }

        _subMeshes = new StreamableMeshSubMesh[meta.SubMeshes.Count];
        for (int i = 0; i < _subMeshes.Length; i++)
        {
            MeshSubMeshMeta subMesh = meta.SubMeshes[i];
            _subMeshes[i] = new StreamableMeshSubMesh(subMesh.Name, subMesh.FirstIndex, subMesh.IndexCount);
        }
    }

    /// <summary>Gets the mesh name.</summary>
    public string Name { get; }

    /// <summary>Whole-mesh bounds, available before any payload load.</summary>
    public BoundingBox3D Bounds { get; }

    /// <summary>Feature flags of the file.</summary>
    public CookedMeshFlags Flags { get; }

    /// <summary>Number of LOD levels in the file.</summary>
    public int LodCount { get; }

    /// <summary>Whether the file carries a cluster table (M3 virtual geometry capability).</summary>
    public bool HasClusters { get; }

    /// <summary>Vertex stream descriptors of the interleaved payload.</summary>
    public ReadOnlySpan<CookedVertexStream> Streams => _streams;

    /// <summary>Submesh (material slot) descriptors, index ranges into LOD0. Materials bind
    /// to slot names externally (prefab layer), never inside the mesh.</summary>
    public ReadOnlySpan<StreamableMeshSubMesh> SubMeshes => _subMeshes;

    /// <summary>
    /// Open a handle over a seekable stream. The stream ownership transfers to the returned asset.
    /// </summary>
    /// <param name="stream">The seekable cooked mesh stream.</param>
    /// <param name="device">The GPU device for residency uploads, null for header-only usage.</param>
    /// <returns>The asset handle.</returns>
    internal static StreamableMesh FromStream(Stream stream, GPUDevice? device)
    {
        return new StreamableMesh(MeshFileReader.Open(stream, string.Empty), device);
    }

    /// <summary>
    /// Open a handle over cooked mesh bytes held in memory.
    /// </summary>
    /// <param name="data">The cooked mesh bytes.</param>
    /// <param name="device">The GPU device for residency uploads, null for header-only usage.</param>
    /// <returns>The asset handle.</returns>
    internal static StreamableMesh FromMemory(ReadOnlySpan<byte> data, GPUDevice? device)
    {
        return new StreamableMesh(MeshFileReader.OpenMemory(data), device);
    }

    /// <summary>
    /// Gets a LOD descriptor.
    /// </summary>
    /// <param name="lodIndex">The LOD index.</param>
    /// <returns>The LOD descriptor.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the index is out of range.</exception>
    public ref readonly StreamableMeshLod GetLod(int lodIndex)
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
    /// <returns>A task completing with the resident <see cref="CookedMesh"/>.</returns>
    /// <exception cref="NotSupportedException">Thrown when the file uses unsupported features.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no GPU device is bound or the LOD is released mid-load.</exception>
    public Task<CookedMesh> LoadLodAsync(int lodIndex)
    {
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
            if (_residencies.TryGetValue(lodIndex, out CookedMesh? resident))
            {
                return Task.FromResult(resident);
            }

            if (_loading.TryGetValue(lodIndex, out Task<CookedMesh>? existing))
            {
                return existing;
            }

            Task<CookedMesh> task = LoadLodCoreAsync(lodIndex, SynchronizationContext.Current);
            _loading[lodIndex] = task;
            return task;
        }
    }

    /// <summary>
    /// Resident LOD lookup.
    /// </summary>
    /// <param name="lodIndex">The LOD index.</param>
    /// <param name="mesh">The resident mesh when loaded.</param>
    /// <returns>True while resident; false while loading, after failure or after release.</returns>
    public bool TryGetLoadedLod(int lodIndex, [NotNullWhen(true)] out CookedMesh? mesh)
    {
        lock (_lock)
        {
            return _residencies.TryGetValue(lodIndex, out mesh);
        }
    }

    /// <summary>
    /// Dispose the GPU residency of one LOD. Call from the thread that owns the GPU device.
    /// </summary>
    /// <param name="lodIndex">The LOD index.</param>
    public void ReleaseLod(int lodIndex)
    {
        CookedMesh? mesh;
        lock (_lock)
        {
            if (!_residencies.Remove(lodIndex, out mesh))
            {
                return;
            }
        }

        mesh?.Dispose();
    }

    private async Task<CookedMesh> LoadLodCoreAsync(int lodIndex, SynchronizationContext? synchronizationContext)
    {
        StreamableMeshLod lod = _lods[lodIndex];
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
        Task<CookedMesh> upload;
        if (synchronizationContext != null && SynchronizationContext.Current != synchronizationContext)
        {
            TaskCompletionSource<CookedMesh> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
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

        CookedMesh mesh = await upload.ConfigureAwait(false);

        lock (_lock)
        {
            _loading.Remove(lodIndex, out _);
            _residencies[lodIndex] = mesh;
        }

        return mesh;
    }

    private CookedMesh CreateResidency(int lodIndex, SafeMemoryHandle vertexData, SafeMemoryHandle indexData)
    {
        StreamableMeshLod lod = _lods[lodIndex];
        uint stride = _streams.Length > 0 ? _streams[0].Stride : 0;

        CookedMesh mesh = new(_device!, (uint)vertexData.AsReadOnlySpan().Length, (uint)indexData.AsReadOnlySpan().Length, stride, lodIndex, Name);
        mesh.UploadVertex(vertexData.AsReadOnlySpan());
        mesh.UploadIndices(indexData.AsReadOnlySpan());

        Span<SubMeshData> subMeshes = stackalloc SubMeshData[_subMeshes.Length];
        for (int i = 0; i < _subMeshes.Length; i++)
        {
            StreamableMeshSubMesh subMesh = _subMeshes[i];
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
        if ((MeshChunkCodec)chunk.Codec != MeshChunkCodec.None)
        {
            throw new NotSupportedException($"Cooked mesh entry '{entryName}' uses unsupported codec {(MeshChunkCodec)chunk.Codec}.");
        }

        if (((CookedMeshFlags)_reader.Meta.Flags & ~SupportedFlags) != 0)
        {
            // Quantization/paging features affect payload interpretation; reject per LOD load.
            CookedMeshFlags unsupported = (CookedMeshFlags)_reader.Meta.Flags & ~SupportedFlags;
            throw new NotSupportedException($"Cooked mesh '{Name}' uses unsupported features: {unsupported}.");
        }
    }

    /// <summary>
    /// Dispose the file reader and all GPU residencies. Residencies must be disposed from the
    /// thread that owns the GPU device — dispose the asset on the main thread.
    /// </summary>
    public void Dispose()
    {
        List<CookedMesh> meshes;
        lock (_lock)
        {
            meshes = [.. _residencies.Values];
            _residencies.Clear();
        }

        foreach (CookedMesh mesh in meshes)
        {
            mesh.Dispose();
        }

        _reader.Dispose();
    }
}
