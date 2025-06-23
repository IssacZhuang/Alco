using Alco.Graphics;
using Alco;

namespace Alco.Engine;

public class Texture2DMeta : Meta
{
    public FilterMode FilterMode { get; set; } = FilterMode.Linear;
    public AddressMode AddressMode { get; set; } = AddressMode.ClampToEdge;

    /// <summary>
    /// The slice padding for 9-slice textures. Defines the padding for left, top, right, and bottom edges.
    /// </summary>
    public Padding SlicePadding { get; set; } = Padding.Zero;
}