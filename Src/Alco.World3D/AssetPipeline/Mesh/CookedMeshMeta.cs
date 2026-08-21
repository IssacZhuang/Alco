using Alco;
using Alco.IO;

namespace Alco.World3D;

/// <summary>
/// Current format version written by the cooker. The major digit must be rejected by readers
/// when higher than supported; meta schema evolution below that is handled by key defaults.
/// </summary>
public static class CookedMeshFormatVersion
{
    /// <summary>The version written by the current cooker.</summary>
    public const string Current = "1.0";

    /// <summary>The highest major version this runtime understands.</summary>
    public const int SupportedMajor = 1;

    /// <summary>
    /// Check a file's version string against the supported range.
    /// </summary>
    /// <param name="version">The version string from a cooked mesh meta.</param>
    /// <exception cref="InvalidDataException">Thrown when the major version is unsupported.</exception>
    public static void Validate(string version)
    {
        if (TryReadMajor(version, out int major) && major > SupportedMajor)
        {
            throw new InvalidDataException($"Cooked mesh format version '{version}' is not supported (supported major: {SupportedMajor}).");
        }
    }

    private static bool TryReadMajor(string version, out int major)
    {
        int dot = version.IndexOf('.');
        ReadOnlySpan<char> majorText = dot >= 0 ? version.AsSpan(0, dot) : version.AsSpan();
        return int.TryParse(majorText, out major);
    }
}

/// <summary>
/// Serializable descriptor of one vertex stream in a cooked mesh file. Maps to
/// <see cref="CookedVertexStream"/> at runtime.
/// </summary>
public sealed class VertexStreamMeta : ISerializable
{
    private uint _semantic;
    private uint _format;
    private uint _offset;
    private uint _stride;
    private BoundingBox3D _quantBounds;

    /// <summary>Stream semantic (<see cref="MeshStreamSemantic"/> value).</summary>
    public uint Semantic => _semantic;

    /// <summary>Vertex format (<see cref="Alco.Graphics.VertexFormat"/> value).</summary>
    public uint Format => _format;

    /// <summary>Byte offset inside the interleaved vertex.</summary>
    public uint Offset => _offset;

    /// <summary>Stride of the owning interleaved vertex payload in bytes.</summary>
    public uint Stride => _stride;

    /// <summary>Quantization domain of the stream (Position only).</summary>
    public BoundingBox3D QuantBounds => _quantBounds;

    /// <summary>Creates an empty stream descriptor for serialization.</summary>
    public VertexStreamMeta()
    {
    }

    /// <summary>Creates a stream descriptor.</summary>
    /// <param name="semantic">Stream semantic value.</param>
    /// <param name="format">Vertex format value.</param>
    /// <param name="offset">Byte offset inside the interleaved vertex.</param>
    /// <param name="stride">Interleaved vertex stride in bytes.</param>
    /// <param name="quantBounds">Quantization domain (Position only).</param>
    public VertexStreamMeta(uint semantic, uint format, uint offset, uint stride, in BoundingBox3D quantBounds)
    {
        _semantic = semantic;
        _format = format;
        _offset = offset;
        _stride = stride;
        _quantBounds = quantBounds;
    }

    /// <inheritdoc />
    public void OnSerialize(SerializeNode node, SerializeMode mode)
    {
        node.BindValue(nameof(_semantic), ref _semantic);
        node.BindValue(nameof(_format), ref _format);
        node.BindValue(nameof(_offset), ref _offset);
        node.BindValue(nameof(_stride), ref _stride);
        node.BindValue(nameof(_quantBounds), ref _quantBounds);
    }
}

/// <summary>
/// Serializable descriptor of one LOD level in a cooked mesh file.
/// </summary>
public sealed class MeshLodMeta : ISerializable
{
    private uint _vertexCount;
    private uint _indexCount;
    private float _maxError;
    private BoundingBox3D _bounds;
    private string _vertexEntry = string.Empty;
    private string _indexEntry = string.Empty;
    private string _clusterEntry = string.Empty;

    /// <summary>Number of vertices of this LOD.</summary>
    public uint VertexCount => _vertexCount;

    /// <summary>Number of indices of this LOD.</summary>
    public uint IndexCount => _indexCount;

    /// <summary>Maximum geometric error of this LOD relative to the source.</summary>
    public float MaxError => _maxError;

    /// <summary>LOD bounds.</summary>
    public BoundingBox3D Bounds => _bounds;

    /// <summary>Content entry name of the vertex payload.</summary>
    public string VertexEntry => _vertexEntry;

    /// <summary>Content entry name of the index payload.</summary>
    public string IndexEntry => _indexEntry;

    /// <summary>Content entry name of the cluster table, empty when absent.</summary>
    public string ClusterEntry => _clusterEntry;

