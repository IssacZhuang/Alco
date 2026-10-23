using Alco.Graphics;
using Alco;

namespace Alco.Rendering;

/// <summary>
/// Metadata for a Texture2D asset: filtering, address mode, padding, alpha handling, and sprite rects.
/// </summary>
public class Texture2DMeta : Meta
{
    /// <summary>A sprite rectangle defined in texture pixel coordinates.</summary>
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
        /// Implicitly converts a <see cref="Texture2DMeta.Rect"/> to a <see cref="RectInt"/>.
        /// </summary>
        /// <param name="value">The source rectangle value.</param>
        /// <returns>A <see cref="RectInt"/> with matching coordinates and size.</returns>
        public static implicit operator RectInt(Rect value)
        {
            return new RectInt(value.X, value.Y, value.Width, value.Height);
        }
    }

    /// <summary>The texture filter mode; null inherits the directory or engine default.</summary>
    public FilterMode? FilterMode { get; set; }
    /// <summary>The texture address mode; null inherits the directory or engine default.</summary>
    public AddressMode? AddressMode { get; set; }

    /// <summary>
    /// The slice padding for 9-slice textures. Defines the padding for left, top, right, and bottom edges.
    /// Null means inherit from directory option or engine default.
    /// </summary>
    public Padding? SlicePadding { get; set; }

    /// <summary>
    /// Whether to convert the image from straight alpha to premultiplied alpha during loading.
    /// Null means inherit from directory option or engine default.
    /// </summary>
    public bool? PremultiplyAlpha { get; set; }

    /// <summary>The named sprite rectangles defined in texture pixel coordinates.</summary>
    public Dictionary<string, Rect> Sprites { get; set; } = new();


}