using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>Sampler selection encoded into <see cref="MeshScene3DInstance.Flags"/>.</summary>
public enum MeshScene3DSamplerMode : uint
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

/// <summary>Engine-level bit flags encoded into <see cref="MeshScene3DInstance.Flags"/>.</summary>
[Flags]
public enum MeshScene3DFlags : uint
{
    /// <summary>No special engine-level flags.</summary>
    None = 0,

    /// <summary>Marks the instance as carrying premultiplied color output.</summary>
    Premultiplied = 1u << 2,
}

/// <summary>Per-instance GPU payload stored in <see cref="MeshScene3D"/> (128 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct MeshScene3DInstance
{
    /// <summary>An empty hidden instance with a zero linear matrix.</summary>
    public static MeshScene3DInstance Hidden => default;

    /// <summary>First affine row-vector dot-product row: <c>(M11, M21, M31, M41)</c>.</summary>
    public Vector4 World0;

    /// <summary>Second affine row-vector dot-product row: <c>(M12, M22, M32, M42)</c>.</summary>
    public Vector4 World1;

    /// <summary>Third affine row-vector dot-product row: <c>(M13, M23, M33, M43)</c>.</summary>
    public Vector4 World2;

    /// <summary>Linear RGBA tint color.</summary>
    public Vector4 Color;

    /// <summary>Opaque per-instance channels available to custom shaders.</summary>
    public Vector4 CustomData0;

    /// <summary>Additional opaque per-instance channels available to custom shaders.</summary>
    public Vector4 CustomData1;

    /// <summary>Bindless descriptor index of the primary sampled texture.</summary>
    public uint TextureId;

    /// <summary>Bindless descriptor index of an optional secondary sampled texture.</summary>
    public uint AuxTextureId;

    /// <summary>Optional index into the bucket's shared material parameter buffer.</summary>
    public uint MaterialParamId;

    /// <summary>Packed sampler mode (bits 0..1), engine flags (bit 2), and caller surface flags (bits 3..31).</summary>
    public uint Flags;

    /// <summary>First opaque 32-bit caller payload.</summary>
    public uint UserData0;

    /// <summary>Second opaque 32-bit caller payload.</summary>
    public uint UserData1;

    /// <summary>Bitmask tested against <see cref="MeshScene3DCullView.RequiredCullMask"/>; zero accepts all masks when required mask is zero.</summary>
    public uint CullMask;

    /// <summary>Opaque caller-reserved 32-bit word.</summary>
    public uint Reserved;

    /// <summary>Packs an affine row-vector world matrix into <see cref="World0"/>, <see cref="World1"/>, and <see cref="World2"/>.</summary>
    /// <param name="world">Row-major affine transformation matrix.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetWorldMatrix(in Matrix4x4 world)
    {
        World0 = new Vector4(world.M11, world.M21, world.M31, world.M41);
        World1 = new Vector4(world.M12, world.M22, world.M32, world.M42);
        World2 = new Vector4(world.M13, world.M23, world.M33, world.M43);
    }

    /// <summary>Reconstructs the row-major affine <see cref="Matrix4x4"/> from <see cref="World0"/>, <see cref="World1"/>, and <see cref="World2"/>.</summary>
    /// <returns>The unpacked affine matrix with bottom-right element equal to 1.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Matrix4x4 GetWorldMatrix()
    {
        return new Matrix4x4(
            World0.X, World1.X, World2.X, 0f,
            World0.Y, World1.Y, World2.Y, 0f,
            World0.Z, World1.Z, World2.Z, 0f,
            World0.W, World1.W, World2.W, 1f);
    }

    /// <summary>Sets this instance's world matrix from a <see cref="Transform3D"/>.</summary>
    /// <param name="transform">The 3D transform to apply.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTransform(in Transform3D transform)
    {
        Matrix4x4 matrix = transform.Matrix;
        SetWorldMatrix(in matrix);
    }

    /// <summary>Sets this instance's world matrix from a 2D transform plus height offset and Z scale, matching <see cref="Rotation2D"/> conventions.</summary>
    /// <param name="transform">The 2D transform in the XY ground plane.</param>
    /// <param name="z">World-space Z translation.</param>
    /// <param name="zScale">Scale along the Z axis.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTransform2D(in Transform2D transform, float z = 0f, float zScale = 1f)
    {
        Matrix4x4 matrix = FromTransform2DMatrix(in transform, z, zScale);
        SetWorldMatrix(in matrix);
    }

    /// <summary>Composes a child local world matrix with a parent world matrix using row-vector convention (<c>local * parent</c>).</summary>
    /// <param name="localWorld">Local transform relative to parent.</param>
    /// <param name="parentWorld">Parent transform in world space.</param>
    /// <returns>The composed world matrix.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix4x4 ComposeWorldMatrix(in Matrix4x4 localWorld, in Matrix4x4 parentWorld)
    {
        return localWorld * parentWorld;
    }

    /// <summary>Composes a child <see cref="Transform3D"/> with a parent <see cref="Transform3D"/>.</summary>
    /// <param name="local">Local transform relative to parent.</param>
    /// <param name="parent">Parent transform in world space.</param>
    /// <returns>The composed world matrix.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix4x4 ComposeTransform(in Transform3D local, in Transform3D parent)
    {
        return local.Matrix * parent.Matrix;
    }

    /// <summary>
    /// Builds a row-major 3D world matrix from a <see cref="Transform2D"/>, preserving the exact clockwise
    /// rotation sign of <see cref="Alco.math.matrix4rotation(Rotation2D)"/> and <see cref="Alco.math.rotate(Vector2, Rotation2D)"/>.
    /// </summary>
    /// <param name="transform">The 2D transform.</param>
    /// <param name="z">World Z translation.</param>
    /// <param name="zScale">Scale along Z.</param>
    /// <returns>The equivalent affine 3D matrix.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix4x4 FromTransform2DMatrix(in Transform2D transform, float z = 0f, float zScale = 1f)
    {
        float s = transform.Rotation.S;
        float c = transform.Rotation.C;
        float sx = transform.Scale.X;
        float sy = transform.Scale.Y;
        return new Matrix4x4(
            sx * c, -sx * s, 0f, 0f,
            sy * s, sy * c, 0f, 0f,
            0f, 0f, zScale, 0f,
            transform.Position.X, transform.Position.Y, z, 1f);
    }

    /// <summary>Packs sampler mode, engine flags, and optional caller flag bits (bits 3..31) into <see cref="Flags"/>.</summary>
    /// <param name="samplerMode">Texture sampling filter and address mode.</param>
    /// <param name="flags">Engine behavior flags.</param>
    /// <param name="userFlags">Optional surface flags placed in bits 3..31 (bits 0..2 are masked out).</param>
    /// <returns>The packed 32-bit flag word.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint PackFlags(
        MeshScene3DSamplerMode samplerMode = MeshScene3DSamplerMode.LinearRepeat,
        MeshScene3DFlags flags = MeshScene3DFlags.None,
        uint userFlags = 0u)
    {
        return ((uint)samplerMode & 3u) | (uint)flags | (userFlags & ~0x7u);
    }
}

