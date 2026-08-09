using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Numerics;


using Random = Alco.FastRandom;
using System.Runtime;
using TestFramework;

namespace Alco.Test
{
    public struct NativeListCollector3D : IBvhCollisionCollector3D, IBvhRayCastCollector3D
    {
        private unsafe NativeArrayList<ColliderCastResult3D>* _list;
        private unsafe NativeArrayList<RayCastResult3D>* _rayList;

        public unsafe NativeListCollector3D(NativeArrayList<ColliderCastResult3D>* list)
        {
            _list = list;
            _rayList = null;
        }

        public unsafe NativeListCollector3D(NativeArrayList<RayCastResult3D>* rayList)
        {
            _list = null;
            _rayList = rayList;
        }

        public unsafe bool OnHit(ColliderCastResult3D result)
        {
            if (_list != null) _list->Add(result);
            return true;
        }

        public unsafe bool OnHit(RayCastResult3D result)
        {
            if (_rayList != null) _rayList->Add(result);
            return true;
        }
    }

    public struct FirstHitCollector3D : IBvhCollisionCollector3D, IBvhRayCastCollector3D
    {
        public ColliderCastResult3D Result;
        public RayCastResult3D RayResult;
        public bool HasHit;

        public bool OnHit(ColliderCastResult3D result)
        {
            Result = result;
            HasHit = true;
            return false;
        }

        public bool OnHit(RayCastResult3D result)
        {
            RayResult = result;
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
                NativeArrayList<RayCastResult3D> hitResults = new NativeArrayList<RayCastResult3D>(8);
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

        [Test(Description = "Morton builder produces query results identical to the pairing baseline")]
        public unsafe void TestMortonBuilderEquivalence()
        {
            FastRandom random = new FastRandom(12345);

            NativeArrayList<ColliderBox3D> boxs = new NativeArrayList<ColliderBox3D>(8);
            NativeArrayList<ColliderSphere3D> spheres = new NativeArrayList<ColliderSphere3D>(8);
            NativeArrayList<ColliderRef3D> colliders = new NativeArrayList<ColliderRef3D>();

            for (int i = 0; i < 500; i++)
            {
                boxs.Add(new ColliderBox3D
                {
                    Shape = new ShapeBox3D(random.NextVector3(-100, 100), random.NextVector3(1, 10), random.NextQuaternionRotation())
                });
                spheres.Add(new ColliderSphere3D
                {
                    shape = new ShapeSphere3D(random.NextVector3(-100, 100), random.NextFloat(1, 10))
                });
            }

            for (int i = 0; i < boxs.Length; i++)
            {
                colliders.Add(ColliderRef3D.Create(boxs.UnsafePointer + i));
            }
            for (int i = 0; i < spheres.Length; i++)
            {
                colliders.Add(ColliderRef3D.Create(spheres.UnsafePointer + i));
            }

            NativeBvh3D pairing = new NativeBvh3D();
            pairing.BuildTree(colliders.AsSpan());

            MortonBvhBuilder3D mortonBuilder = new MortonBvhBuilder3D();
            NativeBvh3D morton = new NativeBvh3D();
            morton.BuildTree(colliders.AsSpan(), mortonBuilder);

            // random rays: closest hit must be identical, including the hit collider identity
            for (int i = 0; i < 2000; i++)
            {
                Vector3 start = random.NextVector3(-125, 125);
                Ray3D ray = Ray3D.CreateWithStartAndEnd(start, start + random.NextVector3(-6, 6));

                RayCastResult3D expected = pairing.CastRayClosestHit(ray);
                RayCastResult3D actual = morton.CastRayClosestHit(ray);

                Assert.AreEqual(expected.Hit, actual.Hit);
                if (expected.Hit)
                {
                    Assert.AreEqual(expected.HitInfo.Fraction, actual.HitInfo.Fraction);
                    Assert.IsTrue(expected.Collider.UnsafePointer == actual.Collider.UnsafePointer);
                }
            }

            // box casts: the same set of colliders must be collected
            for (int i = 0; i < 200; i++)
            {
                ShapeBox3D shape = new ShapeBox3D(random.NextVector3(-100, 100), random.NextVector3(1, 5), random.NextQuaternionRotation());

                NativeArrayList<ColliderCastResult3D> expectedHits = new NativeArrayList<ColliderCastResult3D>(8);
                NativeListCollector3D expectedCollector = new NativeListCollector3D(&expectedHits);
                pairing.CastBox(shape, ref expectedCollector);

                NativeArrayList<ColliderCastResult3D> actualHits = new NativeArrayList<ColliderCastResult3D>(8);
                NativeListCollector3D actualCollector = new NativeListCollector3D(&actualHits);
                morton.CastBox(shape, ref actualCollector);

                Assert.AreEqual(expectedHits.Length, actualHits.Length);

                expectedHits.Dispose();
                actualHits.Dispose();
            }

            boxs.Dispose();
            spheres.Dispose();
            colliders.Dispose();
            pairing.Dispose();
            morton.Dispose();
            mortonBuilder.Dispose();
        }
    }
}

