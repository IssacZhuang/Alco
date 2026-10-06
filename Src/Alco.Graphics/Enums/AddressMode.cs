namespace Alco.Graphics;

/// <summary>
/// How a sampler treats texture coordinates outside the 0..1 range.
/// </summary>
public enum AddressMode
{
    /// <summary>The texture repeats; the fractional part of the coordinate is used.</summary>
    Repeat = 0,
    /// <summary>The texture repeats mirrored, avoiding a seam between repeats.</summary>
    MirrorRepeat = 1,
    /// <summary>Coordinates are clamped to the texture edge; edge texels stretch outward.</summary>
    ClampToEdge = 2
}