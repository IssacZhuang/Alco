using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Represents a sprite region over a GPU <see cref="Alco.Graphics.Texture"/>, carrying UV coordinates and optional import hints.
/// </summary>
public sealed class Sprite
{
    /// <summary>
    /// Gets the name of the sprite.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the GPU texture associated with this sprite.
    /// </summary>
    public Texture Texture { get; }

    /// <summary>
    /// Gets the UV rectangle that defines the portion of the texture used by this sprite.
    /// </summary>
    public Rect UvRect { get; }

    /// <summary>
    /// Gets the 9-slice border padding, in texels, associated with this sprite.
    /// </summary>
    public Padding SlicePadding { get; }

    /// <summary>
    /// Gets the preferred texture sampling filter mode for this sprite.
    /// </summary>
    public Filter FilterMode { get; }

    /// <summary>
    /// Gets whether texels in the sprite are stored in premultiplied alpha format.
    /// </summary>
    public bool IsPremultiplied { get; }

    /// <summary>
    /// Gets the sprite size in texels derived from the texture extent and UV rectangle.
    /// </summary>
    public Vector2 PixelSize { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Sprite"/> class.
    /// </summary>
    /// <param name="name">The name of the sprite.</param>
    /// <param name="texture">The GPU texture used by the sprite.</param>
    /// <param name="uvRect">The UV rectangle that defines the portion of the texture used by this sprite.</param>
    /// <param name="slicePadding">Optional 9-slice border padding in texels.</param>
    /// <param name="filterMode">Preferred sampling filter mode.</param>
    /// <param name="isPremultiplied">Whether texels are premultiplied alpha.</param>
    public Sprite(
        string name,
        Texture texture,
        Rect uvRect,
        Padding slicePadding = default,
        Filter filterMode = Filter.Linear,
        bool isPremultiplied = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(texture);

        Name = name;
        Texture = texture;
        UvRect = uvRect;
        SlicePadding = slicePadding;
        FilterMode = filterMode;
        IsPremultiplied = isPremultiplied;
        PixelSize = new Vector2(texture.Extent.X * uvRect.Size.X, texture.Extent.Y * uvRect.Size.Y);
    }
}