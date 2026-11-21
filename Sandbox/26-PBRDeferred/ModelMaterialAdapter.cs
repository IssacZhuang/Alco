using Alco.Engine;
using Alco.Graphics;
using Alco.Rendering;
using Alco.World3D;

/// <summary>Maps the glTF loader's runtime <see cref="ModelMaterial"/> to the data-only <see cref="PbrMaterialAsset"/> consumed by the material compiler.</summary>
internal static class ModelMaterialAdapter
{
    /// <summary>Creates the material asset descriptor for a glTF material, routing BLEND materials named "Glass" to forward blending and other BLEND materials to alpha masking.</summary>
    public static PbrMaterialAsset ToAsset(ModelMaterial material)
    {
        bool glass = material.AlphaMode == GltfAlphaMode.Blend &&
            material.Name.Contains("Glass", StringComparison.OrdinalIgnoreCase);
        return new PbrMaterialAsset
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
            Textures = TextureSlotsOf(material),
        };
    }

    /// <summary>
    /// The material's loaded textures keyed by texture slot (slot name = the surface's
    /// resource name without the leading underscore); slots whose image is missing or
    /// failed to decode are left out and bind the asset's fallback policy.
    /// </summary>
    public static Dictionary<string, Texture2D> TextureSlotsOf(ModelMaterial material)
    {
        var slots = new Dictionary<string, Texture2D>();
        if (material.AlbedoTexture != null)
        {
            slots["albedoTexture"] = material.AlbedoTexture;
        }
        if (material.NormalTexture != null)
        {
            slots["normalTexture"] = material.NormalTexture;
        }
        if (material.MetallicRoughnessTexture != null)
        {
            slots["metallicRoughnessTexture"] = material.MetallicRoughnessTexture;
        }
        if (material.EmissiveTexture != null)
        {
            slots["emissiveTexture"] = material.EmissiveTexture;
        }
        return slots;
    }

    /// <summary>
    /// The alpha-test threshold of a material asset for the passes that test (G-buffer,
    /// shadow, RSM, voxelization); blend materials route to the glass pass and never
    /// reach these.
    /// </summary>
    public static float ResolveAlphaCutoff(PbrMaterialAsset asset)
        => asset.AlphaMode == MeshAlphaMode.Mask ? asset.AlphaCutoff : 0.0f;
}
