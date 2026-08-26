#nullable enable
using BenchmarkDotNet.Attributes;
using System.Collections.Generic;
using System.Numerics;
using Alco;
using BenchmarkFramework;

namespace Alco.Benchmark;

/// <summary>
/// Compares a single global Morton BVH against the engine <see cref="ChunkedCollisionWorld2D"/>
/// (linear-bucket and adaptive-tree modes) for a large static scene with mixed small and large
/// free-position colliders (dropped items, debris).
/// Chunked variant: 16x16-cell chunk buckets, objects spanning more than two chunks go to a
/// big-object bucket, and the adaptive mode lazily builds a mini Morton BVH per bucket that
/// exceeds the threshold. Workload: 85% tiny clustered objects, 10% medium (multi-chunk),
/// 5% huge (big bucket). Queries mirror MapService_Collision: short rays (bullets), long rays
/// (sight lines), small boxes (explosions), large boxes (area effects), run in parallel batches.
/// A verification pass in GlobalSetup asserts both chunked modes return exactly the same
/// results as the single BVH.
/// </summary>
[CustomConfigParam(3, 12, 64)]
public unsafe class BenchmarkStaticBroadphase
{
    private const float MapHalf = 128f;
    private const int ChunkSize = 16;
    private const int GridDim = (int)(MapHalf * 2f / ChunkSize);
    private const int QueryCount = 10000;
    private const int MutationCount = 100;
    private const float SegmentFractionLimit = 1.0001f;

    [Params(5000, 20000)]
    public int ColliderCount { get; set; }

    private NativeArrayList<ColliderBox2D> _boxes;
    private NativeArrayList<ColliderSphere2D> _spheres;
    private NativeArrayList<ColliderRef2D> _colliders;

    private NativeArrayList<Ray2D> _shortRays;
    private NativeArrayList<Ray2D> _longRays;
    private NativeArrayList<ShapeBox2D> _smallBoxes;
    private NativeArrayList<ShapeBox2D> _largeBoxes;

    private NativeBvh2D _singleBvh = null!;
    private MortonBvhBuilder2D _mortonBuilder = null!;
    private ChunkedCollisionWorld2D _chunkedLinear = null!;
    private ChunkedCollisionWorld2D _chunkedAdaptive = null!;
    private IDisposable[] _linearRegistrations = null!;
    private IDisposable[] _adaptiveRegistrations = null!;

    private float[] _rayFractions = null!;
    private HashSet<int>[] _boxCollectors = null!;
    private int[] _mutationIndices = null!;

    private RayBatchTask _rayTaskSingle = null!;
    private RayBatchTask _rayTaskLinear = null!;
    private RayBatchTask _rayTaskAdaptive = null!;
    private BoxBatchTask _boxTaskSingle = null!;
    private BoxBatchTask _boxTaskLinear = null!;
    private BoxBatchTask _boxTaskAdaptive = null!;

    [GlobalSetup]
    public void Setup()
    {
        GenerateColliders();
        GenerateQueries();

        _mortonBuilder = new MortonBvhBuilder2D();
        _singleBvh = new NativeBvh2D();
        _singleBvh.BuildTree(_colliders.AsSpan(), _mortonBuilder);

        _chunkedLinear = new ChunkedCollisionWorld2D(-MapHalf, -MapHalf, GridDim, GridDim, ChunkSize)
        {
            TreeBuildThreshold = int.MaxValue,
        };
        _chunkedAdaptive = new ChunkedCollisionWorld2D(-MapHalf, -MapHalf, GridDim, GridDim, ChunkSize);
        _linearRegistrations = new IDisposable[_colliders.Length];
        _adaptiveRegistrations = new IDisposable[_colliders.Length];
        for (int i = 0; i < _colliders.Length; i++)
        {
            _linearRegistrations[i] = AddCollider(_chunkedLinear, i);
            _adaptiveRegistrations[i] = AddCollider(_chunkedAdaptive, i);
        }

        _chunkedAdaptive.RebuildDirtyTrees();

        PrintStats();

        _rayFractions = new float[QueryCount];
        _boxCollectors = new HashSet<int>[QueryCount];
        for (int i = 0; i < QueryCount; i++)
        {
            _boxCollectors[i] = new HashSet<int>();
        }

        _rayTaskSingle = new RayBatchTask(_singleBvh) { Rays = _shortRays, Fractions = _rayFractions };
        _rayTaskLinear = new RayBatchTask(_chunkedLinear) { Rays = _shortRays, Fractions = _rayFractions };
        _rayTaskAdaptive = new RayBatchTask(_chunkedAdaptive) { Rays = _shortRays, Fractions = _rayFractions };
        _boxTaskSingle = new BoxBatchTask(_singleBvh) { Boxes = _smallBoxes, Collectors = _boxCollectors };
        _boxTaskLinear = new BoxBatchTask(_chunkedLinear) { Boxes = _smallBoxes, Collectors = _boxCollectors };
        _boxTaskAdaptive = new BoxBatchTask(_chunkedAdaptive) { Boxes = _smallBoxes, Collectors = _boxCollectors };

        FastRandom random = new FastRandom(4242);
        _mutationIndices = new int[MutationCount];
        for (int i = 0; i < MutationCount; i++)
        {
            _mutationIndices[i] = (int)random.NextFloat(0, _colliders.Length);
        }

        Verify();
    }

