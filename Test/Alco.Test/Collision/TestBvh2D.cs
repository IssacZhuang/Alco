using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Numerics;


using Random = Alco.FastRandom;
using System.Runtime;

namespace Alco.Test
{
    public struct NativeListCollector : IBvhCollisionCastCollector2D, IBvhRayCastCollector2D
    {
        private unsafe NativeArrayList<ColliderCastResult2D>* _list;
        private unsafe NativeArrayList<RayCastResult2D>* _rayList;

        public unsafe NativeListCollector(NativeArrayList<ColliderCastResult2D>* list)
        {
            _list = list;
            _rayList = null;
        }

        public unsafe NativeListCollector(NativeArrayList<RayCastResult2D>* rayList)
        {
            _list = null;
            _rayList = rayList;
        }

        public unsafe bool OnHit(ColliderCastResult2D result)
        {
            if (_list != null) _list->Add(result);
            return true;
        }

        public unsafe bool OnHit(RayCastResult2D result)
        {
            if (_rayList != null) _rayList->Add(result);
            return true;
        }
    }

    public struct FirstHitCollector : IBvhCollisionCastCollector2D, IBvhRayCastCollector2D
    {
        public ColliderCastResult2D Result;
        public RayCastResult2D RayResult;
        public bool HasHit;

        public bool OnHit(ColliderCastResult2D result)
        {
            Result = result;
            HasHit = true;
            return false;
        }

        public bool OnHit(RayCastResult2D result)
        {
            RayResult = result;
            HasHit = true;
            return false;
        }
    }

    public struct OrderCollector : IBvhCollisionCastCollector2D
    {
        public List<int> Order;

        public bool OnHit(ColliderCastResult2D result)
        {
            Order.Add(result.Collider.UserData);
            return true;
        }
    }

    /// <summary>
    /// Correctness tests for the 4-wide collider <see cref="NativeBvh2D"/>. All query methods
    /// are cross-validated against a linear scan that runs the exact same shape tests on the
    /// same colliders, so any disagreement exposes lost or duplicated traversal work. The
    /// scenes include rotated boxes, spheres, duplicates, a shared-centroid cluster (identical
    /// Morton codes) and ray origins inside colliders.
    /// </summary>
    public class TestBvh2D
    {
        [Test(Description = "BVH collision 2D with Collector")]
        public unsafe void TestBvhCollision()
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

            for (int i = 0; i < boxs.Length; i++)
            {
                colliders.Add(ColliderRef2D.Create(boxs.UnsafePointer + i));
            }

            for (int i = 0; i < spheres.Length; i++)
            {
                colliders.Add(ColliderRef2D.Create(spheres.UnsafePointer + i));
            }

            NativeBvh2D bvh = new NativeBvh2D();
            bvh.BuildTree(colliders.AsSpan());

            // Test Ray Cast (NativeBvh2D.CastRay / CastRayFirstHit don't use collector anymore)
            {
                Ray2D ray = Ray2D.CreateWithStartAndEnd(new Vector2(-1.2f, 0), new Vector2(120f, 0));

                RayCastResult2D result = bvh.CastRayClosestHit(ray);

                Assert.IsTrue(result.Hit);
                TestContext.WriteLine($"Ray hit at fraction: {result.HitInfo.Fraction}");
            }

            // Test Ray Cast with Collector
            {
                Ray2D ray = Ray2D.CreateWithStartAndEnd(new Vector2(-1.2f, 0), new Vector2(120f, 0));

                FirstHitCollector collector = new FirstHitCollector();
                bvh.CastRay(ray, ref collector);

                Assert.IsTrue(collector.HasHit);
            }

            // Test Ray Cast Multi Hit with NativeListCollector
            {
                Ray2D ray = Ray2D.CreateWithStartAndEnd(new Vector2(-12f, 0), new Vector2(25f, 0));
                NativeArrayList<RayCastResult2D> hitResults = new NativeArrayList<RayCastResult2D>(8);
                NativeListCollector multiCollector = new NativeListCollector(&hitResults);

                bvh.CastRay(ray, ref multiCollector);

                Assert.IsTrue(hitResults.Length > 1);
                TestContext.WriteLine($"Ray hit {hitResults.Length} objects");
                for (int i = 0; i < hitResults.Length; i++)
                {
                    hitResults[i].Collider.IntersectRay(ray, out var hit);
                    TestContext.WriteLine($"Hit {i} at fraction: {hit.Fraction}");
                }
                hitResults.Dispose();
            }

