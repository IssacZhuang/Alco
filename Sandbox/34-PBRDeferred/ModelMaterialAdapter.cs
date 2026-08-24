using Alco.Engine;
using Alco.Graphics;
using Alco.Rendering;
using Alco.World3D;

/// <summary>
/// Maps the glTF loader's runtime <see cref="ModelMaterial"/> (Alco.Engine) to the
/// data-only <see cref="MaterialAsset"/> (Alco.World3D) the material compiler
/// consumes — the game-side glue between the direct glTF scene load and the
/// material system (the .amat asset chain describes the same data from files).
/// Textures stay live objects: the glTF pipeline streams them outside the asset
/// system, so they bind by slot through <see cref="MaterialCompiler.BindTextures"/>
/// as they arrive.
/// </summary>
internal static class ModelMaterialAdapter
{
    /// <summary>
    /// The material asset descriptor of one glTF material. The material evaluates the
    /// built-in PbrStandard surface (no <see cref="MaterialAsset.SurfaceShader"/>), so
    /// the descriptor carries only the flat factors and routing fields.
    /// <br/>glTF alpha routing: only BLEND materials whose name contains "Glass" are
    /// true transparency (forward glass pass); the remaining BLEND materials (Bistro's
    /// foliage, curtains, headlight lenses...) are alpha-cutout content authored as
    /// BLEND, so they map to <see cref="MeshAlphaMode.Mask"/> with the conventional
    /// 0.5 cutoff and stay in the deferred passes.
    /// </summary>
    public static MaterialAsset ToAsset(ModelMaterial material)
    {
        bool glass = material.AlphaMode == GltfAlphaMode.Blend &&
            material.Name.Contains("Glass", StringComparison.OrdinalIgnoreCase);
        return new MaterialAsset
        {
            Name = material.Name,
            BaseColorFactor = material.BaseColorFactor,
            MetallicFactor = material.MetallicFactor,
            RoughnessFactor = material.RoughnessFactor,
            EmissiveFactor = material.EmissiveFactor,
            AlphaMode = material.AlphaMode switch
            {
                _ when glass => MeshAlphaMode.Blend,
                GltfAlphaMode.Mask => MeshAlphaMode.Mask,
                GltfAlphaMode.Blend => MeshAlphaMode.Mask,
                _ => MeshAlphaMode.Opaque,
            },
            AlphaCutoff = material.AlphaMode == GltfAlphaMode.Blend && !glass
                ? 0.5f
                : material.AlphaCutoff,
            DoubleSided = material.DoubleSided,
        };
    }

    /// <summary>
    /// The material's live textures keyed by texture slot (slot name = the surface's
    /// resource name without the leading underscore); null values are still streaming
    /// and bind the fallback textures until assigned.
    /// </summary>
    public static Dictionary<string, Texture2D?> TextureSlotsOf(ModelMaterial material) => new()
    {
        ["albedoTexture"] = material.AlbedoTexture,
        ["normalTexture"] = material.NormalTexture,
        ["metallicRoughnessTexture"] = material.MetallicRoughnessTexture,
        ["emissiveTexture"] = material.EmissiveTexture,
    };

    /// <summary>
    /// The alpha-test threshold of a material asset for the passes that test (G-buffer,
    /// shadow, RSM, voxelization); blend materials route to the glass pass and never
    /// reach these.
    /// </summary>
    public static float ResolveAlphaCutoff(MaterialAsset asset)
        => asset.AlphaMode == MeshAlphaMode.Mask ? asset.AlphaCutoff : 0.0f;
}
