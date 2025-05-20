using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

public abstract class BaseTileSet<TTileData> : AutoDisposable where TTileData : unmanaged, ITileData
{
    protected readonly RenderingSystem _renderingSystem;
    protected readonly object?[] _userDataList;
    protected readonly Dictionary<uint, TileSpriteData[]> _spriteData;
    protected readonly GraphicsArrayBuffer<TTileData> _tileData;
    protected readonly uint[] _tileIdToItemId;

    protected readonly TextureAtlas _atlas;

    public int ItemCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _userDataList.Length;
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
        IReadOnlyList<BaseTileItem<TTileData>> items,
        Material material,
        GPUSampler sampler,
        string name)
    {
        ArgumentNullException.ThrowIfNull(material);

        _renderingSystem = renderingSystem;
        int itemCount = items.Count;



        Dictionary<Texture2D, int> textureToAtlasIndex = new();
        List<Texture2D> uniqueTextures = new();

        int tileCount = 0;
        for (int i = 0; i < itemCount; i++)
        {
            var item = items[i];
            int textureCount = item.Textures.Count;
            tileCount += textureCount;

            for (int j = 0; j < textureCount; j++)
            {

                Texture2D texture = item.Textures[j];
                if (!textureToAtlasIndex.ContainsKey(texture))
                {
                    textureToAtlasIndex[texture] = uniqueTextures.Count;
                    uniqueTextures.Add(texture);
                }
            }
        }

        _tileData = renderingSystem.CreateGraphicsArrayBuffer<TTileData>(tileCount, name + "_sprite_data");

        using TextureAtlasPacker packer = renderingSystem.CreateTextureAtlasPacker(material, 32, 32);
        foreach (var texture in uniqueTextures)
        {
            packer.AddTexture(texture.Name, texture);
        }
        _atlas = packer.BuildTextureAtlas(sampler);

        _spriteData = new Dictionary<uint, TileSpriteData[]>();
        _userDataList = new object?[itemCount];
        _tileIdToItemId = new uint[tileCount];

        int currentTileIndex = 0;
        for (uint i = 0; i < itemCount; i++)
        {
            var item = items[(int)i];
            int textureCount = item.Textures.Count;
            var itemSprites = new TileSpriteData[textureCount];

            _userDataList[i] = item.UserData;
            TTileData tileData = item.TileData;

            for (int j = 0; j < textureCount; j++)
            {
                Texture2D texture = item.Textures[j];
                int atlasIndex = textureToAtlasIndex[texture];
                Sprite sprite = _atlas[atlasIndex];

                itemSprites[j] = new TileSpriteData
                {
                    Sprite = sprite,
                    TileId = (uint)currentTileIndex
                };

                tileData.SetUVRect(sprite.UvRect);
                _tileIdToItemId[currentTileIndex] = i;
                _tileData[currentTileIndex++] = tileData;
            }

            _spriteData[i] = itemSprites;
        }

        _tileData.UpdateBuffer();

    }

    public uint GetItemId(uint tileId)
    {
        return _tileIdToItemId[tileId];
    }

    public object? GetUserData(uint itemId)
    {
        return _userDataList[itemId];
    }

    public TileSpriteData[] GetSprites(uint itemId)
    {
        return _spriteData[itemId];
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tileData.Dispose();
        }
    }
}
