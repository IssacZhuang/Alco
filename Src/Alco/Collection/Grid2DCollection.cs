using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco;

/// <summary>
/// Represents a 2D grid collection storing elements of type T
/// </summary>
/// <typeparam name="T">The type of elements in the grid, must be a reference type</typeparam>
public class Grid2DCollection<T> where T : class
{
    /// <summary>
    /// Structure containing grid cell information
    /// </summary>
    public struct Info
    {
        public int X;
        public int Y;
        public T Data;

        public Info(int x, int y, T data)
        {
            X = x;
            Y = y;
            Data = data;
        }
    }
    private readonly T?[] _data; //flatten 2d array
    private readonly UnorderedList<Info> _infos = new();
    private readonly int _width;
    private readonly int _height;

    /// <summary>
    /// Gets the width of the grid
    /// </summary>
    public int Width => _width;

    /// <summary>
    /// Gets the height of the grid
    /// </summary>
    public int Height => _height;

    /// <summary>
    /// Gets a read-only list of all grid entries with their positions
    /// </summary>
    public IReadOnlyList<Info> Infos => _infos;

    /// <summary>
    /// Initializes a new instance of the Grid2DCollection with specified dimensions
    /// </summary>
    /// <param name="width">Width of the grid</param>
    /// <param name="height">Height of the grid</param>
    public Grid2DCollection(int width, int height)
    {
        _data = new T[width * height];
        _width = width;
        _height = height;
    }

    /// <summary>
    /// Attempts to set data at the specified grid position
    /// </summary>
    /// <param name="x">X coordinate (0-based)</param>
    /// <param name="y">Y coordinate (0-based)</param>
    /// <param name="data">Data to store in the grid cell</param>
    /// <returns>True if data was successfully set, false if position is invalid or cell already occupied</returns>
    public bool TrySet(int x, int y, T data)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height)
            return false;

        T? oldData = _data[y * _width + x];
        if (oldData != null)
        {
            return false;
        }

        _data[y * _width + x] = data;
        _infos.Add(new Info(x, y, data));
        return true;
    }

    /// <summary>
    /// Sets data at the specified grid position, replacing any existing data
    /// </summary>
    /// <param name="x">X coordinate (0-based)</param>
    /// <param name="y">Y coordinate (0-based)</param>
    /// <param name="data">Data to store in the grid cell</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when coordinates are out of grid bounds</exception>
    public void Set(int x, int y, T data)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height)
        {
            throw new ArgumentOutOfRangeException($"x: {x}, y: {y} is out of range: {_width}x{_height}");
        }

        T? oldData = _data[y * _width + x];
        if (oldData != null)
        {
            for (int i = 0; i < _infos.Count; i++)
            {
                if (_infos[i].X == x && _infos[i].Y == y)
                {
                    _infos.RemoveAt(i);
                    break;
                }
            }
        }

        _data[y * _width + x] = data;
        _infos.Add(new Info(x, y, data));
    }

    /// <summary>
    /// Attempts to get data from the specified grid position
    /// </summary>
    /// <param name="x">X coordinate (0-based)</param>
    /// <param name="y">Y coordinate (0-based)</param>
    /// <param name="data">Output parameter that receives the data if found</param>
    /// <returns>True if data exists at specified position, false otherwise</returns>
    public bool TryGet(int x, int y, [NotNullWhen(true)] out T? data)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height)
        {
            data = null;
            return false;
        }

        data = _data[y * _width + x];
        return data != null;
    }

    /// <summary>
    /// Attempts to remove data from the specified grid position
    /// </summary>
    /// <param name="x">X coordinate (0-based)</param>
    /// <param name="y">Y coordinate (0-based)</param>
    /// <param name="data">Output parameter that receives the data if found</param>
    /// <returns>True if data was successfully removed, false if position is invalid or data not found</returns>
    public bool TryRemove(int x, int y, [NotNullWhen(true)] out T? data)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height)
        {
            data = null;
            return false;
        }

        data = _data[y * _width + x];
        if (data == null)
        {
            return false;
        }

        _data[y * _width + x] = null;
        for (int i = 0; i < _infos.Count; i++)
        {
            if (_infos[i].X == x && _infos[i].Y == y)
            {
                _infos.RemoveAt(i);
                break;
            }
        }
        return true;
    }

    /// <summary>
    /// Clears all data from the grid
    /// </summary>
    public void Clear()
    {
        _data.AsSpan().Clear();
        _infos.Clear();
    }

}

