namespace Alco.World3D;

/// <summary>
/// Texture-slot naming shared by material assets, surface shaders and pass adapters:
/// a material texture slot named <c>albedo</c> binds to the shader resource
/// <c>_albedoTexture</c> — the rule is a single leading underscore
/// (<see cref="ShaderResourceName"/>). The four standard slots below are the ones the
/// built-in PbrStandard surface declares; custom surfaces declare their own slots
/// (e.g. <c>noiseMap</c> for a shader resource <c>_noiseMap</c>).
/// </summary>
public static class StandardSurfaceSlotsUtility
{
    /// <summary>Albedo (base color) slot of the built-in surface.</summary>
    public const string Albedo = "albedo";

    /// <summary>Tangent-space normal map slot of the built-in surface.</summary>
    public const string Normal = "normal";

    /// <summary>Metallic-roughness slot of the built-in surface.</summary>
    public const string MetallicRoughness = "metallicRoughness";

    /// <summary>Emissive slot of the built-in surface.</summary>
    public const string Emissive = "emissive";

    /// <summary>
    /// The shader resource name a material texture slot binds to: the slot name with a
    /// leading underscore.
    /// </summary>
    /// <param name="slot">The material texture slot name.</param>
    /// <returns>The shader resource name.</returns>
    public static string ShaderResourceName(string slot) => "_" + slot;
}
