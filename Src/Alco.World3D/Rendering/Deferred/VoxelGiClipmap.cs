using System.Numerics;

namespace Alco.World3D;

/// <summary>
/// An axis-aligned world or local-space bounding box used by voxel global illumination.
/// </summary>
public readonly struct VoxelGiBounds
{
    /// <summary>Gets the minimum corner.</summary>
    public Vector3 Min { get; }

    /// <summary>Gets the maximum corner.</summary>
    public Vector3 Max { get; }

    /// <summary>Creates an axis-aligned bounding box.</summary>
    /// <param name="min">The minimum corner.</param>
    /// <param name="max">The maximum corner.</param>
    public VoxelGiBounds(in Vector3 min, in Vector3 max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>Transforms the box and returns the axis-aligned bounds of the result.</summary>
    /// <param name="transform">The local-to-world transform.</param>
    /// <returns>The transformed axis-aligned bounds.</returns>
    public VoxelGiBounds Transform(in Matrix4x4 transform)
    {
        Vector3 center = (Min + Max) * 0.5f;
        Vector3 extents = (Max - Min) * 0.5f;
        Vector3 transformedCenter = Vector3.Transform(center, transform);
        Vector3 transformedExtents = new(
            MathF.Abs(transform.M11) * extents.X + MathF.Abs(transform.M21) * extents.Y + MathF.Abs(transform.M31) * extents.Z,
            MathF.Abs(transform.M12) * extents.X + MathF.Abs(transform.M22) * extents.Y + MathF.Abs(transform.M32) * extents.Z,
            MathF.Abs(transform.M13) * extents.X + MathF.Abs(transform.M23) * extents.Y + MathF.Abs(transform.M33) * extents.Z);
        return new VoxelGiBounds(transformedCenter - transformedExtents, transformedCenter + transformedExtents);
    }

    /// <summary>Returns whether this box overlaps another box.</summary>
    /// <param name="other">The other box.</param>
    /// <returns><see langword="true"/> when the boxes overlap.</returns>
    public bool Intersects(in VoxelGiBounds other)
    {
        return Max.X >= other.Min.X && Min.X <= other.Max.X
            && Max.Y >= other.Min.Y && Min.Y <= other.Max.Y
            && Max.Z >= other.Min.Z && Min.Z <= other.Max.Z;
    }

    /// <summary>Returns a box containing this box and another box.</summary>
    /// <param name="other">The other box.</param>
    /// <returns>The union of both boxes.</returns>
    public VoxelGiBounds Union(in VoxelGiBounds other)
    {
        return new VoxelGiBounds(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));
    }
}

internal readonly struct VoxelGiDirtyBrick
{
    public uint X { get; }
    public uint Y { get; }
    public uint Z { get; }
    public uint Padding { get; }

    public VoxelGiDirtyBrick(uint x, uint y, uint z)
    {
        X = x;
        Y = y;
        Z = z;
        Padding = 0;
    }
}

/// <summary>
/// CPU-side state for a toroidal voxel clipmap. Dirty work is tracked in world-space
/// brick coordinates so camera scrolling never loses pending structural edits.
/// </summary>
internal sealed class VoxelGiClipmap
{
    private readonly record struct BrickCoordinate(int X, int Y, int Z);

    private sealed class LevelState
    {
        public BrickCoordinate Origin;
        public int RingOffsetX;
        public int RingOffsetY;
        public int RingOffsetZ;
        public bool Initialized;
        public bool FullResetPending;
        public readonly Queue<BrickCoordinate> HighPriorityQueue = new();
        public readonly Queue<BrickCoordinate> StreamingQueue = new();
        public readonly HashSet<BrickCoordinate> HighPriority = new();
        public readonly HashSet<BrickCoordinate> Streaming = new();
    }

    private readonly LevelState[] _levels;
    private readonly int _resolution;
    private readonly int _brickSize;
    private readonly int _bricksPerAxis;
    private readonly float _baseVoxelSize;

    public int LevelCount => _levels.Length;

    public int BrickSize => _brickSize;

    public int BricksPerAxis => _bricksPerAxis;

