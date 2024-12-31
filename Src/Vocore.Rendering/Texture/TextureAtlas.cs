using System.Collections.Frozen;

namespace Vocore.Rendering;

public class TextureAtlas : AutoDisposable
{
    private readonly List<Sprite> _sprites;
    private readonly FrozenDictionary<string, Sprite> _spritesLookup;
    public Texture2D Texture { get; }
    public IReadOnlyList<Sprite> Sprites => _sprites;
    public Sprite this[string name] => _spritesLookup[name];

    public TextureAtlas(Texture2D texture, List<Sprite> sprites)
    {
        Texture = texture;
        _sprites = sprites;
        _spritesLookup = sprites.ToFrozenDictionary(sprite => sprite.Name, sprite => sprite);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sprites.Clear();
            Texture.Dispose();
        }
    }
}
