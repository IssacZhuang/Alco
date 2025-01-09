using System.Numerics;
using System.Runtime.InteropServices;

namespace Vocore.Rendering;

public class TileSet<TUserData> : AutoDisposable
{
    private struct TileData
    {
        public Sprite Sprite;
        public TUserData UserData;
    }

    //per sprite in GPU
    [StructLayout(LayoutKind.Sequential)]
    public struct SpriteData
    {
        public Rect UVRect;
        public Vector2 Scale;
    }


    private readonly RenderingSystem _renderingSystem;
    private readonly List<TileData> _tileData;
    private readonly GraphicsArrayBuffer<SpriteData> _spriteData;

    public GraphicsArrayBuffer<SpriteData> SpriteDataBuffer => _spriteData;

    private TextureAtlas Atlas { get; }


    internal TileSet(RenderingSystem renderingSystem, TileSetParams<TUserData> @params, Material material)
    {
        ArgumentNullException.ThrowIfNull(material);

        _renderingSystem = renderingSystem;
        int tileCount = @params.Items.Count;
        _spriteData = new GraphicsArrayBuffer<SpriteData>(renderingSystem, tileCount, "TileSet_sprite_data");

        Dictionary<Texture2D, int> textureToAtlasIndex = new();
        List<Texture2D> uniqueTextures = new();
        
        foreach (var item in @params.Items)
        {
            if (!textureToAtlasIndex.ContainsKey(item.Texture))
            {
                textureToAtlasIndex[item.Texture] = uniqueTextures.Count;
                uniqueTextures.Add(item.Texture);
            }
        }

        // 创建和打包图集
        using TextureAtlasPacker packer = renderingSystem.CreateTextureAtlasPacker(material);
        foreach (var texture in uniqueTextures)
        {
            packer.AddTexture(texture.Name, texture);
        }
        Atlas = packer.BuildTextureAtlas();

        // 为每个item创建tile数据
        _tileData = new List<TileData>(tileCount);
        for (int i = 0; i < tileCount; i++)
        {
            var item = @params.Items[i];
            int atlasIndex = textureToAtlasIndex[item.Texture];
            Sprite sprite = Atlas[atlasIndex];

            _tileData.Add(new TileData
            {
                Sprite = sprite,
                UserData = item.UserData
            });

            _spriteData[i] = new SpriteData
            {
                UVRect = sprite.UVRect,
                Scale = item.Scale
            };
        }
    }




    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _spriteData.Dispose();
        }
    }
}
