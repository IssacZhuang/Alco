using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.World3D;

/// <summary>Controls the work and fade-in of the local point shadow cache.</summary>
public sealed class World3DPointShadowSettings
{
    /// <summary>Gets or sets whether opted-in point lights can render and sample shadows.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the maximum number of depth faces updated per frame.</summary>
    public int MaxUpdatedFaces { get; set; } = 12;

    /// <summary>Gets or sets the maximum number of depth texels updated per frame.</summary>
    public int MaxUpdatedTexels { get; set; } = 524288;

    /// <summary>Gets or sets the maximum number of face and payload-page candidate commands per frame.</summary>
    public int MaxCasterCandidates { get; set; } = 8192;

    /// <summary>Gets or sets the time in seconds to fade in a newly available face. Zero disables the fade.</summary>
    public float FadeInSeconds { get; set; } = 0.15f;
}

/// <summary>Summarizes the work and usable pages of the most recently recorded point shadow frame.</summary>
/// <param name="CachedLights">Selected lights with at least one usable depth face.</param>
/// <param name="UpdatedFaces">Depth faces recorded for update, including clear-only faces.</param>
/// <param name="UpdatedTexels">Total texels in the updated faces.</param>
/// <param name="DeferredFaces">Requested faces not updated because a complete update did not fit.</param>
/// <param name="CasterCandidates">Local candidate commands uploaded for the recorded updates.</param>
public readonly record struct World3DPointShadowStats(int CachedLights, int UpdatedFaces, int UpdatedTexels,
    int DeferredFaces, int CasterCandidates)
{
    /// <summary>Gets the requested lights whose six faces were already usable.</summary>
    public int CacheHits { get; init; }

    /// <summary>Gets the requested lights with at least one missing, stale, or dirty face.</summary>
    public int CacheMisses { get; init; }

    /// <summary>Gets the occupied pages reassigned to different lights in the recording.</summary>
    public int PageEvictions { get; init; }
}

public sealed partial class World3DScene
{
    private const int PointShadowFaceCount = 6;
    private const uint PointShadowAllFaces = (1u << PointShadowFaceCount) - 1;
    private const int PointShadowPageCount = 40 + 24 + 8;
    private const int PointShadowSearchWorkPerFace = 65536;
    private static readonly PointShadowTier[] PointShadowTiers = [new(64, 0, 40), new(128, 40, 24), new(256, 64, 8)];

    private readonly PointShadowCasterIndex _pointShadowCasters = new();
    private PointShadowCachePage[] _pointShadowCache = new PointShadowCachePage[PointShadowPageCount];
    private PointShadowCachePage[] _pointShadowPending = new PointShadowCachePage[PointShadowPageCount];
    private readonly List<PointShadowRequest> _pointShadowRequests = [];
    private readonly Dictionary<PointShadowLightKey, int> _pointShadowPageByLight = new(PointShadowPageCount);
    private readonly int[] _pointShadowPageRanks = new int[PointShadowPageCount];
    private readonly World3DPointShadowPageGpu[] _pointShadowPageRows = new World3DPointShadowPageGpu[PointShadowPageCount];
    private ulong _pointShadowSubmittedFrame;
    private bool _pointShadowPendingPrepared;

    /// <summary>Gets the local point light shadow configuration.</summary>
    public World3DPointShadowSettings PointShadows { get; } = new();

    /// <summary>Gets the point shadow work and residency of the most recently recorded frame.</summary>
    public World3DPointShadowStats LastRecordedPointShadows { get; private set; }

    private void AddPointShadowCaster(int slot, InstanceEntry instance) => RefreshPointShadowCaster(slot, instance);

    private void RemovePointShadowCaster(int slot, InstanceEntry instance)
    {
        if (_pointShadowCasters.TryRemove(slot, out PointShadowBounds bounds))
            InvalidatePointShadowFaces(_pointShadowCache, in bounds);
    }

    private void UpdatePointShadowCaster(int slot, InstanceEntry previous, InstanceEntry current)
    {
        World3DInstanceDesc a = previous.Description;
        World3DInstanceDesc b = current.Description;
        if (a.Transform == b.Transform && a.Geometry == b.Geometry && a.Material == b.Material && a.CastsShadow == b.CastsShadow
            && (a.Fade == b.Fade || current.Material.Route.AlphaMode != World3DAlphaMode.Mask))
            return;
        RefreshPointShadowCaster(slot, current);
    }

