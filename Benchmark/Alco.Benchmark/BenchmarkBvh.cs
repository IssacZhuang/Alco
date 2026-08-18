using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System;
using System.Collections.Generic;
using System.Numerics;
using Alco;
using BepuPhysics.Trees;
using BepuUtilities;
using BepuUtilities.Memory;

namespace Alco.Benchmark;

public class BenchmarkBvh
{
    NativeArrayList<ColliderBox3D> boxs3D;
    NativeArrayList<ColliderSphere3D> spheres3D;
    NativeArrayList<Ray3D> rays3D;
    NativeArrayList<ColliderRef3D> colliders3D;
    NativeBvh3D bvh3D;

    // order-preserving builds over the same colliders: insertion order (worst case for the
    // range tree) and a Morton-sorted copy (the externally-maintained-order scenario)
    private NativeBvh3D _bvh3DPairing;
    private NativeBvh3D _bvh3DPairingSorted;
    private NativeArrayList<ColliderRef3D> _colliders3DSorted;
    private CastRayTask3D _castRayTask3DPairing;
    private CastRayTask3D _castRayTask3DPairingSorted;

    NativeArrayList<ColliderBox2D> boxs2D;
    NativeArrayList<ColliderSphere2D> spheres2D;
    NativeArrayList<Ray2D> rays2D;
    NativeArrayList<ColliderRef2D> colliders2D;
    NativeBvh2D bvh2D;

    private NativeBvh2D _bvh2DPairing;
    private NativeBvh2D _bvh2DPairingSorted;
    private NativeArrayList<ColliderRef2D> _colliders2DSorted;
    private CastRayTask _castRayTask2DPairing;
    private CastRayTask _castRayTask2DPairingSorted;

    private CastRayTask _castRayTask;
    private CastRayTask3D _castRayTask3D;

    // AABB-only BVH: same AABBs, no collider pointers
    private BvhAabb3D _bvhAabb3D;
    private BoundingBox3D[] _aabbs3D;
    private CastRayTaskAabb3D _castRayTaskAabb3D;

    private BvhAabb2D _bvhAabb2D;
    private BoundingBox2D[] _aabbs2D;
    private CastRayTaskAabb2D _castRayTaskAabb2D;

    // bepuphysics2 bare tree comparison: same collider AABBs, same rays, precise leaf tests via callback
    private BufferPool _bepuPool;
    private Tree _bepuTreeAdd;
    private Tree _bepuTreeBinned;
    private Buffer<NodeChild> _bepuSubtrees;
    private CastRayTaskBepu _castRayTaskBepuAdd;
    private CastRayTaskBepu _castRayTaskBepuBinned;

    [GlobalSetup]
    public unsafe void Setup()
    {
        Setup3D();
        Setup2D();
    }

