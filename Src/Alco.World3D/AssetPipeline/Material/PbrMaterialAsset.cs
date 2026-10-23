using System.Numerics;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// The World3D material asset: a <see cref="MaterialAsset"/> plus the built-in PbrStandard surface's flat metallic-roughness factors and the alpha/double-sided routing fields. Parsed from <c>.amat</c> files carrying the <c>"$type": "Alco.World3D.PbrMaterialAsset"</c> discriminator.
/// </summary>
public sealed class PbrMaterialAsset : MaterialAsset
{
    /// <summary>The module name of the built-in surface every World3D pass composes with when the asset names none.</summary>
    public const string DefaultSurfaceModule = "PbrStandard";

    /// <summary>The shared asset selecting the built-in PbrStandard surface, for pipeline-level defaults.</summary>
    public static PbrMaterialAsset Default { get; } = new() { Name = "PbrStandard" };

    /// <summary>Linear base color factor, multiplied with the albedo texture.</summary>
    public Vector4 BaseColorFactor { get; set; } = Vector4.One;

    /// <summary>Metallic factor in [0, 1].</summary>
    public float MetallicFactor { get; set; }

    /// <summary>Roughness factor in [0, 1].</summary>
    public float RoughnessFactor { get; set; } = 1.0f;

    /// <summary>Linear emissive color factor, multiplied with the emissive texture.</summary>
    public Vector3 EmissiveFactor { get; set; } = Vector3.Zero;

    /// <summary>The alpha handling mode.</summary>
    public MeshAlphaMode AlphaMode { get; set; }

    /// <summary>Alpha cutoff used when <see cref="AlphaMode"/> is <see cref="MeshAlphaMode.Mask"/>.</summary>
    public float AlphaCutoff { get; set; } = 0.5f;

    /// <summary>Whether both faces of triangles are rendered.</summary>
    public bool DoubleSided { get; set; }

    /// <summary>
    /// The World3D fallback policy: flat normal for normal maps (decodes to the identity
    /// tangent-space normal), black for emissive (keeps unstreamed emissive maps dark),
    /// white otherwise.
    /// </summary>
    public override MaterialTextureFallback GetTextureFallback(string slot)
    {
        if (slot.StartsWith("normal", StringComparison.OrdinalIgnoreCase))
        {
            return MaterialTextureFallback.FlatNormal;
        }
        if (slot.StartsWith("emissive", StringComparison.OrdinalIgnoreCase))
        {
            return MaterialTextureFallback.Black;
        }
        return MaterialTextureFallback.White;
    }
}
