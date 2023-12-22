using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Numerics;


using Random = Vocore.Random;
using System.Runtime;

namespace Vocore.Test
{

    public class TestBvh2D
    {
        [Test("Benchmark build BVH tree 2D")]
        public unsafe void TestBuildTree()
        {
            NativeArrayList<ColliderBox2D> boxs = new NativeArrayList<ColliderBox2D>(8);
            NativeArrayList<ColliderSphere2D> spheres = new NativeArrayList<ColliderSphere2D>(8);
            NativeArrayList<Ray2D> rays = new NativeArrayList<Ray2D>();

            int colliderCount = 1500;
            int rayCount = 10000;

            Random random = new Random(12345);
            //random collider
            for (int i = 0; i < colliderCount / 2; i++)
            {

                Vector2 pos = random.NextVector2(-100, 100);
                Vector2 size = random.NextVector2(1, 10);
                Rotation2D rot = random.NextRotation2D();
                boxs.Add(new ColliderBox2D
                {
                    shape = new ShapeBox2D(pos, size, rot)
                });
            }

            for (int i = 0; i < colliderCount / 2; i++)
            {

                Vector2 pos = random.NextVector2(-100, 100);
                float radius = random.NextFloat(1, 10);
                spheres.Add(new ColliderSphere2D
                {
                    shape = new ShapeSphere2D(pos, radius)
                });
            }

            //random ray
            for (int i = 0; i < rayCount; i++)
            {
                Vector2 start = random.NextVector2(-125, 125);
                Vector2 direction = random.NextVector2(-6, 6);
                Vector2 end = start + direction;// random.NextVector2(-125, 125);
                rays.Add(Ray2D.CreateWithStartAndEnd(start, end));
            }

            NativeArrayList<ColliderRef2D> colliders = new NativeArrayList<ColliderRef2D>(colliderCount, false);

            NativeBvh2D bvh = new NativeBvh2D();

            ColliderBox2D* ptrBox = boxs.DataPtr;
            ColliderSphere2D* ptrSphere = spheres.DataPtr;

            colliders.Clear();

            UnitTest.Benchmark("add coliider", () =>
            {
                for (int i = 0; i < boxs.Length; i++)
                {
                    colliders.Add(ColliderRef2D.Create(ptrBox + i));
                }

                for (int i = 0; i < spheres.Length; i++)
                {
                    colliders.Add(ColliderRef2D.Create(ptrSphere + i));
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
            UnitTest.CheckGCAlloc("Ray cast bvh fast", () =>
            {
                bvh.CastBatchRayFast(rays);
            });

            boxs.Dispose();
            spheres.Dispose();
            rays.Dispose();
            colliders.Dispose();
            bvh.Dispose();
        }

        [Test("Test BVH ray collision 2D")]
        public unsafe void TestRayCollision()
        {
            NativeArrayList<ColliderBox2D> boxs = new NativeArrayList<ColliderBox2D>(8);
            NativeArrayList<ColliderSphere2D> spheres = new NativeArrayList<ColliderSphere2D>(8);
            NativeArrayList<ColliderRef2D> colliders = new NativeArrayList<ColliderRef2D>();



            boxs.Add(new ColliderBox2D
            {
                shape = new ShapeBox2D(new Vector2(20, 0), new Vector2(1f), Rotation2D.Identity)
            });


            boxs.Add(new ColliderBox2D
            {
                shape = new ShapeBox2D(new Vector2(10, 0), new Vector2(1f), Rotation2D.Identity)
            });

            spheres.Add(new ColliderSphere2D
            {
                shape = new ShapeSphere2D(new Vector2(-10, 0), 1f)
            });

            spheres.Add(new ColliderSphere2D
            {
                shape = new ShapeSphere2D(Vector2.Zero, 0.8f)
            });

            boxs.Add(new ColliderBox2D
            {
                shape = new ShapeBox2D(Vector2.Zero, new Vector2(1f), Rotation2D.Identity)
            });

            boxs.Add(new ColliderBox2D
            {
                shape = new ShapeBox2D(new Vector2(-10, 0), new Vector2(1f), Rotation2D.Identity)
            });





            for (int i = 0; i < boxs.Length; i++)
            {
                colliders.Add(ColliderRef2D.Create(boxs.DataPtr + i));
            }

            for (int i = 0; i < spheres.Length; i++)
            {
                colliders.Add(ColliderRef2D.Create(spheres.DataPtr + i));
            }

            // colliders.Add(ColliderRef.Create(boxs.Ptr));
            // colliders.Add(ColliderRef.Create(spheres.Ptr));

            NativeBvh2D bvh = new NativeBvh2D();
            Ray2D ray = Ray2D.CreateWithStartAndEnd(new Vector2(-2, 1.1f), new Vector2(200, 1.1f));

            bvh.BuildTree(colliders);

            //RayCastResult result = bvh.CastRay(ray);

            //UnitTest.AssertFalse(result.hit);


            ray = Ray2D.CreateWithStartAndEnd(new Vector2(-1.2f, 0), new Vector2(120f, 0));

            RayCastResult2D result = bvh.CastRay(ray);

            UnitTest.AssertFalse(!result.hit);
            UnitTest.PrintBlue(result.hitInfo.fraction);
            UnitTest.PrintBlue(result.hitInfo.point);

            boxs.Dispose();
            spheres.Dispose();
            colliders.Dispose();
            bvh.Dispose();

        }

        [Test("Test BVH collider collision 2D")]
        public unsafe void TestColliderCollision()
        {
            NativeArrayList<ColliderBox2D> boxs = new NativeArrayList<ColliderBox2D>(8);
            NativeArrayList<ColliderSphere2D> spheres = new NativeArrayList<ColliderSphere2D>(8);
            NativeArrayList<ColliderRef2D> colliders = new NativeArrayList<ColliderRef2D>();

            boxs.Add(new ColliderBox2D
            {
                shape = new ShapeBox2D(Vector2.Zero, new Vector2(1f), Rotation2D.Identity)
            });

            boxs.Add(new ColliderBox2D
            {
                shape = new ShapeBox2D(new Vector2(5, 0), new Vector2(1f), Rotation2D.Identity)
            });

            boxs.Add(new ColliderBox2D
            {
                shape = new ShapeBox2D(new Vector2(5, 5), new Vector2(1f), Rotation2D.Identity)
            });

            spheres.Add(new ColliderSphere2D
            {
                shape = new ShapeSphere2D(Vector2.Zero, 1f)
            });

            for (int i = 0; i < boxs.Length; i++)
            {
                colliders.Add(ColliderRef2D.Create(boxs.DataPtr + i));
            }

            for (int i = 0; i < spheres.Length; i++)
            {
                colliders.Add(ColliderRef2D.Create(spheres.DataPtr + i));
            }

            NativeBvh2D bvh = new NativeBvh2D();

            bvh.BuildTree(colliders);

            ColliderBox2D boxCast1 = new ColliderBox2D
            {
                shape = new ShapeBox2D(new Vector2(-2, 1.1f), new Vector2(1f), Rotation2D.Identity)
            };

            ColliderBox2D boxCast2 = new ColliderBox2D
            {
                shape = new ShapeBox2D(new Vector2(-1.2f, 0), new Vector2(1f), Rotation2D.Identity)
            };

            ColliderSphere2D sphereCast1 = new ColliderSphere2D
            {
                shape = new ShapeSphere2D(new Vector2(-2, 1.1f), 1f)
            };

            ColliderSphere2D sphereCast2 = new ColliderSphere2D
            {
                shape = new ShapeSphere2D(new Vector2(-1.2f, 0), 1f)
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

