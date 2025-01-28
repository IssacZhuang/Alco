using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

public sealed class WaterTileSet<TUserData> : BaseTileSet<WaterTileData, TUserData>
{
    internal WaterTileSet(
        RenderingSystem renderingSystem, 
        IReadOnlyList<BaseTileItem<WaterTileData, TUserData>> items, 
        Material material, 
        GPUSampler sampler, 
        string name
    ) : base(renderingSystem, items, material, sampler, name)
    {
    }

    public void SetTileColor(int index, Vector4 color)
    {
        WaterTileData tileData = _tileData[index];
        tileData.Color = color;
        _tileData[index] = tileData;
        _tileData.UpdateBufferRanged((uint)index, 1);
    }

    public void SetAllTileColor(Vector4 color)
    {
        for (int i = 0; i < _tileData.Length; i++)
        {
            WaterTileData tileData = _tileData[i];
            tileData.Color = color;
            _tileData[i] = tileData;
        }
        _tileData.UpdateBuffer();
    }

    public void SetTileBlendFactor(int index, float blendFactor)
    {
        WaterTileData tileData = _tileData[index];
        tileData.BlendFactor = blendFactor;
        _tileData[index] = tileData;
        _tileData.UpdateBufferRanged((uint)index, 1);
    }

    public void SetAllTileBlendFactor(float blendFactor)
    {
        for (int i = 0; i < _tileData.Length; i++)
        {
            WaterTileData tileData = _tileData[i];
            tileData.BlendFactor = blendFactor;
            _tileData[i] = tileData;
        }
        _tileData.UpdateBuffer();
    }
}