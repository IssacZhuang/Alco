namespace Alco.Graphics;

/// <summary>
/// The dimensionality of a texture.
/// </summary>
public enum TextureDimension
{
    /// <summary>A one-dimensional texture.</summary>
    Texture1D,
    /// <summary>A two-dimensional texture (possibly an array of 2D layers).</summary>
    Texture2D,
    /// <summary>A three-dimensional (volume) texture.</summary>
    Texture3D,
}