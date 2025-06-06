using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using static Alco.math;


namespace Alco.Rendering;

/// <summary>
/// Base class for 2D tile blocks that manage a grid of tiles with rendering capabilities.
/// Uses a pixel space coordinate system where the origin (0,0) is at the top-left corner,
/// the X-axis points to the right, and the Y-axis points downward.
/// </summary>
/// <typeparam name="TTileData">The type of tile data that must be unmanaged and implement ITileData</typeparam>
public abstract class BaseTileBlock2D<TTileData> : AutoDisposable where TTileData : unmanaged, ITileData
{
    protected readonly RenderingSystem _renderingSystem;

    protected readonly uint _length;
    protected readonly int2 _size;
    protected readonly System.Random _random = new System.Random(123);
    
    protected readonly GraphicsArrayBuffer<uint> _tileIdData;
    
    protected readonly Material _material;
    protected readonly Mesh _mesh;
    protected bool _isTileIdDirty;

    protected BaseTileSet<TTileData> _tileSet;

    /// <summary>
    /// Gets or sets the 3D transformation matrix for this tile block
    /// </summary>
    public Transform3D Transform;

    /// <summary>
    /// Gets the size of the tile block in tiles (width x height)
    /// </summary>
    public int2 Size => _size;

    /// <summary>
    /// Gets the tile set used by this tile block
    /// </summary>
    public BaseTileSet<TTileData> TileSet => _tileSet;



