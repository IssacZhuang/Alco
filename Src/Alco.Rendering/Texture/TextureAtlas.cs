using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Alco.Rendering;

/// <summary>
/// A render texture holding a collection of named sprites packed into one atlas.
/// </summary>
public sealed class TextureAtlas : AutoDisposable
{
    private readonly List<Sprite> _sprites;
    private readonly FrozenDictionary<string, Sprite> _spritesLookup;
    /// <summary>The render texture containing the packed sprites; disposed with the atlas.</summary>
    public RenderTexture RenderTexture { get; }

    /// <summary>The number of sprites in the atlas.</summary>
    public int Count => _sprites.Count;
    /// <summary>The sprites in the atlas.</summary>
    public IReadOnlyList<Sprite> Sprites => _sprites;
    /// <summary>Gets the sprite with the given name.</summary>
    /// <param name="name">The sprite name.</param>
    public Sprite this[string name] => _spritesLookup[name];
    /// <summary>Gets the sprite at the given index.</summary>
    /// <param name="index">The sprite index.</param>
    public Sprite this[int index] => _sprites[index];

    /// <summary>Creates an atlas over the given render texture and sprite list.</summary>
    /// <param name="renderTexture">The render texture containing the packed sprites; ownership transfers to the atlas.</param>
    /// <param name="sprites">The sprites to include in the atlas.</param>
    public TextureAtlas(RenderTexture renderTexture, List<Sprite> sprites)
    {
        RenderTexture = renderTexture;
        _sprites = sprites;
        _spritesLookup = sprites.ToFrozenDictionary(sprite => sprite.Name, sprite => sprite);
    }

    /// <summary>Gets the sprite with the given name, returning false when it does not exist.</summary>
    /// <param name="name">The sprite name.</param>
    /// <param name="sprite">The found sprite, or null.</param>
    /// <returns>True when a sprite with the name exists.</returns>
    public bool TryGetSprite(string name, [NotNullWhen(true)] out Sprite? sprite)
    {
        return _spritesLookup.TryGetValue(name, out sprite);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RenderTexture.Dispose();
        }
        _sprites.Clear();
    }
}

