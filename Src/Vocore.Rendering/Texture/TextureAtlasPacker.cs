using Vocore.Graphics;

namespace Vocore.Rendering;

public class TextureAtlasPacker
{
    private struct TextureItem
    {
        public string Name;
        public Texture2D Texture;
    }
    private readonly RenderingSystem _renderingSystem;
    private readonly RectPacker<TextureItem> _packer;
    private readonly PixelFormat _format;

    internal TextureAtlasPacker(RenderingSystem rendering,
    PixelFormat format,
    //it just initial size
    int minWidth = 256,
    int minHeight = 256
    )
    {
        _renderingSystem = rendering;
        _packer = new RectPacker<TextureItem>(minWidth, minHeight);
        _format = format;
    }

    public void AddTexture(string name, Texture2D texture)
    {
        _packer.AddRect((int)texture.Width, (int)texture.Height, new TextureItem { Name = name, Texture = texture });
    }

    public TextureAtlas BuildTextureAtlas()
    {
        RenderTexture atlasTexture = _renderingSystem.CreateRenderTexture(
            _renderingSystem.PrefferedRGBATexturePass,
            (uint)_packer.Width,
            (uint)_packer.Height,
            "atlas_texture"
        );

        //todo: write texture

        List<Sprite> sprites = new List<Sprite>();
        // foreach (var item in _packer.Items)
        // {
        //     sprites.Add(new Sprite(item.Data.Name, atlasTexture, item.Rect.Normalize(atlasTexture.Width, atlasTexture.Height), true));
        // }
        for (int i = 0; i < _packer.Count; i++)
        {
            var item = _packer.GetRect(i);
            sprites.Add(new Sprite(item.Data.Name, atlasTexture, item.Rect.Normalize(atlasTexture.Width, atlasTexture.Height), true));
        }

        return new TextureAtlas(atlasTexture, sprites);
    }
}