    protected BaseTileBlock2D(
        RenderingSystem renderingSystem,
        BaseTileSet<TTileData> tileSet,
        Material material,
        int width,
        int height,
        string name = "tiled_terrain_block_2d"
        )
    {
        _tileSet = tileSet;
        _tileIdData = renderingSystem.CreateGraphicsArrayBuffer<uint>(width * height, name + "_sprite_index_data");
        _material = material.CreateInstance();
        _renderingSystem = renderingSystem;
        _mesh = CreateMesh();



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

    protected virtual Mesh CreateMesh()
    {
        return _renderingSystem.MeshCenteredSprite;
    }


    /// <summary>
    /// Renders the tile block using the provided render context
    /// </summary>
    /// <param name="renderer">The render context to use for rendering</param>
    public abstract void OnRender(IRenderContext renderer);

    /// <summary>
    /// Attempts to get the item ID at the specified tile coordinates.
    /// Coordinates use pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </summary>
    /// <param name="x">The X coordinate of the tile (0 to Size.X-1)</param>
    /// <param name="y">The Y coordinate of the tile (0 to Size.Y-1)</param>
    /// <param name="itemId">When this method returns, contains the item ID if the coordinates are valid</param>
    /// <returns>True if the coordinates are valid and the item ID was retrieved; otherwise, false</returns>
    public bool TryGetItemId(int x, int y, out uint itemId)
    {
        if (x < 0 || y < 0 || x >= _size.X || y >= _size.Y)
        {
            itemId = 0;
            return false;
        }
        itemId = _tileSet.GetItemId(_tileIdData[GetTileIndex(x, y)]);
        return true;
    }

    /// <summary>
    /// Attempts to get the item ID at the specified tile coordinates (as int2).
    /// Coordinates use pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </summary>
    /// <param name="position">The tile coordinates (X, Y)</param>
    /// <param name="itemId">When this method returns, contains the item ID if the coordinates are valid</param>
    /// <returns>True if the coordinates are valid and the item ID was retrieved; otherwise, false</returns>
    public bool TryGetItemId(int2 position, out uint itemId)
    {
        return TryGetItemId(position.X, position.Y, out itemId);
    }

    /// <summary>
    /// Attempts to set the item ID at the specified tile coordinates.
    /// Coordinates use pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </summary>
    /// <param name="x">The X coordinate of the tile (0 to Size.X-1)</param>
    /// <param name="y">The Y coordinate of the tile (0 to Size.Y-1)</param>
    /// <param name="itemId">The item ID to set at the specified coordinates</param>
    /// <returns>True if the coordinates are valid and the item ID was set; otherwise, false</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetItemId(int x, int y, uint itemId)
    {
        if (x < 0 || y < 0 || x >= _size.X || y >= _size.Y)
        {
            return false;
        }
        //random a tile id
        uint tileId = RandomTileId(itemId);

        _tileIdData[GetTileIndex(x, y)] = tileId;
        _isTileIdDirty = true;
        return true;
    }

    /// <summary>
    /// Attempts to set the item ID at the specified tile coordinates (as int2).
    /// Coordinates use pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </summary>
    /// <param name="position">The tile coordinates (X, Y)</param>
    /// <param name="itemId">The item ID to set at the specified coordinates</param>
    /// <returns>True if the coordinates are valid and the item ID was set; otherwise, false</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetItemId(int2 position, uint itemId)
    {
        return TrySetItemId(position.X, position.Y, itemId);
    }

    /// <summary>
    /// Sets the item ID for a rectangular region of tiles.
    /// Coordinates use pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </summary>
    /// <param name="from">The top-left corner of the region (inclusive)</param>
    /// <param name="to">The bottom-right corner of the region (exclusive)</param>
    /// <param name="itemId">The item ID to set for all tiles in the region</param>
    public void SetItemIds(int2 from, int2 to, uint itemId)
    {
        //clamp
        from = clamp(from, new int2(0, 0), _size - new int2(1, 1));
        to = clamp(to, new int2(0, 0), _size - new int2(1, 1));

        for (int i = from.X; i < to.X; i++)
        {
            for (int j = from.Y; j < to.Y; j++)
            {
                _tileIdData[GetTileIndex(i, j)] = RandomTileId(itemId);
            }
        }
        _isTileIdDirty = true;
    }

    /// <summary>
    /// Sets the same item ID for all tiles in the block
    /// </summary>
    /// <param name="itemId">The item ID to set for all tiles</param>
    public void SetAllItemIds(uint itemId)
    {
        for (int i = 0; i < _length; i++)
        {
            _tileIdData[i] = RandomTileId(itemId);
        }
        _isTileIdDirty = true;
    }

    /// <summary>
    /// Attempts to get the user data associated with the tile at the specified coordinates.
    /// Coordinates use pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </summary>
    /// <param name="x">The X coordinate of the tile (0 to Size.X-1)</param>
    /// <param name="y">The Y coordinate of the tile (0 to Size.Y-1)</param>
    /// <param name="userData">When this method returns, contains the user data if available</param>
    /// <returns>True if the coordinates are valid and user data was retrieved; otherwise, false</returns>
    public bool TryGetTileUserData(int x, int y, out object? userData)
    {
        if (!TryGetItemId(x, y, out uint itemId))
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
    /// Attempts to get the user data associated with the tile at the specified coordinates (as int2).
    /// Coordinates use pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </summary>
    /// <param name="position">The tile coordinates (X, Y)</param>
    /// <param name="userData">When this method returns, contains the user data if available</param>
    /// <returns>True if the coordinates are valid and user data was retrieved; otherwise, false</returns>
    public bool TryGetTileUserData(int2 position, out object? userData)
    {
        return TryGetTileUserData(position.X, position.Y, out userData);
    }



    /// <summary>
    /// Updates the tile set and clears all tile ID data to prevent inconsistencies
    /// </summary>
    /// <param name="tileSet">The new tile set to use</param>
    /// <exception cref="ArgumentNullException">Thrown when tileSet is null</exception>
    public void SetTileSet(BaseTileSet<TTileData> tileSet)
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
    /// Updates the tile set without clearing the tile ID data.
    /// Warning: This might cause unexpected behavior if the new tile set has fewer tiles than the old one.
    /// </summary>
    /// <param name="tileSet">The new tile set to use</param>
    /// <exception cref="ArgumentNullException">Thrown when tileSet is null</exception>
    public void UnsafeSetTileSet(BaseTileSet<TTileData> tileSet)
    {
        ArgumentNullException.ThrowIfNull(tileSet);
        _tileSet = tileSet;
        _material.SetRenderTexture(ShaderResourceId.Texture, _tileSet.AtlasTexture);
    }

    /// <summary>
    /// Attempts to determine which tile position a 3D ray intersects with.
    /// Returns tile coordinates in pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </summary>
    /// <param name="ray">The 3D ray to test intersection with</param>
    /// <param name="tilePosition">When this method returns, contains the tile position if intersection occurred</param>
    /// <returns>True if the ray intersects with a valid tile position; otherwise, false</returns>
    public bool TryGetTilePositionByRay(Ray3D ray, out int2 tilePosition)
    {
        Matrix4x4 matrix = Transform.Matrix;
        //to local space
        if (Matrix4x4.Invert(matrix, out Matrix4x4 invMatrix))
        {
            Vector3 start = ray.Origin;
            Vector3 end = ray.Origin + ray.Displacement;

            Vector3 localStart = Vector3.Transform(start, invMatrix);
            Vector3 localEnd = Vector3.Transform(end, invMatrix);

            Plane3D plane = new Plane3D(Vector3.UnitZ, 0);

            Ray3D localRay = new Ray3D(localStart, localEnd - localStart);

            if (plane.IntersectRay(localRay, out Vector3 hitPoint))
            {

                int tileX = (int)floor(hitPoint.X + _size.X * 0.5f);
                int tileY = (int)floor(_size.Y * 0.5f - hitPoint.Y);

                if (tileX >= 0 && tileX < _size.X && tileY >= 0 && tileY < _size.Y)
                {
                    tilePosition = new int2(tileX, tileY);
                    return true;
                }
            }
        }

        tilePosition = new int2(0, 0);
        return false;
    }

    /// <summary>
    /// Converts 2D tile coordinates to a linear tile index.
    /// Coordinates use pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </summary>
    /// <param name="x">The X coordinate of the tile</param>
    /// <param name="y">The Y coordinate of the tile</param>
    /// <returns>The linear index corresponding to the tile coordinates</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetTileIndex(int x, int y)
    {
        return y * _size.X + x;
    }

    /// <summary>
    /// Converts 2D tile coordinates (as int2) to a linear tile index.
    /// Coordinates use pixel space: origin (0,0) at top-left, X points right, Y points down.
    /// </summary>
    /// <param name="position">The tile coordinates (X, Y)</param>
    /// <returns>The linear index corresponding to the tile coordinates</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetTileIndex(int2 position)
    {
        return GetTileIndex(position.X, position.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint RandomTileId(uint itemId)
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