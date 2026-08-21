using System.Runtime.InteropServices;
using Alco;
using Alco.Graphics;

namespace Alco.World3D;

/// <summary>
/// Alpha handling mode of a material. Values are stable on-disk identifiers, engine-neutral.
/// Reserved for the upcoming material asset format — cooked meshes carry material slots
/// (names), never material definitions.
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
/// Semantic identifier of a vertex stream in the cooked mesh format (.amsh).
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
/// Kind of a cooked mesh content entry. Values are stable on-disk identifiers; append only.
/// </summary>
public enum MeshChunkType : uint
{
    /// <summary>Interleaved (M1) or SoA vertex payload of one LOD.</summary>
    VertexData = 0,

    /// <summary>Index payload of one LOD.</summary>
    IndexData = 1,

    /// <summary>Fixed-layout <see cref="ClusterRecord"/> array (reserved, M3).</summary>
    ClusterTable = 2,

    /// <summary>Fixed-layout <see cref="PageRecord"/> array (reserved, M3).</summary>
    PageTable = 3,

    /// <summary>Concatenated fixed-size cluster pages (reserved, M3).</summary>
    PageData = 4,

    /// <summary>Bounds hierarchy for streaming selection (reserved).</summary>
    BoundsHierarchy = 5,

    /// <summary>Cooked collision data (reserved).</summary>
    Collision = 6,

    /// <summary>Tool/user payload.</summary>
    User = 7,
}

/// <summary>
/// Payload compression of a cooked mesh chunk. Compressed chunks must be decoded on a worker
/// before upload and can never be direct-uploaded. M1 ships <see cref="None"/> only.
/// </summary>
public enum MeshChunkCodec : uint
{
    /// <summary>Raw bytes, directly uploadable.</summary>
    None = 0,

    /// <summary>LZ4 block compression (reserved, M2).</summary>
    Lz4 = 1,

    /// <summary>meshoptimizer vertex codec (reserved, M2).</summary>
    MeshoptVertex = 2,

    /// <summary>meshoptimizer index codec (reserved, M2).</summary>
    MeshoptIndex = 3,
}

/// <summary>
/// Feature flags of a cooked mesh file. Stored in the meta as <c>_flags</c>. New flags must be
/// appended and readers must reject files carrying flags they do not understand for the
/// affected payload path.
/// </summary>
[Flags]
public enum CookedMeshFlags : uint
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

    /// <summary>File contains a cluster table (reserved, M3).</summary>
    HasClusters = 1 << 5,

    /// <summary>LOD data is organized in fixed-size pages (reserved, M3).</summary>
    Paged = 1 << 6,

    /// <summary>Bulk payloads live in a sidecar file (reserved).</summary>
    ExternalBulk = 1 << 7,
}

/// <summary>
/// Descriptor of one vertex stream inside an interleaved cooked vertex payload. The disk format
/// (<see cref="Format"/>) is the GPU vertex format, so the payload bytes are directly
/// consumable as a vertex buffer.
/// </summary>
public struct CookedVertexStream
{
    /// <summary>Semantic of the stream.</summary>
    public MeshStreamSemantic Semantic;

    /// <summary>Disk/GPU vertex format of the stream.</summary>
    public VertexFormat Format;

    /// <summary>Byte offset of the stream inside the interleaved vertex.</summary>
    public uint Offset;

    /// <summary>Stride of the owning interleaved vertex payload in bytes.</summary>
    public uint Stride;

    /// <summary>
    /// Quantization domain of the stream (Position only): dequantize as
    /// <c>Min + n * (Max - Min)</c> with n in [0, 1]. Typically the tight position AABB chosen
    /// at cook time for maximum precision; semantically a decode input, not a culling bound.
    /// The zero box means not quantized (authoritative gate: <see cref="CookedMeshFlags.QuantizedPositions"/>).
    /// </summary>
    public BoundingBox3D QuantBounds;
}

