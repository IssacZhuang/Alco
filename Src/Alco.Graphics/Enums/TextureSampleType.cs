
namespace Alco.Graphics;

/// <summary>
/// The sample result type a shader expects from a texture binding; must be
/// compatible with the texture's format.
/// </summary>
public enum TextureSampleType
{
    /// <summary>No sample type declared.</summary>
    None = 0,
    /// <summary>Regular floating-point sampling.</summary>
    Float = 1,
    /// <summary>
    /// Floating-point sampling without filtering (no linear filtering or
    /// mip interpolation); used for formats that lose precision otherwise.
    /// </summary>
    UnfilterableFloat = 2,
    /// <summary>Depth comparison sampling (comparison sampler required).</summary>
    Depth = 3,
    /// <summary>Signed integer sampling; texel values arrive as ints.</summary>
    Sint = 4,
    /// <summary>Unsigned integer sampling; texel values arrive as uints.</summary>
    Uint = 5
}