    private IDisposable AddCollider(ChunkedCollisionWorld2D world, int index)
    {
        // collider order is boxes first, then spheres, matching the _colliders build order
        if (index < _boxes.Length)
        {
            return world.Add(index, _boxes[index].Shape);
        }

        return world.Add(index, _spheres[index - _boxes.Length].Shape);
    }

    private void GenerateColliders()
    {
        FastRandom random = new FastRandom(20260817);

        int total = ColliderCount;
        int smallCount = total * 85 / 100;
        int mediumCount = total * 10 / 100;
        int hugeCount = total - smallCount - mediumCount;

        _boxes = new NativeArrayList<ColliderBox2D>(total / 2 + 1);
        _spheres = new NativeArrayList<ColliderSphere2D>(total / 2 + 1);
        _colliders = new NativeArrayList<ColliderRef2D>(total);

        int clusterCount = Math.Max(1, smallCount / 50);
        Vector2[] clusters = new Vector2[clusterCount];
        for (int i = 0; i < clusterCount; i++)
        {
            clusters[i] = random.NextVector2(-MapHalf + 24f, MapHalf - 24f);
        }

        for (int i = 0; i < smallCount; i++)
        {
            Vector2 center = clusters[(int)random.NextFloat(0, clusterCount)];
            Vector2 pos = Vector2.Clamp(center + random.NextVector2(-8f, 8f),
                new Vector2(-MapHalf + 2f), new Vector2(MapHalf - 2f));
            if ((i & 1) == 0)
            {
                _boxes.Add(new ColliderBox2D { Shape = new ShapeBox2D(pos, random.NextVector2(0.4f, 1.6f), random.NextRotation2D()) });
            }
            else
            {
                _spheres.Add(new ColliderSphere2D { Shape = new ShapeSphere2D(pos, random.NextFloat(0.2f, 0.8f)) });
            }
        }

        for (int i = 0; i < mediumCount; i++)
        {
            Vector2 pos = random.NextVector2(-MapHalf + 16f, MapHalf - 16f);
            if ((i & 1) == 0)
            {
                _boxes.Add(new ColliderBox2D { Shape = new ShapeBox2D(pos, random.NextVector2(8f, 30f), random.NextRotation2D()) });
            }
            else
            {
                _spheres.Add(new ColliderSphere2D { Shape = new ShapeSphere2D(pos, random.NextFloat(4f, 15f)) });
            }
        }

        for (int i = 0; i < hugeCount; i++)
        {
            Vector2 pos = random.NextVector2(-MapHalf + 36f, MapHalf - 36f);
            if ((i & 1) == 0)
            {
                _boxes.Add(new ColliderBox2D { Shape = new ShapeBox2D(pos, random.NextVector2(32f, 56f), random.NextRotation2D()) });
            }
            else
            {
                _spheres.Add(new ColliderSphere2D { Shape = new ShapeSphere2D(pos, random.NextFloat(16f, 28f)) });
            }
        }

        ColliderBox2D* boxPtr = _boxes.UnsafePointer;
        for (int i = 0; i < _boxes.Length; i++)
        {
            ColliderRef2D collider = ColliderRef2D.Create(boxPtr + i);
            collider.UserData = i;
            _colliders.Add(collider);
        }

        ColliderSphere2D* spherePtr = _spheres.UnsafePointer;
        for (int i = 0; i < _spheres.Length; i++)
        {
            ColliderRef2D collider = ColliderRef2D.Create(spherePtr + i);
            collider.UserData = _boxes.Length + i;
            _colliders.Add(collider);
        }
    }

