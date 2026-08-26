using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco;

/// <summary>
/// A chunked collision world for static or rarely-mutating free-position colliders.
/// It complements <see cref="CollisionWorld2D"/> (rebuilt per tick for dynamic colliders):
/// targets are inserted once at their exact free position and removed on pickup, with no
/// per-tick rebuild cost. The world divides the grid into square chunks; each chunk owns a
/// bucket of leaf indices, and buckets that exceed <see cref="TreeBuildThreshold"/> entries
/// are promoted to a mini <see cref="NativeBvh2D"/> during <see cref="RebuildDirtyTrees"/>.
/// Colliders whose AABB spans more than <see cref="BigBucketSpanFactor"/> chunk sizes are
/// routed to a separate big-object bucket (promoted to a tree by the same rule) so they cannot
/// inflate every local bucket they overlap.
/// <para>
/// Threading contract: mutations (<see cref="Add(object, in ShapeBox2D)"/>,
/// <see cref="TargetRegistration.Dispose"/>, <see cref="Clear"/> and
/// <see cref="RebuildDirtyTrees"/>) are main-thread only; once no mutations are pending, all
/// cast methods are read-only and safe to call from multiple threads concurrently. Mutating a
/// chunk immediately drops its tree and marks it pending, so queries never observe stale trees;
/// the next <see cref="RebuildDirtyTrees"/> re-promotes buckets above the threshold.
/// </para>
/// <para>
/// Ray semantics: rays are segments; <paramref name="maxT"/> bounds the accepted hit fraction
/// in displacement units on both the traversal side and the precise test side, so traversal
/// pruning never disagrees with the precise tests. Collect queries may report a leaf more than
/// once when its AABB spans several visited chunks; collectors that require uniqueness
/// (e.g. a HashSet) deduplicate naturally.
/// </para>
/// </summary>
public unsafe class ChunkedCollisionWorld2D : AutoDisposable
{
    /// <summary>
    /// Bucket size at which a chunk (or the big-object bucket) is promoted to a mini BVH during
    /// the next <see cref="RebuildDirtyTrees"/>. Any mutation of the bucket demotes it back to
    /// a linear scan until it reaches the threshold again.
    /// </summary>
    public int TreeBuildThreshold { get; set; } = 64;

    /// <summary>
    /// Gets the AABB span, as a multiple of the chunk size, above which colliders are routed to
    /// the big-object bucket instead of every overlapped per-chunk bucket.
    /// </summary>
    public float BigBucketSpanFactor { get; set; } = 2f;

    private readonly float _gridMinX;
    private readonly float _gridMinY;
    private readonly int _chunkCountX;
    private readonly int _chunkCountY;
    private readonly int _chunkSize;

    private Leaf[] _leaves;
    private int _freeHead;
    private int _count;

    private readonly List<int>?[] _chunkBuckets;
    private readonly List<int> _bigBucket = new List<int>(64);
    private readonly ChunkTree?[] _chunkTrees;
    private readonly HashSet<int> _pendingChunks = new HashSet<int>();
    private ChunkTree? _bigTree;
    private bool _pendingBig;

    private MortonBvhBuilder2D _builder = new MortonBvhBuilder2D();

    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkedCollisionWorld2D"/> class over a
    /// fixed axis-aligned grid of square chunks. Collider AABBs are clamped into the grid, so a
    /// target may extend past the border as long as part of its AABB lies inside.
    /// </summary>
    /// <param name="gridMinX">The world X coordinate of the grid origin (left border).</param>
    /// <param name="gridMinY">The world Y coordinate of the grid origin (bottom border).</param>
    /// <param name="chunkCountX">The number of chunks along the X axis.</param>
    /// <param name="chunkCountY">The number of chunks along the Y axis.</param>
    /// <param name="chunkSize">The chunk edge length in world units.</param>
    public ChunkedCollisionWorld2D(float gridMinX, float gridMinY, int chunkCountX, int chunkCountY, int chunkSize)
    {
        if (chunkCountX <= 0 || chunkCountY <= 0)
        {
            throw new ArgumentException("chunk counts must be positive");
        }

        if (chunkSize <= 0)
        {
            throw new ArgumentException("chunk size must be positive");
        }

        _gridMinX = gridMinX;
        _gridMinY = gridMinY;
        _chunkCountX = chunkCountX;
        _chunkCountY = chunkCountY;
        _chunkSize = chunkSize;
        _leaves = new Leaf[64];
        _chunkBuckets = new List<int>?[chunkCountX * chunkCountY];
        _chunkTrees = new ChunkTree?[chunkCountX * chunkCountY];

        for (int i = 0; i < _leaves.Length; i++)
        {
            _leaves[i].FreeNext = i + 1;
        }

        _freeHead = 0;
    }

