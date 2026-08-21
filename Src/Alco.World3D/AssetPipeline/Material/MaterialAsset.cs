using System.Numerics;

namespace Alco.World3D;

/// <summary>
/// Data-only description of one PBR material — the runtime form of a material asset file
/// (<c>.amat</c>). Pure data: no GPU objects, and texture references stay paths so parsing
/// never blocks on bulk texture IO (textures are resolved separately at warm-up or first
/// use, then bound into the compiled materials via <see cref="MaterialCompiler.BindTextures"/>).
/// Per-pass GPU materials are derived from this description by <see cref="MaterialCompiler"/>.
/// </summary>
public sealed class MaterialAsset
{
    /// <summary>Format version of the material asset files this runtime consumes.</summary>
    public const string FormatVersion = "1.0";

    /// <summary>The material name; defaults to the source file name when the file omits it.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The shading domain. Only <c>"pbr"</c> (metallic-roughness) exists in M1.</summary>
    public string Domain { get; init; } = "pbr";

    /// <summary>Linear base color factor, multiplied with the albedo texture.</summary>
    public Vector4 BaseColorFactor { get; init; } = Vector4.One;

    /// <summary>Metallic factor in [0, 1].</summary>
    public float MetallicFactor { get; init; }

    /// <summary>Roughness factor in [0, 1].</summary>
    public float RoughnessFactor { get; init; } = 1.0f;

    /// <summary>Linear emissive color factor, multiplied with the emissive texture.</summary>
    public Vector3 EmissiveFactor { get; init; } = Vector3.Zero;

    /// <summary>The alpha handling mode.</summary>
    public MeshAlphaMode AlphaMode { get; init; }

    /// <summary>Alpha cutoff used when <see cref="AlphaMode"/> is <see cref="MeshAlphaMode.Mask"/>.</summary>
    public float AlphaCutoff { get; init; } = 0.5f;

    /// <summary>Whether both faces of triangles are rendered.</summary>
    public bool DoubleSided { get; init; }

    /// <summary>Albedo (base color) texture path relative to the asset root; null when absent.</summary>
    public string? AlbedoTexture { get; init; }

    /// <summary>Tangent-space normal map path; null when absent.</summary>
    public string? NormalTexture { get; init; }

    /// <summary>Metallic-roughness texture path (roughness in G, metallic in B); null when absent.</summary>
    public string? MetallicRoughnessTexture { get; init; }

    /// <summary>Emissive texture path; null when absent.</summary>
    public string? EmissiveTexture { get; init; }

    /// <summary>
    /// Enumerate the texture paths referenced by this material in slot order, skipping
    /// empty slots.
    /// </summary>
    public IEnumerable<string> EnumerateTexturePaths()
    {
        if (!string.IsNullOrEmpty(AlbedoTexture))
        {
            yield return AlbedoTexture;
        }
        if (!string.IsNullOrEmpty(NormalTexture))
        {
            yield return NormalTexture;
        }
        if (!string.IsNullOrEmpty(MetallicRoughnessTexture))
        {
            yield return MetallicRoughnessTexture;
        }
        if (!string.IsNullOrEmpty(EmissiveTexture))
        {
            yield return EmissiveTexture;
        }
    }
}
