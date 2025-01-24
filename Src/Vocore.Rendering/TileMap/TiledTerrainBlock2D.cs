using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vocore.Graphics;

using static Vocore.math;

namespace Vocore.Rendering;

/// <summary>
/// A 2D tiled terrain block. The top left corner is (0, 0).
/// </summary>
/// <typeparam name="TUserData">The type of the user data.</typeparam>
public class TiledTerrainBlock2D<TUserData> : AutoDisposable
{
    //per block
    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        public int2 Size;
    }

    private readonly uint _length;
    private readonly int2 _size;
    private TileSet<TUserData> _tileSet;
    private readonly GraphicsArrayBuffer<ColorFloat> _colorData;
    private readonly GraphicsArrayBuffer<uint> _tileIdData;
    private readonly GraphicsArrayBuffer<float> _heightData;
    private readonly Material _material;
    private readonly Mesh _mesh;
    private bool _isTileIdDirty;
    private bool _isColorDirty;
    private bool _isHeightDirty;

    public Transform3D Transform;
    public int2 Size => _size;
    public TileSet<TUserData> TileSet => _tileSet;


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
        _colorData = renderingSystem.CreateGraphicsArrayBuffer<ColorFloat>(width * height, name + "_color_data");
        _tileIdData = renderingSystem.CreateGraphicsArrayBuffer<uint>(width * height, name + "_sprite_index_data");
        _heightData = renderingSystem.CreateGraphicsArrayBuffer<float>(width * height, 0,name + "_height_data");
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

        _material.SetRenderTexture(ShaderResourceId.Texture, _tileSet.AtlasTexture);
        _material.SetBuffer(ShaderResourceId.ColorData, _colorData);
        _material.SetBuffer(ShaderResourceId.TileIdData, _tileIdData);
        _material.SetBuffer(ShaderResourceId.SpriteData, _tileSet.SpriteDataBuffer);
        _material.SetBuffer(ShaderResourceId.TileSetData, _tileSet.TileSetDataBuffer);
        _material.SetBuffer(ShaderResourceId.HeightData, _heightData);



        Transform = Transform3D.Identity;
        _size = new int2(width, height);
        _length = (uint)(width * height);
    }

    public void Render(MaterialRenderer renderer)
    {
        if (_isTileIdDirty)
        {
            _tileIdData.UpdateBuffer();
            _isTileIdDirty = false;
        }

        if (_isColorDirty)
        {
            _colorData.UpdateBuffer();
            _isColorDirty = false;
        }

        if (_isHeightDirty)
        {
            _heightData.UpdateBuffer();
            _isHeightDirty = false;
        }

        renderer.DrawInstancedWithConstant(_mesh, _material, _length, new Constant { Model = Transform.Matrix, Size = _size });
    }

    public void SetTilesColor(ColorFloat color)
    {
        for (int i = 0; i < _length; i++)
        {
            _colorData[i] = color;
        }
        _isColorDirty = true;
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
        _isColorDirty = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTileColor(int x, int y, ColorFloat color)
    {
        _colorData[GetTileIndex(x, y)] = color;
        _isColorDirty = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTileHeight(int x, int y, float height)
    {
        _heightData[GetTileIndex(x, y)] = height;
        _isHeightDirty = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTileId(int x, int y, uint tileId)
    {
        _tileIdData[GetTileIndex(x, y)] = tileId;
        _isTileIdDirty = true;
    }

    public void SetTilesId(int2 from, int2 to, uint tileId)
    {
        for (int i = from.x; i < to.x; i++)
        {
            for (int j = from.y; j < to.y; j++)
            {
                _tileIdData[GetTileIndex(i, j)] = tileId;
            }
        }
        _isTileIdDirty = true;
    }

    public void SetTilesId(uint tileId)
    {
        for (int i = 0; i < _length; i++)
        {
            _tileIdData[i] = tileId;
        }
        _isTileIdDirty = true;
    }

    public uint GetTileId(int x, int y)
    {
        return _tileIdData[GetTileIndex(x, y)];
    }

    public TUserData GetTileUserData(int x, int y)
    {
        return _tileSet.GetUserData(GetTileId(x, y));
    }

    public float GetTileHeight(int x, int y)
    {
        return _heightData[GetTileIndex(x, y)];
    }

    /// <summary>
    /// Update the tile set and clear the tile id data
    /// </summary>
    /// <param name="tileSet">The new tile set</param>
    public void SetTileSet(TileSet<TUserData> tileSet)
    {
        _tileSet = tileSet;
        _material.SetRenderTexture(ShaderResourceId.Texture, _tileSet.AtlasTexture);
        //clear tile id data
        for (int i = 0; i < _tileIdData.Length; i++)
        {
            _tileIdData[i] = 0;
        }
        _isTileIdDirty = true;
    }

    /// <summary>
    /// Update the tile set without clearing the tile id data.
    /// <br/>[Warning] This it might cause some unexpected behavior if the new tile set has less tiles than the old one.
    /// </summary>
    /// <param name="tileSet">The new tile set</param>
    public void UnsafeSetTileSet(TileSet<TUserData> tileSet)
    {
        _tileSet = tileSet;
        _material.SetRenderTexture(ShaderResourceId.Texture, _tileSet.AtlasTexture);
    }

    public bool TryGetTilePositionByRay(Ray3D ray, out int2 tilePosition)
    {
        Matrix4x4 matrix = Transform.Matrix;
        //to local space
        if (Matrix4x4.Invert(matrix, out Matrix4x4 invMatrix))
        {
            Vector3 start = ray.origin;
            Vector3 end = ray.origin + ray.displacement;

            Vector3 localStart = Vector3.Transform(start, invMatrix);
            Vector3 localEnd = Vector3.Transform(end, invMatrix);

            Plane3D plane = new Plane3D(Vector3.UnitZ, 0);

            Ray3D localRay = new Ray3D(localStart, localEnd - localStart);

            if (plane.IntersectRay(localRay, out Vector3 hitPoint))
            {

                int tileX = (int)floor(hitPoint.X + _size.x * 0.5f);
                int tileY = (int)floor(_size.y * 0.5f - hitPoint.Y);

                if (tileX >= 0 && tileX < _size.x && tileY >= 0 && tileY < _size.y)
                {
                    tilePosition = new int2(tileX, tileY);
                    return true;
                }
            }
        }

        tilePosition = new int2(0, 0);
        return false;
    }

    public Vector2 TilePositionToLocalPosition(int2 tilePosition)
    {
        return new Vector2(tilePosition.x - (_size.x - 1) * 0.5f, -tilePosition.y + (_size.y - 1) * 0.5f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetTileIndex(int x, int y)
    {
        return y * _size.x + x;
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
