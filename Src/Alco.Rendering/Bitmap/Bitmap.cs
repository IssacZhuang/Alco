using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

/// <summary>
/// A bitmap is a 2D array of pixels only on CPU.
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
    /// Gets a <see cref="Span{T}"/> representation of the bitmap's pixel data.
    /// </summary>
    /// <remarks>
    /// The span represents the entire bitmap buffer in row-major order.
    /// </remarks>
    /// <returns>A writable span covering the bitmap's pixel data.</returns>
    public Span<T> AsSpan()
    {
        return _data.AsSpan();
    }

    /// <summary>
    /// Gets a <see cref="ReadOnlySpan{T}"/> representation of the bitmap's pixel data.
    /// </summary>
    /// <remarks>
    /// The span represents the entire bitmap buffer in row-major order.
    /// </remarks>
    /// <returns>A read-only span covering the bitmap's pixel data.</returns>
    public ReadOnlySpan<T> AsReadOnlySpan()
    {
        return _data.AsReadOnlySpan();
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
    /// Gets a pointer to the underlying bitmap data buffer for unsafe operations.
    /// </summary>
    /// <remarks>
    /// The pointer provides direct access to the bitmap's internal buffer. Use with caution
    /// and ensure proper bounds checking when using this pointer.
    /// </remarks>
    public T* UnsafePointer => _data.UnsafePointer;


    /// <summary>
    /// Create a new bitmap with the specified width and height.
    /// </summary>
    /// <param name="width">The width of the bitmap.</param>
    /// <param name="height">The height of the bitmap.</param>
    public Bitmap(int width, int height, T? defaultValue = default)
    {
        _data = new GridMap<T>(width, height);
        if (defaultValue.HasValue)
        {
            Fill(defaultValue.Value);
        }
        else
        {
            Clear();
        }
    }

    public Bitmap(uint width, uint height, T? defaultValue = default)
    {
        _data = new GridMap<T>((int)width, (int)height);
        if (defaultValue.HasValue)
        {
            Fill(defaultValue.Value);
        }
        else
        {
            Clear();
        }
    }

    /// <summary>
    /// Sets all pixels in the bitmap to the specified value.
    /// </summary>
    /// <param name="value">The value to initialize all pixels with.</param>
    public void Clear()
    {
        _data.AsSpan().Clear();
    }

    public void Fill(T value)
    {
        _data.AsSpan().Fill(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(int x, int y, T value)
    {
        _data[x, y] = value;
    }

    /// <summary>
    /// Sets all pixels in the specified rectangular region to the given value.
    /// </summary>
    /// <param name="from">The top-left corner of the region (inclusive).</param>
    /// <param name="to">The bottom-right corner of the region (inclusive).</param>
    /// <param name="value">The value to set for all pixels in the region.</param>
    public void Set(int2 from, int2 to, T value)
    {
        int2 size = new int2(Width, Height);

        // Clamp coordinates to valid bounds
        from = math.clamp(from, new int2(0, 0), size - new int2(1, 1));
        to = math.clamp(to, new int2(0, 0), size - new int2(1, 1));

        // Set all pixels in the rectangular region
        for (int y = from.Y; y <= to.Y; y++)
        {
            for (int x = from.X; x <= to.X; x++)
            {
                this[x, y] = value;
            }
        }
    }


    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        _data.Dispose();
    }

}