    private unsafe void Setup3D()
    {
        boxs3D = new NativeArrayList<ColliderBox3D>();
        spheres3D = new NativeArrayList<ColliderSphere3D>();
        rays3D = new NativeArrayList<Ray3D>();
        colliders3D = new NativeArrayList<ColliderRef3D>();

        int colliderCount = 1500;
        int rayCount = 10000;

        FastRandom random = new FastRandom(12345);
        //random collider
        for (int i = 0; i < colliderCount / 2; i++)
        {

            Vector3 pos = random.NextVector3(-100, 100);
            Vector3 size = random.NextVector3(1, 10);
            Quaternion rot = random.NextQuaternionRotation();
            boxs3D.Add(new ColliderBox3D
            {
                Shape = new ShapeBox3D(pos, size, rot)
            });
        }

        for (int i = 0; i < colliderCount / 2; i++)
        {

            Vector3 pos = random.NextVector3(-100, 100);
            float radius = random.NextFloat(1, 10);
            spheres3D.Add(new ColliderSphere3D
            {
                shape = new ShapeSphere3D(pos, radius)
            });
        }

        //random ray
        for (int i = 0; i < rayCount; i++)
        {
            Vector3 start = random.NextVector3(-125, 125);
            Vector3 direction = random.NextVector3(-6, 6);
            Vector3 end = start + direction;// random.NextVector3(-125, 125);
            rays3D.Add(Ray3D.CreateWithStartAndEnd(start, end));
        }

        ColliderBox3D* ptrBox = boxs3D.UnsafePointer;
        ColliderSphere3D* ptrSphere = spheres3D.UnsafePointer;

        for (int i = 0; i < boxs3D.Length; i++)
        {
            colliders3D.Add(ColliderRef3D.Create(ptrBox + i));
        }

        for (int i = 0; i < spheres3D.Length; i++)
        {
            colliders3D.Add(ColliderRef3D.Create(ptrSphere + i));
        }
        bvh3D = new NativeBvh3D();
        _castRayTask3D = new CastRayTask3D(bvh3D);

        // default build = the Morton 4-wide builder owned by the tree
        bvh3D.BuildTree(colliders3D.AsSpan());

        // pairing-order builds: same colliders, insertion order vs Morton-sorted order
        _bvh3DPairing = new NativeBvh3D();
        _bvh3DPairing.BuildTree(colliders3D.AsSpan(), PairingOrderBvhBuilder3D.Shared);
        _castRayTask3DPairing = new CastRayTask3D(_bvh3DPairing);

        _colliders3DSorted = SortCollidersByMorton3D(colliders3D);
        _bvh3DPairingSorted = new NativeBvh3D();
        _bvh3DPairingSorted.BuildTree(_colliders3DSorted.AsSpan(), PairingOrderBvhBuilder3D.Shared);
        _castRayTask3DPairingSorted = new CastRayTask3D(_bvh3DPairingSorted);

        // sanity check: both pairing builds must answer rays identically to the Morton tree
        int pairingMismatch = 0;
        int pairingSortedMismatch = 0;
        for (int i = 0; i < rays3D.Length; i++)
        {
            RayCastResult3D expected = bvh3D.CastRayClosestHit(rays3D[i]);
            RayCastResult3D pairing = _bvh3DPairing.CastRayClosestHit(rays3D[i]);
            RayCastResult3D pairingSorted = _bvh3DPairingSorted.CastRayClosestHit(rays3D[i]);
            if (expected.Hit != pairing.Hit || (expected.Hit && expected.HitInfo.Fraction != pairing.HitInfo.Fraction)) pairingMismatch++;
            if (expected.Hit != pairingSorted.Hit || (expected.Hit && expected.HitInfo.Fraction != pairingSorted.HitInfo.Fraction)) pairingSortedMismatch++;
        }
        Console.WriteLine($"[PairingCheck 3D] ray result mismatches vs Morton (insertion order): {pairingMismatch}, (Morton-sorted): {pairingSortedMismatch}");

        // AABB-only BVH: extract AABBs from the same colliders, build once
        _aabbs3D = new BoundingBox3D[colliders3D.Length];
        for (int i = 0; i < colliders3D.Length; i++)
        {
            _aabbs3D[i] = colliders3D[i].GetBoundingBox();
        }
        _bvhAabb3D = new BvhAabb3D();
        _bvhAabb3D.Build(_aabbs3D);
        _castRayTaskAabb3D = new CastRayTaskAabb3D(_bvhAabb3D);

        // sanity check: AABB BVH ray hits should agree with the collider BVH
        int aabbMismatches = 0;
        for (int i = 0; i < rays3D.Length; i++)
        {
            RayCastResult3D colliderResult = bvh3D.CastRayClosestHit(rays3D[i]);
            bool aabbHit = _bvhAabb3D.RayCastClosest(rays3D[i].Origin, rays3D[i].Displacement, out _, out _);
            if (colliderResult.Hit != aabbHit)
                aabbMismatches++;
        }
        Console.WriteLine($"[AabbCheck] ray hit/miss mismatches vs collider (Morton) BVH: {aabbMismatches}");

        _bepuPool = new BufferPool();

        _bepuTreeAdd = new Tree(_bepuPool, colliderCount);
        BuildBepuAdd();

        _bepuTreeBinned = new Tree(_bepuPool, colliderCount);
        _bepuPool.Take(colliderCount, out _bepuSubtrees);
        BuildBepuBinned();

        _castRayTaskBepuAdd = new CastRayTaskBepu(this, 0);
        _castRayTaskBepuBinned = new CastRayTaskBepu(this, 1);

        // sanity check: bepu tree queries must agree with the engine baseline
        int mismatches = 0;
        for (int i = 0; i < rays3D.Length; i++)
        {
            RayCastResult3D expected = bvh3D.CastRayClosestHit(rays3D[i]);
            RayCastResult3D add = BepuCastRayClosestHit(ref _bepuTreeAdd, rays3D[i]);
            RayCastResult3D binned = BepuCastRayClosestHit(ref _bepuTreeBinned, rays3D[i]);
            if (expected.Hit != add.Hit || (expected.Hit && expected.HitInfo.Fraction != add.HitInfo.Fraction)) mismatches++;
            if (expected.Hit != binned.Hit || (expected.Hit && expected.HitInfo.Fraction != binned.HitInfo.Fraction)) mismatches++;
        }
        Console.WriteLine($"[BepuCheck] ray result mismatches: {mismatches}");

        // work measurement: count precise leaf tests per ray on both sides
        long bepuAddLeafTests = MeasureBepuLeafTests(ref _bepuTreeAdd);
        long bepuBinnedLeafTests = MeasureBepuLeafTests(ref _bepuTreeBinned);
        Console.WriteLine($"[BepuQuality] Add: precise leaf tests/ray = {bepuAddLeafTests / (double)rays3D.Length:F1}");
        Console.WriteLine($"[BepuQuality] Binned: precise leaf tests/ray = {bepuBinnedLeafTests / (double)rays3D.Length:F1}");
    }