    private void RefreshPointShadowCaster(int slot, InstanceEntry instance)
    {
        if (!instance.Description.CastsShadow || instance.Material.Route.AlphaMode == World3DAlphaMode.Blend)
        {
            RemovePointShadowCaster(slot, instance);
            return;
        }

        PointShadowBounds next = GetPointShadowBounds(instance.Spatial);
        if (_pointShadowCasters.TryGetBounds(slot, out PointShadowBounds previous) && previous != next)
            InvalidatePointShadowFaces(_pointShadowCache, in previous);
        _pointShadowCasters.Set(slot, instance.Description.Transform.Position, in next);
        InvalidatePointShadowFaces(_pointShadowCache, in next);
    }

    private void ForgetPointShadowLight(int slot, uint generation)
    {
        PointShadowLightKey key = new(slot, generation);
        for (int i = 0; i < _pointShadowCache.Length; ++i)
        {
            if (_pointShadowCache[i].Light == key)
            {
                _pointShadowCache[i] = default;
                return;
            }
        }
    }

    private void PreparePointShadows(RenderContext context, float deltaTime)
    {
        ResetPointShadowRecording();
        _pointShadowRequests.Clear();
        _pointShadowPageByLight.Clear();
        _pointShadowPendingPrepared = true;
        _pointShadowCache.AsSpan().CopyTo(_pointShadowPending);

        // A sync-only submit may also change resident LODs. Stage invalidation so discard preserves the old cache.
        if (_pointShadowTextures[0] != null)
        {
            foreach (GeometryEntry geometry in _stagedDrawLods.Keys)
            {
                foreach (int slot in geometry.Instances)
                {
                    if (_pointShadowCasters.TryGetBounds(slot, out PointShadowBounds bounds))
                        InvalidatePointShadowFaces(_pointShadowPending, in bounds);
                }
            }
        }
        if (!_recordedWorld || !PointShadows.Enabled)
            return;

        Span<SelectedPointLight> selected = CollectionsMarshal.AsSpan(_selectedPointLights);
        bool ordersDiffer = false;
        double previousImportance = double.PositiveInfinity;
        for (int i = 0; i < selected.Length; ++i)
        {
            ref readonly SelectedPointLight light = ref selected[i];
            if (!light.Description.CastsShadow)
                continue;
            double importance = (double)light.Score * light.Description.ShadowPriority;
            ordersDiffer |= importance > previousImportance;
            previousImportance = importance;
            _pointShadowRequests.Add(new PointShadowRequest(i, importance));
        }
        if (_pointShadowRequests.Count == 0)
            return;

        if (ordersDiffer)
            _pointShadowRequests.Sort(static (a, b) => a.Importance != b.Importance
                ? b.Importance.CompareTo(a.Importance) : a.SelectedIndex.CompareTo(b.SelectedIndex));
        Array.Fill(_pointShadowPageRanks, int.MaxValue);
        for (int i = 0; i < _pointShadowPending.Length; ++i)
        {
            PointShadowLightKey key = _pointShadowPending[i].Light;
            if (key.Generation != 0u)
                _pointShadowPageByLight.Add(key, i);
        }
        for (int i = 0; i < _pointShadowRequests.Count; ++i)
        {
            ref readonly SelectedPointLight light = ref selected[_pointShadowRequests[i].SelectedIndex];
            if (_pointShadowPageByLight.TryGetValue(new PointShadowLightKey(light.Slot, light.Generation), out int page))
                _pointShadowPageRanks[page] = i;
        }

        int candidateLimit = Math.Clamp(PointShadows.MaxCasterCandidates, 0, 65536);
        PointShadowBudget budget = new()
        {
            Faces = Math.Clamp(PointShadows.MaxUpdatedFaces, 0, PointShadowPageCount * PointShadowFaceCount),
            Texels = Math.Clamp(PointShadows.MaxUpdatedTexels, 0, 524288),
            CandidateLimit = candidateLimit,
            SearchWork = Math.Max(4096, candidateLimit * 8),
        };
        int deferredFaces = 0;
        for (int rank = 0; rank < _pointShadowRequests.Count; ++rank)
        {
            ref readonly SelectedPointLight light = ref selected[_pointShadowRequests[rank].SelectedIndex];
            deferredFaces += PreparePointShadowLight(in light, rank, ref budget);
        }

        if (_pointShadowFaceUpdates.Count != 0 && !TryEnsurePointShadowTextures())
        {
            deferredFaces += _pointShadowFaceUpdates.Count;
            ResetPointShadowRecording();
            _pointShadowPendingPrepared = false;
            LastRecordedPointShadows = new World3DPointShadowStats(0, 0, 0, deferredFaces, 0)
            {
                CacheHits = budget.CacheHits,
                CacheMisses = budget.CacheMisses,
            };
            return;
        }

        float fadeSeconds = PointShadows.FadeInSeconds;
        float fadeStep = float.IsFinite(fadeSeconds) && fadeSeconds > 0f
            ? Math.Clamp(float.IsFinite(deltaTime) ? deltaTime / fadeSeconds : 0f, 0f, 1f) : 1f;
        int cachedLights = PublishPointShadowPages(context, selected, fadeStep);
        UploadPointShadowUpdates(context);
        LastRecordedPointShadows = new World3DPointShadowStats(cachedLights, _pointShadowFaceUpdates.Count,
            Math.Clamp(PointShadows.MaxUpdatedTexels, 0, 524288) - budget.Texels, deferredFaces, _pointShadowCandidates.Count)
        {
            CacheHits = budget.CacheHits,
            CacheMisses = budget.CacheMisses,
            PageEvictions = budget.PageEvictions,
        };
    }