    public VoxelGiClipmap(int resolution, int brickSize, float baseVoxelSize, int levelCount)
    {
        if (resolution < brickSize || resolution % brickSize != 0)
        {
            throw new ArgumentException("The clipmap resolution must be divisible by the brick size.", nameof(resolution));
        }

        _resolution = resolution;
        _brickSize = brickSize;
        _bricksPerAxis = resolution / brickSize;
        _baseVoxelSize = baseVoxelSize;
        _levels = new LevelState[levelCount];
        for (int level = 0; level < levelCount; level++)
        {
            _levels[level] = new LevelState();
        }
    }

    public void UpdateOrigins(in Vector3 cameraPosition)
    {
        for (int level = 0; level < _levels.Length; level++)
        {
            UpdateOrigin(level, cameraPosition);
        }
    }

    public Vector4 GetOriginAndVoxelSize(int level)
    {
        LevelState state = _levels[level];
        float voxelSize = GetVoxelSize(level);
        float brickWorldSize = voxelSize * _brickSize;
        Vector3 origin = new(
            state.Origin.X * brickWorldSize,
            state.Origin.Y * brickWorldSize,
            state.Origin.Z * brickWorldSize);
        return new Vector4(origin, voxelSize);
    }

    public Vector4 GetRingOffset(int level)
    {
        LevelState state = _levels[level];
        return new Vector4(state.RingOffsetX, state.RingOffsetY, state.RingOffsetZ, 0.0f);
    }

    public VoxelGiBounds GetLevelBounds(int level)
    {
        Vector4 originAndSize = GetOriginAndVoxelSize(level);
        Vector3 min = new(originAndSize.X, originAndSize.Y, originAndSize.Z);
        Vector3 max = min + new Vector3(originAndSize.W * _resolution);
        return new VoxelGiBounds(min, max);
    }

    public VoxelGiBounds GetBrickBounds(int level, in VoxelGiDirtyBrick brick)
    {
        Vector4 originAndSize = GetOriginAndVoxelSize(level);
        float brickWorldSize = originAndSize.W * _brickSize;
        Vector3 levelOrigin = new(originAndSize.X, originAndSize.Y, originAndSize.Z);
        Vector3 min = levelOrigin + new Vector3(brick.X, brick.Y, brick.Z) * brickWorldSize;
        return new VoxelGiBounds(min, min + new Vector3(brickWorldSize));
    }

    public void Invalidate(in VoxelGiBounds worldBounds, bool highPriority = true)
    {
        for (int level = 0; level < _levels.Length; level++)
        {
            InvalidateLevel(level, worldBounds, highPriority);
        }
    }