    private long MeasureBepuLeafTests(ref Tree tree)
    {
        _bepuLeafTestCount = 0;
        for (int i = 0; i < rays3D.Length; i++)
        {
            var tester = new CountingLeafTester { Colliders = colliders3D.UnsafePointer, Ray = rays3D[i], Owner = this };
            float maximumT = 1f;
            tree.RayCast(rays3D[i].Origin, rays3D[i].Displacement, ref maximumT, _bepuPool, ref tester);
        }
        return _bepuLeafTestCount;
    }

    internal long _bepuLeafTestCount;

    private unsafe struct CountingLeafTester : IRayLeafTester
    {
        public ColliderRef3D* Colliders;
        public Ray3D Ray;
        public BenchmarkBvh Owner;

        public void TestLeaf(int leafIndex, RayData* rayData, float* maximumT, BufferPool pool)
        {
            Owner._bepuLeafTestCount++;
            Colliders[leafIndex].IntersectRay(Ray, out _);
        }
    }

    private void BuildBepuAdd()
    {
        _bepuTreeAdd.Clear();
        for (int i = 0; i < colliders3D.Length; i++)
        {
            BoundingBox3D bounds = colliders3D[i].GetBoundingBox();
            _bepuTreeAdd.Add(new BoundingBox { Min = bounds.Min, Max = bounds.Max }, _bepuPool);
        }
    }

    private void BuildBepuBinned()
    {
        int n = colliders3D.Length;
        for (int i = 0; i < n; i++)
        {
            BoundingBox3D bounds = colliders3D[i].GetBoundingBox();
            _bepuSubtrees[i] = new NodeChild { Min = bounds.Min, Max = bounds.Max, Index = Tree.Encode(i), LeafCount = 1 };
        }
        Tree.BinnedBuild(_bepuSubtrees, _bepuTreeBinned.Nodes, _bepuTreeBinned.Metanodes, _bepuTreeBinned.Leaves, _bepuPool);
        _bepuTreeBinned.NodeCount = n - 1;
        _bepuTreeBinned.LeafCount = n;
    }

