using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vocore.Graphics;

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
        public Vector2 MeshScale;
        public Vector2 UVScale;
    }


    private readonly RenderingSystem _renderingSystem;
    private readonly List<TileData> _tileData;
    private readonly GraphicsArrayBuffer<SpriteData> _spriteData;
    private readonly TextureAtlas _atlas;

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tileData.Count;
    }

    public GraphicsArrayBuffer<SpriteData> SpriteDataBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _spriteData;
    }

    public RenderTexture Atlas
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _atlas.RenderTexture;
    }

    internal TileSet(
        RenderingSystem renderingSystem, 
        TileSetParams<TUserData> @params, 
        Material material, 
        GPUSampler sampler,
        string name)
    {
        ArgumentNullException.ThrowIfNull(material);

        _renderingSystem = renderingSystem;
        int tileCount = @params.Items.Count;
        _spriteData = new GraphicsArrayBuffer<SpriteData>(renderingSystem, tileCount, name + "_sprite_data");

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

        using TextureAtlasPacker packer = renderingSystem.CreateTextureAtlasPacker(material, 32, 32);
        foreach (var texture in uniqueTextures)
        {
            packer.AddTexture(texture.Name, texture);
        }
        _atlas = packer.BuildTextureAtlas(sampler);

        _tileData = new List<TileData>(tileCount);
        for (int i = 0; i < tileCount; i++)
        {
            var item = @params.Items[i];
            int atlasIndex = textureToAtlasIndex[item.Texture];
            Sprite sprite = _atlas[atlasIndex];

            _tileData.Add(new TileData
            {
                Sprite = sprite,
                UserData = item.UserData
            });

            _spriteData[i] = new SpriteData
            {
                UVRect = sprite.UVRect,
                MeshScale = item.MeshScale,
                UVScale = item.UVScale
            };
        }

        _spriteData.UpdateBuffer();
    }

    public TUserData GetUserData(int index)
    {
        return _tileData[index].UserData;
    }

    public TUserData GetUserData(uint index)
    {
        return _tileData[(int)index].UserData;
    }

    public void SetMeshScale(int index, Vector2 scale)
    {
        SpriteData spriteData = _spriteData[index];
        spriteData.MeshScale = scale;
        _spriteData[index] = spriteData;
        _spriteData.UpdateBufferRanged((uint)index, 1);
    }

    public void SetUVScale(int index, Vector2 scale)
    {
        SpriteData spriteData = _spriteData[index];
        spriteData.UVScale = scale;
        _spriteData[index] = spriteData;
        _spriteData.UpdateBufferRanged((uint)index, 1);
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _spriteData.Dispose();
        }
    }
}
