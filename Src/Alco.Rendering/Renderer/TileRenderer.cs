
using System.Collections.Frozen;
using System.Numerics;

namespace Alco.Rendering;

public sealed class NewTileSetitem
{
    public string Name { get; }
    public Material Material { get; }
    public object? UserData { get; }

    public NewTileSetitem(string name, Material material, object? userData)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(material);
        Name = name;
        Material = material;
        UserData = userData;
    }
}

public sealed class NewTileSet
{
    private readonly NewTileSetitem[] _items;
    private readonly FrozenDictionary<NewTileSetitem, int> _itemIndexMap;

    public int Count => _items.Length;

    public NewTileSet(params ReadOnlySpan<NewTileSetitem> items)
    {
        _items = items.ToArray();
        Dictionary<NewTileSetitem, int> itemIndexMap = new();
        for (int i = 0; i < _items.Length; i++)
        {
            itemIndexMap.Add(_items[i], i);
        }
        _itemIndexMap = itemIndexMap.ToFrozenDictionary();
    }

    public NewTileSetitem GetItem(int index)
    {
        return _items[index];
    }

    public int GetItemIndex(NewTileSetitem item)
    {
        return _itemIndexMap[item];
    }
}

public sealed class TileRenderer
{
    private struct TileInstanceData
    {
        public Vector2 Position;

        public TileInstanceData(Vector2 position)
        {
            Position = position;
        }
    }

    private readonly RenderingSystem _rendering;
    private readonly IRenderContext _context;
    private readonly NewTileSet _tileSet;
    //same index as the item
    private readonly InstanceRenderer<TileInstanceData>[] _instanceRenderer;

    private readonly int[] _map;

    //The global transform of the tile renderer
    public Transform3D Transform;

    public string Name { get; }

    internal TileRenderer(RenderingSystem rendering, IRenderContext context, NewTileSet tileSet, int width, int height, string name = "tile_renderer")
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tileSet);

        _rendering = rendering;
        _context = context;
        _tileSet = tileSet;

        Name = name;

        _instanceRenderer = new InstanceRenderer<TileInstanceData>[tileSet.Count];
        for (int i = 0; i < tileSet.Count; i++)
        {
            _instanceRenderer[i] = rendering.CreateInstanceRenderer<TileInstanceData>(context, tileSet.GetItem(i).Material);
        }

        _map = new int[width * height];
    }
}