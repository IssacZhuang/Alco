namespace Alco.Rendering;


using System;
using System.Runtime.CompilerServices;

public sealed class TileMapHeightBuffer : GraphicsArrayBuffer<float>
{
    private int2 _size;
    private bool _isDirty = false;

    internal TileMapHeightBuffer(
        RenderingSystem renderingSystem,
        int width,
        int height,
        string name = "unnamed_graphics_array_buffer")
        : base(renderingSystem, width * height, name)

    {
        _size = new int2(width, height);
        for (int i = 0; i < width * height; i++)
        {
            this[i] = 0;
        }
        UpdateBuffer();
    }



    /// <summary>
    /// Update the buffer from CPU to GPU if it is dirty.
    /// </summary>
    /// <returns>True if the buffer is updated, false otherwise.</returns>
    public bool TryUpdateBuffer()

    {
        if (!_isDirty)
        {
            return false;
        }
        UpdateBuffer();
        _isDirty = false;
        return true;
    }

    /// <summary>
    /// Get the height of the tile at the specified position.
    /// </summary>
    /// <param name="x">The x coordinate of the tile.</param>
    /// <param name="y">The y coordinate of the tile.</param>
    /// <param name="height">The height of the tile.</param>
    /// <returns>True if the height is retrieved, false otherwise.</returns>
    public bool TryGetTileHeight(int x, int y, out float height)

    {
        if (x < 0 || y < 0 || x >= _size.X || y >= _size.Y)
        {
            height = 0;
            return false;
        }
        height = this[GetTileIndex(x, y)];
        return true;
    }

    /// <summary>
    /// Set the height of the tile at the specified position.
    /// </summary>
    /// <param name="x">The x coordinate of the tile.</param>
    /// <param name="y">The y coordinate of the tile.</param>
    /// <param name="height">The height of the tile.</param>
    /// <returns>True if the height is set, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetTileHeight(int x, int y, float height)
    {
        if (x < 0 || y < 0 || x >= _size.X || y >= _size.Y)
        {
            return false;
        }
        this[GetTileIndex(x, y)] = height;
        _isDirty = true;
        return true;

    }

    private int GetTileIndex(int x, int y)
    {
        return y * _size.X + x;
    }

}