    /// <summary>Creates an empty LOD descriptor for serialization.</summary>
    public MeshLodMeta()
    {
    }

    /// <summary>Creates a LOD descriptor.</summary>
    /// <param name="vertexCount">Vertex count.</param>
    /// <param name="indexCount">Index count.</param>
    /// <param name="maxError">Maximum geometric error.</param>
    /// <param name="bounds">LOD bounds.</param>
    /// <param name="vertexEntry">Vertex payload entry name.</param>
    /// <param name="indexEntry">Index payload entry name.</param>
    public MeshLodMeta(uint vertexCount, uint indexCount, float maxError, in BoundingBox3D bounds, string vertexEntry, string indexEntry)
    {
        _vertexCount = vertexCount;
        _indexCount = indexCount;
        _maxError = maxError;
        _bounds = bounds;
        _vertexEntry = vertexEntry;
        _indexEntry = indexEntry;
    }

    /// <inheritdoc />
    public void OnSerialize(SerializeNode node, SerializeMode mode)
    {
        node.BindValue(nameof(_vertexCount), ref _vertexCount);
        node.BindValue(nameof(_indexCount), ref _indexCount);
        node.BindValue(nameof(_maxError), ref _maxError);
        node.BindValue(nameof(_bounds), ref _bounds);
        node.BindString(nameof(_vertexEntry), ref _vertexEntry);
        node.BindString(nameof(_indexEntry), ref _indexEntry);
        node.BindString(nameof(_clusterEntry), ref _clusterEntry);
    }
}

/// <summary>
/// Serializable descriptor of one submesh (material slot) in a cooked mesh file: a named
/// index range. The slot name is the stable identifier the composition layer (prefab)
/// binds materials to — the mesh itself never references material assets.
/// </summary>
public sealed class MeshSubMeshMeta : ISerializable
{
    private string _name = string.Empty;
    private uint _firstIndex;
    private uint _indexCount;

    /// <summary>The slot name (typically the source material name).</summary>
    public string Name => _name;

    /// <summary>First index in the LOD0 index buffer.</summary>
    public uint FirstIndex => _firstIndex;

    /// <summary>Number of indices of the submesh.</summary>
    public uint IndexCount => _indexCount;

    /// <summary>Creates an empty submesh descriptor for serialization.</summary>
    public MeshSubMeshMeta()
    {
    }

    /// <summary>Creates a submesh descriptor.</summary>
    /// <param name="name">The slot name.</param>
    /// <param name="firstIndex">First index in the LOD0 index buffer.</param>
    /// <param name="indexCount">Number of indices.</param>
    public MeshSubMeshMeta(string name, uint firstIndex, uint indexCount)
    {
        _name = name;
        _firstIndex = firstIndex;
        _indexCount = indexCount;
    }

    /// <inheritdoc />
    public void OnSerialize(SerializeNode node, SerializeMode mode)
    {
        node.BindString(nameof(_name), ref _name);
        node.BindValue(nameof(_firstIndex), ref _firstIndex);
        node.BindValue(nameof(_indexCount), ref _indexCount);
    }
}

/// <summary>
/// Serializable typed descriptor of one cooked mesh content entry: links the entry-name
/// locator from the package directory to its interpretation.
/// </summary>
public sealed class MeshChunkMeta : ISerializable
{
    private string _entry = string.Empty;
    private uint _type;
    private uint _codec;
    private uint _uncompressedSize;
    private ulong _hash;

    /// <summary>The content entry name this descriptor refers to.</summary>
    public string Entry => _entry;

    /// <summary>Chunk type (<see cref="MeshChunkType"/> value).</summary>
    public uint Type => _type;

    /// <summary>Chunk codec (<see cref="MeshChunkCodec"/> value).</summary>
    public uint Codec => _codec;

    /// <summary>Uncompressed size in bytes; equals the entry size for codec None.</summary>
    public uint UncompressedSize => _uncompressedSize;

    /// <summary>xxHash64 over the stored entry bytes.</summary>
    public ulong Hash => _hash;

    /// <summary>Creates an empty chunk descriptor for serialization.</summary>
    public MeshChunkMeta()
    {
    }

    /// <summary>Creates a chunk descriptor.</summary>
    /// <param name="entry">Content entry name.</param>
    /// <param name="type">Chunk type value.</param>
    /// <param name="codec">Chunk codec value.</param>
    /// <param name="uncompressedSize">Uncompressed size in bytes.</param>
    /// <param name="hash">xxHash64 over the stored entry bytes.</param>
    public MeshChunkMeta(string entry, uint type, uint codec, uint uncompressedSize, ulong hash)
    {
        _entry = entry;
        _type = type;
        _codec = codec;
        _uncompressedSize = uncompressedSize;
        _hash = hash;
    }

