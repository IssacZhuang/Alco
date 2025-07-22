
using System.Collections.Frozen;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

public sealed class TileItem
{
    public string Name { get; }
    public Material Material { get; }
    public float RenderOrder { get; }
    public object? UserData { get; }
    
    public Vector4 Color { get; set; } = Vector4.One;
    public float BlendFactor { get; set; } = 0.2f;

    public TileItem(string name, Material material, float renderOrder, object? userData)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(material);
        Name = name;
        Material = material;
        UserData = userData;
    }
}

public sealed class TileSet
{
    private readonly TileItem[] _items;
    private readonly FrozenDictionary<TileItem, int> _itemIndexMap;

    public int Count => _items.Length;

    public TileSet(params ReadOnlySpan<TileItem> items)
    {
        _items = items.ToArray();
        Array.Sort(_items, static (a, b) => a.RenderOrder.CompareTo(b.RenderOrder));

        Dictionary<TileItem, int> itemIndexMap = new();
        for (int i = 0; i < _items.Length; i++)
        {
            itemIndexMap.Add(_items[i], i);
        }
        _itemIndexMap = itemIndexMap.ToFrozenDictionary();
    }

    public TileItem GetItem(int index)
    {
        return _items[index];
    }

    public int GetItemIndex(TileItem item)
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
        public int CurrentTileId;
        public int _reserved = 0;
        public Vector4 Color;
        public float BlendFactor;

        public Constant(Matrix4x4 model, int2 size)
        {
            Model = model;
            Size = size;
            CurrentTileId = 0;
            BlendFactor = 0.2f;
            Color = Vector4.One;
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
    private readonly TileSet _tileSet;
    //same index as the item
    private readonly Batch[] _batches;

    private readonly int[] _tileMap;
    private readonly GraphicsBuffer _tileMapBuffer;

    private readonly int _width;
    private readonly int _height;

    private bool _isDirty = true;

    //The global transform of the tile renderer
    public Transform3D Transform;

    public string Name { get; }

    public int2 Size => new int2(_width, _height);

    internal TileRenderer(RenderingSystem rendering, IRenderContext context, TileSet tileSet, int width, int height, string name = "tile_renderer")
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tileSet);

        _rendering = rendering;
        _context = context;
        _tileSet = tileSet;

        Name = name;

        _tileMapBuffer = rendering.CreateGraphicsBuffer((uint)(width * height * sizeof(int)), "tile_map");
        ReadOnlySpan<int> span = _tileMap;
        _tileMapBuffer.UpdateBuffer(span);

        _batches = new Batch[tileSet.Count];
        for (int i = 0; i < tileSet.Count; i++)
        {
            Material material = tileSet.GetItem(i).Material.CreateInstance();
            material.TrySetBuffer(ShaderResourceId.TileMap, _tileMapBuffer);
            _batches[i] = new Batch(rendering.MeshCenteredSprite, rendering.CreateInstanceRenderer<TileInstanceData>(context, material));
        }

        _tileMap = new int[width * height];
        _tileMap.AsSpan().Fill(TileIdEmpty);

        

        _width = width;
        _height = height;

        Transform = Transform3D.Identity;
    }

    private void FillBatches()
    {
        //clear all batches
        for (int i = 0; i < _batches.Length; i++)
        {
            _batches[i].Clear();
        }

        Span<int> spanMap = _tileMap;
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

        ReadOnlySpan<int> span = _tileMap;
        _tileMapBuffer.UpdateBuffer(span);
    }

    public void SetTile(int x, int y, int tileId)
    {
        _tileMap[y * _width + x] = tileId;
        _isDirty = true;
    }

    public void SetAllTiles(int tileId)
    {
        _tileMap.AsSpan().Fill(tileId);
        _isDirty = true;
    }

    public void ClearTile(int x, int y)
    {
        _tileMap[y * _width + x] = TileIdEmpty;
        _isDirty = true;
    }

    public void ClearAllTiles()
    {
        _tileMap.AsSpan().Fill(TileIdEmpty);
        _isDirty = true;
    }

    public void Render()
    {
        if (_isDirty)
        {
            FillBatches();
            _isDirty = false;
        }

        Transform3D transform = Transform;

        Constant constant = new(transform.Matrix, new int2(_width, _height));

        for (int i = 0; i < _batches.Length; i++)
        {
            constant.CurrentTileId = i;
            constant.BlendFactor = _tileSet.GetItem(i).BlendFactor;

            _batches[i].Draw(constant);
        }
    }

    public bool TryGetTilePositionByRay(Ray3D ray, out int2 tilePosition)
    {
        Matrix4x4 matrix = Transform.Matrix;
        //to local space
        if (Matrix4x4.Invert(matrix, out Matrix4x4 invMatrix))
        {
            Vector3 start = ray.Origin;
            Vector3 end = ray.Origin + ray.Displacement;

            Vector3 localStart = Vector3.Transform(start, invMatrix);
            Vector3 localEnd = Vector3.Transform(end, invMatrix);

            Plane3D plane = new Plane3D(Vector3.UnitZ, 0);

            Ray3D localRay = new Ray3D(localStart, localEnd - localStart);

            if (plane.IntersectRay(localRay, out Vector3 hitPoint))
            {
                // TileRenderer Transform corresponds to bottom-left corner (0,0)
                // No offset needed since Transform is already at the correct position
                int tileX = (int)math.round(hitPoint.X);
                int tileY = (int)math.round(hitPoint.Y);

                if (tileX >= 0 && tileX < _width && tileY >= 0 && tileY < _height)
                {
                    tilePosition = new int2(tileX, tileY);
                    return true;
                }
            }
        }

        tilePosition = new int2(0, 0);
        return false;
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