    public void AppendIntersectingBricks(int level, in VoxelGiBounds worldBounds, List<VoxelGiDirtyBrick> output)
    {
        LevelState state = _levels[level];
        if (!state.Initialized)
        {
            return;
        }

        float brickWorldSize = GetVoxelSize(level) * _brickSize;
        int minX = Math.Max((int)MathF.Floor(worldBounds.Min.X / brickWorldSize), state.Origin.X);
        int minY = Math.Max((int)MathF.Floor(worldBounds.Min.Y / brickWorldSize), state.Origin.Y);
        int minZ = Math.Max((int)MathF.Floor(worldBounds.Min.Z / brickWorldSize), state.Origin.Z);
        int maxX = Math.Min((int)MathF.Floor(worldBounds.Max.X / brickWorldSize), state.Origin.X + _bricksPerAxis - 1);
        int maxY = Math.Min((int)MathF.Floor(worldBounds.Max.Y / brickWorldSize), state.Origin.Y + _bricksPerAxis - 1);
        int maxZ = Math.Min((int)MathF.Floor(worldBounds.Max.Z / brickWorldSize), state.Origin.Z + _bricksPerAxis - 1);
        if (minX > maxX || minY > maxY || minZ > maxZ)
        {
            return;
        }

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    output.Add(new VoxelGiDirtyBrick(
                        (uint)(x - state.Origin.X),
                        (uint)(y - state.Origin.Y),
                        (uint)(z - state.Origin.Z)));
                }
            }
        }
    }

    public void InvalidateAll(bool highPriority = true)
    {
        for (int level = 0; level < _levels.Length; level++)
        {
            LevelState state = _levels[level];
            if (!state.Initialized)
            {
                continue;
            }
            ResetPending(state);
            MarkAll(level, highPriority);
            state.FullResetPending = true;
        }
    }

    public int DrainDirtyBricks(int level, int maximumCount, List<VoxelGiDirtyBrick> output)
    {
        output.Clear();
        if (maximumCount <= 0)
        {
            return 0;
        }

        LevelState state = _levels[level];
        DrainQueue(state, state.HighPriorityQueue, state.HighPriority, maximumCount, output);
        if (output.Count < maximumCount)
        {
            DrainQueue(state, state.StreamingQueue, state.Streaming, maximumCount, output);
        }
        return output.Count;
    }

    public int GetPendingBrickCount(int level)
    {
        LevelState state = _levels[level];
        return state.HighPriority.Count + state.Streaming.Count;
    }

    public bool ConsumeFullReset(int level)
    {
        LevelState state = _levels[level];
        bool result = state.FullResetPending;
        state.FullResetPending = false;
        return result;
    }

    /// <summary>Returns failed brick work to the high-priority queue.</summary>
    /// <param name="level">The clipmap level.</param>
    /// <param name="brick">The logical brick coordinate.</param>
    public void RequeueDirtyBrick(int level, in VoxelGiDirtyBrick brick)
    {
        LevelState state = _levels[level];
        Mark(
            state,
            new BrickCoordinate(
                state.Origin.X + (int)brick.X,
                state.Origin.Y + (int)brick.Y,
                state.Origin.Z + (int)brick.Z),
            true);
    }

    private void UpdateOrigin(int level, in Vector3 cameraPosition)
    {
        LevelState state = _levels[level];
        float brickWorldSize = GetVoxelSize(level) * _brickSize;
        int halfBricks = _bricksPerAxis / 2;
        BrickCoordinate desired = new(
            (int)MathF.Floor(cameraPosition.X / brickWorldSize) - halfBricks,
            (int)MathF.Floor(cameraPosition.Y / brickWorldSize) - halfBricks,
            (int)MathF.Floor(cameraPosition.Z / brickWorldSize) - halfBricks);

        if (!state.Initialized)
        {
            state.Origin = desired;
            state.Initialized = true;
            state.FullResetPending = true;
            MarkAll(level, false);
            return;
        }

        BrickCoordinate previous = state.Origin;
        int deltaX = desired.X - previous.X;
        int deltaY = desired.Y - previous.Y;
        int deltaZ = desired.Z - previous.Z;
        if (deltaX == 0 && deltaY == 0 && deltaZ == 0)
        {
            return;
        }

        state.Origin = desired;
        if (Math.Abs(deltaX) >= _bricksPerAxis
            || Math.Abs(deltaY) >= _bricksPerAxis
            || Math.Abs(deltaZ) >= _bricksPerAxis)
        {
            state.RingOffsetX = 0;
            state.RingOffsetY = 0;
            state.RingOffsetZ = 0;
            state.FullResetPending = true;
            ResetPending(state);
            MarkAll(level, false);
            return;
        }

        state.RingOffsetX = PositiveModulo(state.RingOffsetX + deltaX * _brickSize, _resolution);
        state.RingOffsetY = PositiveModulo(state.RingOffsetY + deltaY * _brickSize, _resolution);
        state.RingOffsetZ = PositiveModulo(state.RingOffsetZ + deltaZ * _brickSize, _resolution);

        // Mark only the newly-exposed brick slabs (O(|delta|*N²) instead of
        // O(N³)). For a single-brick step this iterates 256 bricks instead of
        // 4096. Axis-slab overlaps are harmless — Mark deduplicates via HashSet.
        MarkExposedSlab(state, desired, deltaX, 0); // X
        MarkExposedSlab(state, desired, deltaY, 1); // Y
        MarkExposedSlab(state, desired, deltaZ, 2); // Z
    }

    private void MarkExposedSlab(LevelState state, BrickCoordinate desired, int delta, int axis)
    {
        if (delta == 0)
        {
            return;
        }

        int slabStart = delta > 0 ? _bricksPerAxis - delta : 0;
        int slabEnd = delta > 0 ? _bricksPerAxis : -delta;

        for (int a = 0; a < _bricksPerAxis; a++)
        {
            for (int b = 0; b < _bricksPerAxis; b++)
            {
                for (int s = slabStart; s < slabEnd; s++)
                {
                    BrickCoordinate brick = axis == 0
                        ? new BrickCoordinate(desired.X + s, desired.Y + a, desired.Z + b)
                        : axis == 1
                            ? new BrickCoordinate(desired.X + a, desired.Y + s, desired.Z + b)
                            : new BrickCoordinate(desired.X + a, desired.Y + b, desired.Z + s);
                    Mark(state, brick, false);
                }
            }
        }
    }

    private void InvalidateLevel(int level, in VoxelGiBounds worldBounds, bool highPriority)
    {
        LevelState state = _levels[level];
        if (!state.Initialized)
        {
            return;
        }

        float brickWorldSize = GetVoxelSize(level) * _brickSize;
        int minX = Math.Max((int)MathF.Floor(worldBounds.Min.X / brickWorldSize), state.Origin.X);
        int minY = Math.Max((int)MathF.Floor(worldBounds.Min.Y / brickWorldSize), state.Origin.Y);
        int minZ = Math.Max((int)MathF.Floor(worldBounds.Min.Z / brickWorldSize), state.Origin.Z);
        int maxX = Math.Min((int)MathF.Floor(worldBounds.Max.X / brickWorldSize), state.Origin.X + _bricksPerAxis - 1);
        int maxY = Math.Min((int)MathF.Floor(worldBounds.Max.Y / brickWorldSize), state.Origin.Y + _bricksPerAxis - 1);
        int maxZ = Math.Min((int)MathF.Floor(worldBounds.Max.Z / brickWorldSize), state.Origin.Z + _bricksPerAxis - 1);
        if (minX > maxX || minY > maxY || minZ > maxZ)
        {
            return;
        }

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Mark(state, new BrickCoordinate(x, y, z), highPriority);
                }
            }
        }
    }

    private void MarkAll(int level, bool highPriority)
    {
        LevelState state = _levels[level];
        for (int z = 0; z < _bricksPerAxis; z++)
        {
            for (int y = 0; y < _bricksPerAxis; y++)
            {
                for (int x = 0; x < _bricksPerAxis; x++)
                {
                    Mark(state, new BrickCoordinate(state.Origin.X + x, state.Origin.Y + y, state.Origin.Z + z), highPriority);
                }
            }
        }
    }

    private void DrainQueue(
        LevelState state,
        Queue<BrickCoordinate> queue,
        HashSet<BrickCoordinate> pending,
        int maximumCount,
        List<VoxelGiDirtyBrick> output)
    {
        while (output.Count < maximumCount && queue.Count > 0)
        {
            BrickCoordinate worldBrick = queue.Dequeue();
            if (!pending.Remove(worldBrick) || !Contains(state.Origin, worldBrick))
            {
                continue;
            }

            output.Add(new VoxelGiDirtyBrick(
                (uint)(worldBrick.X - state.Origin.X),
                (uint)(worldBrick.Y - state.Origin.Y),
                (uint)(worldBrick.Z - state.Origin.Z)));
        }
    }

    private void Mark(LevelState state, in BrickCoordinate coordinate, bool highPriority)
    {
        if (highPriority)
        {
            if (!state.HighPriority.Add(coordinate))
            {
                return;
            }
            state.Streaming.Remove(coordinate);
            state.HighPriorityQueue.Enqueue(coordinate);
            return;
        }

        if (state.HighPriority.Contains(coordinate) || !state.Streaming.Add(coordinate))
        {
            return;
        }
        state.StreamingQueue.Enqueue(coordinate);
    }

    private bool Contains(in BrickCoordinate origin, in BrickCoordinate coordinate)
    {
        return coordinate.X >= origin.X && coordinate.X < origin.X + _bricksPerAxis
            && coordinate.Y >= origin.Y && coordinate.Y < origin.Y + _bricksPerAxis
            && coordinate.Z >= origin.Z && coordinate.Z < origin.Z + _bricksPerAxis;
    }

    private float GetVoxelSize(int level)
    {
        return _baseVoxelSize * (1 << level);
    }

    private static int PositiveModulo(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static void ResetPending(LevelState state)
    {
        state.HighPriorityQueue.Clear();
        state.StreamingQueue.Clear();
        state.HighPriority.Clear();
        state.Streaming.Clear();
    }
}

