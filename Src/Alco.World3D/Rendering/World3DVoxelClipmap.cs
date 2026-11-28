using System.Numerics;
using System.Runtime.InteropServices;

namespace Alco.World3D;

/// <summary>O(1) 64-bit clipmap origin tracker that computes toroidal scroll deltas and disjoint eviction boxes without iterating bricks on CPU.</summary>
internal sealed class World3DVoxelClipmap
{
    public const int CascadeCount = 4;
    public const int BrickSize = 8;
    public const int MaxEvictBoxesPerFrame = CascadeCount * 3;

    private readonly long[] _committedOriginX = new long[CascadeCount];
    private readonly long[] _committedOriginY = new long[CascadeCount];
    private readonly long[] _committedOriginZ = new long[CascadeCount];
    private readonly long[] _pendingOriginX = new long[CascadeCount];
    private readonly long[] _pendingOriginY = new long[CascadeCount];
    private readonly long[] _pendingOriginZ = new long[CascadeCount];
    private bool _committedValid;
    private int _committedResolution;
    private float _committedBaseVoxelSize;
    private bool _pendingValid;
    private int _pendingResolution;
    private float _pendingBaseVoxelSize;

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public readonly record struct CascadeGpu(
        [field: FieldOffset(0)] Vector4 OriginVoxelSize,
        [field: FieldOffset(16)] uint ToroidalOriginX,
        [field: FieldOffset(20)] uint ToroidalOriginY,
        [field: FieldOffset(24)] uint ToroidalOriginZ,
        [field: FieldOffset(28)] uint BricksPerAxis);

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public readonly record struct EvictBoxGpu(
        [field: FieldOffset(0)] int MinLocalX,
        [field: FieldOffset(4)] int MinLocalY,
        [field: FieldOffset(8)] int MinLocalZ,
        [field: FieldOffset(12)] int CascadeIndex,
        [field: FieldOffset(16)] int MaxLocalExclX,
        [field: FieldOffset(20)] int MaxLocalExclY,
        [field: FieldOffset(24)] int MaxLocalExclZ,
        [field: FieldOffset(28)] int ResetFlag);

    public readonly record struct FramePlan(
        int Resolution,
        int BricksPerAxis,
        float BaseVoxelSize,
        int ResetCascadeMask,
        int ShiftedCascadeMask,
        int EvictedBrickCount,
        CascadeGpu[] Cascades,
        EvictBoxGpu[] EvictBoxes,
        int EvictBoxCount);

    public void Invalidate()
    {
        _committedValid = false;
        _pendingValid = false;
    }

    public static int NormalizeResolution(int requested)
    {
        int clamped = Math.Clamp(requested, 32, 256);
        int pow2 = 32;
        while (pow2 * 2 <= clamped)
            pow2 *= 2;
        return pow2;
    }

    public void Plan(
        World3DPosition focusWorld,
        World3DCell originAnchorCell,
        int requestedResolution,
        float requestedBaseVoxelSize,
        bool forceReset,
        out FramePlan plan)
    {
        int resolution = NormalizeResolution(requestedResolution);
        int bpa = resolution / BrickSize;
        float baseVoxelSize = Math.Clamp(requestedBaseVoxelSize, 0.05f, 4.0f);

        bool globalReset = forceReset
            || !_committedValid
            || _committedResolution != resolution
            || MathF.Abs(_committedBaseVoxelSize - baseVoxelSize) > 1e-5f;

        CascadeGpu[] cascades = new CascadeGpu[CascadeCount];
        EvictBoxGpu[] evictBoxes = new EvictBoxGpu[MaxEvictBoxesPerFrame];
        int evictBoxCount = 0;
        int resetMask = 0;
        int shiftedMask = 0;
        int evictedBricks = 0;
        int halfBpa = bpa / 2;

        for (int c = 0; c < CascadeCount; ++c)
        {
            double voxelSize = (double)baseVoxelSize * (1 << c);
            double brickMeters = voxelSize * BrickSize;

            long newX;
            long newY;
            long newZ;
            if (globalReset)
            {
                newX = (long)Math.Floor(focusWorld.X / brickMeters) - halfBpa;
                newY = (long)Math.Floor(focusWorld.Y / brickMeters) - halfBpa;
                newZ = (long)Math.Floor(focusWorld.Z / brickMeters) - halfBpa;
            }
            else
            {
                newX = _committedOriginX[c] + ComputeStep(focusWorld.X / brickMeters, _committedOriginX[c] + halfBpa);
                newY = _committedOriginY[c] + ComputeStep(focusWorld.Y / brickMeters, _committedOriginY[c] + halfBpa);
                newZ = _committedOriginZ[c] + ComputeStep(focusWorld.Z / brickMeters, _committedOriginZ[c] + halfBpa);
            }

            _pendingOriginX[c] = newX;
            _pendingOriginY[c] = newY;
            _pendingOriginZ[c] = newZ;

            if (globalReset)
            {
                resetMask |= 1 << c;
                evictedBricks += bpa * bpa * bpa;
                evictBoxes[evictBoxCount++] = new EvictBoxGpu(0, 0, 0, c, bpa, bpa, bpa, 1);
            }
            else
            {
                long dx = newX - _committedOriginX[c];
                long dy = newY - _committedOriginY[c];
                long dz = newZ - _committedOriginZ[c];
                if (dx != 0 || dy != 0 || dz != 0)
                {
                    if (Math.Abs(dx) >= bpa || Math.Abs(dy) >= bpa || Math.Abs(dz) >= bpa)
                    {
                        resetMask |= 1 << c;
                        evictedBricks += bpa * bpa * bpa;
                        evictBoxes[evictBoxCount++] = new EvictBoxGpu(0, 0, 0, c, bpa, bpa, bpa, 1);
                    }
                    else
                    {
                        shiftedMask |= 1 << c;
                        evictedBricks += EmitDisjointEvictBoxes(
                            c, bpa, (int)dx, (int)dy, (int)dz, evictBoxes, ref evictBoxCount);
                    }
                }
            }

            double originWorldX = newX * brickMeters;
            double originWorldY = newY * brickMeters;
            double originWorldZ = newZ * brickMeters;
            Vector3 anchorRel = new(
                (float)(originWorldX - (double)originAnchorCell.X * World3DSceneCoordinates.CellSize),
                (float)(originWorldY - (double)originAnchorCell.Y * World3DSceneCoordinates.CellSize),
                (float)(originWorldZ - (double)originAnchorCell.Z * World3DSceneCoordinates.CellSize));

            cascades[c] = new CascadeGpu(
                new Vector4(anchorRel, (float)voxelSize),
                (uint)PositiveMod(newX, bpa),
                (uint)PositiveMod(newY, bpa),
                (uint)PositiveMod(newZ, bpa),
                (uint)bpa);
        }

        _pendingValid = true;
        _pendingResolution = resolution;
        _pendingBaseVoxelSize = baseVoxelSize;

        plan = new FramePlan(
            resolution,
            bpa,
            baseVoxelSize,
            resetMask,
            shiftedMask,
            evictedBricks,
            cascades,
            evictBoxes,
            evictBoxCount);
    }

