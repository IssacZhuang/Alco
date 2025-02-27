using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Numerics;


using Random = Alco.Random;
using System.Runtime;
using TestFramework;

namespace Alco.Test
{

    public class TestBvh3D
    {
        [Test(Description = "Test BVH ray collision 3D")]
        public unsafe void TestRayCollision()
        {
            NativeArrayList<ColliderBox3D> boxs = new NativeArrayList<ColliderBox3D>(8);
            NativeArrayList<ColliderSphere3D> spheres = new NativeArrayList<ColliderSphere3D>(8);
            NativeArrayList<ColliderRef3D> colliders = new NativeArrayList<ColliderRef3D>();



            boxs.Add(new ColliderBox3D
            {
                Shape = new ShapeBox3D(new Vector3(20, 0, 0), new Vector3(1f), Quaternion.Identity)
            });


            boxs.Add(new ColliderBox3D
            {
                Shape = new ShapeBox3D(new Vector3(10, 0, 0), new Vector3(1f), Quaternion.Identity)
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
                Shape = new ShapeBox3D(Vector3.Zero, new Vector3(1f), Quaternion.Identity)
            });

            boxs.Add(new ColliderBox3D
            {
                Shape = new ShapeBox3D(new Vector3(-10, 0, 0), new Vector3(1f), Quaternion.Identity)
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

            Assert.IsFalse(!result.Hit);
            TestContext.WriteLine(result.HitInfo.Fraction);
            TestContext.WriteLine(result.HitInfo.Point);

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
                Shape = new ShapeBox3D(Vector3.Zero, new Vector3(1f), Quaternion.Identity)
            });

            boxs.Add(new ColliderBox3D
            {
                Shape = new ShapeBox3D(new Vector3(5, 0, 0), new Vector3(1f), Quaternion.Identity)
            });

            boxs.Add(new ColliderBox3D
            {
                Shape = new ShapeBox3D(new Vector3(5, 5, 0), new Vector3(1f), Quaternion.Identity)
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
                Shape = new ShapeBox3D(new Vector3(-2, 1.1f, 0), new Vector3(1f), Quaternion.Identity)
            };

            ColliderBox3D boxCast2 = new ColliderBox3D
            {
                Shape = new ShapeBox3D(new Vector3(-1.2f, 0, 0), new Vector3(1f), Quaternion.Identity)
            };

            ColliderSphere3D sphereCast1 = new ColliderSphere3D
            {
                shape = new ShapeSphere3D(new Vector3(-2, 1.1f, 0), 1f)
            };

            ColliderSphere3D sphereCast2 = new ColliderSphere3D
            {
                shape = new ShapeSphere3D(new Vector3(-1.2f, 0, 0), 1f)
            };

            Assert.IsFalse(bvh.CastCollider(boxCast1).Hit);
            Assert.IsTrue(bvh.CastCollider(boxCast2).Hit);

            Assert.IsFalse(bvh.CastCollider(sphereCast1).Hit);
            Assert.IsTrue(bvh.CastCollider(sphereCast2).Hit);

            boxs.Dispose();
            spheres.Dispose();
            colliders.Dispose();
            bvh.Dispose();

        }
    }
}

