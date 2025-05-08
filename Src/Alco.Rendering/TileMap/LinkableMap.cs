
using System.Diagnostics.CodeAnalysis;

namespace Alco.Rendering;

public class LinkableMap
{
    public struct Info
    {
        public int X;
        public int Y;
        public LinkableData Data;

        public Info(int x, int y, LinkableData data)
        {
            X = x;
            Y = y;
            Data = data;
        }
    }
    private readonly LinkableData[] _data; //flatten 2d array
    private readonly UnorderedList<Info> _infos = new();
    private readonly int _width;
    private readonly int _height;

    public int Width => _width;
    public int Height => _height;

    public IReadOnlyList<Info> Infos => _infos;

    public LinkableMap(int width, int height)
    {
        _data = new LinkableData[width * height];
        _width = width;
        _height = height;
    }

    public bool TrySet(int x, int y, LinkableData data)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height)
            return false;

        LinkableData oldData = _data[y * _width + x];
        if (oldData != null)
        {
            return false;
        }

        _data[y * _width + x] = data;
        _infos.Add(new Info(x, y, data));
        return true;
    }

    public void Set(int x, int y, LinkableData data)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height)
        {
            throw new ArgumentOutOfRangeException($"x: {x}, y: {y} is out of range: {_width}x{_height}");
        }

        LinkableData oldData = _data[y * _width + x];
        if (oldData != null)
        {
            _infos.Remove(new Info(x, y, oldData));
        }

        _data[y * _width + x] = data;
        _infos.Add(new Info(x, y, data));
    }

    public bool TryGet(int x, int y, [NotNullWhen(true)] out LinkableData? data)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height)
        {
            data = null;
            return false;
        }

        data = _data[y * _width + x];
        return data != null;
    }
}