    private unsafe RayCastResult3D BepuCastRayClosestHit(ref Tree tree, Ray3D ray)
    {
        var tester = new ClosestHitLeafTester
        {
            Colliders = colliders3D.UnsafePointer,
            Ray = ray,
            Result = RayCastResult3D.none
        };
        // our rays are segments: Origin + Displacement * t, t in [0, 1]
        float maximumT = 1f;
        tree.RayCast(ray.Origin, ray.Displacement, ref maximumT, _bepuPool, ref tester);
        return tester.Result;
    }

    private unsafe struct ClosestHitLeafTester : IRayLeafTester
    {
        public ColliderRef3D* Colliders;
        public Ray3D Ray;
        public RayCastResult3D Result;

        public void TestLeaf(int leafIndex, RayData* rayData, float* maximumT, BufferPool pool)
        {
            ColliderRef3D collider = Colliders[leafIndex];
            if (collider.IntersectRay(Ray, out RaycastHit3D hitInfo))
            {
                // respect the maximumT contract like Bepu's own testers: only segment hits
                // count, and a closer hit tightens the bound so traversal can prune
                if (hitInfo.Fraction <= *maximumT)
                {
                    if (!Result.Hit || hitInfo.Fraction < Result.HitInfo.Fraction)
                    {
                        Result.Hit = true;
                        Result.HitInfo = hitInfo;
                        Result.Collider = collider;
                    }
                    *maximumT = hitInfo.Fraction;
                }
            }
        }
    }

    private class CastRayTaskBepu : ReusableBatchTask
    {
        private readonly BenchmarkBvh _owner;
        private readonly int _treeIndex; // 0 = incremental Add tree, 1 = BinnedBuild tree
        public NativeArrayList<Ray3D> rays;

        public CastRayTaskBepu(BenchmarkBvh owner, int treeIndex)
        {
            _owner = owner;
            _treeIndex = treeIndex;
        }

        protected override void ExecuteCore(int index)
        {
            if (_treeIndex == 0)
            {
                _owner.BepuCastRayClosestHit(ref _owner._bepuTreeAdd, rays[index]);
            }
            else
            {
                _owner.BepuCastRayClosestHit(ref _owner._bepuTreeBinned, rays[index]);
            }
        }
    }

