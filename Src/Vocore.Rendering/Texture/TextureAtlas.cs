using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Vocore.Rendering;

public class TextureAtlas : AutoDisposable
{
    private readonly List<Sprite> _sprites;
    private readonly FrozenDictionary<string, Sprite> _spritesLookup;
    public RenderTexture RenderTexture { get; }

    public int Count => _sprites.Count;
    public IReadOnlyList<Sprite> Sprites => _sprites;
    public Sprite this[string name] => _spritesLookup[name];
    public Sprite this[int index] => _sprites[index];

    public TextureAtlas(RenderTexture renderTexture, List<Sprite> sprites)
    {
        RenderTexture = renderTexture;
        _sprites = sprites;
        _spritesLookup = sprites.ToFrozenDictionary(sprite => sprite.Name, sprite => sprite);
    }

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