            // Test Collider Cast with Collector
            {
                ShapeBox2D boxShape = new ShapeBox2D(new Vector2(-1.2f, 0), new Vector2(1f), Rotation2D.Identity);

                FirstHitCollector colliderCollector = new FirstHitCollector();
                bvh.CastBox(boxShape, ref colliderCollector);

                Assert.IsTrue(colliderCollector.HasHit);
            }

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
            out NativeArrayList<ColliderBox2D> boxs, out NativeArrayList<ColliderSphere2D> spheres, out NativeArrayList<ColliderRef2D> colliders)
        {
            boxs = new NativeArrayList<ColliderBox2D>(8);
            spheres = new NativeArrayList<ColliderSphere2D>(8);
            colliders = new NativeArrayList<ColliderRef2D>();
            for (int i = 0; i < boxCount; i++)
            {
                boxs.Add(new ColliderBox2D
                {
                    Shape = new ShapeBox2D(random.NextVector2(-100, 100), random.NextVector2(0.5f, 6f), random.NextRotation2D())
                });
            }
            for (int i = 0; i < sphereCount; i++)
            {
                spheres.Add(new ColliderSphere2D
                {
                    Shape = new ShapeSphere2D(random.NextVector2(-100, 100), random.NextFloat(0.5f, 8f))
                });
            }
            // shared-centroid cluster: identical Morton codes exercise the midpoint-split fallback
            for (int i = 0; i < 10; i++)
            {
                spheres.Add(new ColliderSphere2D
                {
                    Shape = new ShapeSphere2D(new Vector2(7, 7), random.NextFloat(0.5f, 4f))
                });
            }
            // exact duplicates
            for (int i = 0; i < 6; i++)
            {
                boxs.Add(new ColliderBox2D
                {
                    Shape = new ShapeBox2D(new Vector2(-33, 44), new Vector2(0.7f), random.NextRotation2D())
                });
            }

            for (int i = 0; i < boxs.Length; i++)
            {
                colliders.Add(ColliderRef2D.Create(boxs.UnsafePointer + i));
            }
            for (int i = 0; i < spheres.Length; i++)
            {
                colliders.Add(ColliderRef2D.Create(spheres.UnsafePointer + i));
            }
        }

        /// <summary>Returns whether the [0,1] ray segment touches the bounds, mirroring the traversal's slab acceptance.
        /// The exact shape tests are unbounded forward ray tests, so this gate is what limits queries to the segment.</summary>
        private static bool SegmentIntersectsBounds(BoundingBox2D bounds, in Ray2D ray)
        {
            float tMin = 0f;
            float tMax = 1f;
            Vector2 origin = ray.Origin;
            Vector2 displacement = ray.Displacement;
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
            return tMin <= tMax;
        }

