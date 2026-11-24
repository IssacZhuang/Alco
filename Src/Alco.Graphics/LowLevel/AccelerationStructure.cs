using System.Numerics;

namespace Alco.Graphics.LowLevel;

/// <summary>Options chosen when sizing and building an acceleration structure.</summary>
[Flags]
public enum AccelerationStructureBuildFlags : byte
{
    /// <summary>Builds without enabling update or compaction.</summary>
    None = 0,

    /// <summary>Allows fixed-topology updates; requires <see cref="DeviceCaps.AccelerationStructureUpdate"/>.</summary>
    AllowUpdate = 1 << 0,

    /// <summary>Allows querying the compacted size and recording a compact copy; requires <see cref="DeviceCaps.AccelerationStructureCompaction"/>.</summary>
    AllowCompaction = 1 << 1,
}

/// <summary>Storage and scratch requirements for the supplied build inputs and flags.</summary>
public record struct AccelerationStructureBuildSizes
{
    /// <summary>The minimum number of bytes to allocate for the destination structure.</summary>
    public ulong Size;

    /// <summary>The minimum byte size and offset alignment of the build scratch range.</summary>
    public SizeAlign BuildScratch;

    /// <summary>The minimum byte size and offset alignment of the update scratch range, or zero if updates were not requested.</summary>
    public SizeAlign UpdateScratch;
}

/// <summary>One geometry of indexed triangles in a bottom-level acceleration structure.</summary>
public record struct BlasTriangleGeometry
{
    /// <summary>The GPU range of float32 XYZ vertex positions, starting at the first position.</summary>
    public GPURange Positions;

    /// <summary>The byte stride between positions, a multiple of four and at least twelve.</summary>
    public uint PositionStride;

    /// <summary>The number of positions accessible to indices.</summary>
    public uint VertexCount;

    /// <summary>The GPU range of tightly packed indices, three per triangle.</summary>
    public GPURange Indices;

    /// <summary>The index element type.</summary>
    public IndexType IndexType;

    /// <summary>The number of triangles.</summary>
    public uint TriangleCount;

    /// <summary>Whether triangle hits can be accepted without shader evaluation. Leave false for alpha-tested geometry.</summary>
    public bool Opaque;
}

/// <summary>An instance of a bottom-level acceleration structure in a top-level build.</summary>
public record struct TlasInstance
{
    /// <summary>The referenced bottom-level structure. It must remain alive while any referencing top-level structure is in use.</summary>
    public required GPUBlas Blas { get; init; }

    /// <summary>The invertible affine object-to-world transform in System.Numerics row-vector convention.</summary>
    public required Matrix4x4 Transform { get; init; }

    /// <summary>An application-chosen ID in the range 0 to 0xFFFFFF, independent of the instance's position in the input span.</summary>
    public uint InstanceId { get; init; }

    /// <summary>The visibility mask. A ray can intersect this instance only when the ray and instance masks share a set bit.</summary>
    public byte Mask { get; init; } = byte.MaxValue;

    /// <summary>Creates an instance descriptor with all visibility mask bits set.</summary>
    public TlasInstance() { }
}

/// <summary>Device-owned storage for an acceleration structure. Its layout is opaque to the application.</summary>
/// <seealso href="https://microsoft.github.io/DirectX-Specs/d3d/Raytracing.html#acceleration-structures">DirectX Raytracing: acceleration structures</seealso>
/// <seealso href="https://docs.vulkan.org/spec/latest/chapters/accelstructures.html">Vulkan specification: acceleration structures</seealso>
/// <seealso href="https://developer.apple.com/documentation/metal/mtl4computecommandencoder">Metal 4 compute and acceleration structure commands</seealso>
public abstract class GPUAccelerationStructure
{
    private protected GPUAccelerationStructure() { }

    /// <summary>Releases this structure after all recorded and executing uses, including use through any referencing TLAS, have completed.</summary>
    public abstract void Destroy();
}

/// <summary>A bottom-level acceleration structure containing triangle geometry.</summary>
public abstract class GPUBlas : GPUAccelerationStructure
{
    private protected GPUBlas() { }
}

/// <summary>A top-level acceleration structure containing instances of bottom-level structures.</summary>
public abstract class GPUTlas : GPUAccelerationStructure
{
    private protected GPUTlas() { }
}

public abstract partial class GPUDevice
{
    /// <summary>Queries the byte requirements for building a bottom-level acceleration structure.</summary>
    /// <param name="geometries">Nonempty triangle geometry descriptions in shader-visible geometry-index order.</param>
    /// <param name="flags">The options that will be used for the build.</param>
    /// <returns>The destination and scratch requirements on this device.</returns>
    /// <exception cref="GPUException">The build or requested options are unsupported.</exception>
    public virtual AccelerationStructureBuildSizes GetBlasBuildSizes(ReadOnlySpan<BlasTriangleGeometry> geometries,
        AccelerationStructureBuildFlags flags = AccelerationStructureBuildFlags.None) => throw RayTracingUnavailable();

    /// <summary>Queries the byte requirements for building a top-level acceleration structure.</summary>
    /// <param name="instances">Nonempty instance descriptions in shader-visible instance-index order.</param>
    /// <param name="flags">The options that will be used for the build.</param>
    /// <returns>The destination and scratch requirements on this device.</returns>
    /// <exception cref="GPUException">The build or requested options are unsupported.</exception>
    public virtual AccelerationStructureBuildSizes GetTlasBuildSizes(ReadOnlySpan<TlasInstance> instances,
        AccelerationStructureBuildFlags flags = AccelerationStructureBuildFlags.None) => throw RayTracingUnavailable();

