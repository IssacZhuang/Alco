using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Metadata for an <see cref="Image"/> asset: filtering, address mode, anisotropy,
/// 9-slice padding, alpha handling, and named sprite rectangles.
/// </summary>
public class ImageMeta : Meta
{
    /// <summary>A sprite rectangle defined in image pixel coordinates.</summary>
    public class Rect
    {
        /// <summary>The horizontal origin in pixels.</summary>
        public int X { get; set; }

        /// <summary>The vertical origin in pixels.</summary>
        public int Y { get; set; }

        /// <summary>The rectangle width in pixels.</summary>
        public int Width { get; set; }

        /// <summary>The rectangle height in pixels.</summary>
        public int Height { get; set; }

        /// <summary>
        /// Implicitly converts an <see cref="ImageMeta.Rect"/> to a <see cref="RectInt"/>.
        /// </summary>
        /// <param name="value">The source rectangle value.</param>
        /// <returns>A <see cref="RectInt"/> with matching coordinates and size.</returns>
        public static implicit operator RectInt(Rect value)
        {
            return new RectInt(value.X, value.Y, value.Width, value.Height);
        }
    }

    /// <summary>The texture filter mode; null inherits the directory or engine default.</summary>
    public Filter? FilterMode { get; set; }

    /// <summary>The texture address mode; null inherits the directory or engine default.</summary>
    public AddressMode? AddressMode { get; set; }

    /// <summary>The maximum sampler anisotropy; null inherits the directory or engine default.</summary>
    public ushort? Anisotropy { get; set; }

    /// <summary>
    /// The slice padding for 9-slice images. Defines the padding for left, top, right, and bottom edges.
    /// Null means inherit from directory option or engine default.
    /// </summary>
    public Padding? SlicePadding { get; set; }

    /// <summary>
    /// Whether to convert the image from straight alpha to premultiplied alpha during loading.
    /// Null means inherit from directory option or engine default.
    /// </summary>
    public bool? PremultiplyAlpha { get; set; }

    /// <summary>The named sprite rectangles defined in image pixel coordinates.</summary>
    public Dictionary<string, Rect> Sprites { get; set; } = new();
}
