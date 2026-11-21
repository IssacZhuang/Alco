namespace Alco.Rendering;

/// <summary>Rasterized single-channel glyph distance field bitmap in top-down row-major layout.</summary>
public readonly struct GlyphRasterBitmap
{
    /// <summary>Bitmap width in texels.</summary>
    public int Width { get; init; }

    /// <summary>Bitmap height in texels.</summary>
    public int Height { get; init; }

    /// <summary>Horizontal pixel offset from the pen origin to the bitmap left edge.</summary>
    public int Left { get; init; }

    /// <summary>Vertical pixel offset from the baseline to the bitmap top edge (positive upward).</summary>
    public int Top { get; init; }

    /// <summary>Tightly packed 8-bit SDF coverage bytes (<c>Width * Height</c>).</summary>
    public byte[] Alpha { get; init; }
}
