using StbRectPackSharp;

namespace Vocore.Rendering;

/// <summary>
/// A rectangle packer that efficiently packs rectangles into a collection.
/// Automatically resizes the packing area when needed by alternating between extending width and height.
/// </summary>
public class RectPacker : AutoDisposable
{
    private Packer _packer;
    private bool _extendVertical;

    /// <summary>
    /// Gets the number of rectangles that have been packed.
    /// </summary>
    public int PackedCount => _packer.PackRectangles.Count;

    /// <summary>
    /// Gets the current width of the packing area.
    /// </summary>
    public int Width => _packer.Width;

    /// <summary>
    /// Gets the current height of the packing area.
    /// </summary>
    public int Height => _packer.Height;

    /// <summary>
    /// Gets the packed rectangle at the specified index.
    /// </summary>
    /// <param name="index">The index of the rectangle to retrieve.</param>
    /// <returns>The rectangle at the specified index.</returns>
    public RectInt this[int index]
    {
        get
        {
            var rect = _packer.PackRectangles[index];
            return rect.Rectangle;
        }
    }

    /// <summary>
    /// Creates a new rectangle packer with the specified initial dimensions.
    /// </summary>
    /// <param name="width">The initial width of the packing area.</param>
    /// <param name="height">The initial height of the packing area.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when width or height is less than or equal to 0.</exception>
    public RectPacker(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        _packer = new Packer(width, height);
    }

    private void ResizePacker()
    {
        int newHeight = _extendVertical ? _packer.Height * 2 : _packer.Height;
        int newWidth = _extendVertical ? _packer.Width : _packer.Width * 2;
        _extendVertical = !_extendVertical;

        Packer newPacker = new Packer(newWidth, newHeight);
        for (int i = 0; i < _packer.PackRectangles.Count; i++)
        {
            newPacker.PackRect(_packer.PackRectangles[i].Width, _packer.PackRectangles[i].Height, null);
        }
        _packer = newPacker;
    }

    /// <summary>
    /// Adds a rectangle to the packing area. If there is not enough space, the packing area will be automatically resized.
    /// </summary>
    /// <param name="width">The width of the rectangle to add.</param>
    /// <param name="height">The height of the rectangle to add.</param>
    public void AddRect(int width, int height)
    {
        PackerRectangle? rectangle = _packer.PackRect(width, height, null);
        while (!rectangle.HasValue)
        {
            ResizePacker();
            rectangle = _packer.PackRect(width, height, null);
        }
    }

    /// <summary>
    /// Disposes the underlying packer.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
    protected override void Dispose(bool disposing)
    {
        _packer.Dispose();
    }
}

/// <summary>
/// A generic rectangle packer that efficiently packs rectangles with associated data into a collection.
/// Automatically resizes the packing area when needed by alternating between extending width and height.
/// </summary>
/// <typeparam name="T">The type of data associated with each rectangle.</typeparam>
public class RectPacker<T> : AutoDisposable
{
    /// <summary>
    /// Represents a packed rectangle with its associated data.
    /// </summary>
    public struct Item
    {
        /// <summary>
        /// The packed rectangle.
        /// </summary>
        public RectInt Rect;

        /// <summary>
        /// The data associated with the rectangle.
        /// </summary>
        public T Data;
    }

    private Packer _packer;
    private List<Item> _items;
    private bool _extendVertical;

    /// <summary>
    /// Gets the list of packed items.
    /// </summary>
    public IReadOnlyList<Item> Items => _items;

    /// <summary>
    /// Gets the current width of the packing area.
    /// </summary>
    public int Width => _packer.Width;

    /// <summary>
    /// Gets the current height of the packing area.
    /// </summary>
    public int Height => _packer.Height;

    /// <summary>
    /// Creates a new rectangle packer with the specified initial dimensions.
    /// </summary>
    /// <param name="width">The initial width of the packing area.</param>
    /// <param name="height">The initial height of the packing area.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when width or height is less than or equal to 0.</exception>
    public RectPacker(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        _packer = new Packer(width, height);
        _items = new List<Item>();
    }

    private void ResizePacker()
    {
        int newHeight = _extendVertical ? _packer.Height * 2 : _packer.Height;
        int newWidth = _extendVertical ? _packer.Width : _packer.Width * 2;
        _extendVertical = !_extendVertical;

        Packer newPacker = new Packer(newWidth, newHeight);
        for (int i = 0; i < _packer.PackRectangles.Count; i++)
        {
            newPacker.PackRect(_packer.PackRectangles[i].Width, _packer.PackRectangles[i].Height, null);
        }
        _packer = newPacker;
    }

    /// <summary>
    /// Adds a rectangle with associated data to the packing area. If there is not enough space, the packing area will be automatically resized.
    /// </summary>
    /// <param name="width">The width of the rectangle to add.</param>
    /// <param name="height">The height of the rectangle to add.</param>
    /// <param name="data">The data associated with the rectangle.</param>
    public void AddRect(int width, int height, T data)
    {
        PackerRectangle? rectangle = _packer.PackRect(width, height, data);
        while (!rectangle.HasValue)
        {
            ResizePacker();
            rectangle = _packer.PackRect(width, height, data);
        }
        _items.Add(new Item { Rect = rectangle.Value.Rectangle, Data = data });
    }

    /// <summary>
    /// Disposes the underlying packer.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
    protected override void Dispose(bool disposing)
    {
        _packer.Dispose();
    }
}