/// <summary>Per-bucket GPU metadata read by reset and culling compute shaders (48 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct MeshScene3DBucketMeta
{
    /// <summary>Index count drawn per instance for this bucket.</summary>
    public uint IndexCount;

    /// <summary>Base element offset of this bucket inside the visible slot index buffer.</summary>
    public uint VisibleBase;

    /// <summary>Opaque caller-defined bucket tag forwarded to surface shaders.</summary>
    public uint BucketTag;

    /// <summary>Starting index in the bucket's index buffer.</summary>
    public uint FirstIndex;

    /// <summary>Center of the bucket's local-space bounding box.</summary>
    public Vector3 LocalCenter;

    /// <summary>Constant vertex offset added to each fetched index.</summary>
    public int VertexOffset;

    /// <summary>Half-extents of the bucket's local-space bounding box.</summary>
    public Vector3 LocalExtent;

    /// <summary>Padding to maintain 16-byte struct alignment.</summary>
    public uint Pad0;
}

/// <summary>Root header placed at offset 0 of every <see cref="MeshScene3D"/> graphics material (144 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct MeshScene3DPassHeader
{
    /// <summary>Row-major view-projection matrix.</summary>
    public Matrix4x4 ViewProjection;

    /// <summary>View of the bucket geometry's <see cref="VertexPBR"/> buffer.</summary>
    public GPUBufferView Vertices;

    /// <summary>View of the persistent <see cref="MeshScene3DInstance"/> pool buffer.</summary>
    public GPUBufferView Pool;

    /// <summary>View into the compacted visible slot indices starting at this bucket's base offset.</summary>
    public GPUBufferView VisibleSlots;

    /// <summary>Optional view of the bucket's shared material parameter table.</summary>
    public GPUBufferView MaterialParams;

    /// <summary>Optional per-pass user vector forwarded to shaders.</summary>
    public Vector4 UserParams;

    /// <summary>Elapsed scene time in seconds.</summary>
    public float Time;

    /// <summary>Opaque caller-defined bucket tag copied from <see cref="MeshScene3DBucketDesc.BucketTag"/>.</summary>
    public uint BucketTag;

    /// <summary>Non-zero when <see cref="MaterialParams"/> binds a valid buffer.</summary>
    public uint HasMaterialParams;

    /// <summary>Opaque caller-defined pass flag word forwarded in <see cref="RenderTier"/>.</summary>
    public uint PassFlags;

    /// <summary>Origin offset subtracted from instance translations prior to transformation to preserve precision.</summary>
    public Vector3 OriginOffset;

    /// <summary>Padding to maintain 16-byte struct alignment.</summary>
    public uint Pad0;
}