/// <summary>
/// Fixed-capacity physical brick allocator with one toroidal logical page table per
/// clipmap level. Page zero is represented by table value zero; resident pages are
/// stored as one-based indices for direct shader consumption.
/// </summary>
internal sealed class VoxelGiPagePool
{
    private readonly uint[][] _pageTables;
    private readonly Stack<uint> _freePages;
    private readonly int _bricksPerAxis;
    private readonly int _brickSize;
    private readonly int _resolution;
    private int _allocatedPageCount;

    public int Capacity { get; }

    public int AllocatedPageCount => _allocatedPageCount;

    public int VoxelCapacity => Capacity * _brickSize * _brickSize * _brickSize;

    public VoxelGiPagePool(int capacity, int levelCount, int resolution, int brickSize)
    {
        Capacity = capacity;
        _resolution = resolution;
        _brickSize = brickSize;
        _bricksPerAxis = resolution / brickSize;
        _pageTables = new uint[levelCount][];
        int pageTableLength = _bricksPerAxis * _bricksPerAxis * _bricksPerAxis;
        for (int level = 0; level < levelCount; level++)
        {
            _pageTables[level] = new uint[pageTableLength];
        }

        _freePages = new Stack<uint>(capacity);
        for (int page = capacity - 1; page >= 0; page--)
        {
            _freePages.Push((uint)page);
        }
    }

