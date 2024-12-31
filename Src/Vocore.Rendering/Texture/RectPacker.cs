using StbRectPackSharp;

namespace Vocore.Rendering;

public class RectPacker : AutoDisposable
{
    private Packer _packer;
    private bool _extendVertical;

    public int PackedCount => _packer.PackRectangles.Count;

    public int Width => _packer.Width;
    public int Height => _packer.Height;

    public RectInt this[int index]
    {
        get
        {
            var rect = _packer.PackRectangles[index];
            return new RectInt(rect.Rectangle.X, rect.Rectangle.Y, rect.Rectangle.Width, rect.Rectangle.Height);
        }
    }

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

    public void AddRect(RectInt rect)
    {
        PackerRectangle? rectangle = _packer.PackRect(rect.size.x, rect.size.y, rect);
        while (!rectangle.HasValue)
        {
            ResizePacker();
            rectangle = _packer.PackRect(rect.size.x, rect.size.y, rect);
        }
    }

    protected override void Dispose(bool disposing)
    {
        _packer.Dispose();
    }
}

public class RectPacker<T> : AutoDisposable
{
    public struct Item
    {
        public RectInt Rect;
        public T Data;
    }

    private Packer _packer;
    private List<Item> _items;
    private bool _extendVertical;

    public IReadOnlyList<Item> Items => _items;

    public int Width => _packer.Width;
    public int Height => _packer.Height;

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

    public void AddRect(RectInt rect, T data)
    {
        PackerRectangle? rectangle = _packer.PackRect(rect.size.x, rect.size.y, data);
        while (!rectangle.HasValue)
        {
            ResizePacker();
            rectangle = _packer.PackRect(rect.size.x, rect.size.y, data);
        }
        _items.Add(new Item { Rect = rect, Data = data });
    }

    protected override void Dispose(bool disposing)
    {
        _packer.Dispose();
    }
}
