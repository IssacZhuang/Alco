using Alco.Graphics;
using Alco;

namespace Alco.Engine;

public class Texture2DMeta : Meta
{
    public class Rect
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
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

    public FilterMode FilterMode { get; set; } = FilterMode.Linear;
    public AddressMode AddressMode { get; set; } = AddressMode.ClampToEdge;

    /// <summary>
    /// The slice padding for 9-slice textures. Defines the padding for left, top, right, and bottom edges.
    /// </summary>
    public Padding SlicePadding { get; set; } = Padding.Zero;

    public Dictionary<string, Rect> Sprites { get; set; } = new();


}