    private unsafe void Setup2D()
    {
        boxs2D = new NativeArrayList<ColliderBox2D>(8);
        spheres2D = new NativeArrayList<ColliderSphere2D>(8);
        rays2D = new NativeArrayList<Ray2D>();
        colliders2D = new NativeArrayList<ColliderRef2D>();

        int colliderCount = 1500;
        int rayCount = 10000;

        FastRandom random = new FastRandom(12345);
        //random collider
        for (int i = 0; i < colliderCount / 2; i++)
        {
            Vector2 pos = random.NextVector2(-100, 100);
            Vector2 size = random.NextVector2(1, 10);
            Rotation2D rot = random.NextRotation2D();
            boxs2D.Add(new ColliderBox2D
            {
                Shape = new ShapeBox2D(pos, size, rot)
            });
        }

        for (int i = 0; i < colliderCount / 2; i++)
        {
            Vector2 pos = random.NextVector2(-100, 100);
            float radius = random.NextFloat(1, 10);
            spheres2D.Add(new ColliderSphere2D
            {
                Shape = new ShapeSphere2D(pos, radius)
            });
        }

        //random ray
        for (int i = 0; i < rayCount; i++)
        {
            Vector2 start = random.NextVector2(-125, 125);
            Vector2 direction = random.NextVector2(-6, 6);
            Vector2 end = start + direction;
            rays2D.Add(Ray2D.CreateWithStartAndEnd(start, end));
        }

        ColliderBox2D* ptrBox = boxs2D.UnsafePointer;
        ColliderSphere2D* ptrSphere = spheres2D.UnsafePointer;

        for (int i = 0; i < boxs2D.Length; i++)
        {
            colliders2D.Add(ColliderRef2D.Create(ptrBox + i));
        }

        for (int i = 0; i < spheres2D.Length; i++)
        {
            colliders2D.Add(ColliderRef2D.Create(ptrSphere + i));
        }

        bvh2D = new NativeBvh2D();
        _castRayTask = new CastRayTask(bvh2D);

        // default build = the Morton 4-wide builder owned by the tree
        bvh2D.BuildTree(colliders2D.AsSpan());

        // pairing-order builds: same colliders, insertion order vs Morton-sorted order
        _bvh2DPairing = new NativeBvh2D();
        _bvh2DPairing.BuildTree(colliders2D.AsSpan(), PairingOrderBvhBuilder2D.Shared);
        _castRayTask2DPairing = new CastRayTask(_bvh2DPairing);

        _colliders2DSorted = SortCollidersByMorton2D(colliders2D);
        _bvh2DPairingSorted = new NativeBvh2D();
        _bvh2DPairingSorted.BuildTree(_colliders2DSorted.AsSpan(), PairingOrderBvhBuilder2D.Shared);
        _castRayTask2DPairingSorted = new CastRayTask(_bvh2DPairingSorted);

        // sanity check: both pairing builds must answer rays identically to the Morton tree
        int pairingMismatch2D = 0;
        int pairingSortedMismatch2D = 0;
        for (int i = 0; i < rays2D.Length; i++)
        {
            RayCastResult2D expected = bvh2D.CastRayClosestHit(rays2D[i]);
            RayCastResult2D pairing = _bvh2DPairing.CastRayClosestHit(rays2D[i]);
            RayCastResult2D pairingSorted = _bvh2DPairingSorted.CastRayClosestHit(rays2D[i]);
            if (expected.Hit != pairing.Hit || (expected.Hit && expected.HitInfo.Fraction != pairing.HitInfo.Fraction)) pairingMismatch2D++;
            if (expected.Hit != pairingSorted.Hit || (expected.Hit && expected.HitInfo.Fraction != pairingSorted.HitInfo.Fraction)) pairingSortedMismatch2D++;
        }
        Console.WriteLine($"[PairingCheck 2D] ray result mismatches vs Morton (insertion order): {pairingMismatch2D}, (Morton-sorted): {pairingSortedMismatch2D}");

        // AABB-only BVH 2D: extract AABBs from the same colliders, build once
        _aabbs2D = new BoundingBox2D[colliders2D.Length];
        for (int i = 0; i < colliders2D.Length; i++)
        {
            _aabbs2D[i] = colliders2D[i].GetBoundingBox();
        }
        _bvhAabb2D = new BvhAabb2D();
        _bvhAabb2D.Build(_aabbs2D);
        _castRayTaskAabb2D = new CastRayTaskAabb2D(_bvhAabb2D);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        boxs3D.Dispose();
        spheres3D.Dispose();
        rays3D.Dispose();
        colliders3D.Dispose();
        bvh3D.Dispose();
        _castRayTask3D.Dispose();

        _bvh3DPairing.Dispose();
        _bvh3DPairingSorted.Dispose();
        _colliders3DSorted.Dispose();
        _castRayTask3DPairing.Dispose();
        _castRayTask3DPairingSorted.Dispose();

        _bvhAabb3D.Dispose();
        _castRayTaskAabb3D.Dispose();

        _bepuTreeAdd.Dispose(_bepuPool);
        _bepuTreeBinned.Dispose(_bepuPool);
        _bepuPool.Return(ref _bepuSubtrees);
        _bepuPool.Clear();
        _castRayTaskBepuAdd.Dispose();
        _castRayTaskBepuBinned.Dispose();

        boxs2D.Dispose();
        spheres2D.Dispose();
        rays2D.Dispose();
        colliders2D.Dispose();
        bvh2D.Dispose();
        _castRayTask.Dispose();

        _bvh2DPairing.Dispose();
        _bvh2DPairingSorted.Dispose();
        _colliders2DSorted.Dispose();
        _castRayTask2DPairing.Dispose();
        _castRayTask2DPairingSorted.Dispose();

        _bvhAabb2D.Dispose();
        _castRayTaskAabb2D.Dispose();
    }

