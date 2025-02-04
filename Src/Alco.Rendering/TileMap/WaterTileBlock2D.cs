using System.Numerics;
using System.Runtime.InteropServices;

namespace Alco.Rendering;

public sealed class WaterTileBlock2D<TUserData> : BaseTileBlock2D<WaterTileData, TUserData>
{
    public const string ShaderDefine_LightMap = "USE_LIGHT_MAP";

    //per block
    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        public int2 Size;
    }

    private readonly TileMapHeightBuffer _surfaceHeightData;
    private readonly List<string> _defines = new();
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

    private void UpdateDefines()
    {
        _defines.Clear();
        if (_useLightMap)
        {
            _defines.Add(ShaderDefine_LightMap);
        }
        _material.SetDefines(_defines.ToArray());
    }
}
