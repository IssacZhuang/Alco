using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Numerics;


using Random = Vocore.Random;
using System.Runtime;
using TestFramework;

namespace Vocore.Test
{

    public class TestBvh3D
    {
        [Test(Description = "Benchmark build BVH tree 3D")]
        public unsafe void TestBuildTree()
        {
            NativeArrayList<ColliderBox3D> boxs = new NativeArrayList<ColliderBox3D>(8);
            NativeArrayList<ColliderSphere3D> spheres = new NativeArrayList<ColliderSphere3D>(8);
            NativeArrayList<Ray3D> rays = new NativeArrayList<Ray3D>();

            int colliderCount = 1500;
            int rayCount = 10000;

            Random random = new Random(12345);
            //random collider
            for (int i = 0; i < colliderCount / 2; i++)
            {

                Vector3 pos = random.NextVector3(-100, 100);
                Vector3 size = random.NextVector3(1, 10);
                Quaternion rot = random.NextQuaternionRotation();
                boxs.Add(new ColliderBox3D
                {
                    shape = new ShapeBox3D(pos, size, rot)
                });
            }

            for (int i = 0; i < colliderCount / 2; i++)
            {

                Vector3 pos = random.NextVector3(-100, 100);
                float radius = random.NextFloat(1, 10);
                spheres.Add(new ColliderSphere3D
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
                rays.Add(Ray3D.CreateWithStartAndEnd(start, end));
            }

            NativeArrayList<ColliderRef3D> colliders = new NativeArrayList<ColliderRef3D>(colliderCount, false);

            using ParallelScheduler scheduler = new ParallelScheduler();
            NativeBvh3D bvh = new NativeBvh3D(scheduler);

            ColliderBox3D* ptrBox = boxs.UnsafePointer;
            ColliderSphere3D* ptrSphere = spheres.UnsafePointer;

            colliders.Clear();

            UtilsTest.Benchmark(() =>
            {
                for (int i = 0; i < boxs.Length; i++)
                {
                    colliders.Add(ColliderRef3D.Create(ptrBox + i));
                }

                for (int i = 0; i < spheres.Length; i++)
                {
                    colliders.Add(ColliderRef3D.Create(ptrSphere + i));
                }
            }, "Add coliider benchmark: ");

            // UnitTest.Benchmark("sort coliider", () =>
            // {
            //     colliders.Sort(default(ComparerX));
            // });

            //warm up
            bvh.BuildTree(colliders.MemoryRef);

            UtilsTest.Benchmark(() =>
            {
                bvh.BuildTree(colliders.MemoryRef);
            }, "Build BVH 3D tree benchmark: ");

            TestContext.WriteLine(bvh.Size + "," + bvh.Capacity);

            //warm up
            bvh.CastBatchRay(rays.MemoryRef);
            bvh.CastBatchRayFast(rays.MemoryRef);

            UtilsTest.Benchmark(() =>
            {
                bvh.CastBatchRay(rays.MemoryRef);
            }, "Ray cast bvh 3D benckmark: ");

            bvh.CastBatchRayFast(rays.MemoryRef);

            UtilsTest.Benchmark(() =>
            {
                bvh.CastBatchRayFast(rays.MemoryRef);
            }, "Ray cast bvh 3D fast GC Alloc: ");

            boxs.Dispose();
            spheres.Dispose();
            rays.Dispose();
            colliders.Dispose();
            bvh.Dispose();
        }

        [Test(Description = "Test BVH ray collision 3D")]
        public unsafe void TestRayCollision()
        {
            NativeArrayList<ColliderBox3D> boxs = new NativeArrayList<ColliderBox3D>(8);
            NativeArrayList<ColliderSphere3D> spheres = new NativeArrayList<ColliderSphere3D>(8);
            NativeArrayList<ColliderRef3D> colliders = new NativeArrayList<ColliderRef3D>();



            boxs.Add(new ColliderBox3D
            {
                shape = new ShapeBox3D(new Vector3(20, 0, 0), new Vector3(1f), Quaternion.Identity)
            });


            boxs.Add(new ColliderBox3D
            {
                shape = new ShapeBox3D(new Vector3(10, 0, 0), new Vector3(1f), Quaternion.Identity)
            });

            spheres.Add(new ColliderSphere3D
            {
                shape = new ShapeSphere3D(new Vector3(-10, 0, 0), 1f)
            });

            spheres.Add(new ColliderSphere3D
            {
                shape = new ShapeSphere3D(Vector3.Zero, 0.8f)
            });

            boxs.Add(new ColliderBox3D
            {
                shape = new ShapeBox3D(Vector3.Zero, new Vector3(1f), Quaternion.Identity)
            });

            boxs.Add(new ColliderBox3D
            {
                shape = new ShapeBox3D(new Vector3(-10, 0, 0), new Vector3(1f), Quaternion.Identity)
            });





            for (int i = 0; i < boxs.Length; i++)
            {
                colliders.Add(ColliderRef3D.Create(boxs.UnsafePointer + i));
            }

            for (int i = 0; i < spheres.Length; i++)
            {
                colliders.Add(ColliderRef3D.Create(spheres.UnsafePointer + i));
            }

            // colliders.Add(ColliderRef.Create(boxs.Ptr));
            // colliders.Add(ColliderRef.Create(spheres.Ptr));

            using ParallelScheduler scheduler = new ParallelScheduler();
            NativeBvh3D bvh = new NativeBvh3D(scheduler);
            Ray3D ray = Ray3D.CreateWithStartAndEnd(new Vector3(-2, 1.1f, 0), new Vector3(200, 1.1f, 0));

            bvh.BuildTree(colliders.MemoryRef);

            //RayCastResult result = bvh.CastRay(ray);

            //Assert.IsFalse(result.hit);


            ray = Ray3D.CreateWithStartAndEnd(new Vector3(-1.2f, 0, 0), new Vector3(120f, 0, 0));

            RayCastResult3D result = bvh.CastRay(ray);

            Assert.IsFalse(!result.hit);
            TestContext.WriteLine(result.hitInfo.fraction);
            TestContext.WriteLine(result.hitInfo.point);

            boxs.Dispose();
            spheres.Dispose();
            colliders.Dispose();
            bvh.Dispose();

        }

        [Test(Description = "Test BVH collider collision 3D")]
        public unsafe void TestColliderCollision()
        {
            NativeArrayList<ColliderBox3D> boxs = new NativeArrayList<ColliderBox3D>(8);
            NativeArrayList<ColliderSphere3D> spheres = new NativeArrayList<ColliderSphere3D>(8);
            NativeArrayList<ColliderRef3D> colliders = new NativeArrayList<ColliderRef3D>();

            boxs.Add(new ColliderBox3D
            {
                shape = new ShapeBox3D(Vector3.Zero, new Vector3(1f), Quaternion.Identity)
            });

            boxs.Add(new ColliderBox3D
            {
                shape = new ShapeBox3D(new Vector3(5, 0, 0), new Vector3(1f), Quaternion.Identity)
            });

            boxs.Add(new ColliderBox3D
            {
                shape = new ShapeBox3D(new Vector3(5, 5, 0), new Vector3(1f), Quaternion.Identity)
            });

            spheres.Add(new ColliderSphere3D
            {
                shape = new ShapeSphere3D(Vector3.Zero, 1f)
            });

            for (int i = 0; i < boxs.Length; i++)
            {
                colliders.Add(ColliderRef3D.Create(boxs.UnsafePointer + i));
            }

            for (int i = 0; i < spheres.Length; i++)
            {
                colliders.Add(ColliderRef3D.Create(spheres.UnsafePointer + i));
            }

            using ParallelScheduler scheduler = new ParallelScheduler();
            NativeBvh3D bvh = new NativeBvh3D(scheduler);

            bvh.BuildTree(colliders.MemoryRef);

            ColliderBox3D boxCast1 = new ColliderBox3D
            {
                shape = new ShapeBox3D(new Vector3(-2, 1.1f, 0), new Vector3(1f), Quaternion.Identity)
            };

            ColliderBox3D boxCast2 = new ColliderBox3D
            {
                shape = new ShapeBox3D(new Vector3(-1.2f, 0, 0), new Vector3(1f), Quaternion.Identity)
            };

            ColliderSphere3D sphereCast1 = new ColliderSphere3D
            {
                shape = new ShapeSphere3D(new Vector3(-2, 1.1f, 0), 1f)
            };

            ColliderSphere3D sphereCast2 = new ColliderSphere3D
            {
                shape = new ShapeSphere3D(new Vector3(-1.2f, 0, 0), 1f)
            };

            Assert.IsFalse(bvh.CastCollider(ref boxCast1).hit);
            Assert.IsTrue(bvh.CastCollider(ref boxCast2).hit);

            Assert.IsFalse(bvh.CastCollider(ref sphereCast1).hit);
            Assert.IsTrue(bvh.CastCollider(ref sphereCast2).hit);

            boxs.Dispose();
            spheres.Dispose();
            colliders.Dispose();
            bvh.Dispose();

        }
    }
}