/// <summary>
/// Frozen 96-byte cluster descriptor of the cooked mesh format. Published with the format so
/// cookers can start emitting it without a format break; consumed by the M3 virtual geometry
/// pipeline. Little-endian on disk; blit directly into native/GPU buffers.
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

    /// <summary>Number of indices (3 * triangle count; 8-bit local indices).</summary>
    public uint IndexCount;

    /// <summary>Index into the group table, -1 for roots.</summary>
    public int ParentGroup;

    /// <summary>LOD level the cluster belongs to.</summary>
    public int Lod;

    /// <summary>Index of the owning page.</summary>
    public uint PageIndex;

    /// <summary>Reserved, must be zero.</summary>
    public uint Reserved;
}

/// <summary>
/// Frozen 32-byte page descriptor of the cooked mesh format (M3 virtual geometry). Pages have a
/// fixed size declared in the meta; page N occupies <c>[N * pageSize, (N+1) * pageSize)</c> of
/// the page data entry, so paging needs no per-page directory.
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
/// Well-known cooked vertex layouts and format helpers.
/// </summary>
public static class CookedVertexLayout
{
    /// <summary>Stride of the default interleaved PBR vertex (matches <see cref="VertexPBR"/>).</summary>
    public const uint VertexPBRStride = 48;

    /// <summary>
    /// Create the default interleaved PBR stream descriptors:
    /// position/normal/uv0/tangent at 0/12/24/32 in a 48-byte vertex.
    /// </summary>
    /// <returns>The stream descriptors.</returns>
    public static CookedVertexStream[] CreatePBR()
    {
        return
        [
            new CookedVertexStream { Semantic = MeshStreamSemantic.Position, Format = VertexFormat.Float32x3, Offset = 0, Stride = VertexPBRStride },
            new CookedVertexStream { Semantic = MeshStreamSemantic.Normal, Format = VertexFormat.Float32x3, Offset = 12, Stride = VertexPBRStride },
            new CookedVertexStream { Semantic = MeshStreamSemantic.TexCoord0, Format = VertexFormat.Float32x2, Offset = 24, Stride = VertexPBRStride },
            new CookedVertexStream { Semantic = MeshStreamSemantic.Tangent, Format = VertexFormat.Float32x4, Offset = 32, Stride = VertexPBRStride },
        ];
    }

    /// <summary>
    /// Get the size in bytes of one component tuple of a vertex format.
    /// </summary>
    /// <param name="format">The vertex format.</param>
    /// <returns>The tuple size in bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for undefined formats.</exception>
    public static uint GetFormatSize(VertexFormat format)
    {
        return format switch
        {
            VertexFormat.Uint8x2 or VertexFormat.Sint8x2 or VertexFormat.Unorm8x2 or VertexFormat.Snorm8x2 => 2,
            VertexFormat.Uint8x4 or VertexFormat.Sint8x4 or VertexFormat.Unorm8x4 or VertexFormat.Snorm8x4 => 4,
            VertexFormat.Uint16x2 or VertexFormat.Sint16x2 or VertexFormat.Unorm16x2 or VertexFormat.Snorm16x2
                or VertexFormat.Float16x2 => 4,
            VertexFormat.Uint16x4 or VertexFormat.Sint16x4 or VertexFormat.Unorm16x4 or VertexFormat.Snorm16x4
                or VertexFormat.Float16x4 => 8,
            VertexFormat.Float32 or VertexFormat.Uint32 or VertexFormat.Sint32 => 4,
            VertexFormat.Float32x2 or VertexFormat.Uint32x2 or VertexFormat.Sint32x2 => 8,
            VertexFormat.Float32x3 or VertexFormat.Uint32x3 or VertexFormat.Sint32x3 => 12,
            VertexFormat.Float32x4 or VertexFormat.Uint32x4 or VertexFormat.Sint32x4 => 16,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Undefined vertex format."),
        };
    }
}
