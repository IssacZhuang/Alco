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

    /// <summary>
    /// Correctness tests for the 4-wide collider <see cref="NativeBvh3D"/>. All query methods
    /// are cross-validated against a linear scan that runs the exact same shape tests on the
    /// same colliders, so any disagreement exposes lost or duplicated traversal work. The
    /// scenes include rotated boxes, spheres, duplicates, a shared-centroid cluster (identical
    /// Morton codes) and ray origins inside colliders.
    /// </summary>
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

        // ════════════════════════════════════════════════════════════
        //  Cross-validation against a linear scan
        // ════════════════════════════════════════════════════════════

        /// <summary>Builds a scene of rotated boxes and spheres with Morton-hostile patterns: exact duplicates and a shared-centroid cluster.
        /// The backing shape lists are returned to the caller, who must keep them alive while the tree is queried.</summary>
        private static unsafe void MakeScene(FastRandom random, int boxCount, int sphereCount,
            out NativeArrayList<ColliderBox3D> boxs, out NativeArrayList<ColliderSphere3D> spheres, out NativeArrayList<ColliderRef3D> colliders)
        {
            boxs = new NativeArrayList<ColliderBox3D>(8);
            spheres = new NativeArrayList<ColliderSphere3D>(8);
            colliders = new NativeArrayList<ColliderRef3D>();
            for (int i = 0; i < boxCount; i++)
            {
                boxs.Add(new ColliderBox3D
                {
                    Shape = new ShapeBox3D(random.NextVector3(-100, 100), random.NextVector3(0.5f, 6f), random.NextQuaternionRotation())
                });
            }
            for (int i = 0; i < sphereCount; i++)
            {
                spheres.Add(new ColliderSphere3D
                {
                    shape = new ShapeSphere3D(random.NextVector3(-100, 100), random.NextFloat(0.5f, 8f))
                });
            }
            // shared-centroid cluster: identical Morton codes exercise the midpoint-split fallback
            for (int i = 0; i < 10; i++)
            {
                spheres.Add(new ColliderSphere3D
                {
                    shape = new ShapeSphere3D(new Vector3(7, 7, 7), random.NextFloat(0.5f, 4f))
                });
            }
            // exact duplicates
            for (int i = 0; i < 6; i++)
            {
                boxs.Add(new ColliderBox3D
                {
                    Shape = new ShapeBox3D(new Vector3(-33, 44, 55), new Vector3(0.7f), random.NextQuaternionRotation())
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
        }

        /// <summary>Returns whether the [0,1] ray segment touches the bounds, mirroring the traversal's slab acceptance.
        /// The exact shape tests are unbounded forward ray tests, so this gate is what limits queries to the segment.</summary>
        private static bool SegmentIntersectsBounds(BoundingBox3D bounds, in Ray3D ray)
        {
            float tMin = 0f;
            float tMax = 1f;
            Vector3 origin = ray.Origin;
            Vector3 displacement = ray.Displacement;
            if (displacement.X != 0f)
            {
                float t0 = (bounds.Min.X - origin.X) / displacement.X;
                float t1 = (bounds.Max.X - origin.X) / displacement.X;
                if (t0 > t1) (t0, t1) = (t1, t0);
                tMin = MathF.Max(tMin, t0);
                tMax = MathF.Min(tMax, t1);
            }
            else if (origin.X < bounds.Min.X || origin.X > bounds.Max.X) return false;
            if (displacement.Y != 0f)
            {
                float t0 = (bounds.Min.Y - origin.Y) / displacement.Y;
                float t1 = (bounds.Max.Y - origin.Y) / displacement.Y;
                if (t0 > t1) (t0, t1) = (t1, t0);
                tMin = MathF.Max(tMin, t0);
                tMax = MathF.Min(tMax, t1);
            }
            else if (origin.Y < bounds.Min.Y || origin.Y > bounds.Max.Y) return false;
            if (displacement.Z != 0f)
            {
                float t0 = (bounds.Min.Z - origin.Z) / displacement.Z;
                float t1 = (bounds.Max.Z - origin.Z) / displacement.Z;
                if (t0 > t1) (t0, t1) = (t1, t0);
                tMin = MathF.Max(tMin, t0);
                tMax = MathF.Min(tMax, t1);
            }
            else if (origin.Z < bounds.Min.Z || origin.Z > bounds.Max.Z) return false;
            return tMin <= tMax;
        }

        /// <summary>Cross-validates every query method against a linear scan running the same exact shape tests.</summary>
        private static unsafe void ValidateScene(NativeBvh3D bvh, ColliderRef3D* colliders, int count, FastRandom random, int rayCount, int queryCount)
        {
            NativeArrayList<RayCastResult3D> rayHits = new NativeArrayList<RayCastResult3D>(32);
            NativeArrayList<ColliderCastResult3D> castHits = new NativeArrayList<ColliderCastResult3D>(32);
            HashSet<long> expected = new HashSet<long>();
            HashSet<long> actual = new HashSet<long>();

            for (int i = 0; i < rayCount; i++)
            {
                Vector3 origin;
                Vector3 direction;
                switch (i % 6)
                {
                    case 0: // ray starting inside a collider's AABB (negative raw slab entries)
                    case 1:
                        BoundingBox3D inside = colliders[random.NextInt(0, count)].GetBoundingBox();
                        origin = (inside.Min + inside.Max) * 0.5f;
                        direction = random.NextVector3(-6, 6);
                        break;
                    case 2: // axis-parallel +X
                        origin = random.NextVector3(-130, 130);
                        direction = new Vector3(random.NextFloat(1, 12), 0, 0);
                        break;
                    case 3: // axis-parallel -Z
                        origin = random.NextVector3(-130, 130);
                        direction = new Vector3(0, 0, -random.NextFloat(1, 12));
                        break;
                    case 4: // zero displacement behaves as a point-in-shape test
                        origin = random.NextVector3(-110, 110);
                        direction = Vector3.Zero;
                        break;
                    default:
                        origin = random.NextVector3(-130, 130);
                        direction = random.NextVector3(-8, 8);
                        break;
                }
                Ray3D ray = new Ray3D(origin, direction);

                // closest hit: same minimum fraction within the segment; the exact tests are
                // unbounded forward ray tests, so clamp the reference to fraction <= 1
                bool expectedHit = false;
                float expectedFraction = float.PositiveInfinity;
                for (int j = 0; j < count; j++)
                {
                    if (colliders[j].IntersectRay(ray, out RaycastHit3D hit) && hit.Fraction <= 1f && (!expectedHit || hit.Fraction < expectedFraction))
                    {
                        expectedHit = true;
                        expectedFraction = hit.Fraction;
                    }
                }
                RayCastResult3D result = bvh.CastRayClosestHit(ray);
                Assert.That(result.Hit, Is.EqualTo(expectedHit), $"closest hit/miss mismatch at ray {i} ({origin} -> +{direction})");
                if (expectedHit)
                {
                    Assert.That(result.HitInfo.Fraction, Is.EqualTo(expectedFraction).Within(1e-4f), $"closest fraction mismatch at ray {i}: expected {expectedFraction}, got {result.HitInfo.Fraction}");
                    Assert.IsTrue(result.Collider.IntersectRay(ray, out RaycastHit3D own) && MathF.Abs(own.Fraction - expectedFraction) < 1e-3f,
                        $"closest returned a collider that is not a closest hit at ray {i}");
                }

                // all hits: the same set of colliders whose bounds touch the segment and whose
                // exact test hits (the traversal visits exactly the bounds-touching leaves)
                expected.Clear();
                for (int j = 0; j < count; j++)
                {
                    if (SegmentIntersectsBounds(colliders[j].GetBoundingBox(), ray) && colliders[j].IntersectRay(ray, out _))
                    {
                        expected.Add((long)colliders[j].UnsafePointer);
                    }
                }
                rayHits.Clear();
                NativeListCollector3D rayCollector = new NativeListCollector3D(&rayHits);
                bvh.CastRay(ray, ref rayCollector);
                actual.Clear();
                for (int j = 0; j < rayHits.Length; j++)
                {
                    Assert.IsTrue(actual.Add((long)rayHits[j].Collider.UnsafePointer),
                        $"ray collector reported a collider twice at ray {i}");
                }
                Assert.That(actual.Count, Is.EqualTo(expected.Count), $"ray all-hits set mismatch at ray {i}: expected {expected.Count}, got {actual.Count}");
            }

            for (int i = 0; i < queryCount; i++)
            {
                // box cast
                ShapeBox3D boxShape = new ShapeBox3D(random.NextVector3(-110, 110), random.NextVector3(0.5f, 8f), random.NextQuaternionRotation());
                ColliderBox3D boxCaster = new ColliderBox3D { Shape = boxShape };
                expected.Clear();
                for (int j = 0; j < count; j++)
                {
                    if (boxCaster.CollidesWith(colliders[j].UnsafePointer))
                    {
                        expected.Add((long)colliders[j].UnsafePointer);
                    }
                }
                castHits.Clear();
                NativeListCollector3D castCollector = new NativeListCollector3D(&castHits);
                bvh.CastBox(boxShape, ref castCollector);
                actual.Clear();
                for (int j = 0; j < castHits.Length; j++)
                {
                    Assert.IsTrue(actual.Add((long)castHits[j].Collider.UnsafePointer),
                        $"box cast reported a collider twice at query {i}");
                }
                Assert.That(actual.Count, Is.EqualTo(expected.Count), $"box cast set mismatch at query {i}: expected {expected.Count}, got {actual.Count}");

                // sphere cast
                ShapeSphere3D sphereShape = new ShapeSphere3D(random.NextVector3(-110, 110), random.NextFloat(0.5f, 10f));
                ColliderSphere3D sphereCaster = new ColliderSphere3D { shape = sphereShape };
                expected.Clear();
                for (int j = 0; j < count; j++)
                {
                    if (sphereCaster.CollidesWith(colliders[j].UnsafePointer))
                    {
                        expected.Add((long)colliders[j].UnsafePointer);
                    }
                }
                castHits.Clear();
                castCollector = new NativeListCollector3D(&castHits);
                bvh.CastSphere(sphereShape, ref castCollector);
                actual.Clear();
                for (int j = 0; j < castHits.Length; j++)
                {
                    Assert.IsTrue(actual.Add((long)castHits[j].Collider.UnsafePointer),
                        $"sphere cast reported a collider twice at query {i}");
                }
                Assert.That(actual.Count, Is.EqualTo(expected.Count), $"sphere cast set mismatch at query {i}: expected {expected.Count}, got {actual.Count}");

                // point cast; half the queries target a collider center to guarantee hits
                BoundingBox3D target = colliders[random.NextInt(0, count)].GetBoundingBox();
                Vector3 point = i % 2 == 0
                    ? (target.Min + target.Max) * 0.5f
                    : random.NextVector3(-110, 110);
                expected.Clear();
                for (int j = 0; j < count; j++)
                {
                    if (colliders[j].IntersectPoint(point))
                    {
                        expected.Add((long)colliders[j].UnsafePointer);
                    }
                }
                castHits.Clear();
                castCollector = new NativeListCollector3D(&castHits);
                bvh.CastPoint(point, ref castCollector);
                actual.Clear();
                for (int j = 0; j < castHits.Length; j++)
                {
                    Assert.IsTrue(actual.Add((long)castHits[j].Collider.UnsafePointer),
                        $"point cast reported a collider twice at query {i}");
                }
                Assert.That(actual.Count, Is.EqualTo(expected.Count), $"point cast set mismatch at point {point}: expected {expected.Count}, got {actual.Count}");
            }

            rayHits.Dispose();
            castHits.Dispose();
        }

        [Test(Description = "BVH collider 3D: randomized cross-validation of all five query types against a linear scan")]
        public unsafe void TestBvhCrossValidation3D()
        {
            FastRandom random = new(12345);
            MakeScene(random, 300, 200, out NativeArrayList<ColliderBox3D> boxs, out NativeArrayList<ColliderSphere3D> spheres, out NativeArrayList<ColliderRef3D> colliders);

            NativeBvh3D bvh = new NativeBvh3D();
            bvh.BuildTree(colliders.AsSpan());
            TestContext.WriteLine($"[Bvh3D] {colliders.Length} colliders -> {bvh.NodeCount} nodes, {bvh.LeafCount} leaf blocks, depth {bvh.TreeDepth}");

            ValidateScene(bvh, colliders.UnsafePointer, colliders.Length, random, 2000, 200);

            boxs.Dispose();
            spheres.Dispose();
            colliders.Dispose();
            bvh.Dispose();
        }

        [Test(Description = "BVH collider 3D: explicit Morton builder instance produces the same results as the default build")]
        public unsafe void TestBvhExplicitBuilderMatchesDefault3D()
        {
            FastRandom random = new(777);
            MakeScene(random, 200, 100, out NativeArrayList<ColliderBox3D> boxs, out NativeArrayList<ColliderSphere3D> spheres, out NativeArrayList<ColliderRef3D> colliders);

            NativeBvh3D withDefault = new NativeBvh3D();
            withDefault.BuildTree(colliders.AsSpan());
            MortonBvhBuilder3D builder = new MortonBvhBuilder3D();
            NativeBvh3D withExplicit = new NativeBvh3D();
            withExplicit.BuildTree(colliders.AsSpan(), builder);

            for (int i = 0; i < 500; i++)
            {
                Ray3D ray = new Ray3D(random.NextVector3(-130, 130), random.NextVector3(-8, 8));
                RayCastResult3D a = withDefault.CastRayClosestHit(ray);
                RayCastResult3D b = withExplicit.CastRayClosestHit(ray);
                Assert.That(b.Hit, Is.EqualTo(a.Hit));
                if (a.Hit)
                {
                    Assert.That(b.HitInfo.Fraction, Is.EqualTo(a.HitInfo.Fraction).Within(1e-6f));
                    Assert.IsTrue(a.Collider.UnsafePointer == b.Collider.UnsafePointer);
                }
            }

            boxs.Dispose();
            spheres.Dispose();
            colliders.Dispose();
            withDefault.Dispose();
            withExplicit.Dispose();
            builder.Dispose();
        }

        [Test(Description = "BVH collider 3D: pairing-order builder (order-preserving) matches the linear-scan reference")]
        public unsafe void TestBvhPairingOrderBuilder3D()
        {
            FastRandom random = new(2024);
            NativeBvh3D bvh = new NativeBvh3D();

            // one shared stateless instance across differently sized builds, including counts
            // that exercise every remainder of the four-way chunk split
            foreach (var (boxCount, sphereCount, rayCount, queryCount) in new[]
            {
                (300, 200, 2000, 200), (1, 0, 100, 20), (5, 0, 100, 20), (2, 3, 100, 20), (0, 9, 100, 20), (17, 0, 100, 20)
            })
            {
                MakeScene(random, boxCount, sphereCount, out NativeArrayList<ColliderBox3D> boxs, out NativeArrayList<ColliderSphere3D> spheres, out NativeArrayList<ColliderRef3D> colliders);
                bvh.BuildTree(colliders.AsSpan(), PairingOrderBvhBuilder3D.Shared);
                if (boxCount == 300)
                {
                    TestContext.WriteLine($"[Bvh3D pairing] {colliders.Length} colliders -> {bvh.NodeCount} nodes, {bvh.LeafCount} leaf blocks, depth {bvh.TreeDepth}");
                }
                ValidateScene(bvh, colliders.UnsafePointer, colliders.Length, random, rayCount, queryCount);
                boxs.Dispose();
                spheres.Dispose();
                colliders.Dispose();
            }
            bvh.Dispose();
        }

        [Test(Description = "BVH collider 3D: item counts around the 4-item leaf-block boundary stay correct")]
        public unsafe void TestBvhSmallCounts3D()
        {
            FastRandom random = new(42);
            NativeBvh3D bvh = new NativeBvh3D();
            foreach (int count in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 17 })
            {
                NativeArrayList<ColliderBox3D> boxs = new NativeArrayList<ColliderBox3D>(8);
                NativeArrayList<ColliderSphere3D> spheres = new NativeArrayList<ColliderSphere3D>(8);
                NativeArrayList<ColliderRef3D> colliders = new NativeArrayList<ColliderRef3D>();
                for (int i = 0; i < count; i++)
                {
                    if (i % 2 == 0)
                    {
                        boxs.Add(new ColliderBox3D { Shape = new ShapeBox3D(random.NextVector3(-50, 50), random.NextVector3(0.5f, 4f), random.NextQuaternionRotation()) });
                    }
                    else
                    {
                        spheres.Add(new ColliderSphere3D { shape = new ShapeSphere3D(random.NextVector3(-50, 50), random.NextFloat(0.5f, 4f)) });
                    }
                }
                for (int i = 0; i < boxs.Length; i++)
                {
                    colliders.Add(ColliderRef3D.Create(boxs.UnsafePointer + i));
                }
                for (int i = 0; i < spheres.Length; i++)
                {
                    colliders.Add(ColliderRef3D.Create(spheres.UnsafePointer + i));
                }

                bvh.BuildTree(colliders.AsSpan());
                ValidateScene(bvh, colliders.UnsafePointer, colliders.Length, random, 100, 20);

                boxs.Dispose();
                spheres.Dispose();
                colliders.Dispose();
            }
            bvh.Dispose();
        }

        [Test(Description = "BVH collider 3D: 300 identical colliders (identical Morton codes) are all reachable")]
        public unsafe void TestBvhIdenticalColliders3D()
        {
            NativeArrayList<ColliderSphere3D> spheres = new NativeArrayList<ColliderSphere3D>(300);
            NativeArrayList<ColliderRef3D> colliders = new NativeArrayList<ColliderRef3D>(300);
            for (int i = 0; i < 300; i++)
            {
                spheres.Add(new ColliderSphere3D { shape = new ShapeSphere3D(new Vector3(1, 2, 3), 1.5f) });
            }
            for (int i = 0; i < spheres.Length; i++)
            {
                colliders.Add(ColliderRef3D.Create(spheres.UnsafePointer + i));
            }

            NativeBvh3D bvh = new NativeBvh3D();
            bvh.BuildTree(colliders.AsSpan());

            NativeArrayList<ColliderCastResult3D> hits = new NativeArrayList<ColliderCastResult3D>(300);
            NativeListCollector3D collector = new NativeListCollector3D(&hits);
            bvh.CastPoint(new Vector3(1, 2, 3), ref collector);
            Assert.That(hits.Length, Is.EqualTo(300));

            hits.Clear();
            collector = new NativeListCollector3D(&hits);
            bvh.CastSphere(new ShapeSphere3D(new Vector3(1, 2, 3), 0.1f), ref collector);
            Assert.That(hits.Length, Is.EqualTo(300));

            RayCastResult3D closest = bvh.CastRayClosestHit(new Ray3D(new Vector3(-10, 2, 3), new Vector3(20, 0, 0)));
            Assert.IsTrue(closest.Hit);
            Assert.That(closest.HitInfo.Fraction, Is.EqualTo(0.475f).Within(1e-4f)); // x=-0.5 plane: (-0.5 - -10) / 20

            spheres.Dispose();
            colliders.Dispose();
            hits.Dispose();
            bvh.Dispose();
        }

        [Test(Description = "BVH collider 3D: empty input leaves the tree inert for every query")]
        public unsafe void TestBvhEmptyInput3D()
        {
            NativeBvh3D bvh = new NativeBvh3D();
            bvh.BuildTree(Span<ColliderRef3D>.Empty);

            Assert.That(bvh.NodeCount, Is.EqualTo(0));
            Assert.That(bvh.LeafCount, Is.EqualTo(0));
            Assert.That(bvh.TreeDepth, Is.EqualTo(0));
            Assert.IsFalse(bvh.CastRayClosestHit(new Ray3D(Vector3.Zero, Vector3.One)).Hit);

            NativeArrayList<RayCastResult3D> rayHits = new NativeArrayList<RayCastResult3D>(8);
            NativeListCollector3D rayCollector = new NativeListCollector3D(&rayHits);
            bvh.CastRay(new Ray3D(Vector3.Zero, Vector3.One), ref rayCollector);
            NativeArrayList<ColliderCastResult3D> castHits = new NativeArrayList<ColliderCastResult3D>(8);
            NativeListCollector3D castCollector = new NativeListCollector3D(&castHits);
            bvh.CastBox(new ShapeBox3D(Vector3.Zero, Vector3.One, Quaternion.Identity), ref castCollector);
            bvh.CastSphere(new ShapeSphere3D(Vector3.Zero, 1f), ref castCollector);
            bvh.CastPoint(Vector3.Zero, ref castCollector);
            Assert.That(rayHits.Length + castHits.Length, Is.EqualTo(0));

            rayHits.Dispose();
            castHits.Dispose();
            bvh.Dispose();
        }

        [Test(Description = "BVH collider 3D: rebuilding with different sizes reuses buffers without corrupting results")]
        public unsafe void TestBvhRebuildReuse3D()
        {
            FastRandom random = new(99);
            NativeBvh3D bvh = new NativeBvh3D();

            foreach (var (boxCount, sphereCount, rayCount, queryCount) in new[] { (500, 300, 300, 60), (1, 2, 300, 60), (2000, 1200, 300, 60) })
            {
                MakeScene(random, boxCount, sphereCount, out NativeArrayList<ColliderBox3D> boxs, out NativeArrayList<ColliderSphere3D> spheres, out NativeArrayList<ColliderRef3D> colliders);

                bvh.BuildTree(colliders.AsSpan());
                ValidateScene(bvh, colliders.UnsafePointer, colliders.Length, random, rayCount, queryCount);

                boxs.Dispose();
                spheres.Dispose();
                colliders.Dispose();
            }
            bvh.Dispose();
        }
    }
}