    /// <summary>Allocates unbuilt storage for a bottom-level acceleration structure.</summary>
    /// <param name="size">The byte size from a build-size or compacted-size query on this device.</param>
    /// <returns>The caller-owned structure, ready to be used as a command destination.</returns>
    /// <exception cref="GPUException">Acceleration structures are unsupported or storage could not be allocated.</exception>
    public virtual GPUBlas CreateBlas(ulong size) => throw RayTracingUnavailable();

    /// <summary>Allocates unbuilt storage for a top-level acceleration structure.</summary>
    /// <param name="size">The byte size from a build-size or compacted-size query on this device.</param>
    /// <returns>The caller-owned structure, ready to be used as a command destination.</returns>
    /// <exception cref="GPUException">Acceleration structures are unsupported or storage could not be allocated.</exception>
    public virtual GPUTlas CreateTlas(ulong size) => throw RayTracingUnavailable();

    private GPUException RayTracingUnavailable() =>
        new(Error.Unsupported, $"Inline ray queries are not supported by this {ActiveBackend} device.");
}

public abstract partial class GPUCommandBuffer
{
    /// <summary>Records a triangle BLAS build or update outside a render pass on a general or compute queue.</summary>
    /// <param name="destination">The destination BLAS, on the owning device.</param>
    /// <param name="geometries">Nonempty geometries, consumed by the call; keep their GPU buffers alive until completion.</param>
    /// <param name="scratch">An aligned GPU-only range meeting the build or update scratch requirements.</param>
    /// <param name="flags">The options used when sizing the build. For an update, use the source's original options.</param>
    /// <param name="updateSource">Null for a build; otherwise a BLAS built with AllowUpdate. May be the destination.</param>
    /// <remarks>An update may change vertex positions only, not indices, counts, layout, or geometry flags. Refresh dependent TLASes after an in-place update.</remarks>
    /// <exception cref="GPUException">The build or requested options are unsupported.</exception>
    public virtual void BuildBlas(GPUBlas destination, ReadOnlySpan<BlasTriangleGeometry> geometries, GPURange scratch,
        AccelerationStructureBuildFlags flags = AccelerationStructureBuildFlags.None, GPUBlas? updateSource = null) =>
        throw RayTracingUnavailable();

    /// <summary>Records an instance TLAS build or update outside a render pass on a general or compute queue.</summary>
    /// <param name="destination">The destination TLAS, on the owning device.</param>
    /// <param name="instances">Nonempty instance descriptions consumed by the call. Order BLAS writes before this build.</param>
    /// <param name="scratch">An aligned GPU-only range meeting the build or update scratch requirements.</param>
    /// <param name="flags">The options used when sizing the build. For an update, use the source's original options.</param>
    /// <param name="updateSource">Null for a build; otherwise a TLAS built with AllowUpdate. May be the destination.</param>
    /// <remarks>An update may change transforms only. Use barriers to order scratch reuse and to make builds visible to dependent builds or queries.</remarks>
    /// <exception cref="GPUException">The build or requested options are unsupported.</exception>
    public virtual void BuildTlas(GPUTlas destination, ReadOnlySpan<TlasInstance> instances, GPURange scratch,
        AccelerationStructureBuildFlags flags = AccelerationStructureBuildFlags.None, GPUTlas? updateSource = null) =>
        throw RayTracingUnavailable();

    /// <summary>Records a GPU write of the compacted byte size outside a render pass on a general or compute queue.</summary>
    /// <param name="source">A built acceleration structure with AllowCompaction.</param>
    /// <param name="destination">An eight-byte-aligned GPU range to receive one unsigned 64-bit byte count.</param>
    /// <exception cref="GPUException">Acceleration structure compaction is unsupported.</exception>
    public virtual void WriteAccelerationStructureCompactedSize(GPUAccelerationStructure source, GPURange destination) =>
        throw RayTracingUnavailable();

    /// <summary>Records a compact copy outside a render pass on a general or compute queue.</summary>
    /// <param name="source">A built acceleration structure with AllowCompaction.</param>
    /// <param name="destination">A distinct structure of the same kind, allocated for at least the reported compacted size.</param>
    /// <remarks>Compaction does not retarget references held by existing TLASes.</remarks>
    /// <exception cref="GPUException">Acceleration structure compaction is unsupported.</exception>
    public virtual void CopyCompactedAccelerationStructure(GPUAccelerationStructure source, GPUAccelerationStructure destination) =>
        throw RayTracingUnavailable();

    /// <summary>Sets the single TLAS binding for subsequent compute ray queries in this command buffer.</summary>
    /// <param name="scene">The built TLAS, or null to clear the binding. Order TLAS writes before the consuming dispatch.</param>
    /// <remarks>On completion, a closest-hit query returns the nearest accepted hit. Non-opaque candidates may be accepted by the shader; their order and multiplicity are unspecified.</remarks>
    /// <exception cref="GPUException">Inline ray queries are unsupported.</exception>
    /// <seealso href="https://microsoft.github.io/DirectX-Specs/d3d/Raytracing.html#inline-raytracing">DirectX Raytracing: inline raytracing</seealso>
    /// <seealso href="https://docs.vulkan.org/refpages/latest/refpages/source/VK_KHR_ray_query.html">Vulkan ray query extension</seealso>
    /// <seealso href="https://developer.apple.com/metal/Metal-Shading-Language-Specification.pdf">Metal Shading Language specification: intersection queries</seealso>
    public virtual void SetRayQueryScene(GPUTlas? scene) => throw RayTracingUnavailable();

    private static GPUException RayTracingUnavailable() =>
        new(Error.Unsupported, "This command buffer does not support acceleration structures or inline ray queries.");
}
