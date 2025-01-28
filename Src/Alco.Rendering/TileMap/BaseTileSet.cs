using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

public abstract class BaseTileSet<TTileData, TUserData> : AutoDisposable where TTileData : unmanaged, ITileData
{
    protected readonly RenderingSystem _renderingSystem;
    protected readonly TUserData[] _userDataList;
    protected readonly Dictionary<int, TileSpriteData[]> _spriteData;
    protected readonly GraphicsArrayBuffer<TTileData> _tileData;
    protected readonly int[] _tileIdToItemId;

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
        IReadOnlyList<BaseTileItem<TTileData, TUserData>> items,
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
                var textureItem = item.Textures[j];
                Texture2D texture = textureItem.Texture;
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

        _spriteData = new Dictionary<int, TileSpriteData[]>();
        _userDataList = new TUserData[itemCount];
        _tileIdToItemId = new int[tileCount];

        int currentTileIndex = 0;
        for (int i = 0; i < itemCount; i++)
        {
            var item = items[i];
            int textureCount = item.Textures.Count;
            var itemSprites = new TileSpriteData[textureCount];

            TUserData userData = item.UserData;
            TTileData tileData = item.TileData;

            for (int j = 0; j < textureCount; j++)
            {
                var textureItem = item.Textures[j];
                Texture2D texture = textureItem.Texture;
                int atlasIndex = textureToAtlasIndex[texture];
                Sprite sprite = _atlas[atlasIndex];

                itemSprites[j] = new TileSpriteData
                {
                    Sprite = sprite,
                    TileId = (uint)currentTileIndex
                };

                tileData.SetUVRect(sprite.UVRect);
                _tileIdToItemId[currentTileIndex] = i;
                _tileData[currentTileIndex++] = tileData;
            }

            _spriteData[i] = itemSprites;
        }

        _tileData.UpdateBuffer();

    }

    public int GetItemId(uint tileId)
    {
        return _tileIdToItemId[tileId];
    }

    public TUserData GetUserData(int itemId)
    {
        return _userDataList[itemId];
    }

    public TileSpriteData[] GetSprites(int itemId)
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
