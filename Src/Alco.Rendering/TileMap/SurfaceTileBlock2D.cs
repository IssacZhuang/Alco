using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;

using static Alco.math;

namespace Alco.Rendering;

/// <summary>
/// A fixed size 2D tiled terrain surface block. The top left corner is (0, 0).
/// </summary>
/// <typeparam name="TUserData">The type of the user data.</typeparam>
public sealed class SurfaceTileBlock2D<TUserData> : BaseTileBlock2D<SurfaceTileData, TUserData>
{
    public string ShaderDefine_Cliff = "IS_CLIFF";
    
    //per block
    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        public int2 Size;
    }

    private readonly GraphicsArrayBuffer<float> _heightData;
    private bool _isHeightDirty;
    
    private bool _isCliff;

    public bool IsCliff
    {
        get => _isCliff;
        set
        {
            _isCliff = value;
            if (_isCliff)
            {
                _material.SetDefines(ShaderDefine_Cliff);
            }
            else
            {
                _material.SetDefines([]);
            }
        }
    }

    public GraphicsArrayBuffer<float> HeightData
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _heightData;
    }

    internal SurfaceTileBlock2D(
        RenderingSystem renderingSystem,
        SurfaceTileSet<TUserData> tileSet,
        Material material,
        int width,
        int height,
        string name = "tiled_terrain_block_2d"
        ):base(renderingSystem, tileSet, material, width, height, name)
    {

        _heightData = renderingSystem.CreateGraphicsArrayBuffer<float>(width * height, 0, name + "_height_data");
        _material.TrySetBuffer(ShaderResourceId.HeightData, _heightData);

    }

    public override void OnRender(MaterialRenderer renderer)
    {
        if (_isTileIdDirty)
        {
            _tileIdData.UpdateBuffer();
            _isTileIdDirty = false;
        }

        if (_isHeightDirty)
        {
            _heightData.UpdateBuffer();
            _isHeightDirty = false;
        }

        renderer.DrawInstancedWithConstant(_mesh, _material, _length, new Constant { Model = Transform.Matrix, Size = _size });
    }

    public bool TryGetTileHeight(int x, int y, out float height)
    {
        if (x < 0 || y < 0 || x >= _size.x || y >= _size.y)
        {
            height = 0;
            return false;
        }
        height = _heightData[GetTileIndex(x, y)];
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetTileHeight(int x, int y, float height)
    {
        if (x < 0 || y < 0 || x >= _size.x || y >= _size.y)
        {
            return false;
        }
        _heightData[GetTileIndex(x, y)] = height;
        _isHeightDirty = true;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _heightData.Dispose();
        }
    }
}
