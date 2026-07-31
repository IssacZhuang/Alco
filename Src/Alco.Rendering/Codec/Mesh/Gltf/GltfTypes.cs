using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Alpha handling mode of a glTF material.
/// </summary>
public enum GltfAlphaMode
{
    /// <summary>Fully opaque, alpha is ignored.</summary>
    Opaque,

    /// <summary>Alpha tested against the material's cutoff value.</summary>
    Mask,

    /// <summary>Alpha blended. The deferred PBR pipeline approximates these as alpha tested.</summary>
    Blend,
}

/// <summary>
/// A decoded glTF material, metallic-roughness workflow subset relevant to the engine.
/// Factors are linear; texture references resolve into <see cref="GltfModel.Images"/>.
/// </summary>
public sealed class GltfMaterial
{
    /// <summary>The material name, or empty when unnamed.</summary>
    public required string Name { get; init; }

    /// <summary>Linear base color factor, multiplied with the base color texture.</summary>
    public Vector4 BaseColorFactor { get; init; } = Vector4.One;

    /// <summary>Metallic factor in [0, 1].</summary>
    public float MetallicFactor { get; init; }

    /// <summary>Roughness factor in [0, 1].</summary>
    public float RoughnessFactor { get; init; } = 1.0f;

    /// <summary>Index into <see cref="GltfModel.Images"/> of the base color texture, -1 when absent.</summary>
    public int BaseColorImageIndex { get; init; } = -1;

    /// <summary>Index into <see cref="GltfModel.Images"/> of the normal texture, -1 when absent.</summary>
    public int NormalImageIndex { get; init; } = -1;

    /// <summary>Index into <see cref="GltfModel.Images"/> of the metallic-roughness texture, -1 when absent.</summary>
    public int MetallicRoughnessImageIndex { get; init; } = -1;

    /// <summary>Index into <see cref="GltfModel.Images"/> of the emissive texture, -1 when absent.</summary>
    public int EmissiveImageIndex { get; init; } = -1;

    /// <summary>Linear emissive color factor, multiplied with the emissive texture.</summary>
    public Vector3 EmissiveFactor { get; init; } = Vector3.Zero;

    /// <summary>Horizontal texture wrap mode of the base color texture sampler.</summary>
    public AddressMode WrapS { get; init; }

    /// <summary>Horizontal texture wrap mode of the normal texture sampler.</summary>
    public AddressMode NormalWrapS { get; init; }

    /// <summary>Horizontal texture wrap mode of the metallic-roughness texture sampler.</summary>
    public AddressMode MetallicRoughnessWrapS { get; init; }

    /// <summary>Horizontal texture wrap mode of the emissive texture sampler.</summary>
    public AddressMode EmissiveWrapS { get; init; }

    /// <summary>Vertical texture wrap mode of the base color texture sampler.</summary>
    public AddressMode WrapT { get; init; }

    /// <summary>The alpha handling mode.</summary>
    public GltfAlphaMode AlphaMode { get; init; }

    /// <summary>Alpha cutoff for <see cref="GltfAlphaMode.Mask"/>.</summary>
    public float AlphaCutoff { get; init; } = 0.5f;

    /// <summary>Whether both faces of triangles are rendered.</summary>
    public bool DoubleSided { get; init; }
}

/// <summary>
/// A decoded glTF image reference. Either an external <see cref="Uri"/> relative to the
/// glTF file, or embedded data (GLB chunk / data URI) accessible via <see cref="GetEmbeddedData"/>.
/// </summary>
public sealed class GltfImage
{
    private readonly byte[]? _embeddedData;

    /// <summary>The image name (may be empty), or the file name for external images.</summary>
    public required string Name { get; init; }

    /// <summary>The external URI relative to the glTF file, null for embedded images.</summary>
    public string? Uri { get; init; }

    /// <summary>The MIME type of embedded images (e.g. image/png), null for external images.</summary>
    public string? MimeType { get; init; }

    /// <summary>Embedded image bytes, null for external images.</summary>
    public ReadOnlySpan<byte> EmbeddedData => _embeddedData;

    /// <summary>Create an external image reference.</summary>
    public GltfImage()
    {
    }

    /// <summary>Create an embedded image with the given encoded bytes.</summary>
    /// <param name="data">The encoded image bytes (PNG/JPEG/...).</param>
    public GltfImage(byte[] data)
    {
        _embeddedData = data;
    }
}

/// <summary>
/// A decoded glTF mesh: a named group of primitives. Primitives are stored contiguously
/// in <see cref="GltfModel.Primitives"/> starting at <see cref="PrimitiveStart"/>.
/// </summary>
public sealed class GltfMesh
{
    /// <summary>The mesh name, or empty when unnamed.</summary>
    public required string Name { get; init; }

    /// <summary>Index of the first primitive in <see cref="GltfModel.Primitives"/>.</summary>
    public int PrimitiveStart { get; init; }

    /// <summary>The number of primitives of this mesh.</summary>
    public int PrimitiveCount { get; init; }
}

/// <summary>
/// One renderable instance: a mesh placed into the scene by a node hierarchy transform.
/// </summary>
public readonly struct GltfDrawItem
{
    /// <summary>Index into <see cref="GltfModel.Meshes"/>.</summary>
    public int MeshIndex { get; }

    /// <summary>The engine-space world transform of the mesh.</summary>
    public Matrix4x4 World { get; }

    /// <summary>Create a draw item.</summary>
    public GltfDrawItem(int meshIndex, in Matrix4x4 world)
    {
        MeshIndex = meshIndex;
        World = world;
    }
}
