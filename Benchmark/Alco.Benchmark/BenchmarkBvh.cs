using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
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

    private MortonBvhBuilder3D _mortonBuilder3D;
    NativeBvh3D bvh3DMorton;

    NativeArrayList<ColliderBox2D> boxs2D;
    NativeArrayList<ColliderSphere2D> spheres2D;
    NativeArrayList<Ray2D> rays2D;
    NativeArrayList<ColliderRef2D> colliders2D;
    NativeBvh2D bvh2D;

    private CastRayTask _castRayTask;
    private CastRayTask3D _castRayTask3D;
    private CastRayTask3D _castRayTask3DMorton;

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

        bvh3D.BuildTree(colliders3D.AsSpan());

        _mortonBuilder3D = new MortonBvhBuilder3D();
        bvh3DMorton = new NativeBvh3D();
        _castRayTask3DMorton = new CastRayTask3D(bvh3DMorton);
        bvh3DMorton.BuildTree(colliders3D.AsSpan(), _mortonBuilder3D);

        _bepuPool = new BufferPool();

        _bepuTreeAdd = new Tree(_bepuPool, colliderCount);
        BuildBepuAdd();

        _bepuTreeBinned = new Tree(_bepuPool, colliderCount);
        _bepuPool.Take(colliderCount, out _bepuSubtrees);
        BuildBepuBinned();

        _castRayTaskBepuAdd = new CastRayTaskBepu(this, 0);
        _castRayTaskBepuBinned = new CastRayTaskBepu(this, 1);

        // sanity check: bepu tree queries must agree with the pairing baseline
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
                if (!Result.Hit || hitInfo.Fraction < Result.HitInfo.Fraction)
                {
                    Result.Hit = true;
                    Result.HitInfo = hitInfo;
                    Result.Collider = collider;
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

        bvh2D.BuildTree(colliders2D.AsSpan());
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
        bvh3DMorton.Dispose();
        _castRayTask3DMorton.Dispose();
        _mortonBuilder3D.Dispose();

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
    }

    [Benchmark(Description = "BVH 3D Build tree: ")]
    public void BuildBvh3D()
    {
        bvh3D.BuildTree(colliders3D.AsSpan());
    }

    [Benchmark(Description = "BVH 3D Cast ray: ")]
    public void CastRay3D()
    {
        _castRayTask3D.rays = rays3D;
        _castRayTask3D.RunParallel(rays3D.Length, 16);
    }

    [Benchmark(Description = "BVH 3D (Morton) Build tree: ")]
    public void BuildBvh3DMorton()
    {
        bvh3DMorton.BuildTree(colliders3D.AsSpan(), _mortonBuilder3D);
    }

    [Benchmark(Description = "BVH 3D (Morton) Cast ray: ")]
    public void CastRay3DMorton()
    {
        _castRayTask3DMorton.rays = rays3D;
        _castRayTask3DMorton.RunParallel(rays3D.Length, 16);
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

    [Benchmark(Description = "BVH 2D Build tree: ")]
    public void BuildBvh2D()
    {
        bvh2D.BuildTree(colliders2D.AsSpan());
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

    [Benchmark(Description = "BVH 2D Cast ray: ")]
    public void CastRay2D()
    {
        _castRayTask.rays = rays2D;
        _castRayTask.RunParallel(rays2D.Length, 16);
    }
}