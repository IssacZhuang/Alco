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
            Clear(defaultValue.Value);
        }
    }

    public Bitmap(uint width, uint height, T? defaultValue = default)
    {
        _data = new GridMap<T>((int)width, (int)height);
        if (defaultValue.HasValue)
        {
            Clear(defaultValue.Value);
        }
    }

    /// <summary>
    /// Sets all pixels in the bitmap to the specified value.
    /// </summary>
    /// <param name="value">The value to initialize all pixels with.</param>
    /// <remarks>
    /// This operation is optimized using SIMD instructions when available.
    /// </remarks>
    public void Clear(T value = default)
    {
        //the Span.Fill has been optimized for SIMD
        _data.AsSpan().Fill(value);
    }


    /// <inheritdoc/>
    protected override void Dispose(bool disposing)

    {
        _data.Dispose();
    }

}

