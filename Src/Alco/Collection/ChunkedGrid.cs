using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco;

/// <summary>
/// A sparse 2D grid of cells organized into lazily allocated square chunks, addressing the
/// full 32-bit integer cell domain (including negative coordinates) with no fixed size.
/// Chunk coordinates are plain <see cref="int2"/> values.
/// </summary>
/// <remarks>
/// <para>
/// Storage is a <see cref="Dictionary{TKey, TValue}"/> of chunk cell arrays keyed by chunk
/// coordinate. The chunk edge length is configurable per instance but must be a power of
/// two, so cell-to-chunk floor division and in-chunk indexing compile to a shift and a mask
/// and handle negative coordinates without correction steps.
/// </para>
/// <para>
/// A chunk is allocated only when a value different from <see cref="DefaultValue"/> is
/// written; reading any cell of a missing chunk returns <see cref="DefaultValue"/> (uniform
/// page). Newly allocated chunks are pre-filled with <see cref="DefaultValue"/> so unwritten
/// cells inside an allocated chunk read consistently.
/// </para>
/// <para>
/// Chunks are never reclaimed automatically: writing default values into an existing chunk
/// keeps it allocated. Bulk readers and writers operate on whole chunk arrays via
/// <see cref="TryGetChunk"/> / <see cref="GetOrCreateChunk"/> instead of per-cell dictionary
/// lookups. Chunk arrays are row-major with linear index localY*<see cref="ChunkSize"/> +
/// localX (see <see cref="GetLocalIndex"/>).
/// </para>
/// <para>
/// The grid is not thread-safe.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of the cell values.</typeparam>
public sealed class ChunkedGrid<T>
{
    /// <summary>
    /// The chunk edge length used when none is specified in the constructor.
    /// </summary>
    public const int DefaultChunkSize = 64;

    private readonly Dictionary<int2, T[]> _chunks = [];
    private readonly T _defaultValue;
    private readonly int _chunkSizeLog2;
    private readonly int _localCellMask;

    /// <summary>
    /// The value returned for cells in unallocated chunks.
    /// </summary>
    public T DefaultValue => _defaultValue;

    /// <summary>
    /// The chunk edge length in cells.
    /// </summary>
    public int ChunkSize { get; }

    /// <summary>
    /// The number of cells in one chunk (<see cref="ChunkSize"/> squared).
    /// </summary>
    public int ChunkCells => ChunkSize * ChunkSize;

    /// <summary>
    /// The number of allocated chunks.
    /// </summary>
    public int ChunkCount => _chunks.Count;

    /// <summary>
    /// The allocated chunks keyed by chunk coordinate. The returned arrays are the live chunk
    /// storage and may be mutated for bulk operations; do not reassign dictionary entries.
    /// </summary>
    public IReadOnlyDictionary<int2, T[]> Chunks => _chunks;

    /// <summary>
    /// Creates a chunked grid whose unallocated chunks read as the given default value.
    /// </summary>
    /// <param name="defaultValue">The value of cells in unallocated chunks.</param>
    /// <param name="chunkSize">The chunk edge length in cells; must be a positive power of two.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="chunkSize"/> is not a positive power of two.
    /// </exception>
    public ChunkedGrid(T defaultValue = default!, int chunkSize = DefaultChunkSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        if ((chunkSize & (chunkSize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), chunkSize, "Chunk size must be a power of two.");
        }

        _defaultValue = defaultValue;
        ChunkSize = chunkSize;
        _chunkSizeLog2 = BitOperations.Log2((uint)chunkSize);
        _localCellMask = chunkSize - 1;
    }

