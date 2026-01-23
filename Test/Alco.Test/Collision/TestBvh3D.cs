using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Numerics;


using Random = Alco.FastRandom;
using System.Runtime;
using TestFramework;

namespace Alco.Test
{
    public struct NativeListCollector3D : IBvhCollisionCollector3D
    {
        private unsafe NativeArrayList<ColliderCastResult3D>* _list;

        public unsafe NativeListCollector3D(NativeArrayList<ColliderCastResult3D>* list)
        {
            _list = list;
        }

        public unsafe bool OnHit(ColliderCastResult3D result)
        {
            _list->Add(result);
            return true;
        }
    }

    public struct FirstHitCollector3D : IBvhCollisionCollector3D
    {
        public ColliderCastResult3D Result;
        public bool HasHit;

        public bool OnHit(ColliderCastResult3D result)
        {
            Result = result;
            HasHit = true;
            return false;
        }
    }

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

            NativeBvh3D bvh = new NativeBvh3D();
            bvh.BuildTree(colliders.AsSpan());

            // Test Ray Cast (NativeBvh3D.CastRay / CastRayFirstHit don't use collector anymore)
            {
                Ray3D ray = Ray3D.CreateWithStartAndEnd(new Vector3(-1.2f, 0, 0), new Vector3(120f, 0, 0));

                RayCastResult3D result = bvh.CastRayClosestHit(ray);

                Assert.IsFalse(!result.Hit);
                TestContext.WriteLine(result.HitInfo.Fraction);
                TestContext.WriteLine(result.HitInfo.Point);
            }

            // Test Ray Cast with Collector
            {
                Ray3D ray = Ray3D.CreateWithStartAndEnd(new Vector3(-1.2f, 0, 0), new Vector3(120f, 0, 0));

                FirstHitCollector3D collector = new FirstHitCollector3D();
                bvh.CastRay(ray, ref collector);

                Assert.IsTrue(collector.HasHit);
            }

            // Test Ray Cast Multi Hit with NativeListCollector
            {
                Ray3D ray = Ray3D.CreateWithStartAndEnd(new Vector3(-12f, 0, 0), new Vector3(25f, 0, 0));
                NativeArrayList<ColliderCastResult3D> hitResults = new NativeArrayList<ColliderCastResult3D>(8);
                NativeListCollector3D multiCollector = new NativeListCollector3D(&hitResults);

                bvh.CastRay(ray, ref multiCollector);

                Assert.IsTrue(hitResults.Length > 1);
                TestContext.WriteLine($"Ray hit {hitResults.Length} objects");
                hitResults.Dispose();
            }

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

            NativeBvh3D bvh = new NativeBvh3D();

            bvh.BuildTree(colliders.AsSpan());

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

            FirstHitCollector3D collector = new FirstHitCollector3D();
            bvh.CastBox(boxCast1.Shape, ref collector);
            Assert.IsFalse(collector.HasHit);

            collector = new FirstHitCollector3D();
            bvh.CastBox(boxCast2.Shape, ref collector);
            Assert.IsTrue(collector.HasHit);

            collector = new FirstHitCollector3D();
            bvh.CastSphere(sphereCast1.shape, ref collector);
            Assert.IsFalse(collector.HasHit);

            collector = new FirstHitCollector3D();
            bvh.CastSphere(sphereCast2.shape, ref collector);
            Assert.IsTrue(collector.HasHit);

            boxs.Dispose();
            spheres.Dispose();
            colliders.Dispose();
            bvh.Dispose();

        }
    }
}