    private int PreparePointShadowLight(in SelectedPointLight light, int rank, ref PointShadowBudget budget)
    {
        PointShadowLightKey key = new(light.Slot, light.Generation);
        int oldPage = _pointShadowPageByLight.GetValueOrDefault(key, -1);
        if (oldPage < 0 && (!budget.CanUpdate(1, PointShadowTiers[0].Texels) || budget.SearchWork == 0))
        {
            ++budget.CacheMisses;
            return PointShadowFaceCount;
        }
        PointShadowProjection projection = new(light.Description.Position, light.Description.Radius);
        bool moved = oldPage >= 0 && _pointShadowPending[oldPage].Projection != projection;
        int desiredTier = oldPage >= 0 ? GetPointShadowTier(oldPage) : light.ScreenRadius switch
        {
            >= 96f => 2,
            >= 32f => 1,
            _ => 0,
        };
        uint faces = oldPage >= 0 && !moved ? PointShadowAllFaces & ~_pointShadowPending[oldPage].UsableFaces : PointShadowAllFaces;
        if (faces == 0u)
        {
            ++budget.CacheHits;
            return 0;
        }
        ++budget.CacheMisses;

        if (moved)
            return PrepareMovedPointShadow(key, in projection, rank, oldPage, desiredTier, ref budget);
        int page = oldPage >= 0 ? oldPage : FindPointShadowPage(rank, desiredTier, 1, in budget);
        if (page < 0 || budget.SearchWork == 0)
            return BitOperations.PopCount(faces);

        int tier = GetPointShadowTier(page);
        int texels = PointShadowTiers[tier].Texels;
        uint updated = 0u;
        for (uint remaining = faces; remaining != 0u && budget.CanUpdate(1, texels); remaining &= remaining - 1u)
        {
            int face = BitOperations.TrailingZeroCount(remaining);
            if (!TryAppendPointShadowFace(page, tier, face, in projection, ref budget))
                continue;
            updated |= 1u << face;
            --budget.Faces;
            budget.Texels -= texels;
        }
        if (updated != 0u)
        {
            if (oldPage < 0)
                AssignPointShadowPage(page, key, in projection, rank, ref budget);
            ref PointShadowCachePage entry = ref _pointShadowPending[page];
            entry.ValidFaces |= updated;
            entry.DirtyFaces &= ~updated;
        }
        return BitOperations.PopCount(faces & ~updated);
    }