    public void Commit()
    {
        if (!_pendingValid)
            return;
        Array.Copy(_pendingOriginX, _committedOriginX, CascadeCount);
        Array.Copy(_pendingOriginY, _committedOriginY, CascadeCount);
        Array.Copy(_pendingOriginZ, _committedOriginZ, CascadeCount);
        _committedResolution = _pendingResolution;
        _committedBaseVoxelSize = _pendingBaseVoxelSize;
        _committedValid = true;
    }

    public void Rollback() => _pendingValid = false;

    public bool TryGetCommittedOriginBrick(int cascade, out long x, out long y, out long z)
    {
        if (!_committedValid || (uint)cascade >= CascadeCount)
        {
            x = y = z = 0;
            return false;
        }
        x = _committedOriginX[cascade];
        y = _committedOriginY[cascade];
        z = _committedOriginZ[cascade];
        return true;
    }

    public bool TryGetCoarsestCommittedWorldBounds(out World3DPosition min, out World3DPosition max)
    {
        if (!_committedValid)
        {
            min = default;
            max = default;
            return false;
        }
        int c = CascadeCount - 1;
        double brickMeters = (double)_committedBaseVoxelSize * (1 << c) * BrickSize;
        double spanMeters = brickMeters * (_committedResolution / BrickSize);
        min = new World3DPosition(
            _committedOriginX[c] * brickMeters,
            _committedOriginY[c] * brickMeters,
            _committedOriginZ[c] * brickMeters);
        max = new World3DPosition(min.X + spanMeters, min.Y + spanMeters, min.Z + spanMeters);
        return true;
    }

    private static long ComputeStep(double targetCenterBricks, long currentCenterBricks)
    {
        double diff = targetCenterBricks - currentCenterBricks;
        return Math.Abs(diff) >= 1.0 ? (long)Math.Truncate(diff) : 0;
    }

    private static int PositiveMod(long value, int modulus)
    {
        int r = (int)(value % modulus);
        return r < 0 ? r + modulus : r;
    }

    private static int EmitDisjointEvictBoxes(
        int cascade,
        int bpa,
        int dx,
        int dy,
        int dz,
        EvictBoxGpu[] dst,
        ref int count)
    {
        GetKeepAndNewRanges(dx, bpa, out int keepMinX, out int keepMaxX, out int newMinX, out int newMaxX);
        GetKeepAndNewRanges(dy, bpa, out int keepMinY, out int keepMaxY, out int newMinY, out int newMaxY);
        GetKeepAndNewRanges(dz, bpa, out int keepMinZ, out int keepMaxZ, out int newMinZ, out int newMaxZ);

        int bricks = 0;
        if (newMinX < newMaxX)
        {
            dst[count++] = new EvictBoxGpu(newMinX, 0, 0, cascade, newMaxX, bpa, bpa, 0);
            bricks += (newMaxX - newMinX) * bpa * bpa;
        }
        if (newMinY < newMaxY && keepMinX < keepMaxX)
        {
            dst[count++] = new EvictBoxGpu(keepMinX, newMinY, 0, cascade, keepMaxX, newMaxY, bpa, 0);
            bricks += (keepMaxX - keepMinX) * (newMaxY - newMinY) * bpa;
        }
        if (newMinZ < newMaxZ && keepMinX < keepMaxX && keepMinY < keepMaxY)
        {
            dst[count++] = new EvictBoxGpu(keepMinX, keepMinY, newMinZ, cascade, keepMaxX, keepMaxY, newMaxZ, 0);
            bricks += (keepMaxX - keepMinX) * (keepMaxY - keepMinY) * (newMaxZ - newMinZ);
        }
        return bricks;
    }

    private static void GetKeepAndNewRanges(
        int delta, int bpa, out int keepMin, out int keepMax, out int newMin, out int newMax)
    {
        if (delta > 0)
        {
            keepMin = 0;
            keepMax = bpa - delta;
            newMin = bpa - delta;
            newMax = bpa;
        }
        else if (delta < 0)
        {
            int absD = -delta;
            newMin = 0;
            newMax = absD;
            keepMin = absD;
            keepMax = bpa;
        }
        else
        {
            keepMin = 0;
            keepMax = bpa;
            newMin = 0;
            newMax = 0;
        }
    }
}
