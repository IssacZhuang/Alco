
using System.Collections.Frozen;
using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

public sealed class TileItem
{
    public string Name { get; }
    public GraphicsMaterial Material { get; }
    public float RenderOrder { get; }
    public object? UserData { get; }
    
    public Vector4 Color { get; set; } = Vector4.One;
    public float BlendFactor { get; set; } = 0.2f;
    public float Tiling { get; set; } = 1.0f;

    public TileItem(string name, GraphicsMaterial material, float renderOrder, object? userData)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(material);
        Name = name;
        Material = material;
        UserData = userData;
        RenderOrder = renderOrder;
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
        //Array.Sort(_items, static (a, b) => a.RenderOrder.CompareTo(b.RenderOrder));

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
        public float Tiling;

        public Constant(Matrix4x4 model, int2 size)
        {
            Model = model;
            Size = size;
            CurrentTileId = 0;
            BlendFactor = 0.2f;
            Tiling = 1.0f;
            Color = Vector4.One;
        }
    }

    /// <summary>The compute dispatch constant of the GPU-driven culling; mirrors
    /// <c>CullConstant</c> in TileGpuCull.slang.</summary>
    private struct CullConstant
    {
        public Vector4 Viewport; // xy = min corner, zw = max corner (tile space)
        public int2 Size;
        public uint TileCount;
        public uint Pad0;
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

        // Precomputed batch bounds in tile coordinates
        private readonly RectInt _bounds;

        public bool IsDirty => _isDirty;

        /// <summary>
        /// Gets the bounds of this batch in tile coordinates.
        /// </summary>
        public RectInt Bounds => _bounds;

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

            // Precompute batch bounds in tile coordinates
            // batchX and batchY are already tile coordinates (startX, startY)
            int actualWidth = Math.Min(batchWidth, mapWidth - batchX);
            int actualHeight = Math.Min(batchHeight, mapHeight - batchY);

            _bounds = new RectInt(batchX, batchY, actualWidth, actualHeight);

            _renderers = new Renderer[tileSet.Count];
            for (int i = 0; i < tileSet.Count; i++)
            {
                GraphicsMaterial material = tileSet.GetItem(i).Material.CreateInstance();
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
                constant.Tiling = item.Tiling;

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
    private sealed class BatchUpdateTask : ReusableBatchTask
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

    // GPU-driven mode state; null/empty in the default CPU batched mode.
    private readonly bool _gpuDriven;
    private GraphicsMaterial[]? _gpuMaterials;
    private ComputeMaterialInstance? _cullMaterial;
    private GraphicsBuffer? _groupBaseBuffer;
    private GraphicsBuffer? _recordsBuffer;
    private GraphicsBuffer? _visibleBuffer;
    private uint[]? _groupCounts;
    private uint[]? _groupBase;
    private IndexedIndirectData[]? _recordStaging;
    private uint _instancesResourceId;
    private bool _instancesResourceIdResolved;
    private bool _dataDirty = true;
    private int _uploadFirstRow = -1;
    private int _uploadLastRow = -1;

    // Viewport fields for culling
    private RectInt _viewport;
    private bool _hasViewport;

    /// <summary>
    /// The global transform of the tile renderer.
    /// </summary>
    public Transform3D Transform;

    public string Name { get; }

    /// <summary>
    /// Gets whether the renderer runs the GPU-driven path (compute culling plus indirect
    /// draws) instead of the CPU batched path.
    /// </summary>
    public bool GpuDriven => _gpuDriven;

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
    /// <param name="cullShader">The tile culling compute shader (TileGpuCull); when non-null the
    /// renderer runs the GPU-driven path — visibility is resolved per frame by
    /// <see cref="RecordCull"/> and <see cref="Render"/> records one indirect draw per
    /// non-empty tile id instead of drawing per visible batch.</param>
    internal TileRenderer(RenderingSystem rendering, IRenderContext context, TileSet tileSet, int width, int height, int batchSizeX, int batchSizeY, string name = "tile_renderer", Shader? cullShader = null)
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

        if (cullShader != null)
        {
            _gpuDriven = true;
            _batches = Array.Empty<TileBatch>();
            InitGpuResources(cullShader);
        }
        else
        {
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
        }

        _updateTask = new BatchUpdateTask();
        Transform = Transform3D.Identity;
    }

    /// <summary>
    /// Forces an update of all batches in the renderer.
    /// </summary>
    public void ForceUpdateBuffer()
    {
        if (_gpuDriven)
        {
            // The GPU path uploads through RecordCull; mark everything for re-upload.
            MarkRowsDirty(0, _height - 1);
            return;
        }

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
        if (_gpuDriven)
        {
            MarkRowsDirty(0, _height - 1);
            return;
        }

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

        if (_gpuDriven)
        {
            MarkRowsDirty(tileY, tileY);
            return;
        }

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

        if (_gpuDriven)
        {
            MarkRowsDirty(y, y);
            return;
        }

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

        for (int i = from.Y; i <= to.Y; i++)
        {
            for (int j = from.X; j <= to.X; j++)
            {
                _tileMap[i * _width + j] = tileId;
            }
        }

        if (_gpuDriven)
        {
            // Row-major storage: the rectangle collapses into one contiguous row span.
            MarkRowsDirty(from.Y, to.Y);
            return;
        }

        // Calculate affected batches
        HashSet<int> affectedBatches = new HashSet<int>();

        for (int i = from.Y; i <= to.Y; i++)
        {
            for (int j = from.X; j <= to.X; j++)
            {
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

        if (_gpuDriven)
        {
            MarkRowsDirty(0, _height - 1);
            return;
        }

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

        if (_gpuDriven)
        {
            MarkRowsDirty(y, y);
            return;
        }

        SetBatchDirtyByTilePosition(x, y);
    }

    /// <summary>
    /// Clears all tiles in the map.
    /// </summary>
    public void ClearAllTiles()
    {
        _tileMap.AsSpan().Fill(TileIdEmpty);

        if (_gpuDriven)
        {
            MarkRowsDirty(0, _height - 1);
            return;
        }

        SetAllBatchesDirty();
    }

    /// <summary>
    /// Renders all batches. Only updates dirty batches for better performance.
    /// Uses viewport culling to only render visible batches.
    /// The GPU-driven path instead records one indirect draw per non-empty tile id; visibility
    /// was already resolved by this frame's <see cref="RecordCull"/>.
    /// </summary>
    public void Render()
    {
        if (_gpuDriven)
        {
            RenderGpu();
            return;
        }

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

    /// <summary>
    /// Creates the GPU-driven mode's buffers, per-tile-id materials and the culling compute
    /// material. The per-tile-id materials bind the tile map and the visible-instance buffer so
    /// the recorded indirect draws fetch everything the tile pass needs.
    /// </summary>
    private void InitGpuResources(Shader cullShader)
    {
        int tileCount = _tileSet.Count;
        _gpuMaterials = new GraphicsMaterial[tileCount];
        _groupCounts = new uint[tileCount];
        _groupBase = new uint[tileCount];
        _recordStaging = new IndexedIndirectData[tileCount];

        GraphicsBuffer visibleBuffer = _rendering.CreateGraphicsBuffer((uint)(_width * _height * Unsafe.SizeOf<TileInstanceData>()), $"{Name}_visible_tiles");
        GraphicsBuffer groupBaseBuffer = _rendering.CreateGraphicsBuffer((uint)(tileCount * sizeof(uint)), $"{Name}_group_base");
        GraphicsBuffer recordsBuffer = _rendering.CreateGraphicsBuffer((uint)(tileCount * Unsafe.SizeOf<IndexedIndirectData>()), $"{Name}_draw_records");
        _visibleBuffer = visibleBuffer;
        _groupBaseBuffer = groupBaseBuffer;
        _recordsBuffer = recordsBuffer;

        for (int i = 0; i < tileCount; i++)
        {
            GraphicsMaterial material = _tileSet.GetItem(i).Material.CreateInstance();
            material.TrySetBuffer(ShaderResourceId.TileMap, _tileMapBuffer);
            if (!_instancesResourceIdResolved)
            {
                // Every material derives from the tile pass family, so the first one resolves
                // the shared "instances" resource id for them all.
                _instancesResourceId = material.GetResourceId(ShaderResourceId.Instances);
                _instancesResourceIdResolved = true;
            }
            material.SetBuffer(_instancesResourceId, visibleBuffer);
            _gpuMaterials[i] = material;
        }

        ComputeMaterialInstance cullMaterial = _rendering.CreateComputeMaterial(cullShader).CreateInstance();
        cullMaterial.SetBuffer("tileMap", _tileMapBuffer);
        cullMaterial.SetBuffer("groupBase", groupBaseBuffer);
        cullMaterial.SetBuffer("records", recordsBuffer);
        cullMaterial.SetBuffer("visible", visibleBuffer);
        _cullMaterial = cullMaterial;
    }

    /// <summary>
    /// GPU-driven path only: uploads changed tile rows, refreshes the per-tile-id group tables
    /// when tile data changed, resets the indirect draw records and dispatches the culling
    /// compute for the frame. Call once per frame on the render thread before the pass
    /// <see cref="Render"/> records in.
    /// </summary>
    /// <param name="commandBuffer">The frame command buffer, with no pass open.</param>
    /// <param name="viewport">The camera's culling viewport in world space.</param>
    /// <exception cref="InvalidOperationException">The renderer runs the CPU batched path.</exception>
    public void RecordCull(GPUCommandBuffer commandBuffer, RectInt viewport)
    {
        if (!_gpuDriven)
        {
            throw new InvalidOperationException("RecordCull is only available in the GPU-driven mode; construct the renderer with a culling shader.");
        }

        if (_tileSet.Count == 0)
        {
            return;
        }

        if (_uploadFirstRow >= 0)
        {
            int rowCount = _uploadLastRow - _uploadFirstRow + 1;
            _tileMapBuffer.UpdateBuffer(
                _tileMap.AsSpan(_uploadFirstRow * _width, rowCount * _width),
                (uint)(_uploadFirstRow * _width * sizeof(int)));
            _uploadFirstRow = -1;
            _uploadLastRow = -1;
        }

        if (_dataDirty)
        {
            RecountGroups();
        }

        // Reset every record's instance count to zero (the compute atomically counts them up
        // again); the queue write is ordered ahead of the dispatch.
        _recordsBuffer!.UpdateBuffer(_recordStaging!);

        var constant = new CullConstant
        {
            Viewport = TransformViewportToTileSpace(viewport),
            Size = new int2(_width, _height),
            TileCount = (uint)_tileSet.Count,
            Pad0 = 0,
        };
        using (GPUCommandBuffer.ComputePass computePass = commandBuffer.BeginCompute())
        {
            _cullMaterial!.DispatchByGroupWithConstant(
                computePass, (uint)((_width * _height + 63) / 64), 1, 1, constant);
        }
    }

    /// <summary>
    /// Records the indirect draws of the GPU-driven path: one per tile id present in the map,
    /// with the visible instance count filled in by this frame's culling compute. The draw count
    /// is independent of the viewport's batch coverage.
    /// </summary>
    private void RenderGpu()
    {
        uint[] counts = _groupCounts!;
        GraphicsMaterial[] materials = _gpuMaterials!;
        Transform3D transform = Transform;
        Constant constant = new(transform.Matrix, new int2(_width, _height));

        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 0)
            {
                continue;
            }

            TileItem item = _tileSet.GetItem(i);
            constant.CurrentTileId = i;
            constant.BlendFactor = item.BlendFactor;
            constant.Color = item.Color;
            constant.Tiling = item.Tiling;

            _context.DrawIndexedIndirect(
                _rendering.MeshCenteredSprite,
                materials[i],
                _recordsBuffer!,
                (uint)(i * Unsafe.SizeOf<IndexedIndirectData>()),
                constant);
        }
    }

    /// <summary>
    /// Recomputes the per-tile-id instance counts and visible-buffer segment bases and re-stages
    /// the indirect draw records. GPU-driven path only; called when tile data changed.
    /// </summary>
    private void RecountGroups()
    {
        uint[] counts = _groupCounts!;
        Array.Clear(counts);
        ReadOnlySpan<int> tiles = _tileMap;
        for (int i = 0; i < tiles.Length; i++)
        {
            int tileId = tiles[i];
            if (tileId >= 0 && tileId < counts.Length)
            {
                counts[tileId]++;
            }
        }

        uint[] groupBase = _groupBase!;
        uint segmentBase = 0;
        for (int i = 0; i < groupBase.Length; i++)
        {
            groupBase[i] = segmentBase;
            segmentBase += counts[i];
        }

        uint indexCount = _rendering.MeshCenteredSprite.GetSubMesh(0).IndexCount;
        IndexedIndirectData[] staging = _recordStaging!;
        for (int i = 0; i < staging.Length; i++)
        {
            staging[i] = new IndexedIndirectData(indexCount, 0, 0, 0, groupBase[i]);
        }

        _groupBaseBuffer!.UpdateBuffer(groupBase);
        _dataDirty = false;
    }

    /// <summary>
    /// Marks the row range holding changed tiles for upload, growing to the union with any
    /// pending range. Row-major storage turns the range into one contiguous buffer write.
    /// </summary>
    private void MarkRowsDirty(int firstRow, int lastRow)
    {
        _dataDirty = true;

        if (_uploadFirstRow < 0)
        {
            _uploadFirstRow = firstRow;
            _uploadLastRow = lastRow;
            return;
        }

        _uploadFirstRow = Math.Min(_uploadFirstRow, firstRow);
        _uploadLastRow = Math.Max(_uploadLastRow, lastRow);
    }

    /// <summary>
    /// Transforms a world-space viewport rectangle into tile space through the inverse model
    /// matrix and inflates it by the culling margin. An identity transform (terrain, floors)
    /// keeps the original rectangle.
    /// </summary>
    private Vector4 TransformViewportToTileSpace(RectInt viewport)
    {
        Vector2 min = new(viewport.Origin.X, viewport.Origin.Y);
        Vector2 max = new(viewport.Max.X, viewport.Max.Y);

        Matrix4x4 model = Transform.Matrix;
        if (model != Matrix4x4.Identity && Matrix4x4.Invert(model, out Matrix4x4 inverse))
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                Vector2 corner = i switch
                {
                    0 => new Vector2(viewport.Origin.X, viewport.Origin.Y),
                    1 => new Vector2(viewport.Max.X, viewport.Origin.Y),
                    2 => new Vector2(viewport.Max.X, viewport.Max.Y),
                    _ => new Vector2(viewport.Origin.X, viewport.Max.Y),
                };
                Vector4 tile = Vector4.Transform(new Vector4(corner, 0f, 1f), inverse);
                min = Vector2.Min(min, new Vector2(tile.X, tile.Y));
                max = Vector2.Max(max, new Vector2(tile.X, tile.Y));
            }
        }

        const float Margin = 2f;
        return new Vector4(min.X - Margin, min.Y - Margin, max.X + Margin, max.Y + Margin);
    }



    public Span<int> AsSpan()
    {
        return _tileMap.AsSpan();
    }

    /// <summary>
    /// Sets the viewport for culling. Only batches within the viewport will be rendered.
    /// </summary>
    /// <param name="viewport">The viewport rectangle in tile coordinates.</param>
    public void SetViewport(RectInt viewport)
    {
        // Clamp viewport to valid tile map bounds
        int2 clampedOrigin = math.clamp(viewport.Origin, int2.Zero, Size - int2.One);
        int2 clampedMax = math.clamp(viewport.Max, int2.Zero, Size);
        int2 clampedSize = clampedMax - clampedOrigin;

        _viewport = new RectInt(clampedOrigin, clampedSize);
        _hasViewport = true;
    }

    /// <summary>
    /// Gets the current viewport bounds. Returns default values if no viewport is set.
    /// </summary>
    /// <returns>The current viewport rectangle.</returns>
    public RectInt GetViewport()
    {
        if (_hasViewport)
        {
            return _viewport;
        }
        else
        {
            return new RectInt(int2.Zero, Size);
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

        // Get precomputed batch bounds and use RectInt.Intersects
        TileBatch batch = _batches[batchIndex];
        return batch.Bounds.Intersects(_viewport);
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
            if (_gpuDriven)
            {
                _cullMaterial?.Dispose();
                _groupBaseBuffer?.Dispose();
                _recordsBuffer?.Dispose();
                _visibleBuffer?.Dispose();
            }
            else
            {
                foreach (var batch in _batches)
                {
                    batch.Dispose();
                }
            }
            _tileMapBuffer?.Dispose();
        }
    }
}