    [Benchmark(Description = "BVH 3D (Morton 4-wide) Build tree: ")]
    public void BuildBvh3D()
    {
        bvh3D.BuildTree(colliders3D.AsSpan());
    }

    [Benchmark(Description = "BVH 3D (PairingOrder 4-wide, insertion order) Build tree: ")]
    public void BuildBvh3DPairing()
    {
        _bvh3DPairing.BuildTree(colliders3D.AsSpan(), PairingOrderBvhBuilder3D.Shared);
    }

    [Benchmark(Description = "BVH 3D (PairingOrder 4-wide, Morton-sorted input) Build tree: ")]
    public void BuildBvh3DPairingSorted()
    {
        _bvh3DPairingSorted.BuildTree(_colliders3DSorted.AsSpan(), PairingOrderBvhBuilder3D.Shared);
    }

    [Benchmark(Description = "BVH 3D (Morton 4-wide) Cast ray: ")]
    public void CastRay3D()
    {
        _castRayTask3D.rays = rays3D;
        _castRayTask3D.RunParallel(rays3D.Length, 16);
    }

    [Benchmark(Description = "BVH 3D (PairingOrder 4-wide, insertion order) Cast ray: ")]
    public void CastRay3DPairing()
    {
        _castRayTask3DPairing.rays = rays3D;
        _castRayTask3DPairing.RunParallel(rays3D.Length, 16);
    }

    [Benchmark(Description = "BVH 3D (PairingOrder 4-wide, Morton-sorted input) Cast ray: ")]
    public void CastRay3DPairingSorted()
    {
        _castRayTask3DPairingSorted.rays = rays3D;
        _castRayTask3DPairingSorted.RunParallel(rays3D.Length, 16);
    }

    [Benchmark(Description = "BVH 3D AABB (Morton) Build tree: ")]
    public void BuildBvhAabb3D()
    {
        _bvhAabb3D.Build(_aabbs3D);
    }

    [Benchmark(Description = "BVH 3D AABB (Morton) Cast ray: ")]
    public void CastRayAabb3D()
    {
        _castRayTaskAabb3D.rays = rays3D;
        _castRayTaskAabb3D.RunParallel(rays3D.Length, 16);
    }

    [Benchmark(Description = "Bepu (Add) Build tree: ")]
    public void BuildBepuAddBench()
    {
        BuildBepuAdd();
    }

    [Benchmark(Description = "Bepu (BinnedBuild) Build tree: ")]
    public void BuildBepuBinnedBench()
    {
        BuildBepuBinned();
    }

    [Benchmark(Description = "Bepu (Add) Cast ray: ")]
    public void CastRayBepuAdd()
    {
        _castRayTaskBepuAdd.rays = rays3D;
        _castRayTaskBepuAdd.RunParallel(rays3D.Length, 16);
    }

    [Benchmark(Description = "Bepu (Binned) Cast ray: ")]
    public void CastRayBepuBinned()
    {
        _castRayTaskBepuBinned.rays = rays3D;
        _castRayTaskBepuBinned.RunParallel(rays3D.Length, 16);
    }

    [Benchmark(Description = "BVH 2D (Morton 4-wide) Build tree: ")]
    public void BuildBvh2D()
    {
        bvh2D.BuildTree(colliders2D.AsSpan());
    }

    [Benchmark(Description = "BVH 2D (PairingOrder 4-wide, insertion order) Build tree: ")]
    public void BuildBvh2DPairing()
    {
        _bvh2DPairing.BuildTree(colliders2D.AsSpan(), PairingOrderBvhBuilder2D.Shared);
    }

    [Benchmark(Description = "BVH 2D (PairingOrder 4-wide, Morton-sorted input) Build tree: ")]
    public void BuildBvh2DPairingSorted()
    {
        _bvh2DPairingSorted.BuildTree(_colliders2DSorted.AsSpan(), PairingOrderBvhBuilder2D.Shared);
    }
    private struct CountCollector : IBvhCollisionCastCollector2D
    {
        public int Count;
        public bool OnHit(ColliderCastResult2D result)
        {
            Count++;
            return true;
        }
    }