    private void GenerateQueries()
    {
        FastRandom random = new FastRandom(777);
        _shortRays = new NativeArrayList<Ray2D>(QueryCount);
        _longRays = new NativeArrayList<Ray2D>(QueryCount);
        _smallBoxes = new NativeArrayList<ShapeBox2D>(QueryCount);
        _largeBoxes = new NativeArrayList<ShapeBox2D>(QueryCount);

        for (int i = 0; i < QueryCount; i++)
        {
            Vector2 start = random.NextVector2(-MapHalf + 3f, MapHalf - 3f);
            _shortRays.Add(Ray2D.CreateWithStartAndEnd(start, start + random.NextVector2(-4f, 4f)));
        }

        for (int i = 0; i < QueryCount; i++)
        {
            Vector2 start = random.NextVector2(-MapHalf + 10f, MapHalf - 10f);
            float angle = random.NextFloat(0f, MathF.PI * 2f);
            float length = random.NextFloat(80f, 260f);
            Vector2 direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            _longRays.Add(Ray2D.CreateWithStartAndEnd(start, start + direction * length));
        }

        for (int i = 0; i < QueryCount; i++)
        {
            Vector2 pos = random.NextVector2(-MapHalf + 3f, MapHalf - 3f);
            _smallBoxes.Add(new ShapeBox2D(pos, random.NextVector2(2f, 4f), Rotation2D.Identity));
        }

        for (int i = 0; i < QueryCount; i++)
        {
            Vector2 pos = random.NextVector2(-MapHalf + 20f, MapHalf - 20f);
            _largeBoxes.Add(new ShapeBox2D(pos, random.NextVector2(15f, 40f), Rotation2D.Identity));
        }
    }

    private void PrintStats()
    {
        Console.WriteLine($"[Stats] colliders={_colliders.Length} map={MapHalf * 2}x{MapHalf * 2} chunk={ChunkSize} " +
            $"linear(trees={_chunkedLinear.TreeCount}) adaptive(trees={_chunkedAdaptive.TreeCount} bigBucket={_chunkedAdaptive.BigBucketCount})");
    }

    private void Verify()
    {
        int rayMismatches = VerifyRays(_shortRays) + VerifyRays(_longRays);
        int boxMismatches = VerifyBoxes(_smallBoxes) + VerifyBoxes(_largeBoxes);
        Console.WriteLine($"[Check] ray mismatches={rayMismatches} box mismatches={boxMismatches}");
        if (rayMismatches > 0 || boxMismatches > 0)
        {
            throw new InvalidOperationException($"chunked broadphase disagrees with single BVH: rays={rayMismatches} boxes={boxMismatches}");
        }
    }

    private int VerifyRays(NativeArrayList<Ray2D> rays)
    {
        int mismatches = 0;
        for (int i = 0; i < rays.Length; i++)
        {
            Ray2D ray = rays[i];
            RayCastResult2D expected = ClampToSegment(_singleBvh.CastRayClosestHit(ray));
            bool linearHit = _chunkedLinear.TryCastRayClosestHit<object>(ray, out _, out RaycastHit2D linearHitInfo, SegmentFractionLimit);
            bool adaptiveHit = _chunkedAdaptive.TryCastRayClosestHit<object>(ray, out _, out RaycastHit2D adaptiveHitInfo, SegmentFractionLimit);
            if (RayEquals(expected.Hit, expected.Hit ? expected.HitInfo.Fraction : -1f, linearHit, linearHit ? linearHitInfo.Fraction : -1f)
                && RayEquals(expected.Hit, expected.Hit ? expected.HitInfo.Fraction : -1f, adaptiveHit, adaptiveHit ? adaptiveHitInfo.Fraction : -1f))
            {
                continue;
            }

            if (mismatches < 5)
            {
                Console.WriteLine($"[RayDebug] i={i} origin={ray.Origin} disp={ray.Displacement} " +
                    $"exp(hit={expected.Hit} f={(expected.Hit ? expected.HitInfo.Fraction : -1f)}) " +
                    $"lin(hit={linearHit} f={(linearHit ? linearHitInfo.Fraction : -1f)}) " +
                    $"ada(hit={adaptiveHit} f={(adaptiveHit ? adaptiveHitInfo.Fraction : -1f)})");
            }

            mismatches++;
        }

        return mismatches;
    }

