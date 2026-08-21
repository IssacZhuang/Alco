using System.IO;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Texture-slot binding shared by the material pass adapters: binds each slot (material
/// slot name → shader resource name, see <see cref="StandardSurfaceSlotsUtility"/>) with the
/// shared fallback for absent textures, and fails loudly on slots the shader does not
/// declare — usually a typo in the material asset.
/// </summary>
internal static class MaterialPassTexturesUtility
{
    /// <summary>
    /// Bind the given texture slots into a compiled pass material, applying fallbacks
    /// (flat normal, black emissive, white otherwise) for slots still streaming.
    /// </summary>
    /// <param name="material">A material compiled by a pass for the slot-owning asset.</param>
    /// <param name="slots">The material texture slots to bind, by slot name.</param>
    /// <param name="rendering">The rendering system (fallback texture source).</param>
    /// <param name="flatNormal">The pass's flat-normal fallback, when it owns one.</param>
    /// <exception cref="InvalidDataException">Thrown when the material declares slots the
    /// shader does not have.</exception>
    public static void Bind(
        GraphicsMaterial material,
        IReadOnlyDictionary<string, Texture2D?> slots,
        RenderingSystem rendering,
        Texture2D? flatNormal = null)
    {
        List<string>? unmatched = null;
        foreach (KeyValuePair<string, Texture2D?> pair in slots)
        {
            string resource = StandardSurfaceSlotsUtility.ShaderResourceName(pair.Key);
            Texture2D texture = pair.Value ?? Fallback(pair.Key, rendering, flatNormal);
            if (!material.TrySetTexture(resource, texture))
            {
                (unmatched ??= new List<string>()).Add(pair.Key);
            }
        }
        if (unmatched != null)
        {
            throw new InvalidDataException(
                $"Material '{material.Name}' declares texture slots the shader does not have: {string.Join(", ", unmatched)}.");
        }
    }

    private static Texture2D Fallback(string slot, RenderingSystem rendering, Texture2D? flatNormal) => slot switch
    {
        // The flat normal decodes to the identity tangent-space normal; the black
        // emissive keeps unstreamed emissive maps dark instead of glowing white.
        StandardSurfaceSlotsUtility.Normal => flatNormal ?? rendering.TextureWhite,
        StandardSurfaceSlotsUtility.Emissive => rendering.TextureBlack,
        _ => rendering.TextureWhite,
    };
}