    /// <inheritdoc />
    public void OnSerialize(SerializeNode node, SerializeMode mode)
    {
        node.BindString(nameof(_entry), ref _entry);
        node.BindValue(nameof(_type), ref _type);
        node.BindValue(nameof(_codec), ref _codec);
        node.BindValue(nameof(_uncompressedSize), ref _uncompressedSize);
        node.BindValue(nameof(_hash), ref _hash);
    }
}

/// <summary>
/// Concrete package meta of the cooked mesh format (.amsh, magic <c>"amsh"</c>). The key-value
/// section carries all extensible metadata; hot tables and payloads are content entries.
/// </summary>
public sealed class CookedMeshMeta : PackageMetaBase, IPackageMeta
{
    private static readonly byte[] s_magic = "amsh"u8.ToArray();

    private uint _flags;
    private ulong _sourceHash;
    private uint _cookerVersion;
    private uint _indexFormat;
    private BoundingBox3D _bounds;
    private readonly List<VertexStreamMeta> _streams = new();
    private readonly List<MeshLodMeta> _lods = new();
    private readonly List<MeshSubMeshMeta> _subMeshes = new();
    private readonly List<MeshChunkMeta> _chunks = new();

    /// <summary>Gets the 4-byte magic that identifies cooked mesh packages.</summary>
    public static ReadOnlySpan<byte> Magic => s_magic;

    /// <summary>Feature flags (<see cref="CookedMeshFlags"/>).</summary>
    public uint Flags
    {
        get => _flags;
        init => _flags = value;
    }

    /// <summary>xxHash64 of the source asset bytes (cook-cache invalidation).</summary>
    public ulong SourceHash
    {
        get => _sourceHash;
        init => _sourceHash = value;
    }

    /// <summary>Cooker algorithm version (cook-cache invalidation).</summary>
    public uint CookerVersion
    {
        get => _cookerVersion;
        init => _cookerVersion = value;
    }

    /// <summary>Global index format (<see cref="Alco.Graphics.IndexFormat"/> value).</summary>
    public uint IndexFormat
    {
        get => _indexFormat;
        init => _indexFormat = value;
    }

    /// <summary>Whole-mesh bounds, available from the header before any payload load.</summary>
    public BoundingBox3D Bounds
    {
        get => _bounds;
        init => _bounds = value;
    }

    /// <summary>Vertex stream descriptors.</summary>
    public IReadOnlyList<VertexStreamMeta> Streams => _streams;

    /// <summary>LOD descriptors; entry i is LOD i.</summary>
    public IReadOnlyList<MeshLodMeta> Lods => _lods;

    /// <summary>Submesh (material slot) descriptors, index ranges into LOD0. Materials bind
    /// to slot names externally (prefab layer); the mesh references no material assets.</summary>
    public IReadOnlyList<MeshSubMeshMeta> SubMeshes => _subMeshes;

    /// <summary>Typed chunk descriptors linking content entries to their interpretation.</summary>
    public IReadOnlyList<MeshChunkMeta> Chunks => _chunks;

    /// <summary>Appends a stream descriptor.</summary>
    /// <param name="stream">The descriptor to append.</param>
    public void AddStream(VertexStreamMeta stream) => _streams.Add(stream);

    /// <summary>Appends a LOD descriptor.</summary>
    /// <param name="lod">The descriptor to append.</param>
    public void AddLod(MeshLodMeta lod) => _lods.Add(lod);

    /// <summary>Appends a submesh descriptor.</summary>
    /// <param name="subMesh">The descriptor to append.</param>
    public void AddSubMesh(MeshSubMeshMeta subMesh) => _subMeshes.Add(subMesh);

    /// <summary>Appends a chunk descriptor.</summary>
    /// <param name="chunk">The descriptor to append.</param>
    public void AddChunk(MeshChunkMeta chunk) => _chunks.Add(chunk);

    /// <summary>
    /// Serializes the cooked mesh fields after the inherited package directory.
    /// </summary>
    /// <param name="node">The serialization node.</param>
    /// <param name="mode">The serialization mode.</param>
    public override void OnSerialize(SerializeNode node, SerializeMode mode)
    {
        base.OnSerialize(node, mode);
        node.BindValue(nameof(_flags), ref _flags);
        node.BindValue(nameof(_sourceHash), ref _sourceHash);
        node.BindValue(nameof(_cookerVersion), ref _cookerVersion);
        node.BindValue(nameof(_indexFormat), ref _indexFormat);
        node.BindValue(nameof(_bounds), ref _bounds);
        node.BindCollectionSerializable(nameof(_streams), _streams);
        node.BindCollectionSerializable(nameof(_lods), _lods);
        node.BindCollectionSerializable(nameof(_subMeshes), _subMeshes);
        node.BindCollectionSerializable(nameof(_chunks), _chunks);
    }
}
