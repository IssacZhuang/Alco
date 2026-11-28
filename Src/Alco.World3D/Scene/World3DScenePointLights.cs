using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private const float MinPointLightInfluenceRadius = 0.01f;
    private const float MaxPointLightRadius = (float)((double)World3DSceneCoordinates.CellSize * (1L << 31));

    private readonly SlotMap<PointLightEntry> _localLights = new();
    private readonly RegionalSpatialIndex _localLightIndex = new();
    private readonly List<WorldPointLightHandle> _legacyPointLights = [];
    private readonly List<int> _localLightQuery = [];
    private readonly PriorityQueue<RankedPointLight, ulong> _regularLightRanks = new();
    private readonly PriorityQueue<RankedPointLight, ulong> _wideLightRanks = new();
    private readonly List<RankedPointLight> _orderedLightRanks = [];
    private readonly List<SelectedPointLight> _selectedPointLights = [];
    private BufferSlice _frameLocalLightCandidates;
    private int _recordedLocalLightCount;
    private float _recordedLocalLightDepth;

    /// <summary>Gets the per-view direct point-light budgets.</summary>
    public World3DLocalLightSettings LocalLights { get; } = new();

    /// <summary>Gets the CPU candidate selection counters for the most recent recorded frame.</summary>
    public World3DLocalLightStats LastRecordedLocalLights { get; private set; }

    /// <summary>Registers a persistent light that is unloaded with its sector.</summary>
    /// <param name="sector">The live sector that owns the light.</param>
    /// <param name="description">The light's world-space influence and emitter parameters.</param>
    /// <param name="handle">Receives the light's stable handle.</param>
    /// <returns>Whether the description was valid and a light slot was available.</returns>
    public bool TryAddPointLight(WorldSectorHandle sector, in World3DPointLight description, out WorldPointLightHandle handle)
    {
        BeginEdit();
        handle = default;
        return TryGetSector(sector, out SectorEntry? entry) && TryPackPointLight(in description, out World3DLocalLightGpu gpu)
            && TryAddPointLight(entry, in description, in gpu, out handle);
    }

    /// <summary>Updates a live light in place without changing its owner or handle.</summary>
    /// <param name="handle">The light to update.</param>
    /// <param name="description">The replacement light parameters.</param>
    /// <returns>Whether the handle and new parameters were valid.</returns>
    public bool TryUpdatePointLight(WorldPointLightHandle handle, in World3DPointLight description)
    {
        BeginEdit();
        if (!TryGetPointLightEntry(handle, out PointLightEntry? entry) || !TryPackPointLight(in description, out World3DLocalLightGpu gpu))
            return false;
        UpdatePointLight(handle, entry, in description, in gpu);
        return true;
    }

    /// <summary>Gets a live light's authoritative description.</summary>
    /// <param name="handle">The light to query.</param>
    /// <param name="description">Receives the current light parameters.</param>
    /// <returns>Whether the handle identified a live point light in this scene.</returns>
    public bool TryGetPointLight(WorldPointLightHandle handle, out World3DPointLight description)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        description = default;
        if (!TryGetPointLightEntry(handle, out PointLightEntry? entry))
            return false;
        description = entry.Description;
        return true;
    }

    /// <summary>Removes a light and immediately invalidates its CPU handle.</summary>
    /// <param name="handle">The light to remove.</param>
    /// <returns>Whether the handle identified a live point light in this scene.</returns>
    public bool TryRemovePointLight(WorldPointLightHandle handle)
    {
        BeginEdit();
        if (!TryGetPointLightEntry(handle, out PointLightEntry? entry))
            return false;
        RemovePointLight(handle.Slot, entry, unlinkSector: true);
        return true;
    }

    /// <summary>Replaces the compatibility light list, leaving sector-owned lights unchanged.</summary>
    /// <param name="lights">The new list; invalid entries and entries beyond the remaining capacity are ignored.</param>
    public void SetPointLights(ReadOnlySpan<World3DPointLight> lights)
    {
        BeginEdit();
        int kept = 0;
        foreach (ref readonly World3DPointLight light in lights)
        {
            if (!TryPackPointLight(in light, out World3DLocalLightGpu gpu))
                continue;
            if (kept < _legacyPointLights.Count)
            {
                WorldPointLightHandle handle = _legacyPointLights[kept];
                UpdatePointLight(handle, _localLights.GetLive(handle.Slot), in light, in gpu);
            }
            else
            {
                if (!TryAddPointLight(null, in light, in gpu, out WorldPointLightHandle handle))
                    break;
                _legacyPointLights.Add(handle);
            }
            ++kept;
        }
        for (int i = kept; i < _legacyPointLights.Count; ++i)
        {
            int slot = _legacyPointLights[i].Slot;
            RemovePointLight(slot, _localLights.GetLive(slot), unlinkSector: false);
        }
        _legacyPointLights.RemoveRange(kept, _legacyPointLights.Count - kept);
    }

    private bool TryGetPointLightEntry(WorldPointLightHandle handle, [NotNullWhen(true)] out PointLightEntry? entry)
    {
        if (_localLights.TryGet(handle.SceneId == _id ? handle.Slot : -1, handle.Generation, out entry) && entry.Sector != null)
            return true;
        entry = null;
        return false;
    }

    private bool TryAddPointLight(SectorEntry? sector, in World3DPointLight description, in World3DLocalLightGpu gpu,
        out WorldPointLightHandle handle)
    {
        handle = default;
        PointLightEntry entry = new(sector, in description, in gpu);
        if (!_localLights.TryAdd(entry, out int slot, out uint generation))
            return false;
        entry.Generation = generation;
        entry.IndexHandle = _localLightIndex.Insert(slot, description.Position, GetPointLightBounds(in description));
        sector?.PointLights.Add(slot);
        _gpu.SetLocalLight(slot, in gpu);
        handle = new WorldPointLightHandle(_id, slot, generation);
        return true;
    }

    private void UpdatePointLight(WorldPointLightHandle handle, PointLightEntry entry, in World3DPointLight description,
        in World3DLocalLightGpu gpu)
    {
        World3DPointLight previous = entry.Description;
        if (previous == description)
            return;
        if (previous.Position != description.Position || previous.Radius != description.Radius)
            entry.IndexHandle = _localLightIndex.Update(handle.Slot, entry.IndexHandle, description.Position, GetPointLightBounds(in description));
        if (previous.CastsShadow && !description.CastsShadow)
            ForgetPointShadowLight(handle.Slot, handle.Generation);
        if (entry.Gpu != gpu)
            _gpu.SetLocalLight(handle.Slot, in gpu);
        entry.Description = description;
        entry.Energy = GetPointLightEnergy(in description);
        entry.Gpu = gpu;
    }

    private void RemovePointLight(int slot, PointLightEntry entry, bool unlinkSector)
    {
        ForgetPointShadowLight(slot, entry.Generation);
        _localLightIndex.Remove(entry.IndexHandle);
        if (unlinkSector)
            entry.Sector?.PointLights.Remove(slot);
        _localLights.Remove(slot);
        _gpu.SetLocalLight(slot, default);
        _retireOnSubmit.Add(() => _localLights.Reclaim(slot));
    }

    private static bool TryPackPointLight(in World3DPointLight light, out World3DLocalLightGpu gpu)
    {
        gpu = default;
        if (!(light.Radius > 0f && light.Radius <= MaxPointLightRadius) ||
            !(light.Intensity >= 0f && float.IsFinite(light.Intensity)) ||
            !(light.SourceRadius >= 0f && light.SourceRadius <= light.Radius) ||
            !(light.ShadowPriority >= 0f && float.IsFinite(light.ShadowPriority)) ||
            !IsFinite(light.Color) || Vector3.Min(light.Color, Vector3.Zero) != Vector3.Zero ||
            !World3DSceneCoordinates.TrySplitPosition(light.Position, out World3DCell cell, out Vector3 local))
            return false;
        gpu = new World3DLocalLightGpu(cell, new Vector4(local, light.Radius),
            new Vector4(light.Color, light.Intensity), new Vector4(light.SourceRadius, 0f, 0f, 0f));
        return true;
    }

    private void SelectLocalLights(in World3DView view, World3DPosition eye)
    {
        _selectedPointLights.Clear();
        _frameLocalLightCandidates = default;
        _recordedLocalLightCount = 0;
        _recordedLocalLightDepth = 0f;
        LastRecordedLocalLights = new(_localLights.Count, 0, 0, 0, 0);
        int budget = Math.Clamp(LocalLights.MaxViewLights, 0, LightClusterMaxCandidates);
        float near = view.EyeLocalNear.W;
        float far = MathF.Min(LocalLights.MaximumViewDepth, MaxPointLightRadius);
        if (!_recordedWorld || _localLights.Count == 0 || budget == 0 || !(far > near))
            return;

        _localLightQuery.Clear();
        _regularLightRanks.Clear();
        _wideLightRanks.Clear();
        _orderedLightRanks.Clear();
        _localLightIndex.OverlapAabb(GetLocalLightFrustumBounds(in view, eye, far), _localLightQuery);
        if (_localLightQuery.Count == 0)
            return;

        Matrix4x4 vp = view.CameraVP;
        Vector3 right = new(vp.M11, vp.M21, vp.M31);
        Vector3 up = new(vp.M12, vp.M22, vp.M32);
        Vector3 forward = new(vp.M14, vp.M24, vp.M34);
        float rightScaleSq = right.LengthSquared();
        float upScaleSq = up.LengthSquared();
        float xPlaneLength = MathF.Sqrt(1f + rightScaleSq);
        float yPlaneLength = MathF.Sqrt(1f + upScaleSq);
        float width = view.ViewportSizeInvSize.X;
        float height = view.ViewportSizeInvSize.Y;
        float focalPixels = 0.5f * MathF.Max(MathF.Sqrt(rightScaleSq) * width, MathF.Sqrt(upScaleSq) * height);
        float wideRadius = 0.5f * MathF.Max(width, height);
        int wideBudget = Math.Clamp(LocalLights.MaxWideViewLights, 0, budget);
        int visible = 0;
        int wide = 0;
        foreach (int slot in _localLightQuery)
        {
            PointLightEntry entry = _localLights.GetLive(slot);
            ref readonly World3DPointLight light = ref entry.Description;
            double energy = entry.Energy;
            if (energy <= 0)
                continue;
            Vector3 relative = SubtractPointLightPosition(light.Position, eye);
            float radius = MathF.Max(MinPointLightInfluenceRadius, light.Radius);
            Vector3 magnitude = Vector3.Abs(relative);
            float tolerance = 0.0001f + 2e-6f * MathF.Max(radius, MathF.Max(magnitude.X, MathF.Max(magnitude.Y, magnitude.Z)));
            float depth = Vector3.Dot(relative, forward);
            float front = depth - radius;
            float back = depth + radius + tolerance;
            float conservativeRadius = radius + tolerance;
            if (back < near || front - tolerance > far ||
                MathF.Abs(Vector3.Dot(relative, right)) > depth + conservativeRadius * xPlaneLength ||
                MathF.Abs(Vector3.Dot(relative, up)) > depth + conservativeRadius * yPlaneLength)
                continue;
            ++visible;

            float screenRadius = MathF.Min(radius / MathF.Max(near, front) * focalPixels, 2f * wideRadius);
            float surfaceDistance = MathF.Max(0f, relative.Length() - radius);
            float score = (float)Math.Min(float.MaxValue, energy * MathF.Min(1f, screenRadius / 24f) / (1f + surfaceDistance));
            RankedPointLight rank = new(slot, score, screenRadius, back);
            if (screenRadius >= wideRadius)
            {
                ++wide;
                RetainLightRank(_wideLightRanks, in rank, wideBudget);
            }
            else if (_orderedLightRanks.Count < budget && _regularLightRanks.Count == 0)
            {
                _orderedLightRanks.Add(rank);
            }
            else
            {
                if (_regularLightRanks.Count == 0)
                {
                    foreach (RankedPointLight previous in _orderedLightRanks)
                        _regularLightRanks.Enqueue(previous, previous.Priority);
                    _orderedLightRanks.Clear();
                }
                RetainLightRank(_regularLightRanks, in rank, budget);
            }
        }

        AppendLightRanks(_regularLightRanks);
        AppendLightRanks(_wideLightRanks);
        CollectionsMarshal.AsSpan(_orderedLightRanks).Sort();
        int count = Math.Min(_orderedLightRanks.Count, budget);
        float depthExtent = near * (1f + 1e-4f);
        for (int i = 0; i < count; ++i)
        {
            RankedPointLight rank = _orderedLightRanks[i];
            PointLightEntry entry = _localLights.GetLive(rank.Slot);
            _selectedPointLights.Add(new SelectedPointLight(rank.Slot, entry.Generation, entry.Description, rank.Score, rank.ScreenRadius));
            depthExtent = MathF.Max(depthExtent, rank.Back);
        }
        _recordedLocalLightDepth = MathF.Min(far, depthExtent);
        LastRecordedLocalLights = new(_localLights.Count, _localLightQuery.Count, visible, count, Math.Max(0, wide - wideBudget));
    }

    private void StageLocalLightCandidates(RenderContext context)
    {
        ReadOnlySpan<SelectedPointLight> selected = CollectionsMarshal.AsSpan(_selectedPointLights);
        _recordedLocalLightCount = selected.Length;
        if (selected.IsEmpty)
            return;
        Span<World3DLightClusterCandidateGpu> candidates = context.AllocateTransient<World3DLightClusterCandidateGpu>(selected.Length,
            out _frameLocalLightCandidates);
        for (int i = 0; i < candidates.Length; ++i)
            candidates[i] = new World3DLightClusterCandidateGpu((uint)selected[i].Slot, selected[i].ShadowIndexPlusOne);
        _gpu.SetLocalLightReadPages(selected);
    }

    private void PrepareVoxelPointLights(RenderContext context, in World3DView view, in World3DVoxelClipmap.FramePlan plan)
    {
        int budget = Math.Clamp(VoxelGi.MaxPointLights, 0, 256);
        if (budget == 0 || _localLights.Count == 0)
            return;

        Vector4 origin = plan.Cascades[^1].OriginVoxelSize;
        double x = (double)view.OriginAnchorCell.X * World3DSceneCoordinates.CellSize + origin.X;
        double y = (double)view.OriginAnchorCell.Y * World3DSceneCoordinates.CellSize + origin.Y;
        double z = (double)view.OriginAnchorCell.Z * World3DSceneCoordinates.CellSize + origin.Z;
        double side = (double)origin.W * plan.Resolution;
        World3DPosition center = new(x + side / 2, y + side / 2, z + side / 2);
        _localLightQuery.Clear();
        _regularLightRanks.Clear();
        _localLightIndex.OverlapAabb(new RegionalBounds(x, y, z, x + side, y + side, z + side), _localLightQuery);
        Vector3 halfExtent = new((float)(side / 2));
        foreach (int slot in _localLightQuery)
        {
            PointLightEntry entry = _localLights.GetLive(slot);
            ref readonly World3DPointLight light = ref entry.Description;
            double energy = entry.Energy;
            if (!light.AffectsGlobalIllumination || energy <= 0)
                continue;
            Vector3 offset = SubtractPointLightPosition(light.Position, center);
            Vector3 separation = Vector3.Max(Vector3.Abs(offset) - halfExtent, Vector3.Zero);
            float radius = MathF.Max(MinPointLightInfluenceRadius, light.Radius);
            if (separation.LengthSquared() > radius * radius)
                continue;
            float score = (float)Math.Min(float.MaxValue, energy / (1.0 + offset.LengthSquared()));
            RankedPointLight rank = new(slot, score, 0f, 0f);
            RetainLightRank(_regularLightRanks, in rank, budget);
        }
        _recordedPointLightCount = _regularLightRanks.Count;
        if (_recordedPointLightCount == 0)
            return;
        Span<PointLightGpu> rows = context.AllocateTransient<PointLightGpu>(_recordedPointLightCount, out _framePointLights);
        for (int i = rows.Length - 1; i >= 0; --i)
        {
            World3DLocalLightGpu row = _localLights.GetLive(_regularLightRanks.Dequeue().Slot).Gpu;
            rows[i] = new PointLightGpu(row.Cell, row.LocalPositionAndRadius, row.ColorAndIntensity);
        }
    }

    private static double GetPointLightEnergy(in World3DPointLight light) =>
        (double)MathF.Max(light.Color.X, MathF.Max(light.Color.Y, light.Color.Z)) * light.Intensity;

    private static void RetainLightRank(PriorityQueue<RankedPointLight, ulong> queue,
        in RankedPointLight rank, int capacity)
    {
        if (capacity == 0)
            return;
        if (queue.Count < capacity)
            queue.Enqueue(rank, rank.Priority);
        else
            queue.EnqueueDequeue(rank, rank.Priority);
    }

    private void AppendLightRanks(PriorityQueue<RankedPointLight, ulong> queue)
    {
        foreach ((RankedPointLight rank, _) in queue.UnorderedItems)
            _orderedLightRanks.Add(rank);
    }

    private static Vector3 SubtractPointLightPosition(World3DPosition position, World3DPosition origin) =>
        new((float)(position.X - origin.X), (float)(position.Y - origin.Y), (float)(position.Z - origin.Z));

    private static RegionalBounds GetLocalLightFrustumBounds(in World3DView view, World3DPosition eye, float far)
    {
        Vector3 min = Vector3.Zero;
        Vector3 max = Vector3.Zero;
        for (int i = 0; i < 4; ++i)
        {
            Vector4 ray = Vector4.Transform(new Vector4((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, 0f, 1f), view.CameraInvVP);
            Vector3 direction = new(ray.X, ray.Y, ray.Z);
            min = Vector3.Min(min, direction);
            max = Vector3.Max(max, direction);
        }
        double margin = 0.001 + 2e-6 * far;
        return new RegionalBounds(eye.X + min.X * (double)far - margin, eye.Y + min.Y * (double)far - margin,
            eye.Z + min.Z * (double)far - margin, eye.X + max.X * (double)far + margin,
            eye.Y + max.Y * (double)far + margin, eye.Z + max.Z * (double)far + margin);
    }

    private static RegionalBounds GetPointLightBounds(in World3DPointLight light)
    {
        World3DPosition p = light.Position;
        float r = MathF.Max(MinPointLightInfluenceRadius, light.Radius);
        return new RegionalBounds(p.X - r, p.Y - r, p.Z - r, p.X + r, p.Y + r, p.Z + r);
    }

    private readonly record struct RegionalBounds(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
    {
        /// <summary>Rounds outwards so a float BVH never rejects a touching bound.</summary>
        public BoundingBox3D RelativeTo(World3DPosition origin) => new(
            new Vector3(RoundDown(MinX - origin.X), RoundDown(MinY - origin.Y), RoundDown(MinZ - origin.Z)),
            new Vector3(RoundUp(MaxX - origin.X), RoundUp(MaxY - origin.Y), RoundUp(MaxZ - origin.Z)));

        private static float RoundDown(double value) => (float)value > value ? float.BitDecrement((float)value) : (float)value;
        private static float RoundUp(double value) => (float)value < value ? float.BitIncrement((float)value) : (float)value;
    }

    private readonly record struct RegionIndexHandle(SpatialRegion Region, int Node);

    private sealed class SpatialRegion(World3DCell key, World3DPosition origin)
    {
        public World3DCell Key { get; } = key;
        public World3DPosition Origin { get; } = origin;
        public DynamicBvhAabb3D Leaves { get; } = new(initialCapacity: 4, fattenMargin: 0.25f);
        public int Slot { get; set; }
        public int Node { get; set; }

        /// <summary>Translates the local root bound conservatively for the outer BVH.</summary>
        public BoundingBox3D GetOuterBounds()
        {
            BoundingBox3D bounds = Leaves.RootBounds;
            return new RegionalBounds(Origin.X + bounds.Min.X, Origin.Y + bounds.Min.Y, Origin.Z + bounds.Min.Z,
                Origin.X + bounds.Max.X, Origin.Y + bounds.Max.Y, Origin.Z + bounds.Max.Z).RelativeTo(default);
        }
    }

    private sealed class RegionalSpatialIndex
    {
        // Only region roots use absolute floats, whose ULP can be kilometers at the edge of the supported world.
        private const int RegionSize = 32 * World3DSceneCoordinates.CellSize;
        private readonly DynamicBvhAabb3D _regions = new();
        private readonly SlotMap<SpatialRegion> _regionSlots = new();
        private readonly Dictionary<World3DCell, SpatialRegion> _regionsByCell = [];
        private readonly List<int> _regionHits = [];

        /// <summary>Indexes a bound relative to a stable regional origin near its reference position.</summary>
        public RegionIndexHandle Insert(int slot, World3DPosition originReference, in RegionalBounds bounds)
        {
            World3DCell key = GetRegionCell(originReference);
            if (_regionsByCell.TryGetValue(key, out SpatialRegion? region))
            {
                int node = region.Leaves.Insert(bounds.RelativeTo(region.Origin), slot);
                _regions.UpdateBounds(region.Node, region.GetOuterBounds());
                return new RegionIndexHandle(region, node);
            }

            World3DPosition origin = new((double)key.X * RegionSize, (double)key.Y * RegionSize, (double)key.Z * RegionSize);
            region = new SpatialRegion(key, origin);
            if (!_regionSlots.TryAdd(region, out int regionSlot, out _))
                throw new InvalidOperationException("The regional index cannot outnumber the scene's slots.");
            region.Slot = regionSlot;
            int leaf = region.Leaves.Insert(bounds.RelativeTo(origin), slot);
            region.Node = _regions.Insert(region.GetOuterBounds(), regionSlot);
            _regionsByCell.Add(key, region);
            return new RegionIndexHandle(region, leaf);
        }

        /// <summary>Updates local bounds or moves a leaf between regions.</summary>
        public RegionIndexHandle Update(int slot, RegionIndexHandle handle, World3DPosition originReference, in RegionalBounds bounds)
        {
            SpatialRegion region = handle.Region;
            if (GetRegionCell(originReference) != region.Key)
            {
                Remove(handle);
                return Insert(slot, originReference, in bounds);
            }
            if (region.Leaves.UpdateBounds(handle.Node, bounds.RelativeTo(region.Origin)))
                _regions.UpdateBounds(region.Node, region.GetOuterBounds());
            return handle;
        }

        /// <summary>Removes a leaf and reclaims an empty region immediately.</summary>
        public void Remove(RegionIndexHandle handle)
        {
            SpatialRegion region = handle.Region;
            region.Leaves.Remove(handle.Node);
            if (region.Leaves.Count != 0)
            {
                _regions.UpdateBounds(region.Node, region.GetOuterBounds());
                return;
            }
            _regions.Remove(region.Node);
            _regionsByCell.Remove(region.Key);
            _regionSlots.Remove(region.Slot);
            _regionSlots.Reclaim(region.Slot);
        }

        /// <summary>Appends leaf hits using regional coordinates after the world-scale query.</summary>
        public void OverlapAabb(in RegionalBounds bounds, List<int> hits)
        {
            _regionHits.Clear();
            _regions.OverlapAabb(bounds.RelativeTo(default), _regionHits);
            foreach (int slot in _regionHits)
            {
                SpatialRegion region = _regionSlots.GetLive(slot);
                region.Leaves.OverlapAabb(bounds.RelativeTo(region.Origin), hits);
            }
        }

        /// <summary>Visits leaf overlaps until the visitor declines further work.</summary>
        public void VisitOverlapAabb<TVisitor>(in RegionalBounds bounds, ref TVisitor visitor)
            where TVisitor : struct, IBvhAabb3DLeafVisitor
        {
            RegionVisitor<TVisitor> regions = new(_regionSlots, in bounds, visitor);
            _regions.VisitOverlapAabb(bounds.RelativeTo(default), ref regions);
            visitor = regions.Leaves.Visitor;
        }

        private struct RegionVisitor<TVisitor>(SlotMap<SpatialRegion> regions, in RegionalBounds bounds, TVisitor visitor)
            : IBvhAabb3DLeafVisitor where TVisitor : struct, IBvhAabb3DLeafVisitor
        {
            private readonly RegionalBounds _bounds = bounds;
            public BoundedLeafVisitor<TVisitor> Leaves = new(visitor);

            /// <summary>Visits the region's leaves in its local coordinate system.</summary>
            public bool OnLeaf(int slot)
            {
                SpatialRegion region = regions.GetLive(slot);
                region.Leaves.VisitOverlapAabb(_bounds.RelativeTo(region.Origin), ref Leaves);
                return !Leaves.Stopped;
            }
        }

        private struct BoundedLeafVisitor<TVisitor>(TVisitor visitor) : IBvhAabb3DLeafVisitor
            where TVisitor : struct, IBvhAabb3DLeafVisitor
        {
            public TVisitor Visitor = visitor;
            public bool Stopped;

            public bool OnLeaf(int leaf)
            {
                Stopped = !Visitor.OnLeaf(leaf);
                return !Stopped;
            }
        }

        /// <summary>Clears both levels of the index.</summary>
        public void Clear()
        {
            _regions.Clear();
            _regionSlots.Clear();
            _regionsByCell.Clear();
            _regionHits.Clear();
        }

        private static World3DCell GetRegionCell(World3DPosition p) =>
            new((int)Math.Floor(p.X / RegionSize), (int)Math.Floor(p.Y / RegionSize), (int)Math.Floor(p.Z / RegionSize));
    }

    private sealed class PointLightEntry(SectorEntry? sector, in World3DPointLight description, in World3DLocalLightGpu gpu)
    {
        public SectorEntry? Sector { get; } = sector;
        public World3DPointLight Description = description;
        public double Energy { get; set; } = GetPointLightEnergy(in description);
        public World3DLocalLightGpu Gpu { get; set; } = gpu;
        public uint Generation { get; set; }
        public RegionIndexHandle IndexHandle { get; set; }
    }

    private struct SelectedPointLight(int slot, uint generation, World3DPointLight description, float score, float screenRadius)
    {
        public readonly int Slot = slot;
        public readonly uint Generation = generation;
        public readonly World3DPointLight Description = description;
        public readonly float Score = score;
        public readonly float ScreenRadius = screenRadius;
        public uint ShadowIndexPlusOne;
    }

    private readonly record struct RankedPointLight(ulong Priority, float ScreenRadius, float Back) : IComparable<RankedPointLight>
    {
        // Nonnegative float bits sort numerically. For equal scores, the lower slot wins.
        public RankedPointLight(int slot, float score, float screenRadius, float back)
            : this(((ulong)BitConverter.SingleToUInt32Bits(score) << 32) | (uint)~slot, screenRadius, back) { }

        public int Slot => (int)~(uint)Priority;
        public float Score => BitConverter.UInt32BitsToSingle((uint)(Priority >> 32));
        public int CompareTo(RankedPointLight other) => other.Priority.CompareTo(Priority);
    }

    [StructLayout(LayoutKind.Sequential, Size = 8)]
    private readonly record struct World3DLightClusterCandidateGpu(uint Slot, uint ShadowIndexPlusOne);
}
