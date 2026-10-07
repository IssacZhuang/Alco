namespace Alco.Graphics;

/// <summary>
/// How a texture view is dimensioned for shaders; must be compatible with the
/// underlying texture's dimension and array layer count.
/// </summary>
public enum TextureViewDimension
{
    /// <summary>No view dimension declared.</summary>
    Undefined = 0,
    /// <summary>A single 1D slice.</summary>
    Texture1D = 1,
    // Unsupported
    /// <summary>An array of 1D slices.</summary>
    Texture1DArray = 2,
    /// <summary>A single 2D layer.</summary>
    Texture2D = 3,
    /// <summary>An array of 2D layers.</summary>
    Texture2DArray = 4,
    /// <summary>A 3D volume.</summary>
    Texture3D = 5,
    /// <summary>A cube map (six 2D faces).</summary>
    Cube = 6,
    /// <summary>An array of cube maps.</summary>
    CubeArray = 7,
}