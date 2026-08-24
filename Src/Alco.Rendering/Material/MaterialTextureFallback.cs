namespace Alco.Rendering;

/// <summary>
/// The built-in fallback textures a material can request for one of its texture slots
/// (see <see cref="MaterialAsset.GetTextureFallback"/>): what binds while a slot has no
/// texture or its texture is still streaming. Resolved to device textures by the
/// <see cref="MaterialCompiler"/>.
/// </summary>
public enum MaterialTextureFallback
{
    /// <summary>Opaque white — the neutral default (multiplies to no change).</summary>
    White,

    /// <summary>Opaque black — keeps unbound additive terms (e.g. emissive) dark.</summary>
    Black,

    /// <summary>The 1x1 (128, 128, 255) flat normal — decodes to the identity tangent-space normal.</summary>
    FlatNormal,
}
