
using System.Collections.Frozen;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

public sealed class NewTileSetitem
{
    public string Name { get; }
    public Material Material { get; }
    public float RenderOrder { get; }
    public object? UserData { get; }

    public NewTileSetitem(string name, Material material, float renderOrder, object? userData)
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
        Array.Sort(_items, static (a, b) => a.RenderOrder.CompareTo(b.RenderOrder));

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

public sealed class TileRenderer : AutoDisposable
{
    public const int TileIdEmpty = -1;

    private struct TileInstanceData
    {
        public Vector2 Position;

        public TileInstanceData(Vector2 position)
        {
            Position = position;
        }
    }


    private struct Constant
    {
        public Matrix4x4 Model;
        public int2 Size;

        public Constant(Matrix4x4 model, int2 size)
        {
            Model = model;
            Size = size;
        }
    }


    private class Batch : AutoDisposable
    {
        private readonly InstanceRenderer<TileInstanceData> _renderer;
        private readonly UnorderedList<TileInstanceData> _buffer;
        private readonly Mesh _mesh;

        public Batch(Mesh mesh, InstanceRenderer<TileInstanceData> renderer)
        {
            _renderer = renderer;
            _buffer = new();
            _mesh = mesh;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(TileInstanceData instance)
        {
            _buffer.Add(instance);
        }

        public void Clear()
        {
            _buffer.Clear();
        }

        public void Draw(in Constant constant)
        {
            _renderer.DrawWithConstant(_mesh, constant, _buffer.AsSpan());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _renderer.Dispose();
            }
        }
    }

    private readonly RenderingSystem _rendering;
    private readonly IRenderContext _context;
    private readonly NewTileSet _tileSet;
    //same index as the item
    private readonly Batch[] _batches;

    private readonly int[] _map;
    private readonly int _width;
    private readonly int _height;

    private bool _isDirty = true;

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

        _batches = new Batch[tileSet.Count];
        for (int i = 0; i < tileSet.Count; i++)
        {
            _batches[i] = new Batch(rendering.MeshCenteredSprite, rendering.CreateInstanceRenderer<TileInstanceData>(context, tileSet.GetItem(i).Material));
        }

        _map = new int[width * height];
        _map.AsSpan().Fill(TileIdEmpty);

        _width = width;
        _height = height;
    }

    private void FillBatches()
    {
        //clear all batches
        for (int i = 0; i < _batches.Length; i++)
        {
            _batches[i].Clear();
        }

        Span<int> spanMap = _map;
        int itemCount = _tileSet.Count;
        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                int tileId = spanMap[y * _width + x];
                if (tileId < 0 || tileId >= itemCount)
                {
                    continue;
                }

                _batches[tileId].Add(new TileInstanceData(new Vector2(x, y)));
            }
        }
    }

    public void SetTile(int x, int y, int tileId)
    {
        _map[y * _width + x] = tileId;
        _isDirty = true;
    }

    public void ClearTile(int x, int y)
    {
        _map[y * _width + x] = TileIdEmpty;
        _isDirty = true;
    }

    public void ClearAllTiles()
    {
        _map.AsSpan().Fill(TileIdEmpty);
        _isDirty = true;
    }

    public void Render()
    {
        if (_isDirty)
        {
            FillBatches();
            _isDirty = false;
        }

        Constant constant = new(Transform.Matrix, new int2(_width, _height));

        for (int i = 0; i < _batches.Length; i++)
        {
            _batches[i].Draw(constant);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var batch in _batches)
            {
                batch.Dispose();
            }
        }
    }
}