    /// <summary>
    /// Gets the number of alive targets currently registered in this world.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Gets the number of buckets currently promoted to a mini BVH (big-object tree included).
    /// Intended for diagnostics and debugging overlays.
    /// </summary>
    public int TreeCount
    {
        get
        {
            int trees = _bigTree != null ? 1 : 0;
            for (int i = 0; i < _chunkTrees.Length; i++)
            {
                if (_chunkTrees[i] != null)
                {
                    trees++;
                }
            }

            return trees;
        }
    }

    /// <summary>
    /// Gets the number of colliders routed to the big-object bucket.
    /// Intended for diagnostics and debugging overlays.
    /// </summary>
    public int BigBucketCount => _bigBucket.Count;

    /// <summary>
    /// Adds a box-shaped target at its exact free position and returns a registration whose
    /// disposal removes it again. Main thread only.
    /// </summary>
    /// <param name="target">The payload object reported back by all queries.</param>
    /// <param name="shape">The precise box shape of the target.</param>
    /// <returns>A registration handle; dispose it to remove the target.</returns>
    public TargetRegistration Add(object target, in ShapeBox2D shape)
    {
        int leafIndex = AddLeaf(target, ColliderType2D.Box, shape, default, shape.GetBoundingBox());
        return new TargetRegistration(this, leafIndex);
    }

    /// <summary>
    /// Adds a sphere-shaped target at its exact free position and returns a registration whose
    /// disposal removes it again. Main thread only.
    /// </summary>
    /// <param name="target">The payload object reported back by all queries.</param>
    /// <param name="shape">The precise sphere shape of the target.</param>
    /// <returns>A registration handle; dispose it to remove the target.</returns>
    public TargetRegistration Add(object target, in ShapeSphere2D shape)
    {
        int leafIndex = AddLeaf(target, ColliderType2D.Sphere, default, shape, shape.GetBoundingBox());
        return new TargetRegistration(this, leafIndex);
    }

    /// <summary>
    /// Removes all targets and returns the world to its freshly constructed state (threshold
    /// properties are preserved). Main thread only.
    /// </summary>
    public void Clear()
    {
        for (int i = 0; i < _chunkBuckets.Length; i++)
        {
            _chunkBuckets[i]?.Clear();
            _chunkTrees[i]?.Dispose();
            _chunkTrees[i] = null;
        }

        _bigBucket.Clear();
        _bigTree?.Dispose();
        _bigTree = null;
        _pendingChunks.Clear();
        _pendingBig = false;

        for (int i = 0; i < _leaves.Length; i++)
        {
            _leaves[i] = default;
            _leaves[i].FreeNext = i + 1;
        }

        _freeHead = 0;
        _count = 0;
    }

    /// <summary>
    /// Re-promotes buckets mutated since the last call into mini BVHs. Call on the main thread
    /// after a batch of adds/removals and before the parallel query phase of the frame,
    /// mirroring the rebuild-then-cast ordering of a per-tick collision service.
    /// </summary>
    public void RebuildDirtyTrees()
    {
        foreach (int chunkIndex in _pendingChunks)
        {
            PromoteBucket(_chunkBuckets[chunkIndex], chunkIndex);
        }

        _pendingChunks.Clear();

        if (_pendingBig)
        {
            _pendingBig = false;
            PromoteBucket(_bigBucket, -1);
        }
    }

    /// <summary>
    /// Casts a point against the world and collects all overlapping targets.
    /// </summary>
    /// <typeparam name="TCollector">The collector type gathering the hit targets.</typeparam>
    /// <param name="collector">The collector to gather hit results.</param>
    /// <param name="point">The point to test.</param>
    public void CastPoint<TCollector>(ref TCollector collector, Vector2 point) where TCollector : ICollisionCastCollector
    {
        if (_count == 0)
        {
            return;
        }

        int chunkIndex = ChunkIndexOf(point);
        if (_chunkTrees[chunkIndex] is { } tree)
        {
            CastTreePoint(tree, ref collector, point);
        }
        else
        {
            CastBucketPoint(_chunkBuckets[chunkIndex], ref collector, point);
        }

        if (_bigTree is { } bigTree)
        {
            CastTreePoint(bigTree, ref collector, point);
        }
        else
        {
            CastBucketPoint(_bigBucket, ref collector, point);
        }
    }

