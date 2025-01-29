using System.Numerics;
using System.Runtime.InteropServices;

namespace Alco.Rendering;

public sealed class WaterTileBlock2D<TUserData> : BaseTileBlock2D<WaterTileData, TUserData>
{
    //per block
    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        public int2 Size;
    }

    private TileMapHeightBuffer _surfaceHeightData;


    internal WaterTileBlock2D(
        RenderingSystem renderingSystem,
        BaseTileSet<WaterTileData, TUserData> tileSet,
        TileMapHeightBuffer surfaceHeightData,
        Material material,
        int width,
        int height,

        string name = "tiled_terrain_block_2d") :
        base(renderingSystem, tileSet, material, width, height, name)
    {
        _surfaceHeightData = surfaceHeightData;
        _material.TrySetBuffer(ShaderResourceId.HeightData, _surfaceHeightData);
        _material.TrySetBuffer(ShaderResourceId.TimeData, renderingSystem.TimeData);
    }



    public override void OnRender(MaterialRenderer renderer)
    {
        if (_isTileIdDirty)
        {
            _tileIdData.UpdateBuffer();
            _isTileIdDirty = false;
        }

        _surfaceHeightData.TryUpdateBuffer();

        renderer.DrawInstancedWithConstant(_mesh, _material, _length, new Constant { Model = Transform.Matrix, Size = _size });
    }
}