    private class CastRayTask3D : ReusableBatchTask
    {
        private NativeBvh3D _bvh;
        public NativeArrayList<Ray3D> rays;

        public CastRayTask3D(NativeBvh3D bvh)
        {
            _bvh = bvh;
        }

        protected override void ExecuteCore(int index)
        {
            _bvh.CastRayClosestHit(rays[index]);
        }
    }

    private class CastRayTaskAabb3D : ReusableBatchTask
    {
        private BvhAabb3D _bvh;
        public NativeArrayList<Ray3D> rays;

        public CastRayTaskAabb3D(BvhAabb3D bvh)
        {
            _bvh = bvh;
        }

        protected override void ExecuteCore(int index)
        {
            Ray3D ray = rays[index];
            _bvh.RayCastClosest(ray.Origin, ray.Displacement, out _, out _);
        }
    }

    private class CastRayTaskAabb2D : ReusableBatchTask
    {
        private BvhAabb2D _bvh;
        public NativeArrayList<Ray2D> rays;

        public CastRayTaskAabb2D(BvhAabb2D bvh)
        {
            _bvh = bvh;
        }

        protected override void ExecuteCore(int index)
        {
            Ray2D ray = rays[index];
            _bvh.RayCastClosest(ray.Origin, ray.Displacement, out _, out _);
        }
    }

    private class CastRayTask : ReusableBatchTask
    {
        private NativeBvh2D _bvh;
        public NativeArrayList<Ray2D> rays;

        public CastRayTask(NativeBvh2D bvh)
        {
            _bvh = bvh;
        }

        protected override void ExecuteCore(int index)
        {
            _bvh.CastRayClosestHit(rays[index]);
        }
    }

    [Benchmark(Description = "BVH 2D (Morton 4-wide) Cast ray: ")]
    public void CastRay2D()
    {
        _castRayTask.rays = rays2D;
        _castRayTask.RunParallel(rays2D.Length, 16);
    }

    [Benchmark(Description = "BVH 2D (PairingOrder 4-wide, insertion order) Cast ray: ")]
    public void CastRay2DPairing()
    {
        _castRayTask2DPairing.rays = rays2D;
        _castRayTask2DPairing.RunParallel(rays2D.Length, 16);
    }

    [Benchmark(Description = "BVH 2D (PairingOrder 4-wide, Morton-sorted input) Cast ray: ")]
    public void CastRay2DPairingSorted()
    {
        _castRayTask2DPairingSorted.rays = rays2D;
        _castRayTask2DPairingSorted.RunParallel(rays2D.Length, 16);
    }

    // builds a Morton-ordered copy of the collider list: the order an external system would
    // maintain (and incrementally refine) so the pairing builder gets a coherent sequence
    private static unsafe NativeArrayList<ColliderRef3D> SortCollidersByMorton3D(NativeArrayList<ColliderRef3D> colliders)
    {
        int n = colliders.Length;
        ColliderRef3D* p = colliders.UnsafePointer;
        Vector3[] centers = new Vector3[n];
        Vector3 sceneMin = new(float.MaxValue);
        Vector3 sceneMax = new(float.MinValue);
        for (int i = 0; i < n; i++)
        {
            BoundingBox3D b = p[i].GetBoundingBox();
            centers[i] = (b.Min + b.Max) * 0.5f;
            sceneMin = Vector3.Min(sceneMin, centers[i]);
            sceneMax = Vector3.Max(sceneMax, centers[i]);
        }
        Vector3 extent = sceneMax - sceneMin;
        float invX = extent.X > 0 ? 1f / extent.X : 0f;
        float invY = extent.Y > 0 ? 1f / extent.Y : 0f;
        float invZ = extent.Z > 0 ? 1f / extent.Z : 0f;

        ulong[] keys = new ulong[n];
        for (int i = 0; i < n; i++)
        {
            uint code = Morton3D(
                (centers[i].X - sceneMin.X) * invX,
                (centers[i].Y - sceneMin.Y) * invY,
                (centers[i].Z - sceneMin.Z) * invZ);
            keys[i] = ((ulong)code << 32) | (uint)i;
        }
        Array.Sort(keys);

        NativeArrayList<ColliderRef3D> sorted = new NativeArrayList<ColliderRef3D>(n);
        for (int i = 0; i < n; i++)
        {
            sorted.Add(p[(int)(uint)keys[i]]);
        }
        return sorted;
    }