/// <summary>
/// Normalized 6-plane frustum and bitmask filter describing a 3D culling volume
/// (perspective, orthographic, sheared oblique projection, or axis-aligned bounds).
/// </summary>
public readonly record struct MeshScene3DCullView(
    Vector4 Plane0,
    Vector4 Plane1,
    Vector4 Plane2,
    Vector4 Plane3,
    Vector4 Plane4,
    Vector4 Plane5,
    float CullMargin = 0f,
    uint RequiredCullMask = 0u,
    bool EnableFrustumCulling = true,
    Vector3 OriginOffset = default)
{
    private static readonly Vector4 AlwaysPassPlane = new(0f, 0f, 0f, 1e9f);
    private static readonly Vector4 AlwaysFailPlane = new(0f, 0f, 0f, -1e9f);

    /// <summary>Extracts 6 normalized Gribb-Hartmann frustum planes from a row-major left-handed [0, 1] Z matrix.</summary>
    /// <param name="viewProjection">Combined view-projection matrix (or oblique/sheared projection * view-projection).</param>
    /// <param name="cullMargin">Extra world-space margin added around every tested AABB.</param>
    /// <param name="requiredCullMask">Optional bitmask tested against <see cref="MeshScene3DInstance.CullMask"/>; zero disables mask filtering.</param>
    /// <param name="originOffset">Origin offset subtracted from instance translations before frustum plane testing.</param>
    /// <returns>The constructed cull view.</returns>
    public static MeshScene3DCullView FromViewProjection(
        in Matrix4x4 viewProjection,
        float cullMargin = 0f,
        uint requiredCullMask = 0u,
        Vector3 originOffset = default)
    {
        Vector4 col0 = new(viewProjection.M11, viewProjection.M21, viewProjection.M31, viewProjection.M41);
        Vector4 col1 = new(viewProjection.M12, viewProjection.M22, viewProjection.M32, viewProjection.M42);
        Vector4 col2 = new(viewProjection.M13, viewProjection.M23, viewProjection.M33, viewProjection.M43);
        Vector4 col3 = new(viewProjection.M14, viewProjection.M24, viewProjection.M34, viewProjection.M44);

        return new MeshScene3DCullView(
            NormalizePlane(col3 + col0),
            NormalizePlane(col3 - col0),
            NormalizePlane(col3 + col1),
            NormalizePlane(col3 - col1),
            NormalizePlane(col2),
            NormalizePlane(col3 - col2),
            cullMargin,
            requiredCullMask,
            EnableFrustumCulling: true,
            OriginOffset: originOffset);
    }

    /// <summary>Extracts a frustum cull view from <paramref name="camera"/>.</summary>
    /// <param name="camera">Camera providing a row-major view-projection matrix.</param>
    /// <param name="cullMargin">Extra world-space margin added around every tested AABB.</param>
    /// <param name="requiredCullMask">Optional bitmask tested against <see cref="MeshScene3DInstance.CullMask"/>.</param>
    /// <param name="originOffset">Origin offset subtracted from instance translations before frustum plane testing.</param>
    /// <returns>The constructed cull view.</returns>
    public static MeshScene3DCullView FromCamera(
        ICamera camera,
        float cullMargin = 0f,
        uint requiredCullMask = 0u,
        Vector3 originOffset = default)
    {
        ArgumentNullException.ThrowIfNull(camera);
        Matrix4x4 vp = camera.ViewProjectionMatrix;
        return FromViewProjection(in vp, cullMargin, requiredCullMask, originOffset);
    }

    /// <summary>Constructs an axis-aligned 3D box cull volume from <paramref name="min"/> and <paramref name="max"/>.</summary>
    /// <param name="min">Minimum box corner (in origin-relative space when <paramref name="originOffset"/> is non-zero).</param>
    /// <param name="max">Maximum box corner (in origin-relative space when <paramref name="originOffset"/> is non-zero).</param>
    /// <param name="cullMargin">Extra world-space margin added around every tested AABB.</param>
    /// <param name="requiredCullMask">Optional bitmask tested against <see cref="MeshScene3DInstance.CullMask"/>.</param>
    /// <param name="originOffset">Origin offset subtracted from instance translations before bounds testing.</param>
    /// <returns>The constructed cull view.</returns>
    public static MeshScene3DCullView FromBounds(
        Vector3 min,
        Vector3 max,
        float cullMargin = 0f,
        uint requiredCullMask = 0u,
        Vector3 originOffset = default)
    {
        return new MeshScene3DCullView(
            new Vector4(1f, 0f, 0f, -min.X),
            new Vector4(-1f, 0f, 0f, max.X),
            new Vector4(0f, 1f, 0f, -min.Y),
            new Vector4(0f, -1f, 0f, max.Y),
            new Vector4(0f, 0f, 1f, -min.Z),
            new Vector4(0f, 0f, -1f, max.Z),
            cullMargin,
            requiredCullMask,
            EnableFrustumCulling: true,
            OriginOffset: originOffset);
    }

    /// <summary>Creates a cull view that disables frustum plane testing while still skipping hidden slots and optionally filtering by <paramref name="requiredCullMask"/>.</summary>
    /// <param name="requiredCullMask">Optional bitmask tested against <see cref="MeshScene3DInstance.CullMask"/>.</param>
    /// <returns>The unculled view.</returns>
    public static MeshScene3DCullView Unculled(uint requiredCullMask = 0u)
    {
        return new MeshScene3DCullView(
            default, default, default, default, default, default,
            CullMargin: 0f,
            RequiredCullMask: requiredCullMask,
            EnableFrustumCulling: false);
    }

    private static Vector4 NormalizePlane(Vector4 plane)
    {
        float lenSq = plane.X * plane.X + plane.Y * plane.Y + plane.Z * plane.Z;
        if (lenSq <= 1e-16f)
        {
            return plane.W >= 0f ? AlwaysPassPlane : AlwaysFailPlane;
        }

        float invLen = 1.0f / MathF.Sqrt(lenSq);
        return plane * invLen;
    }
}

