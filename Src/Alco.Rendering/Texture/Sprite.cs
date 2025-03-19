using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Represents a sprite that can be rendered, containing a texture and UV coordinates.
/// </summary>
public sealed class Sprite
{
    /// <summary>
    /// Gets the name of the sprite.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the texture associated with this sprite.
    /// </summary>
    public Texture2D Texture { get; }

    /// <summary>
    /// Gets the UV rectangle that defines the portion of the texture used by this sprite.
    /// </summary>
    public Rect UvRect { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Sprite"/> class.
    /// </summary>
    /// <param name="name">The name of the sprite.</param>
    /// <param name="texture">The texture used by the sprite.</param>
    /// <param name="uvRect">The UV rectangle that defines the portion of the texture used by this sprite.</param>
    internal Sprite(string name, Texture2D texture, Rect uvRect)
    {
        Name = name;
        Texture = texture;
        UvRect = uvRect;
    }
}