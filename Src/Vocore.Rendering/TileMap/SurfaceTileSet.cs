using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class SurfaceTileSet<TUserData> : AutoDisposable
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
        /// <summary>
        /// The tile of lower priority blend the texture from the tile of higher priority.
        /// </summary>
        public float BlendPriority;
        public Vector3 _reserved;//for memory alignment
    }

    //per tile set in GPU
    public struct TileSetData
    {
        /// <summary>
        /// The x,y offset affected by the height.
        /// </summary>
        public Vector2 HeightOffsetFactor;
        /// <summary>
        /// the blend factor of the tile.
        /// </summary>
        public float BlendFactor;
        /// <summary>
        /// The factor of the edge smoothing when the tile height is different from the neighbor tile.
        /// </summary>
        public float EdgeSmoothFactor;

    }


    private readonly RenderingSystem _renderingSystem;
    private readonly List<TileData> _tileData;
    private readonly GraphicsArrayBuffer<SpriteData> _spriteData;
    private readonly GraphicsValueBuffer<TileSetData> _tileSetData;
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

    public GraphicsValueBuffer<TileSetData> TileSetDataBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tileSetData;
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

    public Vector2 HeightOffsetFactor
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tileSetData.Value.HeightOffsetFactor;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _tileSetData.Value.HeightOffsetFactor = value;
            _tileSetData.UpdateBuffer();
        }
    }

    public float BlendFactor
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tileSetData.Value.BlendFactor;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _tileSetData.Value.BlendFactor = value;
            _tileSetData.UpdateBuffer();
        }
    }

    public float EdgeSmoothFactor
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tileSetData.Value.EdgeSmoothFactor;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _tileSetData.Value.EdgeSmoothFactor = value;
            _tileSetData.UpdateBuffer();
        }
    }


    internal SurfaceTileSet(
        RenderingSystem renderingSystem,
        SurfaceTileSetParams<TUserData> @params,
        Material material,
        GPUSampler sampler,
        string name)
    {
        ArgumentNullException.ThrowIfNull(material);

        _renderingSystem = renderingSystem;
        int tileCount = @params.Items.Count;
        _spriteData = renderingSystem.CreateGraphicsArrayBuffer<SpriteData>(tileCount, name + "_sprite_data");

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
                UVScale = item.UVScale,
                BlendPriority = item.BlendPriority
            };
        }

        _spriteData.UpdateBuffer();

        _tileSetData = renderingSystem.CreateGraphicsValueBuffer<TileSetData>(name + "_tile_set_data");
        _tileSetData.Value = new TileSetData
        {
            HeightOffsetFactor = @params.HeightOffsetFactor,
            BlendFactor = @params.BlendFactor,
            EdgeSmoothFactor = @params.EdgeSmoothFactor
        };
        _tileSetData.UpdateBuffer();
    }

    public TUserData GetUserData(int index)
    {
        return _tileData[index].UserData;
    }

    public TUserData GetUserData(uint index)
    {
        return _tileData[(int)index].UserData;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _spriteData.Dispose();
            _tileSetData.Dispose();
        }
    }
}