    private int PrepareMovedPointShadow(PointShadowLightKey key, in PointShadowProjection projection, int rank, int oldPage,
        int desiredTier, ref PointShadowBudget budget)
    {
        // The old transform must never be sampled with the new light vector. Retain the prior fade only when
        // all faces can be captured in one recording, so continuously moving lights need not fade every frame.
        int page = budget.CanUpdate(PointShadowFaceCount, PointShadowTiers[desiredTier].Texels)
            ? oldPage : FindPointShadowPage(rank, desiredTier - 1, PointShadowFaceCount, in budget);
        if (page < 0 || budget.SearchWork == 0)
        {
            _pointShadowPending[oldPage].Fades = default;
            return PointShadowFaceCount;
        }

        int tier = GetPointShadowTier(page);
        int updateStart = _pointShadowFaceUpdates.Count;
        int batchStart = _pointShadowBatches.Count;
        int candidateStart = _pointShadowCandidates.Count;
        for (int face = 0; face < PointShadowFaceCount; ++face)
        {
            if (TryAppendPointShadowFace(page, tier, face, in projection, ref budget))
                continue;
            _pointShadowFaceUpdates.RemoveRange(updateStart, _pointShadowFaceUpdates.Count - updateStart);
            _pointShadowBatches.RemoveRange(batchStart, _pointShadowBatches.Count - batchStart);
            _pointShadowCandidates.RemoveRange(candidateStart, _pointShadowCandidates.Count - candidateStart);
            _pointShadowPending[oldPage].Fades = default;
            return PointShadowFaceCount;
        }

        PointShadowFaceFades previousFades = _pointShadowPending[oldPage].Fades;
        if (page != oldPage)
        {
            _pointShadowPending[oldPage] = default;
            _pointShadowPageRanks[oldPage] = int.MaxValue;
            _pointShadowPageByLight.Remove(key);
        }
        AssignPointShadowPage(page, key, in projection, rank, ref budget);
        _pointShadowPending[page].ValidFaces = PointShadowAllFaces;
        _pointShadowPending[page].Fades = previousFades;
        budget.Faces -= PointShadowFaceCount;
        budget.Texels -= PointShadowFaceCount * PointShadowTiers[tier].Texels;
        return 0;
    }

    private int FindPointShadowPage(int rank, int desiredTier, int faces, in PointShadowBudget budget)
    {
        int victim = -1;
        int victimRank = -1;
        for (int tier = desiredTier; tier >= 0; --tier)
        {
            PointShadowTier desc = PointShadowTiers[tier];
            if (!budget.CanUpdate(faces, desc.Texels))
                continue;
            for (int page = desc.FirstPage; page < desc.FirstPage + desc.PageCount; ++page)
            {
                ref readonly PointShadowCachePage entry = ref _pointShadowPending[page];
                if (entry.Light.Generation == 0u)
                    return page;
                int incumbentRank = _pointShadowPageRanks[page];
                // Keep already processed pages and avoid churn between nearly equally important visible lights.
                if (incumbentRank <= rank || (incumbentRank != int.MaxValue &&
                    _pointShadowRequests[rank].Importance <= 1.2 * _pointShadowRequests[incumbentRank].Importance))
                    continue;
                if (victim < 0 || incumbentRank > victimRank || (incumbentRank == victimRank &&
                    entry.LastUsedFrame < _pointShadowPending[victim].LastUsedFrame))
                {
                    victim = page;
                    victimRank = incumbentRank;
                }
            }
        }
        return victim;
    }

    private void AssignPointShadowPage(int page, PointShadowLightKey key, in PointShadowProjection projection, int rank,
        ref PointShadowBudget budget)
    {
        PointShadowLightKey previous = _pointShadowPending[page].Light;
        if (previous.Generation != 0u && previous != key)
            ++budget.PageEvictions;
        _pointShadowPageByLight.Remove(previous);
        _pointShadowPending[page] = new PointShadowCachePage { Light = key, Projection = projection };
        _pointShadowPageByLight.Add(key, page);
        _pointShadowPageRanks[page] = rank;
    }

