using System.Runtime.InteropServices;
using Alco;

namespace Alco.World3D;

/// <summary>
/// Alpha handling mode of a material. Values are stable on-disk identifiers, engine-neutral.
/// </summary>
public enum MeshAlphaMode : uint
{
    /// <summary>Fully opaque, alpha is ignored.</summary>
    Opaque = 0,

    /// <summary>Alpha tested against the material's cutoff value.</summary>
    Mask = 1,

    /// <summary>Alpha blended.</summary>
    Blend = 2,
}

/// <summary>
/// Stable on-disk vertex attribute storage formats for <c>.amsh</c> assets.
/// </summary>
public enum MeshVertexFormat : uint
{
    /// <summary>Two 32-bit floats.</summary>
    R32G32Float = 1,

    /// <summary>Three 32-bit floats.</summary>
    R32G32B32Float = 2,

    /// <summary>Four 32-bit floats.</summary>
    R32G32B32A32Float = 3,

    /// <summary>Two 16-bit floats.</summary>
    R16G16Float = 4,

    /// <summary>Four 16-bit floats.</summary>
    R16G16B16A16Float = 5,

    /// <summary>Four normalized unsigned 8-bit integers.</summary>
    R8G8B8A8Unorm = 6,

    /// <summary>Four unsigned 8-bit integers.</summary>
    R8G8B8A8Uint = 7,

    /// <summary>Four unsigned 16-bit integers.</summary>
    R16G16B16A16Uint = 8,

    /// <summary>One unsigned 32-bit integer.</summary>
    R32Uint = 9,

    /// <summary>One 32-bit float.</summary>
    R32Float = 10,
}

/// <summary>
/// Semantic identifier of a vertex stream in the mesh asset format (.amsh).
/// Values are stable on-disk identifiers; append new semantics only.
/// </summary>
public enum MeshStreamSemantic : uint
{
    /// <summary>Object-space position.</summary>
    Position = 0,

    /// <summary>Object-space normal.</summary>
    Normal = 1,

    /// <summary>Tangent with bitangent sign in w.</summary>
    Tangent = 2,

    /// <summary>Primary texture coordinate.</summary>
    TexCoord0 = 3,

    /// <summary>Secondary texture coordinate.</summary>
    TexCoord1 = 4,

    /// <summary>Vertex color.</summary>
    Color0 = 5,

    /// <summary>First custom slot.</summary>
    Custom0 = 6,
}

/// <summary>
/// Kind of a mesh asset content entry. Values are stable on-disk identifiers; append only.
/// </summary>
public enum MeshChunkType : uint
{
    /// <summary>Interleaved (M1) or SoA vertex payload of one LOD.</summary>
    VertexData = 0,

    /// <summary>Index payload of one LOD.</summary>
    IndexData = 1,

    /// <summary>Fixed-layout <see cref="ClusterRecord"/> array (M3 virtual geometry).</summary>
    ClusterTable = 2,

    /// <summary>Fixed-layout <see cref="PageRecord"/> array (M3 virtual geometry).</summary>
    PageTable = 3,

    /// <summary>Concatenated fixed-size cluster pages (M3 virtual geometry).</summary>
    PageData = 4,

    /// <summary>Bounds hierarchy for streaming selection (reserved).</summary>
    BoundsHierarchy = 5,

    /// <summary>Collision data (reserved).</summary>
    Collision = 6,

    /// <summary>Tool/user payload.</summary>
    User = 7,
}

/// <summary>
/// Payload compression of a mesh asset chunk. Compressed chunks must be decoded on a worker
/// before upload. M1 ships <see cref="None"/> only.
/// </summary>
public enum MeshChunkCodec : uint
{
    /// <summary>Raw uncompressed bytes.</summary>
    None = 0,

    /// <summary>LZ4 block compression (reserved, M2).</summary>
    Lz4 = 1,

    /// <summary>meshoptimizer vertex codec (reserved, M2).</summary>
    MeshoptVertex = 2,

    /// <summary>meshoptimizer index codec (reserved, M2).</summary>
    MeshoptIndex = 3,
}

/// <summary>
/// Feature flags of a mesh asset file. Stored in the meta as <c>_flags</c>. New flags must be
/// appended and readers must reject files carrying flags they do not understand for the
/// affected payload path.
/// </summary>
[Flags]
public enum MeshAssetFlags : uint
{
    /// <summary>No features.</summary>
    None = 0,

    /// <summary>Single interleaved vertex entry per LOD (the M1 layout).</summary>
    Interleaved = 1 << 0,

    /// <summary>Position stream is snorm16 within the stream's quantization bounds.</summary>
    QuantizedPositions = 1 << 1,

    /// <summary>Normal/tangent are octahedral encoded (reserved).</summary>
    OctEncodedNormals = 1 << 2,

    /// <summary>Texture coordinates are float16 (reserved).</summary>
    HalfTexCoords = 1 << 3,

    /// <summary>File contains more than one LOD entry set.</summary>
    HasLods = 1 << 4,

    /// <summary>File contains a cluster table (M3 virtual geometry).</summary>
    HasClusters = 1 << 5,

    /// <summary>LOD data is accompanied by fixed-size pages (M3 virtual geometry).</summary>
    Paged = 1 << 6,

    /// <summary>Bulk payloads live in a sidecar file (reserved).</summary>
    ExternalBulk = 1 << 7,
}

/// <summary>
/// Descriptor of one vertex stream inside an interleaved mesh asset vertex payload.
/// </summary>
public struct MeshVertexStream
{
    /// <summary>Semantic of the stream.</summary>
    public MeshStreamSemantic Semantic;

    /// <summary>Stable disk vertex format of the stream.</summary>
    public MeshVertexFormat Format;

