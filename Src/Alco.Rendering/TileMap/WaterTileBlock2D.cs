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

    private readonly GraphicsArrayBuffer<float> _dummyHeightData;
    private GraphicsArrayBuffer<float>? _surfaceHeightData;

    public GraphicsArrayBuffer<float> SurfaceHeightData
    {
        get => _surfaceHeightData ?? _dummyHeightData;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _surfaceHeightData = value;
            _material.SetBuffer(ShaderResourceId.HeightData, _surfaceHeightData);
        }
    }

    internal WaterTileBlock2D(
        RenderingSystem renderingSystem,
        BaseTileSet<WaterTileData, TUserData> tileSet,
        Material material,
        int width,
        int height,
        string name = "tiled_terrain_block_2d") :
        base(renderingSystem, tileSet, material, width, height, name)
    {
        _dummyHeightData = renderingSystem.CreateGraphicsArrayBuffer<float>(width * height);
        for (int i = 0; i < width * height; i++)
        {
            _dummyHeightData[i] = 0;
        }
        _dummyHeightData.UpdateBuffer();
        _material.TrySetBuffer(ShaderResourceId.HeightData, _dummyHeightData);
        _material.TrySetBuffer(ShaderResourceId.TimeData, renderingSystem.TimeData);
    }

    public override void OnRender(MaterialRenderer renderer)
    {
        if (_isTileIdDirty)
        {
            _tileIdData.UpdateBuffer();
            _isTileIdDirty = false;
        }

        renderer.DrawInstancedWithConstant(_mesh, _material, _length, new Constant { Model = Transform.Matrix, Size = _size });
    }
}
