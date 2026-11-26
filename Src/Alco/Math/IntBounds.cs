using System;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// Axis-aligned integer cell bounds with inclusive minimum and exclusive maximum coordinates.
    /// </summary>
    /// <param name="MinX">Inclusive minimum X cell.</param>
    /// <param name="MinY">Inclusive minimum Y cell.</param>
    /// <param name="MaxX">Exclusive maximum X cell.</param>
    /// <param name="MaxY">Exclusive maximum Y cell.</param>
    public readonly record struct IntBounds(int MinX, int MinY, int MaxX, int MaxY)
    {
        /// <summary>Gets the horizontal cell span (clamped to zero when empty).</summary>
        public int Width => Math.Max(0, MaxX - MinX);

        /// <summary>Gets the vertical cell span (clamped to zero when empty).</summary>
        public int Height => Math.Max(0, MaxY - MinY);

        /// <summary>Gets whether the bounds contain zero cells.</summary>
        public bool IsEmpty => MinX >= MaxX || MinY >= MaxY;

        /// <summary>Returns whether cell (<paramref name="x"/>, <paramref name="y"/>) lies inside these bounds.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int x, int y) => x >= MinX && x < MaxX && y >= MinY && y < MaxY;

        /// <summary>Returns whether <paramref name="cell"/> lies inside these bounds.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int2 cell) => Contains(cell.X, cell.Y);

        /// <summary>Returns whether any cell of the default 32x32 chunk at <paramref name="chunkCoord"/> overlaps these bounds.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IntersectsChunk(int2 chunkCoord) => IntersectsChunk(chunkCoord, ChunkUtility.DefaultChunkShift);

        /// <summary>Returns whether any cell of the chunk at <paramref name="chunkCoord"/> overlaps these bounds.</summary>
        /// <param name="chunkCoord">The chunk coordinate.</param>
        /// <param name="chunkShift">Log2 of the chunk edge length.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IntersectsChunk(int2 chunkCoord, int chunkShift)
        {
            if (IsEmpty)
            {
                return false;
            }

            long chunkSize = 1L << chunkShift;
            long cMinX = (long)chunkCoord.X << chunkShift;
            long cMinY = (long)chunkCoord.Y << chunkShift;
            return cMinX + chunkSize > MinX && cMinX < MaxX && cMinY + chunkSize > MinY && cMinY < MaxY;
        }

        /// <summary>Returns the cell bounds covered by the chunk at <paramref name="chunkCoord"/>.</summary>
        /// <param name="chunkCoord">The chunk coordinate.</param>
        /// <param name="chunkShift">Log2 of the chunk edge length.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntBounds FromChunk(int2 chunkCoord, int chunkShift = ChunkUtility.DefaultChunkShift)
        {
            int chunkSize = 1 << chunkShift;
            int minX = chunkCoord.X << chunkShift;
            int minY = chunkCoord.Y << chunkShift;
            return new IntBounds(minX, minY, minX + chunkSize, minY + chunkSize);
        }

        /// <summary>Converts a <see cref="RectInt"/> into half-open <see cref="IntBounds"/>.</summary>
        /// <param name="rect">The source rectangle.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntBounds FromRectInt(RectInt rect)
        {
            return new IntBounds(rect.Origin.X, rect.Origin.Y, rect.Origin.X + rect.Size.X, rect.Origin.Y + rect.Size.Y);
        }

        /// <summary>Converts these bounds to a <see cref="RectInt"/>, returning <see langword="default"/> when empty.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RectInt ToRectInt()
        {
            if (IsEmpty)
            {
                return default;
            }

            return new RectInt(new int2(MinX, MinY), new int2(MaxX - MinX, MaxY - MinY));
        }

        /// <summary>Returns the inclusive-min, exclusive-max chunk bounds overlapping these cell bounds.</summary>
        /// <param name="chunkShift">Log2 of the chunk edge length.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IntBounds ToChunkBounds(int chunkShift = ChunkUtility.DefaultChunkShift)
        {
            if (IsEmpty)
            {
                return default;
            }

            return new IntBounds(
                MinX >> chunkShift,
                MinY >> chunkShift,
                ((MaxX - 1) >> chunkShift) + 1,
                ((MaxY - 1) >> chunkShift) + 1);
        }

        /// <summary>Returns the intersection of these bounds with <paramref name="other"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IntBounds Intersect(IntBounds other)
        {
            return new IntBounds(
                Math.Max(MinX, other.MinX),
                Math.Max(MinY, other.MinY),
                Math.Min(MaxX, other.MaxX),
                Math.Min(MaxY, other.MaxY));
        }

        /// <summary>Returns the smallest bounds enclosing both this instance and <paramref name="other"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IntBounds Union(IntBounds other)
        {
            if (IsEmpty)
            {
                return other;
            }

            if (other.IsEmpty)
            {
                return this;
            }

            return new IntBounds(
                Math.Min(MinX, other.MinX),
                Math.Min(MinY, other.MinY),
                Math.Max(MaxX, other.MaxX),
                Math.Max(MaxY, other.MaxY));
        }

        /// <summary>Expands all four sides outward by <paramref name="pad"/> cells.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IntBounds Expand(int pad)
        {
            if (IsEmpty)
            {
                return this;
            }

            return new IntBounds(MinX - pad, MinY - pad, MaxX + pad, MaxY + pad);
        }

        /// <summary>Returns whether <paramref name="inner"/> lies completely within these bounds.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsBounds(IntBounds inner)
        {
            if (inner.IsEmpty)
            {
                return true;
            }

            if (IsEmpty)
            {
                return false;
            }

            return inner.MinX >= MinX
                && inner.MinY >= MinY
                && inner.MaxX <= MaxX
                && inner.MaxY <= MaxY;
        }
    }
}
