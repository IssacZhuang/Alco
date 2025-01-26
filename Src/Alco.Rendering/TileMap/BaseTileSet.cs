using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

public abstract class BaseTileSet<TTileData, TUserData> : AutoDisposable where TTileData : unmanaged, ITileData
{
    protected struct SpriteData
    {
        public Sprite Sprite;
        public TUserData UserData;
    }


    protected readonly RenderingSystem _renderingSystem;
    protected readonly List<SpriteData> _spriteData;
    protected readonly GraphicsArrayBuffer<TTileData> _tileData;

    protected readonly TextureAtlas _atlas;

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _spriteData.Count;
    }

    public GraphicsArrayBuffer<TTileData> TileDataBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tileData;
    }

    public TextureAtlas Atlas
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _atlas;
    }

    public RenderTexture AtlasTexture
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _atlas.RenderTexture;
    }

    internal BaseTileSet(
        RenderingSystem renderingSystem,
        BaseTileSetParams<TTileData, TUserData> @params,
        Material material,
        GPUSampler sampler,
        string name)
    {
        ArgumentNullException.ThrowIfNull(material);

        _renderingSystem = renderingSystem;
        int tileCount = @params.Count;
        _tileData = renderingSystem.CreateGraphicsArrayBuffer<TTileData>(tileCount, name + "_sprite_data");

        Dictionary<Texture2D, int> textureToAtlasIndex = new();
        List<Texture2D> uniqueTextures = new();

        for (int i = 0; i < tileCount; i++)
        {
            @params.Get(i, out Texture2D texture, out TUserData _, out TTileData _);
            if (!textureToAtlasIndex.ContainsKey(texture))
            {
                textureToAtlasIndex[texture] = uniqueTextures.Count;
                uniqueTextures.Add(texture);
            }
        }

        using TextureAtlasPacker packer = renderingSystem.CreateTextureAtlasPacker(material, 32, 32);
        foreach (var texture in uniqueTextures)
        {
            packer.AddTexture(texture.Name, texture);
        }
        _atlas = packer.BuildTextureAtlas(sampler);

        _spriteData = new List<SpriteData>(tileCount);
        for (int i = 0; i < tileCount; i++)
        {
            @params.Get(i, out Texture2D texture, out TUserData userData, out TTileData tileData);
            int atlasIndex = textureToAtlasIndex[texture];
            Sprite sprite = _atlas[atlasIndex];

            _spriteData.Add(new SpriteData
            {
                Sprite = sprite,
                UserData = userData
            });

            tileData.SetUVRect(sprite.UVRect);
            _tileData[i] = tileData;
        }

        _tileData.UpdateBuffer();

    }

    public TUserData GetUserData(int index)
    {
        return _spriteData[index].UserData;
    }

    public TUserData GetUserData(uint index)
    {
        return _spriteData[(int)index].UserData;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tileData.Dispose();
        }
    }
}
