using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Collections.Generic;
using System.Numerics;
using Vocore.Unsafe;

namespace Vocore.Benchmark;

public class BenchmarkBvh
{
    NativeArrayList<ColliderBox3D> boxs3D;
    NativeArrayList<ColliderSphere3D> spheres3D;
    NativeArrayList<Ray3D> rays3D;
    NativeArrayList<ColliderRef3D> colliders3D;
    ParallelScheduler scheduler3D;
    NativeBvh3D bvh3D;

    NativeArrayList<ColliderBox2D> boxs2D;
    NativeArrayList<ColliderSphere2D> spheres2D;
    NativeArrayList<Ray2D> rays2D;
    NativeArrayList<ColliderRef2D> colliders2D;
    ParallelScheduler scheduler2D;
    NativeBvh2D bvh2D;

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

        Random random = new Random(12345);
        //random collider
        for (int i = 0; i < colliderCount / 2; i++)
        {

            Vector3 pos = random.NextVector3(-100, 100);
            Vector3 size = random.NextVector3(1, 10);
            Quaternion rot = random.NextQuaternionRotation();
            boxs3D.Add(new ColliderBox3D
            {
                shape = new ShapeBox3D(pos, size, rot)
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

        scheduler3D = new ParallelScheduler();
        bvh3D = new NativeBvh3D(scheduler3D);

        bvh3D.BuildTree(colliders3D.MemoryRef);
        bvh3D.CastBatchRay(rays3D.MemoryRef);
    }

    private unsafe void Setup2D()
    {
        boxs2D = new NativeArrayList<ColliderBox2D>(8);
        spheres2D = new NativeArrayList<ColliderSphere2D>(8);
        rays2D = new NativeArrayList<Ray2D>();
        colliders2D = new NativeArrayList<ColliderRef2D>();

        int colliderCount = 1500;
        int rayCount = 10000;

        Random random = new Random(12345);
        //random collider
        for (int i = 0; i < colliderCount / 2; i++)
        {
            Vector2 pos = random.NextVector2(-100, 100);
            Vector2 size = random.NextVector2(1, 10);
            Rotation2D rot = random.NextRotation2D();
            boxs2D.Add(new ColliderBox2D
            {
                shape = new ShapeBox2D(pos, size, rot)
            });
        }

        for (int i = 0; i < colliderCount / 2; i++)
        {
            Vector2 pos = random.NextVector2(-100, 100);
            float radius = random.NextFloat(1, 10);
            spheres2D.Add(new ColliderSphere2D
            {
                shape = new ShapeSphere2D(pos, radius)
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

        scheduler2D = new ParallelScheduler();
        bvh2D = new NativeBvh2D(scheduler2D);

        bvh2D.BuildTree(colliders2D.MemoryRef);
        bvh2D.CastBatchRay(rays2D.MemoryRef);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        boxs3D.Dispose();
        spheres3D.Dispose();
        rays3D.Dispose();
        colliders3D.Dispose();
        bvh3D.Dispose();
        scheduler3D.Dispose();

        boxs2D.Dispose();
        spheres2D.Dispose();
        rays2D.Dispose();
        colliders2D.Dispose();
        bvh2D.Dispose();
        scheduler2D.Dispose();
    }

    [Benchmark(Description = "BVH 3D Build tree: ")]
    public void BuildBvh3D()
    {
        bvh3D.BuildTree(colliders3D.MemoryRef);
    }

    [Benchmark(Description = "BVH 3D Cast ray: ")]
    public void CastRay3D()
    {
        bvh3D.CastBatchRay(rays3D.MemoryRef);
    }

    [Benchmark(Description = "BVH 2D Build tree: ")]
    public void BuildBvh2D()
    {
        bvh2D.BuildTree(colliders2D.MemoryRef);
    }

    [Benchmark(Description = "BVH 2D Cast ray: ")]
    public void CastRay2D()
    {
        bvh2D.CastBatchRay(rays2D.MemoryRef);
    }
}