    private int VerifyBoxes(NativeArrayList<ShapeBox2D> shapes)
    {
        HashSet<int> expected = new HashSet<int>();
        int mismatches = 0;
        for (int i = 0; i < shapes.Length; i++)
        {
            expected.Clear();
            HashSetCollector singleCollector = new HashSetCollector { Set = expected };
            _singleBvh.CastBox(shapes[i], ref singleCollector);

            if (!CollectChunked(_chunkedLinear, shapes[i]).SetEquals(expected))
            {
                mismatches++;
            }

            if (!CollectChunked(_chunkedAdaptive, shapes[i]).SetEquals(expected))
            {
                mismatches++;
            }
        }

        return mismatches;
    }

    private static HashSet<int> CollectChunked(ChunkedCollisionWorld2D world, in ShapeBox2D shape)
    {
        BoxIdCollector collector = new BoxIdCollector { Set = new HashSet<int>() };
        world.CastBox(ref collector, shape);
        return collector.Set;
    }

    // the engine's IntersectRay treats Ray2D as an infinite ray (fraction in displacement
    // units, unbounded); both compared structures query with finite-segment semantics, so
    // hits beyond the segment end are rejected on both sides
    private static RayCastResult2D ClampToSegment(RayCastResult2D result)
    {
        if (result.Hit && result.HitInfo.Fraction > SegmentFractionLimit)
        {
            return RayCastResult2D.none;
        }

        return result;
    }

    private static bool RayEquals(bool expectedHit, float expectedFraction, bool actualHit, float actualFraction)
    {
        if (expectedHit != actualHit)
        {
            return false;
        }

        return !expectedHit || MathF.Abs(expectedFraction - actualFraction) < 1e-4f;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _boxes.Dispose();
        _spheres.Dispose();
        _colliders.Dispose();
        _shortRays.Dispose();
        _longRays.Dispose();
        _smallBoxes.Dispose();
        _largeBoxes.Dispose();
        _singleBvh.Dispose();
        _mortonBuilder.Dispose();
        _chunkedLinear.Dispose();
        _chunkedAdaptive.Dispose();
        _rayTaskSingle.Dispose();
        _rayTaskLinear.Dispose();
        _rayTaskAdaptive.Dispose();
        _boxTaskSingle.Dispose();
        _boxTaskLinear.Dispose();
        _boxTaskAdaptive.Dispose();
    }

    [Benchmark(Description = "Build: single Morton BVH")]
    public void BuildSingleBvh()
    {
        _singleBvh.BuildTree(_colliders.AsSpan(), _mortonBuilder);
    }

    [Benchmark(Description = "Build: chunked insert all")]
    public void BuildChunked()
    {
        _chunkedLinear.Clear();
        for (int i = 0; i < _colliders.Length; i++)
        {
            _linearRegistrations[i] = AddCollider(_chunkedLinear, i);
        }
    }

    [Benchmark(Description = "Mutate: chunked remove+insert x100")]
    public void MutateChunked()
    {
        for (int i = 0; i < _mutationIndices.Length; i++)
        {
            int index = _mutationIndices[i];
            _linearRegistrations[index].Dispose();
            _linearRegistrations[index] = AddCollider(_chunkedLinear, index);
        }
    }

    [Benchmark(Description = "Mutate: chunked adaptive remove+insert+rebuild x100")]
    public void MutateChunkedAdaptive()
    {
        for (int i = 0; i < _mutationIndices.Length; i++)
        {
            int index = _mutationIndices[i];
            _adaptiveRegistrations[index].Dispose();
            _adaptiveRegistrations[index] = AddCollider(_chunkedAdaptive, index);
        }

        _chunkedAdaptive.RebuildDirtyTrees();
    }

    [Benchmark(Description = "RayShort: single BVH")]
    public void RayShortSingle()
    {
        _rayTaskSingle.Rays = _shortRays;
        _rayTaskSingle.RunParallel(QueryCount, 16);
    }

    [Benchmark(Description = "RayShort: chunked linear")]
    public void RayShortLinear()
    {
        _rayTaskLinear.Rays = _shortRays;
        _rayTaskLinear.RunParallel(QueryCount, 16);
    }

    [Benchmark(Description = "RayShort: chunked adaptive")]
    public void RayShortAdaptive()
    {
        _rayTaskAdaptive.Rays = _shortRays;
        _rayTaskAdaptive.RunParallel(QueryCount, 16);
    }

    [Benchmark(Description = "RayLong: single BVH")]
    public void RayLongSingle()
    {
        _rayTaskSingle.Rays = _longRays;
        _rayTaskSingle.RunParallel(QueryCount, 16);
    }

