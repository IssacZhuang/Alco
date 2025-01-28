using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

public sealed class SurfaceTileSet<TUserData> : BaseTileSet<SurfaceTileData, TUserData>
{
    public SurfaceTileSet(
        RenderingSystem renderingSystem,
        IReadOnlyList<BaseTileItem<SurfaceTileData, TUserData>> items, 
        Material material, 
        GPUSampler sampler, 
        string name
        ) : base(renderingSystem, @items, material, sampler, name)
    {

    }

    public void SetTileColor(uint itemId, Vector4 color)
    {
        TileSpriteData[] sprites = GetSprites(itemId);
        foreach (var sprite in sprites)
        {
            SurfaceTileData tileData = _tileData[(int)sprite.TileId];
            tileData.Color = color;
            _tileData[(int)sprite.TileId] = tileData;
        }
        _tileData.UpdateBuffer();
    }

    public void SetAllTileColor(Vector4 color)
    {
        for (int i = 0; i < _tileData.Length; i++)
        {
            SurfaceTileData tileData = _tileData[i];
            tileData.Color = color;
            _tileData[i] = tileData;
        }
        _tileData.UpdateBuffer();
    }

    public void SetTileBlendFactor(uint itemId, float blendFactor)
    {
        TileSpriteData[] sprites = GetSprites(itemId);
        foreach (var sprite in sprites)
        {
            SurfaceTileData tileData = _tileData[(int)sprite.TileId];
            tileData.BlendFactor = blendFactor;
            _tileData[(int)sprite.TileId] = tileData;
        }
        _tileData.UpdateBuffer();
    }

    public void SetAllTileBlendFactor(float blendFactor)
    {
        for (int i = 0; i < _tileData.Length; i++)
        {
            SurfaceTileData tileData = _tileData[i];
            tileData.BlendFactor = blendFactor;
            _tileData[i] = tileData;
        }
        _tileData.UpdateBuffer();
    }

    public void SetTileBlendPriority(uint itemId, float blendPriority)
    {
        TileSpriteData[] sprites = GetSprites(itemId);
        foreach (var sprite in sprites)
        {
            SurfaceTileData tileData = _tileData[(int)sprite.TileId];
            tileData.BlendPriority = blendPriority;
            _tileData[(int)sprite.TileId] = tileData;
        }
        _tileData.UpdateBuffer();
    }

    public void SetAllTileBlendPriority(float blendPriority)
    {
        for (int i = 0; i < _tileData.Length; i++)
        {
            SurfaceTileData tileData = _tileData[i];
            tileData.BlendPriority = blendPriority;
            _tileData[i] = tileData;
        }
        _tileData.UpdateBuffer();
    }

    public void SetTileEdgeSmoothFactor(uint itemId, float edgeSmoothFactor)
    {
        TileSpriteData[] sprites = GetSprites(itemId);
        foreach (var sprite in sprites)
        {
            SurfaceTileData tileData = _tileData[(int)sprite.TileId];
            tileData.EdgeSmoothFactor = edgeSmoothFactor;
            _tileData[(int)sprite.TileId] = tileData;
        }
        _tileData.UpdateBuffer();
    }
}
