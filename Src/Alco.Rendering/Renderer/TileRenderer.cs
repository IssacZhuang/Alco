
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

    private class Renderer : AutoDisposable
    {
        private readonly InstanceRenderer<TileInstanceData> _renderer;
        private readonly UnorderedList<TileInstanceData> _buffer;
        private readonly Mesh _mesh;

        public Renderer(Mesh mesh, InstanceRenderer<TileInstanceData> renderer)
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

    /// <summary>
    /// Represents a tile batch that manages rendering for a specific region of the tile map.
    /// </summary>
    private sealed class TileBatch : AutoDisposable
    {
        private readonly Renderer[] _renderers;
        private readonly TileSet _tileSet;
        private readonly int _batchX;
        private readonly int _batchY;
        private readonly int _batchWidth;
        private readonly int _batchHeight;
        private readonly int _mapWidth;
        private readonly int _mapHeight;
        private bool _isDirty = true;

        public bool IsDirty => _isDirty;

        public TileBatch(TileSet tileSet, RenderingSystem rendering, IRenderContext context,
            int batchX, int batchY, int batchWidth, int batchHeight, int mapWidth, int mapHeight, GraphicsBuffer tileMapBuffer)
        {
            _tileSet = tileSet;
            _batchX = batchX;
            _batchY = batchY;
            _batchWidth = batchWidth;
            _batchHeight = batchHeight;
            _mapWidth = mapWidth;
            _mapHeight = mapHeight;

            _renderers = new Renderer[tileSet.Count];
            for (int i = 0; i < tileSet.Count; i++)
            {
                Material material = tileSet.GetItem(i).Material.CreateInstance();
                material.TrySetBuffer(ShaderResourceId.TileMap, tileMapBuffer);
                _renderers[i] = new Renderer(rendering.MeshCenteredSprite,
                    rendering.CreateInstanceRenderer<TileInstanceData>(context, material));
            }
        }

        public void SetDirty()
        {
            _isDirty = true;
        }

        public void UpdateBuffer(ReadOnlySpan<int> tileMap)
        {
            if (!_isDirty) return;

            // Clear all renderers in this batch
            for (int i = 0; i < _renderers.Length; i++)
            {
                _renderers[i].Clear();
            }

            int itemCount = _tileSet.Count;

            // Process tiles in this batch region
            int startX = _batchX;
            int startY = _batchY;
            int endX = startX + _batchWidth;
            int endY = startY + _batchHeight;

            for (int x = startX; x < endX; x++)
            {
                for (int y = startY; y < endY; y++)
                {

                    if (x >= _mapWidth || y >= _mapHeight)
                        continue;

                    int tileId = tileMap[y * _mapWidth + x];
                    if (tileId < 0 || tileId >= itemCount)
                        continue;

                    _renderers[tileId].Add(new TileInstanceData(new Vector2(x, y)));
                }
            }

            _isDirty = false;
        }

        public void Render(in Constant baseConstant)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                TileItem item = _tileSet.GetItem(i);
                Constant constant = baseConstant;
                constant.CurrentTileId = i;
                constant.BlendFactor = item.BlendFactor;
                constant.Color = item.Color;

                _renderers[i].Draw(constant);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var renderer in _renderers)
                {
                    renderer.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Parallel task for updating multiple tile batches.
    /// </summary>
    private sealed class BatchUpdateTask : ReuseableBatchTask
    {
        public readonly List<TileBatch> Batches = new List<TileBatch>();
        public int[] TileMap = null!;

        protected override void ExecuteCore(int index)
        {
            TileBatch batch = Batches[index];
            batch.UpdateBuffer(TileMap.AsSpan());
        }
    }

    private readonly RenderingSystem _rendering;
    private readonly IRenderContext _context;
    private readonly TileSet _tileSet;

    private readonly int[] _tileMap;
    private readonly GraphicsBuffer _tileMapBuffer;

    private readonly int _width;
    private readonly int _height;
    private readonly int _batchSizeX;
    private readonly int _batchSizeY;
    private readonly int _batchCountX;
    private readonly int _batchCountY;
    private readonly TileBatch[] _batches;
    private readonly BatchUpdateTask _updateTask;

    // Viewport fields for culling
    private int2 _viewportFrom;
    private int2 _viewportTo;
    private bool _hasViewport;

    //The global transform of the tile renderer
    public Transform3D Transform;

    public string Name { get; }

    public int2 Size => new int2(_width, _height);

    /// <summary>
    /// Gets whether a viewport is currently set.
    /// </summary>
    public bool HasViewport => _hasViewport;


    /// <summary>
    /// Gets the batch size in the X direction.
    /// </summary>
    public int BatchSizeX => _batchSizeX;

    /// <summary>
    /// Gets the batch size in the Y direction.
    /// </summary>
    public int BatchSizeY => _batchSizeY;

    internal TileRenderer(RenderingSystem rendering, IRenderContext context, TileSet tileSet, int width, int height, string name = "tile_renderer")
        : this(rendering, context, tileSet, width, height, 64, 64, name)
    {
    }

    /// <summary>
    /// Initializes a new instance of the TileRenderer class with specified batch sizes.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="context">The render context.</param>
    /// <param name="tileSet">The tile set to use.</param>
    /// <param name="width">The width of the tile map.</param>
    /// <param name="height">The height of the tile map.</param>
    /// <param name="batchSizeX">The width of each batch in tiles.</param>
    /// <param name="batchSizeY">The height of each batch in tiles.</param>
    /// <param name="name">The name of the renderer.</param>
    internal TileRenderer(RenderingSystem rendering, IRenderContext context, TileSet tileSet, int width, int height, int batchSizeX, int batchSizeY, string name = "tile_renderer")
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tileSet);

        if (width <= 0 || height <= 0)
            throw new ArgumentException("Width and height must be positive.");
        if (batchSizeX <= 0 || batchSizeY <= 0)
            throw new ArgumentException("Batch sizes must be positive.");

        _rendering = rendering;
        _context = context;
        _tileSet = tileSet;
        _width = width;
        _height = height;
        _batchSizeX = batchSizeX;
        _batchSizeY = batchSizeY;

        Name = name;

        // Calculate batch counts
        _batchCountX = (width + batchSizeX - 1) / batchSizeX; // Ceiling division
        _batchCountY = (height + batchSizeY - 1) / batchSizeY; // Ceiling division

        // Initialize tile map
        _tileMap = new int[width * height];
        _tileMap.AsSpan().Fill(TileIdEmpty);

        // Create tile map buffer
        _tileMapBuffer = rendering.CreateGraphicsBuffer((uint)(width * height * sizeof(int)), "tile_map");
        ReadOnlySpan<int> span = _tileMap;
        _tileMapBuffer.UpdateBuffer(span);

        // Initialize batches
        _batches = new TileBatch[_batchCountX * _batchCountY];
        for (int batchY = 0; batchY < _batchCountY; batchY++)
        {
            for (int batchX = 0; batchX < _batchCountX; batchX++)
            {
                int batchIndex = batchY * _batchCountX + batchX;
                int startX = batchX * batchSizeX;
                int startY = batchY * batchSizeY;
                int actualBatchWidth = Math.Min(batchSizeX, width - startX);
                int actualBatchHeight = Math.Min(batchSizeY, height - startY);

                _batches[batchIndex] = new TileBatch(tileSet, rendering, context,
                    startX, startY, actualBatchWidth, actualBatchHeight, width, height, _tileMapBuffer);
            }
        }

        _updateTask = new BatchUpdateTask();
        Transform = Transform3D.Identity;
    }

    /// <summary>
    /// Forces an update of all batches in the renderer.
    /// </summary>
    public void ForceUpdateBuffer()
    {
        // Mark all batches as dirty
        SetAllBatchesDirty();

        // Update tile map buffer
        ReadOnlySpan<int> span = _tileMap;
        _tileMapBuffer.UpdateBuffer(span);

        // Update all batches
        TryUpdateDirtyBatches();
    }

    /// <summary>
    /// Marks all batches as dirty, forcing them to re-record render commands on the next render.
    /// </summary>
    public void SetAllBatchesDirty()
    {
        for (int i = 0; i < _batches.Length; i++)
        {
            _batches[i].SetDirty();
        }
    }

    /// <summary>
    /// Marks the batch containing the specified tile position as dirty.
    /// </summary>
    /// <param name="tileX">The X coordinate of the tile.</param>
    /// <param name="tileY">The Y coordinate of the tile.</param>
    public void SetBatchDirtyByTilePosition(int tileX, int tileY)
    {
        if (!IsInBounds(tileX, tileY)) return;

        int batchX = tileX / _batchSizeX;
        int batchY = tileY / _batchSizeY;
        int batchIndex = batchY * _batchCountX + batchX;

        if (batchIndex >= 0 && batchIndex < _batches.Length)
        {
            _batches[batchIndex].SetDirty();
        }
    }

    /// <summary>
    /// Updates all dirty batches.
    /// </summary>
    private bool TryUpdateDirtyBatches()
    {
        _updateTask.Batches.Clear();
        _updateTask.TileMap = _tileMap;

        // Collect dirty batches
        int dirtyCount = 0;
        for (int i = 0; i < _batches.Length; i++)
        {
            if (_batches[i].IsDirty)
            {
                _updateTask.Batches.Add(_batches[i]);
                dirtyCount++;
            }
        }

        // Update batches
        if (dirtyCount == 1)
        {
            // Single batch update
            _updateTask.Batches[0].UpdateBuffer(_tileMap.AsSpan());
        }
        else if (dirtyCount > 1)
        {
            // Parallel batch update
            _updateTask.RunParallel(dirtyCount, 1);
        }

        return dirtyCount > 0;
    }

    /// <summary>
    /// Sets a tile at the specified position.
    /// </summary>
    /// <param name="x">The X coordinate of the tile.</param>
    /// <param name="y">The Y coordinate of the tile.</param>
    /// <param name="tileId">The tile ID to set.</param>
    public void SetTile(int x, int y, int tileId)
    {
        if (!IsInBounds(x, y))
        {
            return;
        }

        _tileMap[y * _width + x] = tileId;
        SetBatchDirtyByTilePosition(x, y);
    }

    /// <summary>
    /// Sets tiles in a rectangular region.
    /// </summary>
    /// <param name="from">The starting position (inclusive).</param>
    /// <param name="to">The ending position (inclusive).</param>
    /// <param name="tileId">The tile ID to set.</param>
    public void SetTile(int2 from, int2 to, int tileId)
    {
        int2 size = new int2(_width, _height);

        from = math.clamp(from, new int2(0, 0), size - new int2(1, 1));
        to = math.clamp(to, new int2(0, 0), size - new int2(1, 1));

        // Calculate affected batches
        HashSet<int> affectedBatches = new HashSet<int>();

        for (int i = from.Y; i <= to.Y; i++)
        {
            for (int j = from.X; j <= to.X; j++)
            {
                _tileMap[i * _width + j] = tileId;

                // Calculate which batch this tile belongs to
                int batchX = j / _batchSizeX;
                int batchY = i / _batchSizeY;
                int batchIndex = batchY * _batchCountX + batchX;
                affectedBatches.Add(batchIndex);
            }
        }

        // Mark affected batches as dirty
        foreach (int batchIndex in affectedBatches)
        {
            if (batchIndex >= 0 && batchIndex < _batches.Length)
            {
                _batches[batchIndex].SetDirty();
            }
        }
    }

    /// <summary>
    /// Sets all tiles to the specified tile ID.
    /// </summary>
    /// <param name="tileId">The tile ID to set.</param>
    public void SetAllTiles(int tileId)
    {
        _tileMap.AsSpan().Fill(tileId);
        SetAllBatchesDirty();
    }

    public bool TryGetTile(int x, int y, out int tileId)
    {
        if (!IsInBounds(x, y))
        {
            tileId = TileIdEmpty;
            return false;
        }

        tileId = _tileMap[y * _width + x];
        return tileId >= 0 && tileId < _tileSet.Count;
    }

    /// <summary>
    /// Clears a tile at the specified position.
    /// </summary>
    /// <param name="x">The X coordinate of the tile.</param>
    /// <param name="y">The Y coordinate of the tile.</param>
    public void ClearTile(int x, int y)
    {
        if (!IsInBounds(x, y))
        {
            return;
        }

        _tileMap[y * _width + x] = TileIdEmpty;
        SetBatchDirtyByTilePosition(x, y);
    }

    /// <summary>
    /// Clears all tiles in the map.
    /// </summary>
    public void ClearAllTiles()
    {
        _tileMap.AsSpan().Fill(TileIdEmpty);
        SetAllBatchesDirty();
    }

    /// <summary>
    /// Renders all batches. Only updates dirty batches for better performance.
    /// Uses viewport culling to only render visible batches.
    /// </summary>
    public void Render()
    {
        if (TryUpdateDirtyBatches())
        {
            ReadOnlySpan<int> span = _tileMap;
            _tileMapBuffer.UpdateBuffer(span);
        }

        // Render only visible batches
        Transform3D transform = Transform;
        Constant constant = new(transform.Matrix, new int2(_width, _height));

        for (int i = 0; i < _batches.Length; i++)
        {
            if (IsBatchInViewport(i))
            {
                _batches[i].Render(constant);
            }
        }
    }



    public Span<int> AsSpan()
    {
        return _tileMap.AsSpan();
    }

    /// <summary>
    /// Sets the viewport for culling. Only batches within the viewport will be rendered.
    /// </summary>
    /// <param name="from">The top-left position of the viewport (inclusive).</param>
    /// <param name="to">The bottom-right position of the viewport (inclusive).</param>
    public void SetViewport(int2 from, int2 to)
    {
        from = math.clamp(from, new int2(0, 0), Size - new int2(1, 1));
        to = math.clamp(to, new int2(0, 0), Size - new int2(1, 1));

        _viewportFrom = from;
        _viewportTo = to;
        _hasViewport = true;
    }

    /// <summary>
    /// Clears the viewport, causing all batches to be rendered.
    /// </summary>
    public void ClearViewport()
    {
        _hasViewport = false;
    }

    /// <summary>
    /// Gets the current viewport bounds. Returns default values if no viewport is set.
    /// </summary>
    /// <param name="from">The top-left position of the viewport (inclusive).</param>
    /// <param name="to">The bottom-right position of the viewport (inclusive).</param>
    public void GetViewport(out int2 from, out int2 to)
    {
        if (_hasViewport)
        {
            from = _viewportFrom;
            to = _viewportTo;
        }
        else
        {
            from = int2.Zero;
            to = Size - int2.One;
        }
    }

    /// <summary>
    /// Checks if a batch is within the current viewport.
    /// </summary>
    /// <param name="batchIndex">The index of the batch to check.</param>
    /// <returns>True if the batch intersects with the viewport, false otherwise.</returns>
    private bool IsBatchInViewport(int batchIndex)
    {
        // If no viewport is set, all batches are considered visible
        if (!_hasViewport)
            return true;

        // Calculate batch coordinates from index
        int batchX = batchIndex % _batchCountX;
        int batchY = batchIndex / _batchCountX;

        // Calculate batch world coordinates
        int batchStartX = batchX * _batchSizeX;
        int batchStartY = batchY * _batchSizeY;
        int batchEndX = Math.Min(batchStartX + _batchSizeX - 1, _width - 1);
        int batchEndY = Math.Min(batchStartY + _batchSizeY - 1, _height - 1);

        // Check if batch intersects with viewport
        bool intersects = !(batchEndX < _viewportFrom.X ||
                           batchStartX > _viewportTo.X ||
                           batchEndY < _viewportFrom.Y ||
                           batchStartY > _viewportTo.Y);

        return intersects;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsInBounds(int x, int y)
    {
        return x >= 0 && x < _width && y >= 0 && y < _height;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var batch in _batches)
            {
                batch.Dispose();
            }
            _tileMapBuffer?.Dispose();
        }
    }
}