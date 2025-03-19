using System.Numerics;
using System.Runtime.InteropServices;

namespace Alco.Rendering;

public class PlantTileBlock2D<TUserData> : BaseTileBlock2D<PlantTileData, TUserData>
{
    public const string ShaderDefine_LightMap = "USE_LIGHT_MAP";

    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        public int2 Size;
    }
    private readonly TileMapHeightBuffer _heightData;
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

    internal PlantTileBlock2D(
        RenderingSystem renderingSystem,
        BaseTileSet<PlantTileData, TUserData> tileSet,
        TileMapHeightBuffer heightData,
        Material material,
        int width,
        int height,
        string name = "tiled_terrain_block_2d"
        ) : base(renderingSystem, tileSet, material, width, height, name)
    {
        _heightData = heightData;
        _material.SetBuffer(ShaderResourceId.HeightData, _heightData);
        _material.SetBuffer(ShaderResourceId.TimeData, renderingSystem.TimeData);
    }



    protected override Mesh CreateMesh()
    {
        return _renderingSystem.MeshMidUpSprite;
    }


    public override void OnRender(RenderContext renderer)
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
        if (_useLightMap)
        {
            _defines.Add(ShaderDefine_LightMap);
        }
        _material.SetDefines(_defines.ToArray());
    }
}