    private static unsafe NativeArrayList<ColliderRef2D> SortCollidersByMorton2D(NativeArrayList<ColliderRef2D> colliders)
    {
        int n = colliders.Length;
        ColliderRef2D* p = colliders.UnsafePointer;
        Vector2[] centers = new Vector2[n];
        Vector2 sceneMin = new(float.MaxValue);
        Vector2 sceneMax = new(float.MinValue);
        for (int i = 0; i < n; i++)
        {
            BoundingBox2D b = p[i].GetBoundingBox();
            centers[i] = (b.Min + b.Max) * 0.5f;
            sceneMin = Vector2.Min(sceneMin, centers[i]);
            sceneMax = Vector2.Max(sceneMax, centers[i]);
        }
        Vector2 extent = sceneMax - sceneMin;
        float invX = extent.X > 0 ? 1f / extent.X : 0f;
        float invY = extent.Y > 0 ? 1f / extent.Y : 0f;

        ulong[] keys = new ulong[n];
        for (int i = 0; i < n; i++)
        {
            uint code = Morton2D(
                (centers[i].X - sceneMin.X) * invX,
                (centers[i].Y - sceneMin.Y) * invY);
            keys[i] = ((ulong)code << 32) | (uint)i;
        }
        Array.Sort(keys);

        NativeArrayList<ColliderRef2D> sorted = new NativeArrayList<ColliderRef2D>(n);
        for (int i = 0; i < n; i++)
        {
            sorted.Add(p[(int)(uint)keys[i]]);
        }
        return sorted;
    }

    private static uint Morton3D(float x, float y, float z)
    {
        const float scale = (1 << 10) - 1;
        uint xi = (uint)Math.Min(Math.Max(x * scale, 0f), scale);
        uint yi = (uint)Math.Min(Math.Max(y * scale, 0f), scale);
        uint zi = (uint)Math.Min(Math.Max(z * scale, 0f), scale);
        return (Part1By2(xi) << 2) | (Part1By2(yi) << 1) | Part1By2(zi);
    }

    private static uint Morton2D(float x, float y)
    {
        const float scale = (1 << 10) - 1;
        uint xi = (uint)Math.Min(Math.Max(x * scale, 0f), scale);
        uint yi = (uint)Math.Min(Math.Max(y * scale, 0f), scale);
        return (Part1By1(xi) << 1) | Part1By1(yi);
    }

    private static uint Part1By2(uint v)
    {
        v = (v | (v << 16)) & 0x030000FFu;
        v = (v | (v << 8)) & 0x0300F00Fu;
        v = (v | (v << 4)) & 0x030C30C3u;
        v = (v | (v << 2)) & 0x09249249u;
        return v;
    }

    private static uint Part1By1(uint v)
    {
        v = (v | (v << 8)) & 0x00FF00FFu;
        v = (v | (v << 4)) & 0x0F0F0F0Fu;
        v = (v | (v << 2)) & 0x33333333u;
        v = (v | (v << 1)) & 0x55555555u;
        return v;
    }

    [Benchmark(Description = "BVH 2D AABB (Morton) Build tree: ")]
    public void BuildBvhAabb2D()
    {
        _bvhAabb2D.Build(_aabbs2D);
    }

    [Benchmark(Description = "BVH 2D AABB (Morton) Cast ray: ")]
    public void CastRayAabb2D()
    {
        _castRayTaskAabb2D.rays = rays2D;
        _castRayTaskAabb2D.RunParallel(rays2D.Length, 16);
    }
}