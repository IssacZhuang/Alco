using System.Collections.Frozen;

namespace Vocore.Rendering;

public class TextureAtlas : AutoDisposable
{
    private readonly List<Sprite> _sprites;
    private readonly FrozenDictionary<string, Sprite> _spritesLookup;
    public RenderTexture RenderTexture { get; }
    public IReadOnlyList<Sprite> Sprites => _sprites;
    public Sprite this[string name] => _spritesLookup[name];

    public TextureAtlas(RenderTexture renderTexture, List<Sprite> sprites)
    {
        RenderTexture = renderTexture;
        _sprites = sprites;
        _spritesLookup = sprites.ToFrozenDictionary(sprite => sprite.Name, sprite => sprite);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sprites.Clear();
            RenderTexture.Dispose();
        }
    }
}