/// <summary>Registration descriptor for one indexed draw bucket inside <see cref="MeshScene3D"/>.</summary>
/// <param name="Tier">Caller-defined render tier key queried by <see cref="MeshScene3D.RenderTier"/>.</param>
/// <param name="Geometry">GPU geometry supplying vertices, indices, and default local bounds.</param>
/// <param name="Material">Graphics material whose root starts with <see cref="MeshScene3DPassHeader"/> (144 bytes).</param>
/// <param name="DepthStencil">Default depth-stencil state applied when drawing this bucket.</param>
/// <param name="BucketTag">Opaque caller-defined identifier forwarded to vertex and pixel stages.</param>
/// <param name="MaterialParamsBuffer">Optional shared parameter table indexed by <see cref="MeshScene3DInstance.MaterialParamId"/>.</param>
/// <param name="FirstIndex">Starting index inside <paramref name="Geometry"/>'s index buffer.</param>
/// <param name="IndexCount">Number of indices to draw per instance, or 0 to draw all remaining indices from <paramref name="FirstIndex"/>.</param>
/// <param name="VertexOffset">Constant vertex offset added to each fetched index.</param>
/// <param name="LocalBoundsMin">Optional override for the bucket's local bounding box minimum.</param>
/// <param name="LocalBoundsMax">Optional override for the bucket's local bounding box maximum.</param>
public readonly record struct MeshScene3DBucketDesc(
    int Tier,
    ModelGeometry Geometry,
    Material Material,
    DepthStencilState DepthStencil,
    uint BucketTag = 0u,
    Alco.Graphics.Buffer? MaterialParamsBuffer = null,
    uint FirstIndex = 0u,
    uint IndexCount = 0u,
    int VertexOffset = 0,
    Vector3? LocalBoundsMin = null,
    Vector3? LocalBoundsMax = null);

/// <summary>Per-frame telemetry reported by <see cref="MeshScene3D.Stats"/>.</summary>
/// <param name="TotalSlots">High-water slot count allocated in the pool.</param>
/// <param name="LiveSlots">Active registered instances across all buckets.</param>
/// <param name="BucketCount">Number of registered draw buckets.</param>
/// <param name="DirtyPatchesUploaded">Dirty instance patches scattered on the last sync.</param>
/// <param name="DrawCallsSubmitted">Indirect draws recorded since the last cull/prepare pass.</param>
/// <param name="ActiveTextureCount">Distinct bindless textures retained by the pool.</param>
public readonly record struct MeshScene3DStats(
    int TotalSlots,
    int LiveSlots,
    int BucketCount,
    int DirtyPatchesUploaded,
    int DrawCallsSubmitted,
    int ActiveTextureCount);
