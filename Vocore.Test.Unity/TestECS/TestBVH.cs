using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using UnityToolBox.UnitTest;
using Unity.Entities;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

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
            NativeArrayList<ColliderBox> boxs = new NativeArrayList<ColliderBox>();
            NativeArrayList<ColliderSphere> spheres = new NativeArrayList<ColliderSphere>();
            NativeArrayList<Ray> rays = new NativeArrayList<Ray>();

            int colliderCount = 1500;
            int rayCount = 1000;

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

            NativeArrayList<ColliderRef> colliders = new NativeArrayList<ColliderRef>(64, false);

            NativeBVH bvh = new NativeBVH();


            TestHelper.Benchmark("sort coliider", () =>
            {
                colliders.Sort(default(ComparerX));
            });

            ColliderBox* ptr = boxs.Ptr;
            for (int i = 0; i < colliderCount; i++)
            {
                colliders.Add(ColliderRef.Create(ref *(ptr + i)));
            }

            bvh.BuildTree(colliders);
            colliders.Clear();

            TestHelper.Benchmark("Build BVH tree", () =>
            {
                for (int i = 0; i < colliderCount; i++)
                {
                    colliders.Add(ColliderRef.Create(ref *(ptr + i)));
                }
                bvh.BuildTree(colliders);

            });

            TestHelper.Benchmark("Ray cast bvh", () =>
            {
                for (int i = 0; i < rayCount; i++)
                {
                    bvh.CastRay(rays[i]);
                }
            });

            // TestHelper.Benchmark("Ray cast brute force", () =>
            // {
            //     for (int i = 0; i < rayCount; i++)
            //     {
            //         for (int j = 0; j < colliderCount; j++)
            //         {
            //             UtilsCollision.RayAABB(rays[i], list[j].GetBoundingBox());
            //         }
            //     }
            // });

        }
    }
}

