using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>Sampler selection encoded into <see cref="SpriteSceneInstance.Flags"/>.</summary>
public enum SpriteSceneSamplerMode : uint
{
    /// <summary>Bilinear filtering with repeating wrap.</summary>
    LinearRepeat = 0,

    /// <summary>Bilinear filtering with edge clamping.</summary>
    LinearClamp = 1,

    /// <summary>Point filtering with repeating wrap.</summary>
    NearestRepeat = 2,

    /// <summary>Point filtering with edge clamping.</summary>
    NearestClamp = 3,
}

/// <summary>Engine-level bit flags encoded into <see cref="SpriteSceneInstance.Flags"/>.</summary>
[Flags]
public enum SpriteSceneFlags : uint
{
    /// <summary>No special engine-level flags.</summary>
    None = 0,

    /// <summary>Marks the instance as carrying premultiplied color output.</summary>
    Premultiplied = 1u << 2,
}

/// <summary>Per-instance GPU payload stored in <see cref="SpriteScene2D"/> (96 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SpriteSceneInstance
{
    /// <summary>An empty hidden instance with zero scale and zero color.</summary>
    public static SpriteSceneInstance Hidden => default;

    /// <summary>World-space center position.</summary>
    public Vector2 Position;

    /// <summary>2D rotation stored as (sine, cosine) matching <see cref="Rotation2D"/>.</summary>
    public Rotation2D Rotation;

    /// <summary>World-space width and height scale.</summary>
    public Vector2 Scale;

    /// <summary>Base world Z depth offset.</summary>
    public float Depth;

    /// <summary>Packed sampler mode (bits 0..1), engine flags (bit 2), and caller surface flags (bits 3..31).</summary>
    public uint Flags;

    /// <summary>Linear RGBA tint color.</summary>
    public Vector4 Color;

    /// <summary>Normalized UV rectangle inside the primary texture.</summary>
    public Rect UvRect;

    /// <summary>Opaque per-instance channels available to custom <c>ISpriteSceneSurface</c> shaders.</summary>
    public Vector4 CustomData;

    /// <summary>Bindless descriptor index of the primary sampled texture.</summary>
    public uint TextureId;

    /// <summary>Bindless descriptor index of an optional secondary sampled texture.</summary>
    public uint AuxTextureId;

    /// <summary>Optional index into the bucket's shared material parameter buffer.</summary>
    public uint MaterialParamId;

    /// <summary>Opaque 32-bit caller payload forwarded to vertex and pixel surface stages.</summary>
    public uint UserData;

    /// <summary>Packs sampler mode, engine flags, and optional caller flag bits (bits 3..31) into <see cref="Flags"/>.</summary>
    /// <param name="samplerMode">Texture sampling filter and address mode.</param>
    /// <param name="flags">Engine behavior flags.</param>
    /// <param name="userFlags">Optional surface flags placed in bits 3..31 (bits 0..2 are masked out).</param>
    /// <returns>The packed 32-bit flag word.</returns>
    public static uint PackFlags(
        SpriteSceneSamplerMode samplerMode = SpriteSceneSamplerMode.LinearRepeat,
        SpriteSceneFlags flags = SpriteSceneFlags.None,
        uint userFlags = 0u)
    {
        return ((uint)samplerMode & 3u) | (uint)flags | (userFlags & ~0x7u);
    }
}

/// <summary>Per-bucket GPU metadata read by reset and culling compute shaders (16 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SpriteSceneBucketMeta
{
    /// <summary>Vertex count per instance drawn for this bucket.</summary>
    public uint VertexCount;

    /// <summary>Base element offset of this bucket inside the visible slot index buffer.</summary>
    public uint VisibleBase;

    /// <summary>Conservative local bounding extent scale used for rotated AABB frustum culling.</summary>
    public float LocalExtentScale;

    /// <summary>Opaque caller-defined bucket tag forwarded to surface shaders.</summary>
    public uint BucketTag;
}

/// <summary>Root header matching <c>SpriteScenePassHeader</c> in Slang (128 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SpriteScenePassHeader
{
    /// <summary>Row-major camera view-projection matrix.</summary>
    public Matrix4x4 ViewProjection;

    /// <summary>View of the persistent <see cref="SpriteSceneInstance"/> pool buffer.</summary>
    public GPUBufferView Pool;

    /// <summary>View into the compacted visible slot indices starting at this bucket's base offset.</summary>
    public GPUBufferView VisibleSlots;

    /// <summary>Optional view of the bucket's shared material parameter table.</summary>
    public GPUBufferView MaterialParams;

    /// <summary>Elapsed scene time in seconds.</summary>
    public float Time;

    /// <summary>Opaque caller-defined bucket tag copied from <see cref="SpriteSceneBucketDesc.BucketTag"/>.</summary>
    public uint BucketTag;

    /// <summary>Optional per-pass user vector forwarded to surfaces.</summary>
    public Vector4 UserParams;

    /// <summary>Non-zero when <see cref="MaterialParams"/> binds a valid buffer.</summary>
    public uint HasMaterialParams;

    /// <summary>Padding to maintain 16-byte alignment.</summary>
    public uint Pad0;

    /// <summary>Padding to maintain 16-byte alignment.</summary>
    public uint Pad1;

    /// <summary>Padding to maintain 16-byte alignment.</summary>
    public uint Pad2;
}

/// <summary>Registration descriptor for one draw bucket inside <see cref="SpriteScene2D"/>.</summary>
/// <param name="Tier">Caller-defined render tier key queried by <see cref="SpriteScene2D.RenderTier"/>.</param>
/// <param name="Material">Graphics material compiled from <c>SpriteScenePass</c> (or compatible root layout).</param>
/// <param name="DepthStencil">Depth-stencil state applied when drawing this bucket.</param>
/// <param name="VertexCount">Procedural vertices expanded per visible instance in the vertex stage (defaults to 6 for a quad).</param>
/// <param name="LocalExtentScale">Conservative local bounding extent multiplier used for rotated AABB culling (defaults to 0.5).</param>
/// <param name="BucketTag">Opaque caller-defined identifier forwarded to custom <c>ISpriteSceneSurface</c> shaders.</param>
/// <param name="MaterialParamsBuffer">Optional shared parameter table indexed by <see cref="SpriteSceneInstance.MaterialParamId"/>.</param>
public readonly record struct SpriteSceneBucketDesc(
    int Tier,
    Material Material,
    DepthStencilState DepthStencil,
    uint VertexCount = 6u,
    float LocalExtentScale = 0.5f,
    uint BucketTag = 0u,
    Alco.Graphics.Buffer? MaterialParamsBuffer = null);

/// <summary>Per-frame telemetry reported by <see cref="SpriteScene2D.Stats"/>.</summary>
/// <param name="TotalSlots">High-water slot count allocated in the pool.</param>
/// <param name="LiveSlots">Active registered instances across all buckets.</param>
/// <param name="BucketCount">Number of registered draw buckets.</param>
/// <param name="DirtyPatchesUploaded">Dirty instance patches scattered on the last cull pass.</param>
/// <param name="DrawCallsSubmitted">Indirect draws recorded since the last cull pass.</param>
/// <param name="ActiveTextureCount">Distinct bindless textures retained by the pool.</param>
public readonly record struct SpriteSceneStats(
    int TotalSlots,
    int LiveSlots,
    int BucketCount,
    int DirtyPatchesUploaded,
    int DrawCallsSubmitted,
    int ActiveTextureCount);
