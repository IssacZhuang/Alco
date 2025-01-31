using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

/// <summary>
/// A bitmap is a 2D array of pixels.
/// </summary>
/// <typeparam name="T">The type of the pixel.</typeparam>
public unsafe class Bitmap<T> : AutoDisposable where T : unmanaged

{
    private GridMap<T> _data;

    /// <summary>
    /// Access the pixel at the specified coordinates.
    /// </summary>
    /// <param name="x">The x coordinate of the pixel.</param>
    /// <param name="y">The y coordinate of the pixel.</param>
    /// <returns>The pixel at the specified coordinates.</returns>
    public T this[int x, int y]

    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _data[x, y];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _data[x, y] = value;
    }


    /// <summary>
    /// Get the width of the bitmap.
    /// </summary>  
    public int Width => _data.Width;

    /// <summary>
    /// Get the height of the bitmap.
    /// </summary>
    public int Height => _data.Height;

    /// <summary>
    /// Get the pointer to the data of the bitmap.
    /// </summary>
    public T* UnsafePointer => _data.UnsafePointer;


    /// <summary>
    /// Create a new bitmap with the specified width and height.
    /// </summary>
    /// <param name="width">The width of the bitmap.</param>
    /// <param name="height">The height of the bitmap.</param>
    public Bitmap(int width, int height)
    {
        _data = new GridMap<T>(width, height);
    }


    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        _data.Dispose();
    }

}