    /// <summary>
    /// Casts a box collider against the world and collects all overlapping targets.
    /// </summary>
    /// <typeparam name="TCollector">The collector type gathering the hit targets.</typeparam>
    /// <param name="collector">The collector to gather hit results.</param>
    /// <param name="shape">The box shape to cast.</param>
    public void CastBox<TCollector>(ref TCollector collector, in ShapeBox2D shape) where TCollector : ICollisionCastCollector
    {
        if (_count == 0)
        {
            return;
        }

        ColliderBox2D caster = new ColliderBox2D { Shape = shape };
        ColliderRef2D casterRef = ColliderRef2D.Create(&caster);
        BoundingBox2D queryBounds = shape.GetBoundingBox();

        int x0 = ClampChunkX(queryBounds.Min.X);
        int y0 = ClampChunkY(queryBounds.Min.Y);
        int x1 = ClampChunkX(queryBounds.Max.X);
        int y1 = ClampChunkY(queryBounds.Max.Y);
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                int chunkIndex = y * _chunkCountX + x;
                bool continueQuery;
                if (_chunkTrees[chunkIndex] is { } tree)
                {
                    continueQuery = CastTreeBox(tree, ref collector, shape);
                }
                else
                {
                    continueQuery = CastBucketCaster(_chunkBuckets[chunkIndex], ref collector, casterRef);
                }

                if (!continueQuery)
                {
                    return;
                }
            }
        }

        if (_bigTree is { } bigTree)
        {
            CastTreeBox(bigTree, ref collector, shape);
        }
        else
        {
            CastBucketCaster(_bigBucket, ref collector, casterRef);
        }
    }

    /// <summary>
    /// Casts a sphere collider against the world and collects all overlapping targets.
    /// </summary>
    /// <typeparam name="TCollector">The collector type gathering the hit targets.</typeparam>
    /// <param name="collector">The collector to gather hit results.</param>
    /// <param name="shape">The sphere shape to cast.</param>
    public void CastSphere<TCollector>(ref TCollector collector, in ShapeSphere2D shape) where TCollector : ICollisionCastCollector
    {
        if (_count == 0)
        {
            return;
        }

        ColliderSphere2D caster = new ColliderSphere2D { Shape = shape };
        ColliderRef2D casterRef = ColliderRef2D.Create(&caster);
        BoundingBox2D queryBounds = shape.GetBoundingBox();

        int x0 = ClampChunkX(queryBounds.Min.X);
        int y0 = ClampChunkY(queryBounds.Min.Y);
        int x1 = ClampChunkX(queryBounds.Max.X);
        int y1 = ClampChunkY(queryBounds.Max.Y);
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                int chunkIndex = y * _chunkCountX + x;
                bool continueQuery;
                if (_chunkTrees[chunkIndex] is { } tree)
                {
                    continueQuery = CastTreeSphere(tree, ref collector, shape);
                }
                else
                {
                    continueQuery = CastBucketCaster(_chunkBuckets[chunkIndex], ref collector, casterRef);
                }

                if (!continueQuery)
                {
                    return;
                }
            }
        }

        if (_bigTree is { } bigTree)
        {
            CastTreeSphere(bigTree, ref collector, shape);
        }
        else
        {
            CastBucketCaster(_bigBucket, ref collector, casterRef);
        }
    }

    /// <summary>
    /// Casts a ray segment against the world and collects all targets hit within
    /// <paramref name="maxT"/> displacement units.
    /// </summary>
    /// <typeparam name="TCollector">The collector type gathering the hit targets.</typeparam>
    /// <param name="collector">The collector to gather hit results.</param>
    /// <param name="ray">The ray to cast; the default segment spans Origin to Origin + Displacement.</param>
    /// <param name="maxT">The hit fraction limit in displacement units; 1 casts the full segment.</param>
    public void CastRay<TCollector>(ref TCollector collector, in Ray2D ray, float maxT = 1f) where TCollector : IRayCastCollector2D
    {
        if (_count == 0)
        {
            return;
        }

        RayChunkWalker walker = RayChunkWalker.Create(this, ray, maxT);
        while (walker.MoveNext(out int chunkIndex))
        {
            bool continueQuery;
            if (_chunkTrees[chunkIndex] is { } tree)
            {
                continueQuery = CastTreeRay(tree, ref collector, ray, maxT);
            }
            else
            {
                continueQuery = CastBucketRay(_chunkBuckets[chunkIndex], ref collector, ray, maxT);
            }

            if (!continueQuery)
            {
                return;
            }
        }

        if (_bigTree is { } bigTree)
        {
            CastTreeRay(bigTree, ref collector, ray, maxT);
        }
        else
        {
            CastBucketRay(_bigBucket, ref collector, ray, maxT);
        }
    }

    /// <summary>
    /// Casts a ray segment against the world and returns the closest hit target.
    /// </summary>
    /// <typeparam name="TTarget">The expected payload type of the hit target.</typeparam>
    /// <param name="ray">The ray to cast; the default segment spans Origin to Origin + Displacement.</param>
    /// <param name="hitTarget">The closest hit target, or null when none is found.</param>
    /// <param name="hit">The precise hit information of the closest hit.</param>
    /// <param name="maxT">The hit fraction limit in displacement units; 1 casts the full segment.</param>
    /// <returns>True when a target of type <typeparamref name="TTarget"/> was hit.</returns>
    public bool TryCastRayClosestHit<TTarget>(in Ray2D ray, out TTarget? hitTarget, out RaycastHit2D hit, float maxT = 1f) where TTarget : class
    {
        hitTarget = null;
        hit = default;

        if (_count == 0)
        {
            return false;
        }

        RayBest best = default;
        RayChunkWalker walker = RayChunkWalker.Create(this, ray, maxT);
        while (walker.MoveNext(out int chunkIndex))
        {
            if (_chunkTrees[chunkIndex] is { } tree)
            {
                VisitTreeClosest(tree, ray, maxT, ref best);
            }
            else
            {
                VisitBucketClosest(_chunkBuckets[chunkIndex], ray, maxT, ref best);
            }
        }

        if (_bigTree is { } bigTree)
        {
            VisitTreeClosest(bigTree, ray, maxT, ref best);
        }
        else
        {
            VisitBucketClosest(_bigBucket, ray, maxT, ref best);
        }

        if (!best.Hit)
        {
            return false;
        }

        if (_leaves[best.LeafIndex].Target is not TTarget typed)
        {
            return false;
        }

        hitTarget = typed;
        hit = best.HitInfo;
        return true;
    }

    /// <summary>
    /// Collects all targets whose AABB overlaps the given bounds. This is a broadphase-only
    /// query (no precise shape test); use it for region enumeration and culling-style consumers.
    /// </summary>
    /// <param name="bounds">The bounds to test against.</param>
    /// <param name="collector">The collector receiving the overlapping targets.</param>
    public void CollectTargets(BoundingBox2D bounds, ICollection<object> collector)
    {
        if (_count == 0)
        {
            return;
        }

        int x0 = ClampChunkX(bounds.Min.X);
        int y0 = ClampChunkY(bounds.Min.Y);
        int x1 = ClampChunkX(bounds.Max.X);
        int y1 = ClampChunkY(bounds.Max.Y);
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                CollectBucketBounds(_chunkBuckets[y * _chunkCountX + x], bounds, collector);
            }
        }

        CollectBucketBounds(_bigBucket, bounds, collector);
    }

    /// <summary>
    /// A registration handle for a target added to a <see cref="ChunkedCollisionWorld2D"/>.
    /// Disposing removes the target; disposing twice is a no-op.
    /// </summary>
    public readonly struct TargetRegistration : IDisposable
    {
        private readonly ChunkedCollisionWorld2D? _world;
        private readonly int _leafIndex;

        internal TargetRegistration(ChunkedCollisionWorld2D world, int leafIndex)
        {
            _world = world;
            _leafIndex = leafIndex;
        }

        /// <summary>
        /// Removes the registered target from its world. Main thread only.
        /// </summary>
        public void Dispose()
        {
            _world?.RemoveLeaf(_leafIndex);
        }
    }

    private struct Leaf
    {
        public ColliderType2D Type;
        public bool Allocated;
        public object? Target;
        public BoundingBox2D Bounds;
        public ShapeBox2D Box;
        public ShapeSphere2D Sphere;
        public int FreeNext;
    }

    private struct RayBest
    {
        public bool Hit;
        public RaycastHit2D HitInfo;
        public int LeafIndex;
    }

    // adapts the bvh collider-level collectors back to payload objects; Stopped remembers an
    // early-out requested by the user collector so the outer walk can stop as well
    private struct TreeCastAdapter<TCollector> : IBvhCollisionCastCollector2D where TCollector : ICollisionCastCollector
    {
        public ChunkedCollisionWorld2D World;
        public TCollector UserCollector;
        public bool Stopped;

        public bool OnHit(ColliderCastResult2D result)
        {
            object? target = World._leaves[result.Collider.UserData].Target;
            if (target == null || Stopped)
            {
                return !Stopped;
            }

            if (!UserCollector.OnHit(target))
            {
                Stopped = true;
                return false;
            }

            return true;
        }
    }

    private struct TreeRayAdapter<TCollector> : IBvhRayCastCollector2D where TCollector : IRayCastCollector2D
    {
        public ChunkedCollisionWorld2D World;
        public TCollector UserCollector;
        public float MaxT;
        public bool Stopped;

        public bool OnHit(RayCastResult2D result)
        {
            if (Stopped)
            {
                return false;
            }

            if (result.HitInfo.Fraction > MaxT)
            {
                return true;
            }

            object? target = World._leaves[result.Collider.UserData].Target;
            if (target == null)
            {
                return true;
            }

            if (!UserCollector.OnHit(target, result.HitInfo))
            {
                Stopped = true;
                return false;
            }

            return true;
        }
    }

    // a mini BVH over one bucket; the stable native staging buffers (copied from the managed
    // leaves at build time) are owned and disposed together with the tree, so the collider
    // pointers inside the tree stay valid for as long as the tree exists
    private sealed class ChunkTree : IDisposable
    {
        public NativeArrayList<ColliderBox2D> Boxes;
        public NativeArrayList<ColliderSphere2D> Spheres;
        public NativeArrayList<ColliderRef2D> Refs;
        public NativeBvh2D Tree = new NativeBvh2D();

        public void Dispose()
        {
            Boxes.Dispose();
            Spheres.Dispose();
            Refs.Dispose();
            Tree.Dispose();
        }
    }

    // walks the chunks crossed by a ray segment clipped to the grid bounds (Amanatides-Woo)
    private struct RayChunkWalker
    {
        private int _cx;
        private int _cy;
        private int _stepX;
        private int _stepY;
        private float _tMaxX;
        private float _tMaxY;
        private float _tDeltaX;
        private float _tDeltaY;
        private float _tExit;
        private int _chunkCountX;
        private int _chunkCountY;
        private bool _valid;

        public static RayChunkWalker Create(ChunkedCollisionWorld2D world, in Ray2D ray, float maxT)
        {
            RayChunkWalker walker = default;
            float gridMaxX = world._gridMinX + world._chunkCountX * world._chunkSize;
            float gridMaxY = world._gridMinY + world._chunkCountY * world._chunkSize;

            float tEnter = 0f;
            float tExit = maxT;
            if (!ClipAxis(ray.Origin.X, ray.Displacement.X, world._gridMinX, gridMaxX, ref tEnter, ref tExit)
                || !ClipAxis(ray.Origin.Y, ray.Displacement.Y, world._gridMinY, gridMaxY, ref tEnter, ref tExit)
                || tEnter > tExit)
            {
                return walker;
            }

            Vector2 entry = ray.Origin + ray.Displacement * tEnter;
            walker._cx = world.ClampChunkX(entry.X);
            walker._cy = world.ClampChunkY(entry.Y);
            walker._stepX = Math.Sign(ray.Displacement.X);
            walker._stepY = Math.Sign(ray.Displacement.Y);
            walker._tDeltaX = walker._stepX != 0 ? world._chunkSize / MathF.Abs(ray.Displacement.X) : float.PositiveInfinity;
            walker._tDeltaY = walker._stepY != 0 ? world._chunkSize / MathF.Abs(ray.Displacement.Y) : float.PositiveInfinity;
            walker._tMaxX = walker._stepX != 0
                ? (world._gridMinX + (walker._cx + (walker._stepX > 0 ? 1 : 0)) * world._chunkSize - ray.Origin.X) / ray.Displacement.X
                : float.PositiveInfinity;
            walker._tMaxY = walker._stepY != 0
                ? (world._gridMinY + (walker._cy + (walker._stepY > 0 ? 1 : 0)) * world._chunkSize - ray.Origin.Y) / ray.Displacement.Y
                : float.PositiveInfinity;
            walker._tExit = tExit;
            walker._chunkCountX = world._chunkCountX;
            walker._chunkCountY = world._chunkCountY;
            walker._valid = true;
            return walker;
        }

        public bool MoveNext(out int chunkIndex)
        {
            chunkIndex = _cy * _chunkCountX + _cx;
            if (!_valid)
            {
                return false;
            }

            Advance();
            return true;
        }

        private void Advance()
        {
            if (_tMaxX < _tMaxY)
            {
                if (_tMaxX > _tExit)
                {
                    _valid = false;
                    return;
                }

                _cx += _stepX;
                _tMaxX += _tDeltaX;
                if (_cx < 0 || _cx >= _chunkCountX)
                {
                    _valid = false;
                }
            }
            else
            {
                if (_tMaxY > _tExit)
                {
                    _valid = false;
                    return;
                }

                _cy += _stepY;
                _tMaxY += _tDeltaY;
                if (_cy < 0 || _cy >= _chunkCountY)
                {
                    _valid = false;
                }
            }
        }

        private static bool ClipAxis(float origin, float displacement, float min, float max, ref float tEnter, ref float tExit)
        {
            if (MathF.Abs(displacement) < 1e-20f)
            {
                return origin >= min && origin <= max;
            }

            float t0 = (min - origin) / displacement;
            float t1 = (max - origin) / displacement;
            if (t0 > t1)
            {
                (t0, t1) = (t1, t0);
            }

            tEnter = MathF.Max(tEnter, t0);
            tExit = MathF.Min(tExit, t1);
            return tEnter <= tExit;
        }
    }

    private int AddLeaf(object target, ColliderType2D type, in ShapeBox2D box, in ShapeSphere2D sphere, BoundingBox2D bounds)
    {
        if (_freeHead >= _leaves.Length)
        {
            GrowLeaves();
        }

        int leafIndex = _freeHead;
        ref Leaf leaf = ref _leaves[leafIndex];
        _freeHead = leaf.FreeNext;
        leaf.Type = type;
        leaf.Allocated = true;
        leaf.Target = target;
        leaf.Bounds = bounds;
        leaf.Box = box;
        leaf.Sphere = sphere;
        _count++;

        float span = MathF.Max(bounds.Size.X, bounds.Size.Y);
        if (span > _chunkSize * BigBucketSpanFactor)
        {
            _bigBucket.Add(leafIndex);
            MarkBigDirty();
            return leafIndex;
        }

        int x0 = ClampChunkX(bounds.Min.X);
        int y0 = ClampChunkY(bounds.Min.Y);
        int x1 = ClampChunkX(bounds.Max.X);
        int y1 = ClampChunkY(bounds.Max.Y);
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                Bucket(x, y).Add(leafIndex);
                MarkChunkDirty(y * _chunkCountX + x);
            }
        }

        return leafIndex;
    }

    private void RemoveLeaf(int leafIndex)
    {
        if (leafIndex < 0 || leafIndex >= _leaves.Length || !_leaves[leafIndex].Allocated)
        {
            return;
        }

        ref Leaf leaf = ref _leaves[leafIndex];
        float span = MathF.Max(leaf.Bounds.Size.X, leaf.Bounds.Size.Y);
        if (span > _chunkSize * BigBucketSpanFactor)
        {
            _bigBucket.Remove(leafIndex);
            MarkBigDirty();
        }
        else
        {
            int x0 = ClampChunkX(leaf.Bounds.Min.X);
            int y0 = ClampChunkY(leaf.Bounds.Min.Y);
            int x1 = ClampChunkX(leaf.Bounds.Max.X);
            int y1 = ClampChunkY(leaf.Bounds.Max.Y);
            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    int chunkIndex = y * _chunkCountX + x;
                    _chunkBuckets[chunkIndex]?.Remove(leafIndex);
                    MarkChunkDirty(chunkIndex);
                }
            }
        }

        leaf.Allocated = false;
        leaf.Target = null;
        leaf.FreeNext = _freeHead;
        _freeHead = leafIndex;
        _count--;
    }

    private void GrowLeaves()
    {
        Leaf[] grown = new Leaf[_leaves.Length * 2];
        Array.Copy(_leaves, grown, _leaves.Length);
        for (int i = _leaves.Length; i < grown.Length; i++)
        {
            grown[i].FreeNext = i + 1;
        }

        _leaves = grown;
    }

    private List<int> Bucket(int x, int y)
    {
        int index = y * _chunkCountX + x;
        List<int>? bucket = _chunkBuckets[index];
        if (bucket == null)
        {
            bucket = new List<int>(64);
            _chunkBuckets[index] = bucket;
        }

        return bucket;
    }

    private void MarkChunkDirty(int chunkIndex)
    {
        _pendingChunks.Add(chunkIndex);
        if (_chunkTrees[chunkIndex] != null)
        {
            _chunkTrees[chunkIndex]!.Dispose();
            _chunkTrees[chunkIndex] = null;
        }
    }

    private void MarkBigDirty()
    {
        _pendingBig = true;
        if (_bigTree != null)
        {
            _bigTree.Dispose();
            _bigTree = null;
        }
    }

    private void PromoteBucket(List<int>? bucket, int chunkIndex)
    {
        if (bucket == null || bucket.Count < TreeBuildThreshold)
        {
            return;
        }

        ChunkTree tree = BuildTree(bucket);
        if (chunkIndex < 0)
        {
            _bigTree = tree;
        }
        else
        {
            _chunkTrees[chunkIndex] = tree;
        }
    }

    private ChunkTree BuildTree(List<int> bucket)
    {
        ChunkTree tree = new ChunkTree();

        // the staging lists must be pre-sized to their final count before any collider pointer is
        // taken: growing a NativeArrayList reallocates and frees its buffer, which would dangle
        // every ColliderRef2D created so far
        int boxCount = 0;
        int sphereCount = 0;
        for (int i = 0; i < bucket.Count; i++)
        {
            if (_leaves[bucket[i]].Type == ColliderType2D.Box)
            {
                boxCount++;
            }
            else
            {
                sphereCount++;
            }
        }

        tree.Boxes = new NativeArrayList<ColliderBox2D>(Math.Max(boxCount, 1));
        tree.Spheres = new NativeArrayList<ColliderSphere2D>(Math.Max(sphereCount, 1));
        tree.Refs = new NativeArrayList<ColliderRef2D>(bucket.Count);

        for (int i = 0; i < bucket.Count; i++)
        {
            int leafIndex = bucket[i];
            Leaf leaf = _leaves[leafIndex];
            if (leaf.Type == ColliderType2D.Box)
            {
                tree.Boxes.Add(new ColliderBox2D { Shape = leaf.Box });
                ColliderRef2D collider = ColliderRef2D.Create(tree.Boxes.UnsafePointer + tree.Boxes.Length - 1);
                collider.UserData = leafIndex;
                tree.Refs.Add(collider);
            }
            else
            {
                tree.Spheres.Add(new ColliderSphere2D { Shape = leaf.Sphere });
                ColliderRef2D collider = ColliderRef2D.Create(tree.Spheres.UnsafePointer + tree.Spheres.Length - 1);
                collider.UserData = leafIndex;
                tree.Refs.Add(collider);
            }
        }

        tree.Tree.BuildTree(tree.Refs.AsSpan(), _builder);
        return tree;
    }

    private int ChunkIndexOf(Vector2 position)
    {
        int x = Math.Clamp((int)MathF.Floor((position.X - _gridMinX) / _chunkSize), 0, _chunkCountX - 1);
        int y = Math.Clamp((int)MathF.Floor((position.Y - _gridMinY) / _chunkSize), 0, _chunkCountY - 1);
        return y * _chunkCountX + x;
    }

    private int ClampChunkX(float coordinate)
    {
        return Math.Clamp((int)MathF.Floor((coordinate - _gridMinX) / _chunkSize), 0, _chunkCountX - 1);
    }

    private int ClampChunkY(float coordinate)
    {
        return Math.Clamp((int)MathF.Floor((coordinate - _gridMinY) / _chunkSize), 0, _chunkCountY - 1);
    }

    private bool LeafCollidesWith(int leafIndex, ColliderRef2D caster)
    {
        Leaf leaf = _leaves[leafIndex];
        if (leaf.Type == ColliderType2D.Box)
        {
            ColliderBox2D box = new ColliderBox2D { Shape = leaf.Box };
            ColliderRef2D leafRef = ColliderRef2D.Create(&box);
            return leafRef.CollidesWith(caster);
        }

        ColliderSphere2D sphere = new ColliderSphere2D { Shape = leaf.Sphere };
        ColliderRef2D sphereRef = ColliderRef2D.Create(&sphere);
        return sphereRef.CollidesWith(caster);
    }

    private bool LeafIntersectRay(int leafIndex, in Ray2D ray, float maxT, out RaycastHit2D hit)
    {
        hit = default;
        Leaf leaf = _leaves[leafIndex];
        bool hitResult;
        if (leaf.Type == ColliderType2D.Box)
        {
            ColliderBox2D box = new ColliderBox2D { Shape = leaf.Box };
            hitResult = box.IntersectRay(ray, out hit);
        }
        else
        {
            ColliderSphere2D sphere = new ColliderSphere2D { Shape = leaf.Sphere };
            hitResult = sphere.IntersectRay(ray, out hit);
        }

        return hitResult && hit.Fraction <= maxT;
    }

    private bool LeafContainsPoint(int leafIndex, Vector2 point)
    {
        Leaf leaf = _leaves[leafIndex];
        if (leaf.Type == ColliderType2D.Box)
        {
            ColliderBox2D box = new ColliderBox2D { Shape = leaf.Box };
            return box.IntersectPoint(point);
        }

        ColliderSphere2D sphere = new ColliderSphere2D { Shape = leaf.Sphere };
        return sphere.IntersectPoint(point);
    }

    private bool CastBucketCaster<TCollector>(List<int>? bucket, ref TCollector collector, ColliderRef2D caster)
        where TCollector : ICollisionCastCollector
    {
        if (bucket == null)
        {
            return true;
        }

        for (int i = 0; i < bucket.Count; i++)
        {
            if (!LeafCollidesWith(bucket[i], caster) || _leaves[bucket[i]].Target == null)
            {
                continue;
            }

            if (!collector.OnHit(_leaves[bucket[i]].Target!))
            {
                return false;
            }
        }

        return true;
    }

    private bool CastBucketPoint<TCollector>(List<int>? bucket, ref TCollector collector, Vector2 point)
        where TCollector : ICollisionCastCollector
    {
        if (bucket == null)
        {
            return true;
        }

        for (int i = 0; i < bucket.Count; i++)
        {
            if (!LeafContainsPoint(bucket[i], point) || _leaves[bucket[i]].Target == null)
            {
                continue;
            }

            if (!collector.OnHit(_leaves[bucket[i]].Target!))
            {
                return false;
            }
        }

        return true;
    }

    private bool CastBucketRay<TCollector>(List<int>? bucket, ref TCollector collector, in Ray2D ray, float maxT)
        where TCollector : IRayCastCollector2D
    {
        if (bucket == null)
        {
            return true;
        }

        for (int i = 0; i < bucket.Count; i++)
        {
            if (!LeafIntersectRay(bucket[i], ray, maxT, out RaycastHit2D hit) || _leaves[bucket[i]].Target == null)
            {
                continue;
            }

            if (!collector.OnHit(_leaves[bucket[i]].Target!, hit))
            {
                return false;
            }
        }

        return true;
    }

    private void VisitBucketClosest(List<int>? bucket, in Ray2D ray, float maxT, ref RayBest best)
    {
        if (bucket == null)
        {
            return;
        }

        for (int i = 0; i < bucket.Count; i++)
        {
            if (!LeafIntersectRay(bucket[i], ray, maxT, out RaycastHit2D hit))
            {
                continue;
            }

            if (!best.Hit || hit.Fraction < best.HitInfo.Fraction)
            {
                best.Hit = true;
                best.HitInfo = hit;
                best.LeafIndex = bucket[i];
            }
        }
    }

    private bool CastTreeBox<TCollector>(ChunkTree? tree, ref TCollector collector, in ShapeBox2D shape)
        where TCollector : ICollisionCastCollector
    {
        if (tree == null)
        {
            return true;
        }

        TreeCastAdapter<TCollector> adapter = new TreeCastAdapter<TCollector> { World = this, UserCollector = collector };
        tree.Tree.CastBox(shape, ref adapter);
        collector = adapter.UserCollector;
        return !adapter.Stopped;
    }

    private bool CastTreeSphere<TCollector>(ChunkTree? tree, ref TCollector collector, in ShapeSphere2D shape)
        where TCollector : ICollisionCastCollector
    {
        if (tree == null)
        {
            return true;
        }

        TreeCastAdapter<TCollector> adapter = new TreeCastAdapter<TCollector> { World = this, UserCollector = collector };
        tree.Tree.CastSphere(shape, ref adapter);
        collector = adapter.UserCollector;
        return !adapter.Stopped;
    }

    private bool CastTreePoint<TCollector>(ChunkTree? tree, ref TCollector collector, Vector2 point)
        where TCollector : ICollisionCastCollector
    {
        if (tree == null)
        {
            return true;
        }

        TreeCastAdapter<TCollector> adapter = new TreeCastAdapter<TCollector> { World = this, UserCollector = collector };
        tree.Tree.CastPoint(point, ref adapter);
        collector = adapter.UserCollector;
        return !adapter.Stopped;
    }

    private bool CastTreeRay<TCollector>(ChunkTree? tree, ref TCollector collector, in Ray2D ray, float maxT)
        where TCollector : IRayCastCollector2D
    {
        if (tree == null)
        {
            return true;
        }

        TreeRayAdapter<TCollector> adapter = new TreeRayAdapter<TCollector> { World = this, UserCollector = collector, MaxT = maxT };
        tree.Tree.CastRay(ray, ref adapter);
        collector = adapter.UserCollector;
        return !adapter.Stopped;
    }

    private void VisitTreeClosest(ChunkTree? tree, in Ray2D ray, float maxT, ref RayBest best)
    {
        if (tree == null)
        {
            return;
        }

        RayCastResult2D result = tree.Tree.CastRayClosestHit(ray);
        if (!result.Hit || result.HitInfo.Fraction > maxT)
        {
            return;
        }

        if (!best.Hit || result.HitInfo.Fraction < best.HitInfo.Fraction)
        {
            best.Hit = true;
            best.HitInfo = result.HitInfo;
            best.LeafIndex = result.Collider.UserData;
        }
    }

    private void CollectBucketBounds(List<int>? bucket, in BoundingBox2D bounds, ICollection<object> collector)
    {
        if (bucket == null)
        {
            return;
        }

        for (int i = 0; i < bucket.Count; i++)
        {
            Leaf leaf = _leaves[bucket[i]];
            if (leaf.Allocated && leaf.Bounds.Intersects(bounds))
            {
                collector.Add(leaf.Target!);
            }
        }
    }

    /// <summary>
    /// Releases all native buffers owned by this world.
    /// </summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected override void Dispose(bool disposing)
    {
        for (int i = 0; i < _chunkTrees.Length; i++)
        {
            _chunkTrees[i]?.Dispose();
            _chunkTrees[i] = null;
        }

        _bigTree?.Dispose();
        _bigTree = null;
        _builder.Dispose();
    }
}
