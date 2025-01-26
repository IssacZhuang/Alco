using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public sealed class SurfaceTileSet<TUserData> : BaseTileSet<SurfaceTileData, TUserData>
{
    public SurfaceTileSet(RenderingSystem renderingSystem, SurfaceTileSetParams<TUserData> @params, Material material, GPUSampler sampler, string name) : base(renderingSystem, @params, material, sampler, name)
    {
    }

    public void SetTileColor(int index, Vector4 color)
    {
        SurfaceTileData tileData = _tileData[index];
        tileData.Color = color;
        _tileData[index] = tileData;
        _tileData.UpdateBufferRanged((uint)index, 1);
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

    public void SetTileBlendFactor(int index, float blendFactor)
    {
        SurfaceTileData tileData = _tileData[index];
        tileData.BlendFactor = blendFactor;
        _tileData[index] = tileData;
        _tileData.UpdateBufferRanged((uint)index, 1);
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

    public void SetTileBlendPriority(int index, float blendPriority)
    {
        SurfaceTileData tileData = _tileData[index];
        tileData.BlendPriority = blendPriority;
        _tileData[index] = tileData;
        _tileData.UpdateBufferRanged((uint)index, 1);
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

    public void SetTileEdgeSmoothFactor(int index, float edgeSmoothFactor)
    {
        SurfaceTileData tileData = _tileData[index];
        tileData.EdgeSmoothFactor = edgeSmoothFactor;
        _tileData[index] = tileData;
        _tileData.UpdateBufferRanged((uint)index, 1);
    }
}
