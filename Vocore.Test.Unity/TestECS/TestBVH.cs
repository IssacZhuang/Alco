using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Unity.Jobs;
using UnityToolBox.UnitTest;
using Unity.Entities;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

using Vocore.Unsafe;

namespace Vocore.Test.Unity
{
    public struct ComparerX : IComparer<ColliderRef>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(ColliderRef x, ColliderRef y)
        {
            return x.GetBoundingBox().min.x.CompareTo(y.GetBoundingBox().min.x);
        }
    }

    public class TestBVH
    {
        [UnitTest("Benchmark build BVH tree")]
        public unsafe void TestBuildTree()
        {
            TestHelper.PrintBlue(UtilsMemory.SizeOf<NativeBVH.Node>());
            NativeArrayList<ColliderBox> boxs = new NativeArrayList<ColliderBox>(8);
            NativeArrayList<ColliderSphere> spheres = new NativeArrayList<ColliderSphere>(8);
            NativeArrayList<Ray> rays = new NativeArrayList<Ray>();

            int colliderCount = 1500;
            int rayCount = 10000;

            Random random = new Random(12345);
            //random collider
            for (int i = 0; i < colliderCount / 2; i++)
            {

                float3 pos = random.NextFloat3(-100, 100);
                float3 size = random.NextFloat3(1, 10);
                quaternion rot = random.NextQuaternionRotation();
                boxs.Add(new ColliderBox
                {
                    shape = new ShapeBox(pos, size, rot)
                });
            }

            for (int i = 0; i < colliderCount / 2; i++)
            {

                float3 pos = random.NextFloat3(-100, 100);
                float radius = random.NextFloat(1, 10);
                spheres.Add(new ColliderSphere
                {
                    shape = new ShapeSphere(pos, radius)
                });
            }

            //random ray
            for (int i = 0; i < rayCount; i++)
            {
                float3 start = random.NextFloat3(-125, 125);
                float3 end = random.NextFloat3(-125, 125);
                rays.Add(Ray.CreateWithStartAndEnd(start, end));
            }

            NativeArrayList<ColliderRef> colliders = new NativeArrayList<ColliderRef>(colliderCount, false);

            NativeBVH bvh = new NativeBVH();

            ColliderBox* ptrBox = boxs.Ptr;
            ColliderSphere* ptrSphere = spheres.Ptr;

            colliders.Clear();

            TestHelper.Benchmark("add coliider", () =>
            {
                for (int i = 0; i < boxs.Length; i++)
                {
                    colliders.Add(ColliderRef.Create(ptrBox + i));
                }

                for (int i = 0; i < spheres.Length; i++)
                {
                    colliders.Add(ColliderRef.Create(ptrSphere + i));
                }
            });

            // TestHelper.Benchmark("sort coliider", () =>
            // {
            //     colliders.Sort(default(ComparerX));
            // });

            TestHelper.Benchmark("Build BVH tree", () =>
            {
                bvh.BuildTree(colliders);
            });

            TestHelper.Benchmark("Ray cast bvh", () =>
            {
                bvh.CastBatchRay(rays);
            });

            TestHelper.Benchmark("Ray cast bvh fast", () =>
            {
                bvh.CastBatchRayFast(rays);
            });

            boxs.Dispose();
            spheres.Dispose();
            rays.Dispose();
            colliders.Dispose();
            bvh.Dispose();
        }
    }
}

