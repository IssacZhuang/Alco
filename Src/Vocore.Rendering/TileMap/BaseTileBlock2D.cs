using System.Numerics;
using System.Runtime.CompilerServices;

using static Vocore.math;

namespace Vocore.Rendering;

public abstract class BaseTileBlock2D<TUserData> : AutoDisposable
{
    protected readonly uint _length;
    protected readonly int2 _size;
    protected SurfaceTileSet<TUserData> _tileSet;
    protected readonly GraphicsArrayBuffer<uint> _tileIdData;
    
    protected readonly Material _material;
    protected readonly Mesh _mesh;
    protected bool _isTileIdDirty;

    public Transform3D Transform;
    public int2 Size => _size;
    public SurfaceTileSet<TUserData> TileSet => _tileSet;

    protected BaseTileBlock2D(
        RenderingSystem renderingSystem,
        SurfaceTileSet<TUserData> tileSet,
        Material material,
        int width,
        int height,
        string name = "tiled_terrain_block_2d"
        )
    {
        _tileSet = tileSet;

        _tileIdData = renderingSystem.CreateGraphicsArrayBuffer<uint>(width * height, name + "_sprite_index_data");
        _material = material.CreateInstance();
        _mesh = renderingSystem.MeshSprite;


        for (int i = 0; i < _tileIdData.Length; i++)
        {
            _tileIdData[i] = 0;
        }

        _tileIdData.UpdateBuffer();

        _material.SetRenderTexture(ShaderResourceId.Texture, _tileSet.AtlasTexture);
        _material.TrySetBuffer(ShaderResourceId.TileIdData, _tileIdData);
        _material.TrySetBuffer(ShaderResourceId.TileData, _tileSet.TileDataBuffer);

        Transform = Transform3D.Identity;
        _size = new int2(width, height);
        _length = (uint)(width * height);
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

    

    /// <summary>
    /// Update the tile set and clear the tile id data
    /// </summary>
    /// <param name="tileSet">The new tile set</param>
    public void SetTileSet(SurfaceTileSet<TUserData> tileSet)
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
    public void UnsafeSetTileSet(SurfaceTileSet<TUserData> tileSet)
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
            _tileIdData.Dispose();
        }
    }
}