    [Benchmark(Description = "RayLong: chunked linear")]
    public void RayLongLinear()
    {
        _rayTaskLinear.Rays = _longRays;
        _rayTaskLinear.RunParallel(QueryCount, 16);
    }

    [Benchmark(Description = "RayLong: chunked adaptive")]
    public void RayLongAdaptive()
    {
        _rayTaskAdaptive.Rays = _longRays;
        _rayTaskAdaptive.RunParallel(QueryCount, 16);
    }

    [Benchmark(Description = "BoxSmall: single BVH")]
    public void BoxSmallSingle()
    {
        _boxTaskSingle.Boxes = _smallBoxes;
        _boxTaskSingle.RunParallel(QueryCount, 16);
    }

    [Benchmark(Description = "BoxSmall: chunked linear")]
    public void BoxSmallLinear()
    {
        _boxTaskLinear.Boxes = _smallBoxes;
        _boxTaskLinear.RunParallel(QueryCount, 16);
    }

    [Benchmark(Description = "BoxSmall: chunked adaptive")]
    public void BoxSmallAdaptive()
    {
        _boxTaskAdaptive.Boxes = _smallBoxes;
        _boxTaskAdaptive.RunParallel(QueryCount, 16);
    }

    [Benchmark(Description = "BoxLarge: single BVH")]
    public void BoxLargeSingle()
    {
        _boxTaskSingle.Boxes = _largeBoxes;
        _boxTaskSingle.RunParallel(QueryCount, 16);
    }

    [Benchmark(Description = "BoxLarge: chunked linear")]
    public void BoxLargeLinear()
    {
        _boxTaskLinear.Boxes = _largeBoxes;
        _boxTaskLinear.RunParallel(QueryCount, 16);
    }

    [Benchmark(Description = "BoxLarge: chunked adaptive")]
    public void BoxLargeAdaptive()
    {
        _boxTaskAdaptive.Boxes = _largeBoxes;
        _boxTaskAdaptive.RunParallel(QueryCount, 16);
    }

    private struct HashSetCollector : IBvhCollisionCastCollector2D
    {
        public HashSet<int> Set;

        public bool OnHit(ColliderCastResult2D result)
        {
            Set.Add(result.Collider.UserData);
            return true;
        }
    }

    private struct BoxIdCollector : ICollisionCastCollector
    {
        public HashSet<int> Set;

        public bool OnHit(object target)
        {
            Set.Add((int)target);
            return true;
        }
    }

    private sealed class RayBatchTask : ReusableBatchTask
    {
        private readonly NativeBvh2D? _bvh;
        private readonly ChunkedCollisionWorld2D? _chunked;

        public NativeArrayList<Ray2D> Rays;
        public float[]? Fractions;

        public RayBatchTask(NativeBvh2D bvh)
        {
            _bvh = bvh;
        }

        public RayBatchTask(ChunkedCollisionWorld2D chunked)
        {
            _chunked = chunked;
        }

        protected override void ExecuteCore(int index)
        {
            Ray2D ray = Rays[index];
            if (_bvh != null)
            {
                RayCastResult2D result = ClampToSegment(_bvh.CastRayClosestHit(ray));
                Fractions![index] = result.Hit ? result.HitInfo.Fraction : -1f;
                return;
            }

            if (_chunked!.TryCastRayClosestHit<object>(ray, out _, out RaycastHit2D hit, SegmentFractionLimit))
            {
                Fractions![index] = hit.Fraction;
            }
            else
            {
                Fractions![index] = -1f;
            }
        }
    }

    private sealed class BoxBatchTask : ReusableBatchTask
    {
        private readonly NativeBvh2D? _bvh;
        private readonly ChunkedCollisionWorld2D? _chunked;

        public NativeArrayList<ShapeBox2D> Boxes;
        public HashSet<int>[]? Collectors;

        public BoxBatchTask(NativeBvh2D bvh)
        {
            _bvh = bvh;
        }

        public BoxBatchTask(ChunkedCollisionWorld2D chunked)
        {
            _chunked = chunked;
        }

        protected override void ExecuteCore(int index)
        {
            ShapeBox2D shape = Boxes[index];
            HashSet<int> collector = Collectors![index];
            collector.Clear();
            if (_bvh != null)
            {
                HashSetCollector boxCollector = new HashSetCollector { Set = collector };
                _bvh.CastBox(shape, ref boxCollector);
                return;
            }

            BoxIdCollector chunkedCollector = new BoxIdCollector { Set = collector };
            _chunked!.CastBox(ref chunkedCollector, shape);
        }
    }
}
