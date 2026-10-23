using System;
using System.Runtime.CompilerServices;


using Alco;

/// <summary>
/// A two-dimensional grid of unmanaged values stored in native memory.
/// </summary>
public unsafe struct GridMap<T> : IDisposable where T : unmanaged
{
    private NativeBuffer<T> _data;
    private int2 _size;
    public readonly T defaultValue;

    /// <summary>
    /// Gets the grid dimensions.
    /// </summary>
    public int2 Size => _size;
    /// <summary>
    /// Gets the number of columns in the grid.
    /// </summary>
    public int Width => _size.X;
    /// <summary>
    /// Gets the number of rows in the grid.
    /// </summary>
    public int Height => _size.Y;
    /// <summary>
    /// Gets the total number of cells in the grid.
    /// </summary>
    public int DataLength => _data.Length;

    public unsafe T* UnsafePointer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _data.UnsafePointer;
    }

    /// <summary>
    /// Returns a writable span over all grid cells.
    /// </summary>
    /// <returns>A span over the backing storage.</returns>
    public Span<T> AsSpan()
    {
        return new Span<T>(UnsafePointer, _data.Length);
    }

    /// <summary>
    /// Returns a read-only span over all grid cells.
    /// </summary>
    /// <returns>A read-only span over the backing storage.</returns>
    public ReadOnlySpan<T> AsReadOnlySpan()
    {
        return new ReadOnlySpan<T>(UnsafePointer, _data.Length);
    }

    /// <summary>
    /// Initializes a grid with the specified dimensions and default cell value.
    /// </summary>
    /// <param name="width">The number of columns.</param>
    /// <param name="height">The number of rows.</param>
    /// <param name="defaultValue">The value assigned to out-of-range reads and resets.</param>
    public GridMap(int width, int height, T defaultValue = default(T))
    {
        _size = new int2(width, height);
        _data = new NativeBuffer<T>(width * height);
        this.defaultValue = defaultValue;
    }

    public T this[int x, int y]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (InRange(x, y))
            {
                return _data[x + y * Width];
            }
            return defaultValue;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (InRange(x, y))
            {
                _data[x + y * Width] = value;
            }
        }
    }

    /// <summary>
    /// Determines whether the specified coordinates lie within the grid.
    /// </summary>
    /// <param name="x">The column index.</param>
    /// <param name="y">The row index.</param>
    /// <returns>True if the coordinates are valid grid indices.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InRange(int x, int y)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height;
    }

    /// <summary>
    /// Sets every cell to the default value.
    /// </summary>
    public void Reset()
    {
        for (int i = 0; i < _data.Length; i++)
        {
            _data[i] = defaultValue;
        }
    }

    /// <summary>
    /// Reallocates the grid with the specified dimensions without preserving existing contents.
    /// </summary>
    /// <param name="width">The new number of columns.</param>
    /// <param name="height">The new number of rows.</param>
    public void ResizeNoCopy(int width, int height)
    {
        _data.Dispose();
        _size = new int2(width, height);
        _data = new NativeBuffer<T>(width * height);
    }

    /// <summary>
    /// Releases the native memory backing the grid.
    /// </summary>
    public void Dispose()
    {
        _data.Dispose();
    }
}