    private int PublishPointShadowPages(RenderContext context, Span<SelectedPointLight> selected, float fadeStep)
    {
        int count = 0;
        Array.Clear(_pointShadowPageRows);
        for (int page = 0; page < _pointShadowPending.Length; ++page)
        {
            int rank = _pointShadowPageRanks[page];
            if (rank == int.MaxValue)
                continue;
            ref SelectedPointLight light = ref selected[_pointShadowRequests[rank].SelectedIndex];
            ref PointShadowCachePage entry = ref _pointShadowPending[page];
            if (entry.Projection != new PointShadowProjection(light.Description.Position, light.Description.Radius))
                continue;
            uint usable = entry.UsableFaces;
            for (int face = 0; face < PointShadowFaceCount; ++face)
            {
                ref float fade = ref entry.Fades[face];
                fade = (usable & (1u << face)) != 0u ? Math.Min(1f, fade + fadeStep) : 0f;
            }
            entry.LastUsedFrame = _pointShadowSubmittedFrame + 1;
            if (usable == 0u)
                continue;

            int tier = GetPointShadowTier(page);
            uint firstLayer = (uint)((page - PointShadowTiers[tier].FirstPage) * PointShadowFaceCount);
            ref PointShadowFaceFades fades = ref entry.Fades;
            _pointShadowPageRows[page] = new World3DPointShadowPageGpu((uint)tier, firstLayer, usable,
                entry.Projection.Near, entry.Projection.Radius, fades[0], fades[1], fades[2], fades[3], fades[4], fades[5], 0u);
            _pointShadowSampledTiers |= 1u << tier;
            light.ShadowIndexPlusOne = (uint)page + 1u;
            ++count;
        }
        if (count != 0)
            _pointShadowFramePages = context.UploadTransient<World3DPointShadowPageGpu>(_pointShadowPageRows);
        return count;
    }

    private void ReconcilePointShadows(bool submitted)
    {
        if (!_pointShadowPendingPrepared)
            return;
        if (submitted)
        {
            (_pointShadowCache, _pointShadowPending) = (_pointShadowPending, _pointShadowCache);
            ++_pointShadowSubmittedFrame;
        }
        _pointShadowPendingPrepared = false;
    }

    private static void InvalidatePointShadowFaces(Span<PointShadowCachePage> pages, in PointShadowBounds bounds)
    {
        for (int i = 0; i < pages.Length; ++i)
        {
            ref PointShadowCachePage page = ref pages[i];
            uint usable = page.UsableFaces;
            if (usable == 0u || !PointShadowBoundsOverlapLight(in bounds, in page.Projection))
                continue;
            while (usable != 0u)
            {
                int face = BitOperations.TrailingZeroCount(usable);
                if (PointShadowBoundsOverlapFace(in bounds, in page.Projection, face))
                    page.DirtyFaces |= 1u << face;
                usable &= usable - 1u;
            }
        }
    }

    private static int GetPointShadowTier(int page) => page >= PointShadowTiers[2].FirstPage ? 2
        : page >= PointShadowTiers[1].FirstPage ? 1 : 0;

    private readonly record struct PointShadowTier(int Resolution, int FirstPage, int PageCount)
    {
        public int Texels => Resolution * Resolution;
    }

    private readonly record struct PointShadowLightKey(int Slot, uint Generation);
    private readonly record struct PointShadowRequest(int SelectedIndex, double Importance);

    private readonly record struct PointShadowProjection(World3DPosition Position, float Radius)
    {
        public float Near => Math.Max(Radius / 4096f, Math.Min(Radius * 0.25f, 0.005f));
    }

    [InlineArray(PointShadowFaceCount)]
    private struct PointShadowFaceFades
    {
        private float _first;
    }

    private struct PointShadowCachePage
    {
        public PointShadowLightKey Light;
        public PointShadowProjection Projection;
        public uint ValidFaces;
        public uint DirtyFaces;
        public PointShadowFaceFades Fades;
        public ulong LastUsedFrame;
        public readonly uint UsableFaces => ValidFaces & ~DirtyFaces;
    }

    private struct PointShadowBudget
    {
        public int Faces;
        public int Texels;
        public int CandidateLimit;
        public int SearchWork;
        public int CacheHits;
        public int CacheMisses;
        public int PageEvictions;
        public readonly bool CanUpdate(int faces, int texelsPerFace) => Faces >= faces && Texels >= faces * texelsPerFace;
    }
}
