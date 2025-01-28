using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using static Alco.math;


namespace Alco.Rendering;

public abstract class BaseTileBlock2D<TTileData, TUserData> : AutoDisposable where TTileData : unmanaged, ITileData
{
    protected readonly uint _length;
    protected readonly int2 _size;
    protected readonly System.Random _random = new System.Random(123);
    
    protected readonly GraphicsArrayBuffer<uint> _tileIdData;
    
    protected readonly Material _material;
    protected readonly Mesh _mesh;
    protected bool _isTileIdDirty;

    protected BaseTileSet<TTileData, TUserData> _tileSet;

    public Transform3D Transform;
    public int2 Size => _size;

    public BaseTileSet<TTileData, TUserData> TileSet => _tileSet;



    protected BaseTileBlock2D(
        RenderingSystem renderingSystem,
        BaseTileSet<TTileData, TUserData> tileSet,
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

        Transform = Transform3D.Identity;
        _size = new int2(width, height);
        _length = (uint)(width * height);

        _material.TrySetBuffer(ShaderResourceId.TileIdData, _tileIdData);
        _material.TrySetBuffer(ShaderResourceId.TileData, _tileSet.TileDataBuffer);
        _material.SetRenderTexture(ShaderResourceId.Texture, _tileSet.AtlasTexture);
    }

    public abstract void OnRender(MaterialRenderer renderer);

    public bool TryGetItemId(int x, int y, out int itemId)
    {
        if (x < 0 || y < 0 || x >= _size.x || y >= _size.y)
        {
            itemId = 0;
            return false;
        }
        itemId = _tileSet.GetItemId(_tileIdData[GetTileIndex(x, y)]);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetItemId(int x, int y, int itemId)
    {
        if (x < 0 || y < 0 || x >= _size.x || y >= _size.y)
        {
            return false;
        }
        //random a tile id
        uint tileId = RandomTileId(itemId);

        _tileIdData[GetTileIndex(x, y)] = tileId;
        _isTileIdDirty = true;
        return true;
    }

    public void SetItemIds(int2 from, int2 to, int itemId)
    {
        //clamp
        from = clamp(from, new int2(0, 0), _size - new int2(1, 1));
        to = clamp(to, new int2(0, 0), _size - new int2(1, 1));

        for (int i = from.x; i < to.x; i++)
        {
            for (int j = from.y; j < to.y; j++)
            {
                _tileIdData[GetTileIndex(i, j)] = RandomTileId(itemId);
            }
        }
        _isTileIdDirty = true;
    }

    public void SetAllItemIds(int itemId)
    {
        for (int i = 0; i < _length; i++)
        {
            _tileIdData[i] = RandomTileId(itemId);
        }
        _isTileIdDirty = true;
    }

    //to fix
    public bool TryGetTileUserData(int x, int y, out TUserData userData)
    {
        if (!TryGetItemId(x, y, out int itemId))
        {
            userData = default!;
            return false;
        }
        if (itemId < 0)
        {
            userData = default!;
            return false;
        }

        userData = _tileSet.GetUserData(itemId);
        return true;
    }



    /// <summary>
    /// Update the tile set and clear the tile id data
    /// </summary>
    /// <param name="tileSet">The new tile set</param>
    public void SetTileSet(BaseTileSet<TTileData, TUserData> tileSet)
    {
        ArgumentNullException.ThrowIfNull(tileSet);
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
    public void UnsafeSetTileSet(BaseTileSet<TTileData, TUserData> tileSet)
    {
        ArgumentNullException.ThrowIfNull(tileSet);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint RandomTileId(int itemId)
    {
        var sprites = _tileSet.GetSprites(itemId);
        if (sprites.Length <= 1)
        {
            return sprites[0].TileId;
        }
        return sprites[_random.Next(sprites.Length)].TileId;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tileIdData.Dispose();
        }
    }
}