    public ReadOnlySpan<uint> GetPageTable(int level)
    {
        return _pageTables[level];
    }

    public bool TrySetResident(int level, in VoxelGiDirtyBrick brick, in Vector4 ringOffset, bool resident)
    {
        int slot = GetPageTableSlot(brick, ringOffset);
        uint entry = _pageTables[level][slot];
        if (!resident)
        {
            if (entry == 0)
            {
                return true;
            }
            _pageTables[level][slot] = 0;
            _freePages.Push(entry - 1);
            _allocatedPageCount--;
            return true;
        }

        if (entry != 0)
        {
            return true;
        }
        if (!_freePages.TryPop(out uint page))
        {
            return false;
        }

        _pageTables[level][slot] = page + 1;
        _allocatedPageCount++;
        return true;
    }

    public void ResetLevel(int level)
    {
        uint[] pageTable = _pageTables[level];
        for (int i = 0; i < pageTable.Length; i++)
        {
            uint entry = pageTable[i];
            if (entry == 0)
            {
                continue;
            }
            _freePages.Push(entry - 1);
            pageTable[i] = 0;
            _allocatedPageCount--;
        }
    }

    public void Reset()
    {
        for (int level = 0; level < _pageTables.Length; level++)
        {
            Array.Clear(_pageTables[level]);
        }
        _freePages.Clear();
        for (int page = Capacity - 1; page >= 0; page--)
        {
            _freePages.Push((uint)page);
        }
        _allocatedPageCount = 0;
    }

    private int GetPageTableSlot(in VoxelGiDirtyBrick brick, in Vector4 ringOffset)
    {
        int ringBrickX = (int)ringOffset.X / _brickSize;
        int ringBrickY = (int)ringOffset.Y / _brickSize;
        int ringBrickZ = (int)ringOffset.Z / _brickSize;
        int x = ((int)brick.X + ringBrickX) % _bricksPerAxis;
        int y = ((int)brick.Y + ringBrickY) % _bricksPerAxis;
        int z = ((int)brick.Z + ringBrickZ) % _bricksPerAxis;
        return x + y * _bricksPerAxis + z * _bricksPerAxis * _bricksPerAxis;
    }
}

internal static class VoxelGiProbeGrid
{
    /// <summary>Calculates a camera-relative probe origin snapped to whole probe cells.</summary>
    /// <param name="cameraPosition">The camera position.</param>
    /// <param name="spacing">The world-space probe spacing.</param>
    /// <param name="probeCountX">The X-axis probe count.</param>
    /// <param name="probeCountY">The Y-axis probe count.</param>
    /// <param name="probeCountZ">The Z-axis probe count.</param>
    /// <returns>The snapped origin in xyz and spacing in w.</returns>
    public static Vector4 CalculateSnappedOrigin(
        in Vector3 cameraPosition,
        float spacing,
        int probeCountX,
        int probeCountY,
        int probeCountZ)
    {
        Vector3 halfExtent = new(
            (probeCountX - 1) * spacing * 0.5f,
            (probeCountY - 1) * spacing * 0.5f,
            (probeCountZ - 1) * spacing * 0.5f);
        Vector3 desired = cameraPosition - halfExtent;
        Vector3 snapped = new(
            MathF.Floor(desired.X / spacing) * spacing,
            MathF.Floor(desired.Y / spacing) * spacing,
            MathF.Floor(desired.Z / spacing) * spacing);
        return new Vector4(snapped, spacing);
    }
}