    /// <summary>Byte offset of the stream inside the interleaved vertex.</summary>
    public uint Offset;

    /// <summary>Stride of the owning interleaved vertex payload in bytes.</summary>
    public uint Stride;

    /// <summary>
    /// Quantization domain of the stream (Position only): dequantize as
    /// <c>Min + n * (Max - Min)</c> with n in [0, 1].
    /// </summary>
    public BoundingBox3D QuantBounds;
}

/// <summary>
/// Frozen 96-byte cluster descriptor of the mesh asset format (M3 virtual geometry).
/// Little-endian on disk; blit directly into native/GPU buffers.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ClusterRecord
{
    /// <summary>Cluster bounds minimum corner.</summary>
    public fixed float BoundsMin[3];

    /// <summary>Cluster bounds maximum corner.</summary>
    public fixed float BoundsMax[3];

    /// <summary>Bounding sphere: center.xyz and radius (w).</summary>
    public fixed float Sphere[4];

    /// <summary>Normal cone axis, normalized.</summary>
    public fixed float ConeAxis[3];

    /// <summary>Cosine of the normal cone half-angle; 1.0 = degenerate cone.</summary>
    public float ConeCutoff;

    /// <summary>Maximum geometric error of the cluster (DAG cut selection).</summary>
    public float MaxError;

    /// <summary>Cluster flags (reserved).</summary>
    public uint Flags;

    /// <summary>Offset into the owning page's vertex pool.</summary>
    public uint VertexOffset;

    /// <summary>Number of vertices in the owning page's vertex pool.</summary>
    public uint VertexCount;

    /// <summary>Offset into the owning page's index pool.</summary>
    public uint IndexOffset;

    /// <summary>Number of indices (3 * triangle count).</summary>
    public uint IndexCount;

    /// <summary>Index into the group table, -1 for roots.</summary>
    public int ParentGroup;

    /// <summary>LOD level the cluster belongs to.</summary>
    public int Lod;

    /// <summary>Index of the owning page.</summary>
    public uint PageIndex;

    /// <summary>Index into the mesh's material slot (submesh) table.</summary>
    public ushort MaterialSlot;

    /// <summary>Reserved, must be zero.</summary>
    public ushort Reserved;
}

/// <summary>
/// Frozen 32-byte page descriptor of the mesh asset format (M3 virtual geometry).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PageRecord
{
    /// <summary>xxHash64 of the page payload.</summary>
    public ulong Hash;

    /// <summary>Index of the first cluster in the page.</summary>
    public uint ClusterFirst;

    /// <summary>Number of clusters in the page.</summary>
    public uint ClusterCount;

    /// <summary>Uncompressed payload size in bytes.</summary>
    public uint UncompressedSize;

    /// <summary>Payload codec (<see cref="MeshChunkCodec"/> values).</summary>
    public uint Codec;

    /// <summary>Page flags (reserved).</summary>
    public uint Flags;

    /// <summary>Reserved, must be zero.</summary>
    public uint Reserved;
}

/// <summary>
/// Well-known mesh vertex layouts and format helpers.
/// </summary>
public static class MeshVertexLayout
{
    /// <summary>Stride of the default interleaved PBR vertex (matches <see cref="Alco.Rendering.VertexPBR"/>).</summary>
    public const uint VertexPBRStride = 48;

    /// <summary>
    /// Create the default interleaved PBR stream descriptors:
    /// position/normal/uv0/tangent at 0/12/24/32 in a 48-byte vertex.
    /// </summary>
    /// <returns>The stream descriptors.</returns>
    public static MeshVertexStream[] CreatePBR()
    {
        return
        [
            new MeshVertexStream { Semantic = MeshStreamSemantic.Position, Format = MeshVertexFormat.R32G32B32Float, Offset = 0, Stride = VertexPBRStride },
            new MeshVertexStream { Semantic = MeshStreamSemantic.Normal, Format = MeshVertexFormat.R32G32B32Float, Offset = 12, Stride = VertexPBRStride },
            new MeshVertexStream { Semantic = MeshStreamSemantic.TexCoord0, Format = MeshVertexFormat.R32G32Float, Offset = 24, Stride = VertexPBRStride },
            new MeshVertexStream { Semantic = MeshStreamSemantic.Tangent, Format = MeshVertexFormat.R32G32B32A32Float, Offset = 32, Stride = VertexPBRStride },
        ];
    }

    /// <summary>
    /// Gets the size in bytes of one component tuple of a <see cref="MeshVertexFormat"/>.
    /// </summary>
    /// <param name="format">The vertex format.</param>
    /// <returns>The tuple size in bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for undefined formats.</exception>
    public static int GetByteSize(MeshVertexFormat format)
    {
        return format switch
        {
            MeshVertexFormat.R16G16Float or MeshVertexFormat.R8G8B8A8Unorm
                or MeshVertexFormat.R8G8B8A8Uint or MeshVertexFormat.R32Uint or MeshVertexFormat.R32Float => 4,
            MeshVertexFormat.R32G32Float or MeshVertexFormat.R16G16B16A16Float
                or MeshVertexFormat.R16G16B16A16Uint => 8,
            MeshVertexFormat.R32G32B32Float => 12,
            MeshVertexFormat.R32G32B32A32Float => 16,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Undefined mesh vertex format."),
        };
    }

    /// <summary>
    /// Gets the size in bytes of one component tuple of a <see cref="MeshVertexFormat"/> as an unsigned integer.
    /// </summary>
    /// <param name="format">The vertex format.</param>
    /// <returns>The tuple size in bytes.</returns>
    public static uint GetFormatSize(MeshVertexFormat format) => (uint)GetByteSize(format);
}
