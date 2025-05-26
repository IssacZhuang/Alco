using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Numerics;


using Random = Alco.Random;
using System.Runtime;

namespace Alco.Test
{

    public class TestBvh2D
    {
        [Test(Description = "BVH ray collision 2D")]
        public unsafe void TestRayCollision()
        {
            NativeArrayList<ColliderBox2D> boxs = new NativeArrayList<ColliderBox2D>(8);
            NativeArrayList<ColliderSphere2D> spheres = new NativeArrayList<ColliderSphere2D>(8);
            NativeArrayList<ColliderRef2D> colliders = new NativeArrayList<ColliderRef2D>();



            boxs.Add(new ColliderBox2D
            {
                Shape = new ShapeBox2D(new Vector2(20, 0), new Vector2(1f), Rotation2D.Identity)
            });


            boxs.Add(new ColliderBox2D
            {
                Shape = new ShapeBox2D(new Vector2(10, 0), new Vector2(1f), Rotation2D.Identity)
            });

            spheres.Add(new ColliderSphere2D
            {
                Shape = new ShapeSphere2D(new Vector2(-10, 0), 1f)
            });

            spheres.Add(new ColliderSphere2D
            {
                Shape = new ShapeSphere2D(Vector2.Zero, 0.8f)
            });

            boxs.Add(new ColliderBox2D
            {
                Shape = new ShapeBox2D(Vector2.Zero, new Vector2(1f), Rotation2D.Identity)
            });

            boxs.Add(new ColliderBox2D
            {
                Shape = new ShapeBox2D(new Vector2(-10, 0), new Vector2(1f), Rotation2D.Identity)
            });





            for (int i = 0; i < boxs.Length; i++)
            {
                colliders.Add(ColliderRef2D.Create(boxs.UnsafePointer + i));
            }

            for (int i = 0; i < spheres.Length; i++)
            {
                colliders.Add(ColliderRef2D.Create(spheres.UnsafePointer + i));
            }

            // colliders.Add(ColliderRef.Create(boxs.Ptr));
            // colliders.Add(ColliderRef.Create(spheres.Ptr));

            using ParallelScheduler scheduler = new ParallelScheduler();
            NativeBvh2D bvh = new NativeBvh2D(scheduler);
            Ray2D ray = Ray2D.CreateWithStartAndEnd(new Vector2(-2, 1.1f), new Vector2(200, 1.1f));

            bvh.BuildTree(colliders.AsReadOnlySpan());

            //RayCastResult result = bvh.CastRay(ray);

            //Assert.IsFalse(result.hit);


            ray = Ray2D.CreateWithStartAndEnd(new Vector2(-1.2f, 0), new Vector2(120f, 0));

            RayCastResult2D result = bvh.CastRay(ray);

            Assert.IsFalse(!result.Hit);
            TestContext.WriteLine(result.HitInfo.Fraction);
            TestContext.WriteLine(result.HitInfo.Point);

            boxs.Dispose();
            spheres.Dispose();
            colliders.Dispose();
            bvh.Dispose();

        }

        [Test(Description = "BVH collider collision 2D")]
        public unsafe void TestColliderCollision()
        {
            NativeArrayList<ColliderBox2D> boxs = new NativeArrayList<ColliderBox2D>(8);
            NativeArrayList<ColliderSphere2D> spheres = new NativeArrayList<ColliderSphere2D>(8);
            NativeArrayList<ColliderRef2D> colliders = new NativeArrayList<ColliderRef2D>();

            boxs.Add(new ColliderBox2D
            {
                Shape = new ShapeBox2D(Vector2.Zero, new Vector2(1f), Rotation2D.Identity)
            });

            boxs.Add(new ColliderBox2D
            {
                Shape = new ShapeBox2D(new Vector2(5, 0), new Vector2(1f), Rotation2D.Identity)
            });

            boxs.Add(new ColliderBox2D
            {
                Shape = new ShapeBox2D(new Vector2(5, 5), new Vector2(1f), Rotation2D.Identity)
            });

            spheres.Add(new ColliderSphere2D
            {
                Shape = new ShapeSphere2D(Vector2.Zero, 1f)
            });

            for (int i = 0; i < boxs.Length; i++)
            {
                colliders.Add(ColliderRef2D.Create(boxs.UnsafePointer + i));
            }

            for (int i = 0; i < spheres.Length; i++)
            {
                colliders.Add(ColliderRef2D.Create(spheres.UnsafePointer + i));
            }

            using ParallelScheduler scheduler = new ParallelScheduler();
            NativeBvh2D bvh = new NativeBvh2D(scheduler);

            bvh.BuildTree(colliders.AsReadOnlySpan());

            ColliderBox2D boxCast1 = new ColliderBox2D
            {
                Shape = new ShapeBox2D(new Vector2(-2, 1.1f), new Vector2(1f), Rotation2D.Identity)
            };

            ColliderBox2D boxCast2 = new ColliderBox2D
            {
                Shape = new ShapeBox2D(new Vector2(-1.2f, 0), new Vector2(1f), Rotation2D.Identity)
            };

            ColliderSphere2D sphereCast1 = new ColliderSphere2D
            {
                Shape = new ShapeSphere2D(new Vector2(-2, 1.1f), 1f)
            };

            ColliderSphere2D sphereCast2 = new ColliderSphere2D
            {
                Shape = new ShapeSphere2D(new Vector2(-1.2f, 0), 1f)
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