    /// <summary>
    /// Accesses the cell at the specified world coordinates.
    /// Reading a cell in an unallocated chunk returns <see cref="DefaultValue"/>. Writing a
    /// non-default value to an unallocated chunk allocates it; writing the default value to an
    /// unallocated chunk is a no-op that keeps it unallocated.
    /// </summary>
    /// <param name="x">The world cell x coordinate.</param>
    /// <param name="y">The world cell y coordinate.</param>
    /// <returns>The value of the cell.</returns>
    public T this[int x, int y]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_chunks.TryGetValue(GetChunkCoord(x, y), out T[]? cells))
            {
                return cells[GetLocalIndex(x, y)];
            }
            return _defaultValue;
        }
        set
        {
            int2 chunkCoord = GetChunkCoord(x, y);
            if (_chunks.TryGetValue(chunkCoord, out T[]? cells))
            {
                cells[GetLocalIndex(x, y)] = value;
                return;
            }
            if (EqualityComparer<T>.Default.Equals(value, _defaultValue))
            {
                return;
            }
            cells = CreateChunk();
            cells[GetLocalIndex(x, y)] = value;
            _chunks.Add(chunkCoord, cells);
        }
    }

    /// <summary>
    /// Sets the cell at the specified world coordinates. Equivalent to the indexer setter.
    /// </summary>
    /// <param name="x">The world cell x coordinate.</param>
    /// <param name="y">The world cell y coordinate.</param>
    /// <param name="value">The value to store.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(int x, int y, T value)
    {
        this[x, y] = value;
    }

    /// <summary>
    /// Converts a world cell coordinate to its chunk coordinate.
    /// </summary>
    /// <param name="cellX">The world cell x coordinate.</param>
    /// <param name="cellY">The world cell y coordinate.</param>
    /// <returns>The chunk coordinate containing the given cell.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int2 GetChunkCoord(int cellX, int cellY)
    {
        return new int2(cellX >> _chunkSizeLog2, cellY >> _chunkSizeLog2);
    }

    /// <summary>
    /// Extracts the in-chunk cell coordinate (0..<see cref="ChunkSize"/>) from a world cell
    /// coordinate along one axis.
    /// </summary>
    /// <param name="cell">The world cell coordinate along one axis.</param>
    /// <returns>The local cell coordinate, always non-negative.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetLocalCell(int cell)
    {
        return cell & _localCellMask;
    }

    /// <summary>
    /// Computes the row-major linear index of a world cell inside its chunk
    /// (localY*<see cref="ChunkSize"/> + localX).
    /// </summary>
    /// <param name="cellX">The world cell x coordinate.</param>
    /// <param name="cellY">The world cell y coordinate.</param>
    /// <returns>The linear index into a chunk cell array, in [0, <see cref="ChunkCells"/>).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetLocalIndex(int cellX, int cellY)
    {
        return ((cellY & _localCellMask) << _chunkSizeLog2) | (cellX & _localCellMask);
    }

    /// <summary>
    /// Gets the world-space origin (minimum corner cell) of a chunk.
    /// </summary>
    /// <param name="chunkCoord">The chunk coordinate.</param>
    /// <returns>The world cell coordinate of the chunk's minimum corner.</returns>
    public int2 GetChunkOrigin(int2 chunkCoord)
    {
        return new int2(chunkCoord.X * ChunkSize, chunkCoord.Y * ChunkSize);
    }

    /// <summary>
    /// Attempts to get the cell array of an allocated chunk.
    /// </summary>
    /// <param name="chunkCoord">The chunk coordinate.</param>
    /// <param name="cells">The chunk cell array, row-major with linear index localY*<see cref="ChunkSize"/> + localX.</param>
    /// <returns>True if the chunk is allocated; false otherwise, with a null array.</returns>
    public bool TryGetChunk(int2 chunkCoord, out T[]? cells)
    {
        return _chunks.TryGetValue(chunkCoord, out cells);
    }

    /// <summary>
    /// Determines whether the given chunk is allocated.
    /// </summary>
    /// <param name="chunkCoord">The chunk coordinate.</param>
    /// <returns>True if the chunk is allocated; false otherwise.</returns>
    public bool HasChunk(int2 chunkCoord)
    {
        return _chunks.ContainsKey(chunkCoord);
    }

    /// <summary>
    /// Gets the cell array of a chunk, allocating it if necessary. A newly allocated chunk is
    /// pre-filled with <see cref="DefaultValue"/>. Use this for bulk writes that should bypass
    /// the per-cell lazy allocation check.
    /// </summary>
    /// <param name="chunkCoord">The chunk coordinate.</param>
    /// <returns>The chunk cell array, row-major with linear index localY*<see cref="ChunkSize"/> + localX.</returns>
    public T[] GetOrCreateChunk(int2 chunkCoord)
    {
        if (_chunks.TryGetValue(chunkCoord, out T[]? cells))
        {
            return cells;
        }
        cells = CreateChunk();
        _chunks.Add(chunkCoord, cells);
        return cells;
    }

    /// <summary>
    /// Removes all chunks. Every cell reads as <see cref="DefaultValue"/> afterwards.
    /// </summary>
    public void Clear()
    {
        _chunks.Clear();
    }

    private T[] CreateChunk()
    {
        T[] cells = new T[ChunkCells];
        if (!EqualityComparer<T>.Default.Equals(_defaultValue, default!))
        {
            cells.AsSpan().Fill(_defaultValue);
        }
        return cells;
    }
}
