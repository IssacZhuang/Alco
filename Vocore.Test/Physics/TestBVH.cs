using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

using Unity.Mathematics;
using Random = Unity.Mathematics.Random;
using System.Runtime;

namespace Vocore.Test
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
        [Test("Benchmark build BVH tree")]
        public unsafe void TestBuildTree()
        {
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

            UnitTest.Benchmark("add coliider", () =>
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

            // UnitTest.Benchmark("sort coliider", () =>
            // {
            //     colliders.Sort(default(ComparerX));
            // });

            //warm up
            bvh.BuildTree(colliders);

            UnitTest.Benchmark("Build BVH tree", () =>
            {
                bvh.BuildTree(colliders);
            });



            UnitTest.PrintBlue(bvh.Size + "," + bvh.Capacity);

            //warm up
            bvh.CastBatchRay(rays);
            UnitTest.Benchmark("Ray cast bvh", () =>
            {
                bvh.CastBatchRay(rays);
            });

            UnitTest.Benchmark("Ray cast bvh fast", () =>
            {
                bvh.CastBatchRayFast(rays);
            });

            boxs.Dispose();
            spheres.Dispose();
            rays.Dispose();
            colliders.Dispose();
            bvh.Dispose();
        }

        [Test("Test BVH ray collision")]
        public unsafe void TestRayCollision()
        {
            NativeArrayList<ColliderBox> boxs = new NativeArrayList<ColliderBox>(8);
            NativeArrayList<ColliderSphere> spheres = new NativeArrayList<ColliderSphere>(8);
            NativeArrayList<ColliderRef> colliders = new NativeArrayList<ColliderRef>();
            Ray ray = Ray.CreateWithStartAndEnd(new float3(-2, 1.1f, 0), new float3(200, 1.1f, 0));


            boxs.Add(new ColliderBox
            {
                shape = new ShapeBox(new float3(10, 0, 0), new float3(1f), quaternion.identity)
            });

            boxs.Add(new ColliderBox
            {
                shape = new ShapeBox(float3.zero, new float3(1f), quaternion.identity)
            });

            boxs.Add(new ColliderBox
            {
                shape = new ShapeBox(new float3(-10, 0, 0), new float3(1f), quaternion.identity)
            });


            spheres.Add(new ColliderSphere
            {
                shape = new ShapeSphere(float3.zero, 1f)
            });

            colliders.Add(ColliderRef.Create(boxs.Ptr));
            colliders.Add(ColliderRef.Create(spheres.Ptr));

            NativeBVH bvh = new NativeBVH();

            bvh.BuildTree(colliders);

            RayCastResult result = bvh.CastRay(ray);

            UnitTest.AssertFalse(result.hit);


            ray = Ray.CreateWithStartAndEnd(new float3(-1.2f, 0, 0), new float3(120f, 0, 0));

            result = bvh.CastRay(ray);

            UnitTest.AssertFalse(!result.hit);
            UnitTest.PrintBlue(result.hitInfo.point);

        }

        [Test("Test BVH collider collision")]
        public unsafe void TestColliderCollision()
        {
            NativeArrayList<ColliderBox> boxs = new NativeArrayList<ColliderBox>(8);
            NativeArrayList<ColliderSphere> spheres = new NativeArrayList<ColliderSphere>(8);
            NativeArrayList<ColliderRef> colliders = new NativeArrayList<ColliderRef>();

            boxs.Add(new ColliderBox
            {
                shape = new ShapeBox(float3.zero, new float3(1f), quaternion.identity)
            });

            boxs.Add(new ColliderBox
            {
                shape = new ShapeBox(new float3(5, 0, 0), new float3(1f), quaternion.identity)
            });

            boxs.Add(new ColliderBox
            {
                shape = new ShapeBox(new float3(5, 5, 0), new float3(1f), quaternion.identity)
            });

            spheres.Add(new ColliderSphere
            {
                shape = new ShapeSphere(float3.zero, 1f)
            });

            for (int i = 0; i < boxs.Length; i++)
            {
                colliders.Add(ColliderRef.Create(boxs.Ptr + i));
            }

            for (int i = 0; i < spheres.Length; i++)
            {
                colliders.Add(ColliderRef.Create(spheres.Ptr + i));
            }

            NativeBVH bvh = new NativeBVH();

            bvh.BuildTree(colliders);

            ColliderBox boxCast1 = new ColliderBox
            {
                shape = new ShapeBox(new float3(-2, 1.1f, 0), new float3(1f), quaternion.identity)
            };

            ColliderBox boxCast2 = new ColliderBox
            {
                shape = new ShapeBox(new float3(-1.2f, 0, 0), new float3(1f), quaternion.identity)
            };

            ColliderSphere sphereCast1 = new ColliderSphere
            {
                shape = new ShapeSphere(new float3(-2, 1.1f, 0), 1f)
            };

            ColliderSphere sphereCast2 = new ColliderSphere
            {
                shape = new ShapeSphere(new float3(-1.2f, 0, 0), 1f)
            };

            UnitTest.AssertFalse(bvh.CastColliderBox(ref boxCast1).hit);
            UnitTest.AssertFalse(!bvh.CastColliderBox(ref boxCast2).hit);

            UnitTest.AssertFalse(bvh.CastColliderSphere(ref sphereCast1).hit);
            UnitTest.AssertFalse(!bvh.CastColliderSphere(ref sphereCast2).hit);

        }
    }
}

