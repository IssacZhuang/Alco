using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Numerics;


using Random = Vocore.Random;
using System.Runtime;

namespace Vocore.Test
{
    public struct ComparerX : IComparer<ColliderRef>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(ColliderRef x, ColliderRef y)
        {
            return x.GetBoundingBox().min.X.CompareTo(y.GetBoundingBox().min.X);
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

                Vector3 pos = random.NextVector3(-100, 100);
                Vector3 size = random.NextVector3(1, 10);
                Quaternion rot = random.NextQuaternionRotation();
                boxs.Add(new ColliderBox
                {
                    shape = new ShapeBox(pos, size, rot)
                });
            }

            for (int i = 0; i < colliderCount / 2; i++)
            {

                Vector3 pos = random.NextVector3(-100, 100);
                float radius = random.NextFloat(1, 10);
                spheres.Add(new ColliderSphere
                {
                    shape = new ShapeSphere(pos, radius)
                });
            }

            //random ray
            for (int i = 0; i < rayCount; i++)
            {
                Vector3 start = random.NextVector3(-125, 125);
                Vector3 direction = random.NextVector3(-6, 6);
                Vector3 end = start + direction;// random.NextVector3(-125, 125);
                rays.Add(Ray.CreateWithStartAndEnd(start, end));
            }

            NativeArrayList<ColliderRef> colliders = new NativeArrayList<ColliderRef>(colliderCount, false);

            NativeBVH bvh = new NativeBVH();

            ColliderBox* ptrBox = boxs.DataPtr;
            ColliderSphere* ptrSphere = spheres.DataPtr;

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

            bvh.CastBatchRayFast(rays);
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



            boxs.Add(new ColliderBox
            {
                shape = new ShapeBox(new Vector3(20, 0, 0), new Vector3(1f), Quaternion.Identity)
            });


            boxs.Add(new ColliderBox
            {
                shape = new ShapeBox(new Vector3(10, 0, 0), new Vector3(1f), Quaternion.Identity)
            });

            spheres.Add(new ColliderSphere
            {
                shape = new ShapeSphere(new Vector3(-10, 0, 0), 1f)
            });

            spheres.Add(new ColliderSphere
            {
                shape = new ShapeSphere(Vector3.Zero, 0.8f)
            });

            boxs.Add(new ColliderBox
            {
                shape = new ShapeBox(Vector3.Zero, new Vector3(1f), Quaternion.Identity)
            });

            boxs.Add(new ColliderBox
            {
                shape = new ShapeBox(new Vector3(-10, 0, 0), new Vector3(1f), Quaternion.Identity)
            });





            for (int i = 0; i < boxs.Length; i++)
            {
                colliders.Add(ColliderRef.Create(boxs.DataPtr + i));
            }

            for (int i = 0; i < spheres.Length; i++)
            {
                colliders.Add(ColliderRef.Create(spheres.DataPtr + i));
            }

            // colliders.Add(ColliderRef.Create(boxs.Ptr));
            // colliders.Add(ColliderRef.Create(spheres.Ptr));

            NativeBVH bvh = new NativeBVH();
            Ray ray = Ray.CreateWithStartAndEnd(new Vector3(-2, 1.1f, 0), new Vector3(200, 1.1f, 0));

            bvh.BuildTree(colliders);

            //RayCastResult result = bvh.CastRay(ray);

            //UnitTest.AssertFalse(result.hit);


            ray = Ray.CreateWithStartAndEnd(new Vector3(-1.2f, 0, 0), new Vector3(120f, 0, 0));

            RayCastResult result = bvh.CastRay(ray);

            UnitTest.AssertFalse(!result.hit);
            UnitTest.PrintBlue(result.hitInfo.fraction);
            UnitTest.PrintBlue(result.hitInfo.point);

            boxs.Dispose();
            spheres.Dispose();
            colliders.Dispose();
            bvh.Dispose();

        }

        [Test("Test BVH collider collision")]
        public unsafe void TestColliderCollision()
        {
            NativeArrayList<ColliderBox> boxs = new NativeArrayList<ColliderBox>(8);
            NativeArrayList<ColliderSphere> spheres = new NativeArrayList<ColliderSphere>(8);
            NativeArrayList<ColliderRef> colliders = new NativeArrayList<ColliderRef>();

            boxs.Add(new ColliderBox
            {
                shape = new ShapeBox(Vector3.Zero, new Vector3(1f), Quaternion.Identity)
            });

            boxs.Add(new ColliderBox
            {
                shape = new ShapeBox(new Vector3(5, 0, 0), new Vector3(1f), Quaternion.Identity)
            });

            boxs.Add(new ColliderBox
            {
                shape = new ShapeBox(new Vector3(5, 5, 0), new Vector3(1f), Quaternion.Identity)
            });

            spheres.Add(new ColliderSphere
            {
                shape = new ShapeSphere(Vector3.Zero, 1f)
            });

            for (int i = 0; i < boxs.Length; i++)
            {
                colliders.Add(ColliderRef.Create(boxs.DataPtr + i));
            }

            for (int i = 0; i < spheres.Length; i++)
            {
                colliders.Add(ColliderRef.Create(spheres.DataPtr + i));
            }

            NativeBVH bvh = new NativeBVH();

            bvh.BuildTree(colliders);

            ColliderBox boxCast1 = new ColliderBox
            {
                shape = new ShapeBox(new Vector3(-2, 1.1f, 0), new Vector3(1f), Quaternion.Identity)
            };

            ColliderBox boxCast2 = new ColliderBox
            {
                shape = new ShapeBox(new Vector3(-1.2f, 0, 0), new Vector3(1f), Quaternion.Identity)
            };

            ColliderSphere sphereCast1 = new ColliderSphere
            {
                shape = new ShapeSphere(new Vector3(-2, 1.1f, 0), 1f)
            };

            ColliderSphere sphereCast2 = new ColliderSphere
            {
                shape = new ShapeSphere(new Vector3(-1.2f, 0, 0), 1f)
            };

            UnitTest.AssertFalse(bvh.CastColliderBox(ref boxCast1).hit);
            UnitTest.AssertFalse(!bvh.CastColliderBox(ref boxCast2).hit);

            UnitTest.AssertFalse(bvh.CastColliderSphere(ref sphereCast1).hit);
            UnitTest.AssertFalse(!bvh.CastColliderSphere(ref sphereCast2).hit);

            boxs.Dispose();
            spheres.Dispose();
            colliders.Dispose();
            bvh.Dispose();

        }
    }
}

