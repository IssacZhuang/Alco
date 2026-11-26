using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// Represents a clipped local cell sub-rectangle inside a single chunk.
    /// Local bounds are half-open <c>[LocalMin, LocalMax)</c>.
    /// </summary>
    /// <param name="ChunkCoord">The owning chunk coordinate.</param>
    /// <param name="LocalMinX">Inclusive local start X within the chunk.</param>
    /// <param name="LocalMinY">Inclusive local start Y within the chunk.</param>
    /// <param name="LocalMaxX">Exclusive local end X within the chunk.</param>
    /// <param name="LocalMaxY">Exclusive local end Y within the chunk.</param>
    /// <param name="WorldOriginX">World cell X coordinate of local (0, 0) in this chunk.</param>
    /// <param name="WorldOriginY">World cell Y coordinate of local (0, 0) in this chunk.</param>
    public readonly record struct ChunkCellSlice(
        int2 ChunkCoord,
        int LocalMinX,
        int LocalMinY,
        int LocalMaxX,
        int LocalMaxY,
        int WorldOriginX,
        int WorldOriginY);

    /// <summary>
    /// Allocation-free enumerable iterating every chunk slice intersecting an <see cref="IntBounds"/>.
    /// </summary>
    public readonly struct ChunkSliceEnumerable
    {
        private readonly IntBounds _bounds;
        private readonly int _chunkShift;

        /// <summary>Initializes an enumerable over <paramref name="bounds"/> at the given chunk shift.</summary>
        public ChunkSliceEnumerable(IntBounds bounds, int chunkShift = ChunkUtility.DefaultChunkShift)
        {
            if ((uint)chunkShift > 16u)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkShift), chunkShift, "Chunk shift must be in [0, 16].");
            }

            _bounds = bounds;
            _chunkShift = chunkShift;
        }

        /// <summary>Gets a value-type enumerator over intersecting chunk slices.</summary>
        public ChunkSliceEnumerator GetEnumerator() => new(_bounds, _chunkShift);
    }

    /// <summary>
    /// Value-type enumerator producing row-major <see cref="ChunkCellSlice"/> entries for an <see cref="IntBounds"/>.
    /// </summary>
    public struct ChunkSliceEnumerator
    {
        private readonly IntBounds _bounds;
        private readonly int _chunkShift;
        private readonly int _chunkSize;
        private readonly int _chunkMask;
        private readonly int _minCx;
        private readonly int _maxCx;
        private readonly int _maxCy;
        private int _cx;
        private int _cy;
        private bool _done;
        private ChunkCellSlice _current;

        /// <summary>Initializes an enumerator over <paramref name="bounds"/> at the given chunk shift.</summary>
        /// <param name="bounds">The half-open integer cell bounds to slice.</param>
        /// <param name="chunkShift">Log2 of the chunk edge length.</param>
        public ChunkSliceEnumerator(IntBounds bounds, int chunkShift = ChunkUtility.DefaultChunkShift)
        {
            if ((uint)chunkShift > 16u)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkShift), chunkShift, "Chunk shift must be in [0, 16].");
            }

            _bounds = bounds;
            _chunkShift = chunkShift;
            _chunkSize = 1 << chunkShift;
            _chunkMask = _chunkSize - 1;

            if (bounds.IsEmpty)
            {
                _minCx = 0;
                _maxCx = -1;
                _maxCy = -1;
                _cx = 0;
                _cy = 0;
                _done = true;
                _current = default;
                return;
            }

            _minCx = bounds.MinX >> chunkShift;
            _maxCx = (bounds.MaxX - 1) >> chunkShift;
            int minCy = bounds.MinY >> chunkShift;
            _maxCy = (bounds.MaxY - 1) >> chunkShift;
            _cx = _minCx;
            _cy = minCy;
            _done = false;
            _current = default;
        }

        /// <summary>Gets the current clipped chunk slice.</summary>
        public readonly ChunkCellSlice Current => _current;

        /// <summary>Advances to the next intersecting chunk slice.</summary>
        public bool MoveNext()
        {
            if (_done)
            {
                return false;
            }

            int localMinX = _cx == _minCx ? (_bounds.MinX & _chunkMask) : 0;
            int localMaxX = _cx == _maxCx ? (((_bounds.MaxX - 1) & _chunkMask) + 1) : _chunkSize;
            int minCy = _bounds.MinY >> _chunkShift;
            int localMinY = _cy == minCy ? (_bounds.MinY & _chunkMask) : 0;
            int localMaxY = _cy == _maxCy ? (((_bounds.MaxY - 1) & _chunkMask) + 1) : _chunkSize;

            _current = new ChunkCellSlice(
                new int2(_cx, _cy),
                localMinX,
                localMinY,
                localMaxX,
                localMaxY,
                _cx << _chunkShift,
                _cy << _chunkShift);

            if (_cx < _maxCx)
            {
                _cx++;
            }
            else if (_cy < _maxCy)
            {
                _cx = _minCx;
                _cy++;
            }
            else
            {
                _done = true;
            }

            return true;
        }
    }

    /// <summary>
    /// Signed chunk coordinate math, translation-invariant cell snapping, and zero-allocation rectangle slicing helpers.
    /// </summary>
    public static class ChunkUtility
    {
        /// <summary>Default chunk edge length in cells (32).</summary>
        public const int DefaultChunkSize = 32;

        /// <summary>Log2 of <see cref="DefaultChunkSize"/>.</summary>
        public const int DefaultChunkShift = 5;

        /// <summary>Local coordinate mask for <see cref="DefaultChunkSize"/> (<c>DefaultChunkSize - 1</c>).</summary>
        public const int DefaultChunkMask = DefaultChunkSize - 1;

        /// <summary>Total cells in a default 32x32 chunk.</summary>
        public const int DefaultChunkArea = DefaultChunkSize * DefaultChunkSize;

        /// <summary>Converts a signed cell coordinate into its owning chunk coordinate.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CellToChunk(int cell, int chunkShift = DefaultChunkShift) => cell >> chunkShift;

        /// <summary>Converts a signed 2D cell coordinate into its owning chunk coordinate.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 CellToChunk(int2 cell, int chunkShift = DefaultChunkShift)
            => new(cell.X >> chunkShift, cell.Y >> chunkShift);

        /// <summary>Extracts the non-negative local cell coordinate within a chunk along one axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CellToLocal(int cell, int chunkMask = DefaultChunkMask) => cell & chunkMask;

        /// <summary>Computes the row-major linear cell index inside a chunk from local coordinates.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LocalIndex(int localX, int localY, int chunkShift = DefaultChunkShift)
            => (localY << chunkShift) | localX;

        /// <summary>Computes the row-major linear cell index inside a chunk directly from world cell coordinates.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CellToLocalIndex(
            int cellX,
            int cellY,
            int chunkShift = DefaultChunkShift,
            int chunkMask = DefaultChunkMask)
            => ((cellY & chunkMask) << chunkShift) | (cellX & chunkMask);

        /// <summary>Gets the world cell origin (minimum corner) of a chunk.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 ChunkOrigin(int2 chunkCoord, int chunkShift = DefaultChunkShift)
            => new(chunkCoord.X << chunkShift, chunkCoord.Y << chunkShift);

        /// <summary>
        /// Converts a world coordinate on one axis into its signed integer cell coordinate using
        /// translation-invariant rounding (<c>floor(world + 0.5)</c>).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WorldToCell(float world) => (int)MathF.Floor(world + 0.5f);

        /// <summary>
        /// Converts a world position into its signed integer cell coordinate using translation-invariant rounding.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 WorldToCell(Vector2 world) => new(WorldToCell(world.X), WorldToCell(world.Y));

        /// <summary>
        /// Computes the bottom-left integer cell coordinate of a footprint centered at <paramref name="worldCenter"/>
        /// with size <paramref name="size"/> cells, invariant across positive and negative coordinates.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 CalculateBaseCell(Vector2 worldCenter, int2 size)
        {
            float offsetX = (1 - size.X) * 0.5f;
            float offsetY = (1 - size.Y) * 0.5f;
            return new int2(
                (int)MathF.Floor(worldCenter.X + offsetX + 0.5f),
                (int)MathF.Floor(worldCenter.Y + offsetY + 0.5f));
        }

        /// <summary>Enumerates every chunk intersecting <paramref name="cellBounds"/> with clipped local coordinates.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ChunkSliceEnumerable EnumerateSlices(IntBounds cellBounds, int chunkShift = DefaultChunkShift)
            => new(cellBounds, chunkShift);
    }
}