        /// <summary>Cross-validates every query method against a linear scan running the same exact shape tests.</summary>
        private static unsafe void ValidateScene(NativeBvh2D bvh, ColliderRef2D* colliders, int count, FastRandom random, int rayCount, int queryCount)
        {
            NativeArrayList<RayCastResult2D> rayHits = new NativeArrayList<RayCastResult2D>(32);
            NativeArrayList<ColliderCastResult2D> castHits = new NativeArrayList<ColliderCastResult2D>(32);
            HashSet<long> expected = new HashSet<long>();
            HashSet<long> actual = new HashSet<long>();

            for (int i = 0; i < rayCount; i++)
            {
                Vector2 origin;
                Vector2 direction;
                switch (i % 6)
                {
                    case 0: // ray starting inside a collider's AABB (negative raw slab entries)
                    case 1:
                        BoundingBox2D inside = colliders[random.NextInt(0, count)].GetBoundingBox();
                        origin = (inside.Min + inside.Max) * 0.5f;
                        direction = random.NextVector2(-6, 6);
                        break;
                    case 2: // axis-parallel +X
                        origin = random.NextVector2(-130, 130);
                        direction = new Vector2(random.NextFloat(1, 12), 0);
                        break;
                    case 3: // axis-parallel -Y
                        origin = random.NextVector2(-130, 130);
                        direction = new Vector2(0, -random.NextFloat(1, 12));
                        break;
                    case 4: // zero displacement behaves as a point-in-shape test
                        origin = random.NextVector2(-110, 110);
                        direction = Vector2.Zero;
                        break;
                    default:
                        origin = random.NextVector2(-130, 130);
                        direction = random.NextVector2(-8, 8);
                        break;
                }
                Ray2D ray = new Ray2D(origin, direction);

                // closest hit: same minimum fraction within the segment; the exact tests are
                // unbounded forward ray tests, so clamp the reference to fraction <= 1
                bool expectedHit = false;
                float expectedFraction = float.PositiveInfinity;
                for (int j = 0; j < count; j++)
                {
                    if (colliders[j].IntersectRay(ray, out RaycastHit2D hit) && hit.Fraction <= 1f && (!expectedHit || hit.Fraction < expectedFraction))
                    {
                        expectedHit = true;
                        expectedFraction = hit.Fraction;
                    }
                }
                RayCastResult2D result = bvh.CastRayClosestHit(ray);
                Assert.AreEqual(expectedHit, result.Hit, $"closest hit/miss mismatch at ray {i} ({origin} -> +{direction})");
                if (expectedHit)
                {
                    Assert.AreEqual(expectedFraction, result.HitInfo.Fraction, 1e-4f, $"closest fraction mismatch at ray {i}: expected {expectedFraction}, got {result.HitInfo.Fraction}");
                    Assert.IsTrue(result.Collider.IntersectRay(ray, out RaycastHit2D own) && MathF.Abs(own.Fraction - expectedFraction) < 1e-3f,
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
                NativeListCollector rayCollector = new NativeListCollector(&rayHits);
                bvh.CastRay(ray, ref rayCollector);
                actual.Clear();
                for (int j = 0; j < rayHits.Length; j++)
                {
                    Assert.IsTrue(actual.Add((long)rayHits[j].Collider.UnsafePointer),
                        $"ray collector reported a collider twice at ray {i}");
                }
                Assert.AreEqual(expected.Count, actual.Count, $"ray all-hits set mismatch at ray {i}: expected {expected.Count}, got {actual.Count}");
            }

            for (int i = 0; i < queryCount; i++)
            {
                // box cast
                ShapeBox2D boxShape = new ShapeBox2D(random.NextVector2(-110, 110), random.NextVector2(0.5f, 8f), random.NextRotation2D());
                ColliderBox2D boxCaster = new ColliderBox2D { Shape = boxShape };
                expected.Clear();
                for (int j = 0; j < count; j++)
                {
                    if (boxCaster.CollidesWith(colliders[j].UnsafePointer))
                    {
                        expected.Add((long)colliders[j].UnsafePointer);
                    }
                }
                castHits.Clear();
                NativeListCollector castCollector = new NativeListCollector(&castHits);
                bvh.CastBox(boxShape, ref castCollector);
                actual.Clear();
                for (int j = 0; j < castHits.Length; j++)
                {
                    Assert.IsTrue(actual.Add((long)castHits[j].Collider.UnsafePointer),
                        $"box cast reported a collider twice at query {i}");
                }
                Assert.AreEqual(expected.Count, actual.Count, $"box cast set mismatch at query {i}: expected {expected.Count}, got {actual.Count}");

                // sphere cast
                ShapeSphere2D sphereShape = new ShapeSphere2D(random.NextVector2(-110, 110), random.NextFloat(0.5f, 10f));
                ColliderSphere2D sphereCaster = new ColliderSphere2D { Shape = sphereShape };
                expected.Clear();
                for (int j = 0; j < count; j++)
                {
                    if (sphereCaster.CollidesWith(colliders[j].UnsafePointer))
                    {
                        expected.Add((long)colliders[j].UnsafePointer);
                    }
                }
                castHits.Clear();
                castCollector = new NativeListCollector(&castHits);
                bvh.CastSphere(sphereShape, ref castCollector);
                actual.Clear();
                for (int j = 0; j < castHits.Length; j++)
                {
                    Assert.IsTrue(actual.Add((long)castHits[j].Collider.UnsafePointer),
                        $"sphere cast reported a collider twice at query {i}");
                }
                Assert.AreEqual(expected.Count, actual.Count, $"sphere cast set mismatch at query {i}: expected {expected.Count}, got {actual.Count}");

                // point cast; half the queries target a collider center to guarantee hits
                BoundingBox2D target = colliders[random.NextInt(0, count)].GetBoundingBox();
                Vector2 point = i % 2 == 0
                    ? (target.Min + target.Max) * 0.5f
                    : random.NextVector2(-110, 110);
                expected.Clear();
                for (int j = 0; j < count; j++)
                {
                    if (colliders[j].IntersectPoint(point))
                    {
                        expected.Add((long)colliders[j].UnsafePointer);
                    }
                }
                castHits.Clear();
                castCollector = new NativeListCollector(&castHits);
                bvh.CastPoint(point, ref castCollector);
                actual.Clear();
                for (int j = 0; j < castHits.Length; j++)
                {
                    Assert.IsTrue(actual.Add((long)castHits[j].Collider.UnsafePointer),
                        $"point cast reported a collider twice at query {i}");
                }
                Assert.AreEqual(expected.Count, actual.Count, $"point cast set mismatch at point {point}: expected {expected.Count}, got {actual.Count}");
            }

            rayHits.Dispose();
            castHits.Dispose();
        }

        [Test(Description = "BVH collider 2D: randomized cross-validation of all five query types against a linear scan")]
        public unsafe void TestBvhCrossValidation()
        {
            FastRandom random = new(12345);
            MakeScene(random, 300, 200, out NativeArrayList<ColliderBox2D> boxs, out NativeArrayList<ColliderSphere2D> spheres, out NativeArrayList<ColliderRef2D> colliders);

            NativeBvh2D bvh = new NativeBvh2D();
            bvh.BuildTree(colliders.AsSpan());
            TestContext.WriteLine($"[Bvh2D] {colliders.Length} colliders -> {bvh.NodeCount} nodes, {bvh.LeafCount} leaf blocks, depth {bvh.TreeDepth}");

            ValidateScene(bvh, colliders.UnsafePointer, colliders.Length, random, 2000, 200);

            boxs.Dispose();
            spheres.Dispose();
            colliders.Dispose();
            bvh.Dispose();
        }

        [Test(Description = "BVH collider 2D: explicit Morton builder instance produces the same results as the default build")]
        public unsafe void TestBvhExplicitBuilderMatchesDefault()
        {
            FastRandom random = new(777);
            MakeScene(random, 200, 100, out NativeArrayList<ColliderBox2D> boxs, out NativeArrayList<ColliderSphere2D> spheres, out NativeArrayList<ColliderRef2D> colliders);

            NativeBvh2D withDefault = new NativeBvh2D();
            withDefault.BuildTree(colliders.AsSpan());
            MortonBvhBuilder2D builder = new MortonBvhBuilder2D();
            NativeBvh2D withExplicit = new NativeBvh2D();
            withExplicit.BuildTree(colliders.AsSpan(), builder);

            for (int i = 0; i < 500; i++)
            {
                Ray2D ray = new Ray2D(random.NextVector2(-130, 130), random.NextVector2(-8, 8));
                RayCastResult2D a = withDefault.CastRayClosestHit(ray);
                RayCastResult2D b = withExplicit.CastRayClosestHit(ray);
                Assert.AreEqual(a.Hit, b.Hit);
                if (a.Hit)
                {
                    Assert.AreEqual(a.HitInfo.Fraction, b.HitInfo.Fraction, 1e-6f);
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

        [Test(Description = "BVH collider 2D: pairing-order builder (order-preserving) matches the linear-scan reference")]
        public unsafe void TestBvhPairingOrderBuilder()
        {
            FastRandom random = new(2024);
            NativeBvh2D bvh = new NativeBvh2D();

            // one shared stateless instance across differently sized builds, including counts
            // that exercise every remainder of the four-way chunk split
            foreach (var (boxCount, sphereCount, rayCount, queryCount) in new[]
            {
                (300, 200, 2000, 200), (1, 0, 100, 20), (5, 0, 100, 20), (2, 3, 100, 20), (0, 9, 100, 20), (17, 0, 100, 20)
            })
            {
                MakeScene(random, boxCount, sphereCount, out NativeArrayList<ColliderBox2D> boxs, out NativeArrayList<ColliderSphere2D> spheres, out NativeArrayList<ColliderRef2D> colliders);
                bvh.BuildTree(colliders.AsSpan(), PairingOrderBvhBuilder2D.Shared);
                if (boxCount == 300)
                {
                    TestContext.WriteLine($"[Bvh2D pairing] {colliders.Length} colliders -> {bvh.NodeCount} nodes, {bvh.LeafCount} leaf blocks, depth {bvh.TreeDepth}");
                }
                ValidateScene(bvh, colliders.UnsafePointer, colliders.Length, random, rayCount, queryCount);
                boxs.Dispose();
                spheres.Dispose();
                colliders.Dispose();
            }
            bvh.Dispose();
        }

        [Test(Description = "BVH collider 2D: pairing-order trees emit point-cast hits in input order")]
        public unsafe void TestBvhPairingOrderPointSequence()
        {
            NativeArrayList<ColliderBox2D> boxs = new NativeArrayList<ColliderBox2D>(9);
            NativeArrayList<ColliderRef2D> colliders = new NativeArrayList<ColliderRef2D>(9);
            for (int i = 0; i < 9; i++)
            {
                boxs.Add(new ColliderBox2D { Shape = new ShapeBox2D(new Vector2(i * 0.1f, 0), new Vector2(1f + i), Rotation2D.Identity) });
            }
            for (int i = 0; i < boxs.Length; i++)
            {
                ColliderRef2D colliderRef = ColliderRef2D.Create(boxs.UnsafePointer + i);
                colliderRef.UserData = i;
                colliders.Add(colliderRef);
            }

            NativeBvh2D bvh = new NativeBvh2D();
            bvh.BuildTree(colliders.AsSpan(), PairingOrderBvhBuilder2D.Shared);

            OrderCollector collector = new OrderCollector { Order = new List<int>() };
            bvh.CastPoint(Vector2.Zero, ref collector);
            Assert.AreEqual(9, collector.Order.Count, "all overlapping boxes must be hit");
            for (int i = 0; i < collector.Order.Count; i++)
            {
                Assert.AreEqual(i, collector.Order[i], $"point-cast hit sequence must follow input order (position {i})");
            }

            boxs.Dispose();
            colliders.Dispose();
            bvh.Dispose();
        }

        [Test(Description = "BVH collider 2D: item counts around the 4-item leaf-block boundary stay correct")]
        public unsafe void TestBvhSmallCounts()
        {
            FastRandom random = new(42);
            NativeBvh2D bvh = new NativeBvh2D();
            foreach (int count in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 17 })
            {
                NativeArrayList<ColliderBox2D> boxs = new NativeArrayList<ColliderBox2D>(8);
                NativeArrayList<ColliderSphere2D> spheres = new NativeArrayList<ColliderSphere2D>(8);
                NativeArrayList<ColliderRef2D> colliders = new NativeArrayList<ColliderRef2D>();
                for (int i = 0; i < count; i++)
                {
                    if (i % 2 == 0)
                    {
                        boxs.Add(new ColliderBox2D { Shape = new ShapeBox2D(random.NextVector2(-50, 50), random.NextVector2(0.5f, 4f), random.NextRotation2D()) });
                    }
                    else
                    {
                        spheres.Add(new ColliderSphere2D { Shape = new ShapeSphere2D(random.NextVector2(-50, 50), random.NextFloat(0.5f, 4f)) });
                    }
                }
                for (int i = 0; i < boxs.Length; i++)
                {
                    colliders.Add(ColliderRef2D.Create(boxs.UnsafePointer + i));
                }
                for (int i = 0; i < spheres.Length; i++)
                {
                    colliders.Add(ColliderRef2D.Create(spheres.UnsafePointer + i));
                }

                bvh.BuildTree(colliders.AsSpan());
                ValidateScene(bvh, colliders.UnsafePointer, colliders.Length, random, 100, 20);

                boxs.Dispose();
                spheres.Dispose();
                colliders.Dispose();
            }
            bvh.Dispose();
        }

        [Test(Description = "BVH collider 2D: 300 identical colliders (identical Morton codes) are all reachable")]
        public unsafe void TestBvhIdenticalColliders()
        {
            NativeArrayList<ColliderSphere2D> spheres = new NativeArrayList<ColliderSphere2D>(300);
            NativeArrayList<ColliderRef2D> colliders = new NativeArrayList<ColliderRef2D>(300);
            for (int i = 0; i < 300; i++)
            {
                spheres.Add(new ColliderSphere2D { Shape = new ShapeSphere2D(new Vector2(1, 2), 1.5f) });
            }
            for (int i = 0; i < spheres.Length; i++)
            {
                colliders.Add(ColliderRef2D.Create(spheres.UnsafePointer + i));
            }

            NativeBvh2D bvh = new NativeBvh2D();
            bvh.BuildTree(colliders.AsSpan());

            NativeArrayList<ColliderCastResult2D> hits = new NativeArrayList<ColliderCastResult2D>(300);
            NativeListCollector collector = new NativeListCollector(&hits);
            bvh.CastPoint(new Vector2(1, 2), ref collector);
            Assert.AreEqual(300, hits.Length);

            hits.Clear();
            collector = new NativeListCollector(&hits);
            bvh.CastSphere(new ShapeSphere2D(new Vector2(1, 2), 0.1f), ref collector);
            Assert.AreEqual(300, hits.Length);

            RayCastResult2D closest = bvh.CastRayClosestHit(new Ray2D(new Vector2(-10, 2), new Vector2(20, 0)));
            Assert.IsTrue(closest.Hit);
            Assert.AreEqual(0.475f, closest.HitInfo.Fraction, 1e-4f); // x=-0.5 surface: (-0.5 - -10) / 20

            spheres.Dispose();
            colliders.Dispose();
            hits.Dispose();
            bvh.Dispose();
        }

        [Test(Description = "BVH collider 2D: empty input leaves the tree inert for every query")]
        public unsafe void TestBvhEmptyInput()
        {
            NativeBvh2D bvh = new NativeBvh2D();
            bvh.BuildTree(Span<ColliderRef2D>.Empty);

            Assert.AreEqual(0, bvh.NodeCount);
            Assert.AreEqual(0, bvh.LeafCount);
            Assert.AreEqual(0, bvh.TreeDepth);
            Assert.IsFalse(bvh.CastRayClosestHit(new Ray2D(Vector2.Zero, Vector2.One)).Hit);

            NativeArrayList<RayCastResult2D> rayHits = new NativeArrayList<RayCastResult2D>(8);
            NativeListCollector rayCollector = new NativeListCollector(&rayHits);
            bvh.CastRay(new Ray2D(Vector2.Zero, Vector2.One), ref rayCollector);
            NativeArrayList<ColliderCastResult2D> castHits = new NativeArrayList<ColliderCastResult2D>(8);
            NativeListCollector castCollector = new NativeListCollector(&castHits);
            bvh.CastBox(new ShapeBox2D(Vector2.Zero, Vector2.One, Rotation2D.Identity), ref castCollector);
            bvh.CastSphere(new ShapeSphere2D(Vector2.Zero, 1f), ref castCollector);
            bvh.CastPoint(Vector2.Zero, ref castCollector);
            Assert.AreEqual(0, rayHits.Length + castHits.Length);

            rayHits.Dispose();
            castHits.Dispose();
            bvh.Dispose();
        }

        [Test(Description = "BVH collider 2D: rebuilding with different sizes reuses buffers without corrupting results")]
        public unsafe void TestBvhRebuildReuse()
        {
            FastRandom random = new(99);
            NativeBvh2D bvh = new NativeBvh2D();

            foreach (var (boxCount, sphereCount, rayCount, queryCount) in new[] { (500, 300, 300, 60), (1, 2, 300, 60), (2000, 1200, 300, 60) })
            {
                MakeScene(random, boxCount, sphereCount, out NativeArrayList<ColliderBox2D> boxs, out NativeArrayList<ColliderSphere2D> spheres, out NativeArrayList<ColliderRef2D> colliders);

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
