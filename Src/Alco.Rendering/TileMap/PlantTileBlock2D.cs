using System.Numerics;
using System.Runtime.InteropServices;

namespace Alco.Rendering;

public class PlantTileBlock2D<TUserData> : BaseTileBlock2D<PlantTileData, TUserData>
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        public int2 Size;
    }
    private readonly GraphicsArrayBuffer<float> _heightData;

    internal PlantTileBlock2D(
        RenderingSystem renderingSystem,
        BaseTileSet<PlantTileData, TUserData> tileSet,
        Material material,
        int width,
        int height,
        string name = "tiled_terrain_block_2d"
        ) : base(renderingSystem, tileSet, material, width, height, name)
    {
        _heightData = renderingSystem.CreateGraphicsArrayBuffer<float>(width * height, "height_data");
        _material.SetBuffer(ShaderResourceId.HeightData, _heightData);
        _material.SetBuffer(ShaderResourceId.TimeData, renderingSystem.TimeData);
    }


    protected override Mesh CreateMesh()
    {
        return _renderingSystem.MeshMidUpSprite;
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