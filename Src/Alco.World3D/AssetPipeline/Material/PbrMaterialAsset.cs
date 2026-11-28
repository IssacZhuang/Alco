using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// The World3D PBR material asset parsed from <c>.amat</c> files carrying
/// <c>"$type": "Alco.World3D.PbrMaterialAsset"</c>.
/// </summary>
public sealed class PbrMaterialAsset : MaterialAsset
{
    /// <summary>The built-in surface module every World3D pass composes with when the asset names none.</summary>
    public const string DefaultSurfaceModule = "World3DPbrStandard";

    /// <summary>Shared default PBR material targeting <see cref="DefaultSurfaceModule"/>.</summary>
    public static PbrMaterialAsset Default { get; } = new() { Name = DefaultSurfaceModule };

    /// <summary>Linear base color factor, multiplied with the base color texture.</summary>
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

    /// <summary>Optical transmission factor in [0, 1] for refractive glass materials.</summary>
    public float TransmissionFactor { get; set; }

    /// <summary>Index of refraction in [1, 3] for dielectrics and transmissive glass.</summary>
    public float Ior { get; set; } = 1.5f;

    /// <summary>Shared sampler bank selector applied to all texture slots of this material.</summary>
    public World3DMaterialSampler Sampler { get; set; } = World3DMaterialSampler.LinearRepeat;

    /// <summary>
    /// Converts this material asset into a validated <see cref="World3DMaterialDesc"/>, mapping canonical
    /// PBR texture slots (<c>baseColor</c>, <c>normal</c>, <c>metallicRoughness</c>, <c>emissive</c>).
    /// </summary>
    /// <param name="resolveTexture">Callback resolving an <see cref="Image"/> to a registered <see cref="WorldTextureHandle"/>.</param>
    /// <param name="description">Receives the constructed <see cref="World3DMaterialDesc"/> on success.</param>
    /// <param name="error">Receives a human-readable validation error on failure.</param>
    /// <returns><see langword="true"/> when all factors, slots, and texture resolutions succeeded.</returns>
    public bool TryBuildMaterialDesc(
        Func<Image, WorldTextureHandle> resolveTexture,
        out World3DMaterialDesc description,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(resolveTexture);
        description = default;

        if (!IsFinite(BaseColorFactor) || !IsUnit(BaseColorFactor.W))
        {
            error = $"Material '{Name}' has an invalid BaseColorFactor.";
            return false;
        }
        if (!IsFinite(EmissiveFactor))
        {
            error = $"Material '{Name}' has a non-finite EmissiveFactor.";
            return false;
        }
        if (!IsUnit(MetallicFactor) || !IsUnit(RoughnessFactor) || !IsUnit(AlphaCutoff) || !IsUnit(TransmissionFactor))
        {
            error = $"Material '{Name}' has a scalar PBR factor outside [0, 1].";
            return false;
        }
        if (Ior != 0f && !(Ior >= 1f && Ior <= 3f))
        {
            error = $"Material '{Name}' IOR ({Ior}) must be 0 or in [1, 3].";
            return false;
        }
        if (Surface != null && string.IsNullOrWhiteSpace(Surface))
        {
            error = $"Material '{Name}' has a whitespace-only Surface module name.";
            return false;
        }

        WorldTextureHandle baseColorTex = default;
        WorldTextureHandle normalTex = default;
        WorldTextureHandle mrTex = default;
        WorldTextureHandle emissiveTex = default;

        foreach ((string slot, Image image) in Textures)
        {
            WorldTextureHandle resolved = resolveTexture(image);
            if (resolved == default)
            {
                error = $"Material '{Name}' could not resolve texture for slot '{slot}'.";
                return false;
            }

            if (slot.Equals("baseColor", StringComparison.OrdinalIgnoreCase))
                baseColorTex = resolved;
            else if (slot.Equals("normal", StringComparison.OrdinalIgnoreCase))
                normalTex = resolved;
            else if (slot.Equals("metallicRoughness", StringComparison.OrdinalIgnoreCase))
                mrTex = resolved;
            else if (slot.Equals("emissive", StringComparison.OrdinalIgnoreCase))
                emissiveTex = resolved;
            else
            {
                error = $"Material '{Name}' specifies unknown PBR texture slot '{slot}'. Allowed slots: baseColor, normal, metallicRoughness, emissive.";
                return false;
            }
        }

        World3DAlphaMode alphaMode = AlphaMode switch
        {
            MeshAlphaMode.Opaque => World3DAlphaMode.Opaque,
            MeshAlphaMode.Mask => World3DAlphaMode.Mask,
            MeshAlphaMode.Blend => World3DAlphaMode.Blend,
            _ => (World3DAlphaMode)uint.MaxValue,
        };
        if (alphaMode > World3DAlphaMode.Blend)
        {
            error = $"Material '{Name}' has unsupported alpha mode {AlphaMode}.";
            return false;
        }

        description = new World3DMaterialDesc
        {
            BaseColor = BaseColorFactor,
            Emissive = EmissiveFactor,
            Metallic = MetallicFactor,
            Roughness = RoughnessFactor,
            AlphaCutoff = AlphaCutoff,
            AlphaMode = alphaMode,
            DoubleSided = DoubleSided,
            Sampler = Sampler,
            BaseColorTexture = baseColorTex,
            NormalTexture = normalTex,
            MetallicRoughnessTexture = mrTex,
            EmissiveTexture = emissiveTex,
            Transmission = TransmissionFactor,
            Ior = Ior,
            SurfaceModule = string.IsNullOrEmpty(Surface) ? DefaultSurfaceModule : Surface.Trim(),
        };
        error = null;
        return true;
    }

    private static bool IsUnit(float v) => v >= 0f && v <= 1f;
    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool IsFinite(Vector4 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W);
}
