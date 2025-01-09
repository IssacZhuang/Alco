using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class TiledTerrainBlock2D<TUserData> : AutoDisposable
{
    //per block
    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        public int2 Size;
    }

    private uint _length;
    private int2 _size;
    private readonly TileSet<TUserData> _tileSet;
    private readonly GraphicsArrayBuffer<ColorFloat> _colorData;
    private readonly GraphicsArrayBuffer<uint> _tileIdData;
    private readonly Material _material;
    private readonly Mesh _mesh;
    private bool _isIndexDirty;

    public Transform3D Transform;


    internal TiledTerrainBlock2D(
        RenderingSystem renderingSystem,
        TileSet<TUserData> tileSet,
        Material material,
        int width,
        int height,
        string name = "tiled_terrain_block_2d"
        )
    {
        _tileSet = tileSet;
        _colorData = new GraphicsArrayBuffer<ColorFloat>(renderingSystem, width * height, name + "_color_data");
        _tileIdData = new GraphicsArrayBuffer<uint>(renderingSystem, width * height, name + "_sprite_index_data");
        _material = material.CreateInstance();
        _mesh = renderingSystem.MeshSprite;

        for (int i = 0; i < _colorData.Length; i++)
        {
            _colorData[i] = ColorFloat.White;
        }

        for (int i = 0; i < _tileIdData.Length; i++)
        {
            _tileIdData[i] = 0;
        }

        _colorData.UpdateBuffer();
        _tileIdData.UpdateBuffer();

        _material.SetRenderTexture(ShaderResourceId.Texture, _tileSet.Atlas);
        _material.SetBuffer(ShaderResourceId.ColorData, _colorData);
        _material.SetBuffer(ShaderResourceId.TileIdData, _tileIdData);
        _material.SetBuffer(ShaderResourceId.SpriteData, _tileSet.SpriteDataBuffer);

        Transform = Transform3D.Identity;
        _size = new int2(width, height);
        _length = (uint)(width * height);
    }

    public void Render(MaterialRenderer renderer)
    {
        if (_isIndexDirty)
        {
            _tileIdData.UpdateBuffer();
            _isIndexDirty = false;
        }
        renderer.DrawInstancedWithConstant(_mesh, _material, _length, new Constant { Model = Transform.Matrix, Size = _size });
    }

    public void SetTilesColor(ColorFloat color)
    {
        for (int i = 0; i < _length; i++)
        {
            _colorData[i] = color;
        }
    }

    public void SetTilesColor(int2 from, int2 to, ColorFloat color)
    {
        for (int i = from.x; i < to.x; i++)
        {
            for (int j = from.y; j < to.y; j++)
            {
                SetTileColor(i, j, color);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTileColor(int x, int y, ColorFloat color)
    {
        _colorData[y * _size.x + x] = color;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTileId(int x, int y, uint spriteIndex)
    {
        _tileIdData[y * _size.x + x] = spriteIndex;
        _isIndexDirty = true;
    }

    public void SetTilesId(int2 from, int2 to, uint spriteIndex)
    {
        for (int i = from.x; i < to.x; i++)
        {
            for (int j = from.y; j < to.y; j++)
            {
                _tileIdData[j * _size.x + i] = spriteIndex;
            }
        }
        _isIndexDirty = true;
    }

    public uint GetTileId(int x, int y)
    {
        return _tileIdData[y * _size.x + x];
    }

    public TUserData GetTileUserData(int x, int y)
    {
        return _tileSet.GetUserData(GetTileId(x, y));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _colorData.Dispose();
            _tileIdData.Dispose();
        }
    }
}
