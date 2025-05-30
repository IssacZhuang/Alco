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
public sealed class SurfaceTileBlock2D : BaseTileBlock2D<SurfaceTileData>
{
    public const string ShaderDefine_IS_Cliff = "IS_CLIFF";
    public const string ShaderDefine_USE_LIGHT_MAP = "USE_LIGHT_MAP";


    //per block
    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        public int2 Size;
    }

    private readonly TileMapHeightBuffer _heightData;

    private readonly List<string> _defines = new();
    private bool _isCliff;
    private bool _useLightMap;

    public RenderTexture LightMap
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _material.SetRenderTexture(ShaderResourceId.LightMap, value);
        }
    }



    public bool UseLightMap
    {
        get => _useLightMap;
        set
        {
            _useLightMap = value;
            UpdateDefines();
        }
    }

    public bool IsCliff
    {
        get => _isCliff;
        set
        {
            _isCliff = value;
            UpdateDefines();
        }
    }

    public GraphicsArrayBuffer<float> HeightData
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _heightData;
    }

    internal SurfaceTileBlock2D(
        RenderingSystem renderingSystem,
        SurfaceTileSet tileSet,
        TileMapHeightBuffer heightData,
        Material material,
        int width,
        int height,
        string name = "tiled_terrain_block_2d"

        ):base(renderingSystem, tileSet, material, width, height, name)
    {

        _heightData = heightData;
        _material.TrySetBuffer(ShaderResourceId.HeightData, _heightData);

    }

    public override void OnRender(IRenderContext renderer)
    {
        if (_isTileIdDirty)
        {
            _tileIdData.UpdateBuffer();
            _isTileIdDirty = false;
        }

        _heightData.TryUpdateBuffer();

        renderer.DrawInstancedWithConstant(_mesh, _material, _length, new Constant { Model = Transform.Matrix, Size = _size });

    }

    private void UpdateDefines()
    {
        _defines.Clear();
        if (_isCliff)
        {
            _defines.Add(ShaderDefine_IS_Cliff);
        }
        if (_useLightMap)
        {
            _defines.Add(ShaderDefine_USE_LIGHT_MAP);
        }
        _material.SetDefines(_defines.ToArray());
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
