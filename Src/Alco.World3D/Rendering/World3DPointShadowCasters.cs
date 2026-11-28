using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    // Larger than the whole supported cell range plus the largest supported light radius in either direction.
    private const double PointShadowBroadphaseLimit = World3DSceneCoordinates.CellSize * (double)(1L << 32);
    private static readonly Vector3[] PointShadowFaceForwards =
        [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];

    private readonly Dictionary<DrawKey, int> _pointShadowLocalBatchIndices = [];
    private readonly List<PointShadowLocalBatch> _pointShadowLocalBatches = [];
    private readonly List<PointShadowLocalCaster> _pointShadowLocalCasters = [];

    private bool TryAppendPointShadowFace(int page, int tier, int face, in PointShadowProjection projection,
        ref PointShadowBudget budget)
    {
        if (budget.SearchWork == 0)
            return false;

        PointShadowTier tierDesc = PointShadowTiers[tier];
        if (!TryCreatePointShadowView(in projection, face, tierDesc.Resolution, out World3DView view))
            return false;

        _pointShadowLocalBatches.Clear();
        _pointShadowLocalBatchIndices.Clear();
        _pointShadowLocalCasters.Clear();
        int searchLimit = Math.Min(budget.SearchWork, PointShadowSearchWorkPerFace);
        PointShadowCasterVisitor visitor = new(this, in projection, face, searchLimit,
            budget.CandidateLimit - _pointShadowCandidates.Count);
        RegionalBounds query = GetPointShadowFaceQuery(in projection, face);
        _pointShadowCasters.Visit(in query, ref visitor);
        budget.SearchWork -= searchLimit - visitor.RemainingSearchWork;
        if (!visitor.Complete)
            return false; // Never publish an incomplete depth layer, even when the overflow is only conservative work.

        int firstBatch = _pointShadowBatches.Count;
        int viewIndex = _pointShadowFaceUpdates.Count;
        int firstCommand = _pointShadowCandidates.Count;
        Span<PointShadowLocalBatch> localBatches = CollectionsMarshal.AsSpan(_pointShadowLocalBatches);
        for (int i = 0; i < localBatches.Length; ++i)
        {
            ref PointShadowLocalBatch batch = ref localBatches[i];
            batch.Next = firstCommand;
            PointShadowBatchGpu gpu = new(batch.Key.PageId, (uint)viewIndex, (uint)firstCommand, (uint)batch.Count);
            _pointShadowBatches.Add(new PointShadowBatch(batch.Key, gpu));
            firstCommand += batch.Count;
        }
        CollectionsMarshal.SetCount(_pointShadowCandidates, firstCommand);
        Span<PointShadowCandidateGpu> candidates = CollectionsMarshal.AsSpan(_pointShadowCandidates);
        foreach (PointShadowLocalCaster caster in _pointShadowLocalCasters)
        {
            int index = localBatches[caster.Batch].Next++;
            candidates[index] = new PointShadowCandidateGpu((uint)caster.Slot, (uint)(firstBatch + caster.Batch));
        }

        uint layer = (uint)((page - tierDesc.FirstPage) * PointShadowFaceCount + face);
        _pointShadowFaceUpdates.Add(new PointShadowFaceUpdate(tier, layer, firstBatch, localBatches.Length, view));
        return true;
    }

    private bool TryAddPointShadowLocalCaster(int slot, int candidateLimit)
    {
        InstanceEntry instance = _instances.GetLive(slot);
        DrawRoute route = GetDrawRoute(instance);
        GeometryEntry geometry = instance.Geometry;
        int staged = _stagedDrawLods.GetValueOrDefault(geometry);
        Span<uint> pages = stackalloc uint[MaxGeometryLods];
        int pageCount = 0;
        for (int lod = 0; lod < MaxGeometryLods; ++lod)
        {
            ResidentLod? effective = (staged & (1 << lod)) != 0 ? geometry.Pending[lod]!.Value.Replacement : geometry.Resident[lod];
            if (effective is not ResidentLod resident || pages[..pageCount].Contains(resident.Allocation.PageId))
                continue;
            if (_pointShadowLocalCasters.Count == candidateLimit)
                return false;

            DrawKey key = new(route, resident.Allocation.Buffer, resident.Allocation.PageId);
            if (!_pointShadowLocalBatchIndices.TryGetValue(key, out int batch))
            {
                batch = _pointShadowLocalBatches.Count;
                _pointShadowLocalBatchIndices.Add(key, batch);
                _pointShadowLocalBatches.Add(new PointShadowLocalBatch(in key));
            }
            ++CollectionsMarshal.AsSpan(_pointShadowLocalBatches)[batch].Count;
            _pointShadowLocalCasters.Add(new PointShadowLocalCaster(slot, batch));
            pages[pageCount++] = resident.Allocation.PageId;
        }
        return true;
    }

    private static bool TryCreatePointShadowView(in PointShadowProjection projection, int face, int resolution, out World3DView view)
    {
        view = default;
        float near = projection.Near;
        float far = projection.Radius;
        if (!(near > 0f && far > near) || !World3DSceneCoordinates.TrySplitPosition(projection.Position, out World3DCell cell, out Vector3 local))
            return false;

        Vector3 up = face < 4 ? Vector3.UnitZ : Vector3.UnitX;
        Matrix4x4 rotation = Matrix4x4.CreateLookAtLeftHanded(Vector3.Zero, PointShadowFaceForwards[face], up);
        float a = far / (far - near);
        // Exact unit x/y scale is a 90-degree frustum; no trigonometric drift at seams.
        Matrix4x4 perspective = new(1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, a, 1f, 0f, 0f, -a * near, 0f);
        Matrix4x4 viewProjection = rotation * perspective;
        // The shared square capture-view factory only packs matrices and the cell anchor. It does not alter projection.
        return World3DView.TryCreateOrthographic(in viewProjection, cell, local, resolution, out view);
    }

    private static PointShadowBounds GetPointShadowBounds(in World3DInstanceSpatialGpu spatial)
    {
        Vector4 c = spatial.BoundsCenterRadius;
        Vector4 e = spatial.BoundsExtent;
        (double minX, double maxX) = GetPointShadowBoundsAxis(spatial.Cell.X, spatial.ObjectToCell.M41, c.X, e.X);
        (double minY, double maxY) = GetPointShadowBoundsAxis(spatial.Cell.Y, spatial.ObjectToCell.M42, c.Y, e.Y);
        (double minZ, double maxZ) = GetPointShadowBoundsAxis(spatial.Cell.Z, spatial.ObjectToCell.M43, c.Z, e.Z);
        return new PointShadowBounds(new World3DPosition(minX, minY, minZ), new World3DPosition(maxX, maxY, maxZ));
    }

    private static (double Min, double Max) GetPointShadowBoundsAxis(int cell, float pivot, float center, float extent)
    {
        double origin = (double)cell * World3DSceneCoordinates.CellSize;
        double centerLocal = (double)pivot + center;
        // Leave room for float eye-relative pivot math and addition of large transformed vertices.
        double guard = 1e-4 + (Math.Abs((double)center) + extent) * 2e-6;
        return (Math.BitDecrement(origin + (centerLocal - extent - guard)),
            Math.BitIncrement(origin + (centerLocal + extent + guard)));
    }

    private static double PointShadowQueryGuard(float radius) => 1e-4 + (double)radius * 2e-6;

    private static RegionalBounds GetPointShadowFaceQuery(in PointShadowProjection projection, int face)
    {
        World3DPosition p = projection.Position;
        Vector3 forward = PointShadowFaceForwards[face];
        double radius = projection.Radius;
        double guard = PointShadowQueryGuard(projection.Radius);
        return new RegionalBounds(
            Math.BitDecrement(p.X - (forward.X > 0 ? 0 : radius) - guard),
            Math.BitDecrement(p.Y - (forward.Y > 0 ? 0 : radius) - guard),
            Math.BitDecrement(p.Z - (forward.Z > 0 ? 0 : radius) - guard),
            Math.BitIncrement(p.X + (forward.X < 0 ? 0 : radius) + guard),
            Math.BitIncrement(p.Y + (forward.Y < 0 ? 0 : radius) + guard),
            Math.BitIncrement(p.Z + (forward.Z < 0 ? 0 : radius) + guard));
    }

    private static bool PointShadowBoundsOverlapLight(in PointShadowBounds bounds, in PointShadowProjection projection)
    {
        World3DPosition p = projection.Position;
        double x = PointShadowAxisDistance(bounds.Min.X - p.X, bounds.Max.X - p.X);
        double y = PointShadowAxisDistance(bounds.Min.Y - p.Y, bounds.Max.Y - p.Y);
        double z = PointShadowAxisDistance(bounds.Min.Z - p.Z, bounds.Max.Z - p.Z);
        double radius = projection.Radius + PointShadowQueryGuard(projection.Radius);
        return x * x + y * y + z * z <= radius * radius;
    }

    private static bool PointShadowBoundsOverlapFace(in PointShadowBounds bounds, in PointShadowProjection projection, int face)
    {
        World3DPosition p = projection.Position;
        double minX = bounds.Min.X - p.X, maxX = bounds.Max.X - p.X;
        double minY = bounds.Min.Y - p.Y, maxY = bounds.Max.Y - p.Y;
        double minZ = bounds.Min.Z - p.Z, maxZ = bounds.Max.Z - p.Z;
        (double min, double max, double sideA, double sideB) = (face >> 1) switch
        {
            0 => (minX, maxX, PointShadowAxisDistance(minY, maxY), PointShadowAxisDistance(minZ, maxZ)),
            1 => (minY, maxY, PointShadowAxisDistance(minX, maxX), PointShadowAxisDistance(minZ, maxZ)),
            _ => (minZ, maxZ, PointShadowAxisDistance(minX, maxX), PointShadowAxisDistance(minY, maxY)),
        };
        if ((face & 1) != 0)
            (min, max) = (-max, -min);
        double guard = PointShadowQueryGuard(projection.Radius);
        return max + guard >= Math.Max(projection.Near, Math.Max(sideA, sideB)) && min <= projection.Radius + guard;
    }

    private static double PointShadowAxisDistance(double min, double max) => Math.Max(0.0, Math.Max(min, -max));

    private readonly record struct PointShadowBounds(World3DPosition Min, World3DPosition Max);

    private sealed class PointShadowCasterIndex
    {
        private readonly RegionalSpatialIndex _index = new();
        private readonly Dictionary<int, (RegionIndexHandle Handle, PointShadowBounds Bounds)> _entries = [];

        /// <summary>Returns the double-precision bounds for a tracked caster.</summary>
        public bool TryGetBounds(int slot, out PointShadowBounds bounds)
        {
            bool found = _entries.TryGetValue(slot, out var entry);
            bounds = entry.Bounds;
            return found;
        }

        /// <summary>Updates or inserts a caster without rebuilding the index.</summary>
        public void Set(int slot, World3DPosition pivot, in PointShadowBounds bounds)
        {
            // Clamp only the broadphase, outside every possible query, to keep BVH surface areas finite.
            const double limit = PointShadowBroadphaseLimit;
            RegionalBounds broadphase = new(Math.Clamp(bounds.Min.X, -limit, limit),
                Math.Clamp(bounds.Min.Y, -limit, limit), Math.Clamp(bounds.Min.Z, -limit, limit),
                Math.Clamp(bounds.Max.X, -limit, limit), Math.Clamp(bounds.Max.Y, -limit, limit), Math.Clamp(bounds.Max.Z, -limit, limit));
            ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(_entries, slot, out bool exists);
            entry.Handle = exists ? _index.Update(slot, entry.Handle, pivot, in broadphase) : _index.Insert(slot, pivot, in broadphase);
            entry.Bounds = bounds;
        }

        /// <summary>Removes a tracked caster and returns the old bounds for page invalidation.</summary>
        public bool TryRemove(int slot, out PointShadowBounds bounds)
        {
            if (!_entries.Remove(slot, out var entry))
            {
                bounds = default;
                return false;
            }
            _index.Remove(entry.Handle);
            bounds = entry.Bounds;
            return true;
        }

        /// <summary>Visits broadphase overlaps until the caster work budget is exhausted.</summary>
        public void Visit(in RegionalBounds query, ref PointShadowCasterVisitor visitor) => _index.VisitOverlapAabb(in query, ref visitor);

        /// <summary>Removes all caster entries and their regional trees.</summary>
        public void Clear()
        {
            _index.Clear();
            _entries.Clear();
        }
    }

    private struct PointShadowCasterVisitor(World3DScene scene, in PointShadowProjection projection, int face, int searchLimit,
        int candidateLimit) : IBvhAabb3DLeafVisitor
    {
        private readonly World3DScene _scene = scene;
        private readonly PointShadowProjection _projection = projection;
        private readonly int _face = face;
        private readonly int _candidateLimit = candidateLimit;
        public int RemainingSearchWork = searchLimit;
        public bool Complete = true;

        /// <summary>Evaluates one broadphase hit and stops on the first unit of work that would overflow.</summary>
        public bool OnLeaf(int slot)
        {
            if (RemainingSearchWork == 0)
                return Complete = false;
            --RemainingSearchWork;
            _scene._pointShadowCasters.TryGetBounds(slot, out PointShadowBounds bounds);
            if (!PointShadowBoundsOverlapLight(in bounds, in _projection) || !PointShadowBoundsOverlapFace(in bounds, in _projection, _face))
                return true;
            return Complete = _scene.TryAddPointShadowLocalCaster(slot, _candidateLimit);
        }
    }

    private struct PointShadowLocalBatch(in DrawKey key)
    {
        public readonly DrawKey Key = key;
        public int Count;
        public int Next;
    }

    private readonly record struct PointShadowLocalCaster(int Slot, int Batch);
}
