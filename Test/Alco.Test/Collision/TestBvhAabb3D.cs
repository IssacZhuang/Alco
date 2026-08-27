using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco.Test
{
    /// <summary>
    /// Correctness tests for the 4-wide <see cref="BvhAabb3D"/>. All query methods are
    /// cross-validated against a double-precision brute-force reference over randomized
    /// scenes (including rays that start inside boxes, axis-parallel rays, degenerate and
    /// duplicated boxes), plus hand-picked edge cases the random scenes cannot reach
    /// deterministically (empty input, leaf-block boundaries, scratch reuse across rebuilds).
    /// </summary>
    public class TestBvhAabb3D
    {
        // ════════════════════════════════════════════════════════════
        //  Double-precision brute-force reference
        // ════════════════════════════════════════════════════════════

        /// <summary>Computes the raw slab entry fraction of one box, or NaN when the segment misses it.</summary>
        private static double BoxEntry(BoundingBox3D b, Vector3 origin, Vector3 displacement)
        {
            double invX = displacement.X != 0 ? 1.0 / displacement.X : double.MaxValue;
            double invY = displacement.Y != 0 ? 1.0 / displacement.Y : double.MaxValue;
            double invZ = displacement.Z != 0 ? 1.0 / displacement.Z : double.MaxValue;
            double tx1 = (b.Min.X - origin.X) * invX;
            double tx2 = (b.Max.X - origin.X) * invX;
            double tmin = Math.Min(tx1, tx2);
            double tmax = Math.Max(tx1, tx2);
            double ty1 = (b.Min.Y - origin.Y) * invY;
            double ty2 = (b.Max.Y - origin.Y) * invY;
            tmin = Math.Max(tmin, Math.Min(ty1, ty2));
            tmax = Math.Min(tmax, Math.Max(ty1, ty2));
            double tz1 = (b.Min.Z - origin.Z) * invZ;
            double tz2 = (b.Max.Z - origin.Z) * invZ;
            tmin = Math.Max(tmin, Math.Min(tz1, tz2));
            tmax = Math.Min(tmax, Math.Max(tz1, tz2));
            if (tmax >= Math.Max(tmin, 0) && tmin <= 1)
            {
                return tmin; // raw entry: negative when the origin starts inside the box
            }
            return double.NaN;
        }

        private static void BruteClosest(BoundingBox3D[] boxes, Vector3 origin, Vector3 displacement, out bool hit, out double t)
        {
            hit = false;
            t = double.PositiveInfinity;
            for (int i = 0; i < boxes.Length; i++)
            {
                double entry = BoxEntry(boxes[i], origin, displacement);
                if (!double.IsNaN(entry) && entry < t)
                {
                    t = entry;
                    hit = true;
                }
            }
        }

        private static bool BruteAny(BoundingBox3D[] boxes, Vector3 origin, Vector3 displacement)
        {
            for (int i = 0; i < boxes.Length; i++)
            {
                if (!double.IsNaN(BoxEntry(boxes[i], origin, displacement)))
                {
                    return true;
                }
            }
            return false;
        }

        private static void BruteOverlap(BoundingBox3D[] boxes, BoundingBox3D query, List<int> results)
        {
            for (int i = 0; i < boxes.Length; i++)
            {
                if (query.Min.X <= boxes[i].Max.X && boxes[i].Min.X <= query.Max.X &&
                    query.Min.Y <= boxes[i].Max.Y && boxes[i].Min.Y <= query.Max.Y &&
                    query.Min.Z <= boxes[i].Max.Z && boxes[i].Min.Z <= query.Max.Z)
                {
                    results.Add(i);
                }
            }
            results.Sort();
        }

        private static void BrutePoint(BoundingBox3D[] boxes, Vector3 point, List<int> results)
        {
            for (int i = 0; i < boxes.Length; i++)
            {
                if (boxes[i].Min.X <= point.X && point.X <= boxes[i].Max.X &&
                    boxes[i].Min.Y <= point.Y && point.Y <= boxes[i].Max.Y &&
                    boxes[i].Min.Z <= point.Z && point.Z <= boxes[i].Max.Z)
                {
                    results.Add(i);
                }
            }
            results.Sort();
        }

        // ════════════════════════════════════════════════════════════
        //  Scene generation
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Random scene with the shapes most likely to break a Morton LBVH: plain boxes,
        /// zero-size point boxes, exact duplicates, and a cluster sharing one centroid
        /// (identical Morton codes, exercising the midpoint-split fallback).
        /// </summary>
        private static BoundingBox3D[] MakeScene(FastRandom random, int count)
        {
            BoundingBox3D[] boxes = new BoundingBox3D[count];
            for (int i = 0; i < count; i++)
            {
                if (i % 11 == 10)
                {
                    Vector3 p = random.NextVector3(-100, 100);
                    boxes[i] = new BoundingBox3D(p, p); // zero-size point box
                }
                else if (i > 0 && i % 13 == 12)
                {
                    boxes[i] = boxes[i - 1]; // exact duplicate
                }
                else if (i % 17 == 16)
                {
                    float half = random.NextFloat(0.5f, 4f);
                    boxes[i] = new BoundingBox3D(new Vector3(7 - half), new Vector3(7 + half)); // shared centroid cluster
                }
                else
                {
                    Vector3 c = random.NextVector3(-100, 100);
                    Vector3 half = random.NextVector3(0.5f, 8f);
                    boxes[i] = new BoundingBox3D(c - half, c + half);
                }
            }
            return boxes;
        }

        // ════════════════════════════════════════════════════════════
        //  Cross-validation
        // ════════════════════════════════════════════════════════════

        /// <summary>Cross-validates every query method against the brute-force reference.</summary>
        private static void CrossValidate(BvhAabb3D bvh, BoundingBox3D[] boxes, FastRandom random, int rayCount, int queryCount)
        {
            List<int> expected = new List<int>();
            List<int> actual = new List<int>();

            for (int i = 0; i < rayCount; i++)
            {
                Vector3 origin;
                Vector3 direction;
                switch (i % 6)
                {
                    case 0: // ray starting inside a known box (negative raw entry)
                        origin = (boxes[random.NextInt(0, boxes.Length)].Min + boxes[random.NextInt(0, boxes.Length)].Max) * 0.5f;
                        direction = random.NextVector3(-6, 6);
                        break;
                    case 1: // axis-parallel +X
                        origin = random.NextVector3(-130, 130);
                        direction = new Vector3(random.NextFloat(1, 12), 0, 0);
                        break;
                    case 2: // axis-parallel -Z
                        origin = random.NextVector3(-130, 130);
                        direction = new Vector3(0, 0, -random.NextFloat(1, 12));
                        break;
                    case 3: // plane-parallel diagonal (zero Z component)
                        origin = random.NextVector3(-130, 130);
                        direction = new Vector3(random.NextFloat(-8, 8), random.NextFloat(-8, 8), 0);
                        break;
                    default:
                        origin = random.NextVector3(-130, 130);
                        direction = random.NextVector3(-8, 8);
                        break;
                }

                bool expectedHit = false;
                double expectedT = 0;
                BruteClosest(boxes, origin, direction, out expectedHit, out expectedT);
                bool actualHit = bvh.RayCastClosest(origin, direction, out int actualIndex, out float actualT);

                Assert.That(actualHit, Is.EqualTo(expectedHit), $"closest hit/miss mismatch at ray {i} ({origin} -> +{direction})");
                if (expectedHit)
                {
                    Assert.That(actualT, Is.EqualTo(expectedT).Within(1e-3), $"closest entry t mismatch at ray {i}: expected {expectedT}, got {actualT}");
                    // the returned box must itself be a valid segment hit at the reported t
                    double ownEntry = BoxEntry(boxes[actualIndex], origin, direction);
                    Assert.IsFalse(double.IsNaN(ownEntry), $"closest returned a box the segment misses at ray {i}");
                    Assert.That(actualT, Is.EqualTo(ownEntry).Within(1e-3), $"closest returned index {actualIndex} whose entry disagrees with the reported t at ray {i}");
                }

                Assert.That(bvh.RayCastAny(origin, direction), Is.EqualTo(BruteAny(boxes, origin, direction)), $"any-hit mismatch at ray {i}");
            }

            for (int i = 0; i < queryCount; i++)
            {
                Vector3 center = random.NextVector3(-120, 120);
                Vector3 half = random.NextVector3(0.5f, 15f);
                BoundingBox3D query = new(center - half, center + half);

                expected.Clear();
                actual.Clear();
                BruteOverlap(boxes, query, expected);
                bvh.OverlapAabb(query, actual);
                actual.Sort();
                Assert.That(actual.Count, Is.EqualTo(expected.Count), $"overlap count mismatch at query {i}");
                for (int j = 0; j < expected.Count; j++)
                {
                    Assert.That(actual[j], Is.EqualTo(expected[j]), $"overlap set mismatch at query {i}");
                }

                // half of the point queries target a box center, guaranteeing hits
                Vector3 point = i % 2 == 0
                    ? (boxes[random.NextInt(0, boxes.Length)].Min + boxes[random.NextInt(0, boxes.Length)].Max) * 0.5f
                    : random.NextVector3(-110, 110);
                expected.Clear();
                actual.Clear();
                BrutePoint(boxes, point, expected);
                bvh.QueryPoint(point, actual);
                actual.Sort();
                Assert.That(actual.Count, Is.EqualTo(expected.Count), $"point count mismatch at point {point}");
                for (int j = 0; j < expected.Count; j++)
                {
                    Assert.That(actual[j], Is.EqualTo(expected[j]), $"point set mismatch at point {point}");
                }
            }
        }

        [Test(Description = "BVH AABB 3D: randomized cross-validation against a double-precision brute-force reference")]
        public void TestBvhAabb3DRandomizedCrossValidation()
        {
            FastRandom random = new(12345);
            BoundingBox3D[] boxes = MakeScene(random, 600);
            using BvhAabb3D bvh = new();
            bvh.Build(boxes);
            CrossValidate(bvh, boxes, random, 3000, 500);
        }

        [Test(Description = "BVH AABB 3D: larger scene keeps the same query results as the reference")]
        public void TestBvhAabb3DLargeSceneCrossValidation()
        {
            FastRandom random = new(777);
            BoundingBox3D[] boxes = MakeScene(random, 5000);
            using BvhAabb3D bvh = new();
            bvh.Build(boxes);
            CrossValidate(bvh, boxes, random, 500, 100);
        }

        // ════════════════════════════════════════════════════════════
        //  Edge cases
        // ════════════════════════════════════════════════════════════

        [Test(Description = "BVH AABB 3D: empty input leaves the tree inert for every query")]
        public void TestBvhAabb3DEmptyInput()
        {
            using BvhAabb3D bvh = new();
            bvh.Build(Span<BoundingBox3D>.Empty);

            Assert.That(bvh.NodeCount, Is.EqualTo(0));
            Assert.That(bvh.TreeDepth, Is.EqualTo(0));
            Assert.IsFalse(bvh.RayCastClosest(Vector3.Zero, Vector3.One, out int index, out float t));
            Assert.That(index, Is.EqualTo(-1));
            Assert.IsFalse(bvh.RayCastAny(Vector3.Zero, Vector3.One));

            List<int> results = new();
            bvh.OverlapAabb(new BoundingBox3D(Vector3.One * -100, Vector3.One * 100), results);
            bvh.QueryPoint(Vector3.Zero, results);
            Assert.That(results.Count, Is.EqualTo(0));
        }

        [Test(Description = "BVH AABB 3D: item counts around the 4-item leaf-block boundary stay correct")]
        public void TestBvhAabb3DSmallCounts()
        {
            FastRandom random = new(42);
            foreach (int count in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 17 })
            {
                BoundingBox3D[] boxes = MakeScene(random, count);
                using BvhAabb3D bvh = new();
                bvh.Build(boxes);
                CrossValidate(bvh, boxes, random, 200, 40);
            }
        }

        [Test(Description = "BVH AABB 3D: 300 identical boxes (identical Morton codes) are all reachable")]
        public void TestBvhAabb3DIdenticalBoxes()
        {
            BoundingBox3D box = new(Vector3.One * -1, Vector3.One);
            BoundingBox3D[] boxes = new BoundingBox3D[300];
            Array.Fill(boxes, box);
            using BvhAabb3D bvh = new();
            bvh.Build(boxes);

            // a query covering the scene must return every item exactly once
            List<int> results = new();
            bvh.OverlapAabb(new BoundingBox3D(Vector3.One * -2, Vector3.One * 2), results);
            Assert.That(results.Count, Is.EqualTo(300));

            results.Clear();
            bvh.QueryPoint(Vector3.Zero, results);
            Assert.That(results.Count, Is.EqualTo(300));

            Assert.IsTrue(bvh.RayCastAny(new Vector3(-5, 0, 0), new Vector3(10, 0, 0)));
            Assert.IsTrue(bvh.RayCastClosest(new Vector3(-5, 0, 0), new Vector3(10, 0, 0), out int index, out float t));
            Assert.That(t, Is.EqualTo(0.4f).Within(1e-4)); // x=-1 plane: (-1 - -5) / 10
            Assert.IsTrue(index >= 0 && index < 300);

            // starting inside all of them: the raw entry must be negative
            Assert.IsTrue(bvh.RayCastClosest(Vector3.Zero, new Vector3(2, 0, 0), out _, out t));
            Assert.IsTrue(t < 0f);
        }

        [Test(Description = "BVH AABB 3D: zero-displacement rays behave as point-in-box tests")]
        public void TestBvhAabb3DZeroDisplacementRay()
        {
            BoundingBox3D[] boxes =
            {
                new(new Vector3(-10, -10, -10), new Vector3(10, 10, 10)),
            };
            using BvhAabb3D bvh = new();
            bvh.Build(boxes);

            Assert.IsTrue(bvh.RayCastAny(new Vector3(1, 2, 3), Vector3.Zero));
            Assert.IsTrue(bvh.RayCastClosest(new Vector3(1, 2, 3), Vector3.Zero, out _, out float t));
            Assert.IsTrue(t < 0f); // origin inside: raw entry behind the origin

            Assert.IsFalse(bvh.RayCastAny(new Vector3(20, 0, 0), Vector3.Zero));
            Assert.IsFalse(bvh.RayCastClosest(new Vector3(20, 0, 0), Vector3.Zero, out _, out _));
        }

        [Test(Description = "BVH AABB 3D: rebuilding with different sizes reuses scratch without corrupting results")]
        public void TestBvhAabb3DRebuildReuse()
        {
            FastRandom random = new(99);
            using BvhAabb3D bvh = new();

            BoundingBox3D[] big = MakeScene(random, 1000);
            bvh.Build(big);
            CrossValidate(bvh, big, random, 300, 60);

            BoundingBox3D[] small = MakeScene(random, 3);
            bvh.Build(small);
            CrossValidate(bvh, small, random, 300, 60);

            BoundingBox3D[] bigger = MakeScene(random, 4000);
            bvh.Build(bigger);
            CrossValidate(bvh, bigger, random, 300, 60);
        }

        [Test(Description = "BVH AABB 3D: rebuilding the same input produces identical query results")]
        public void TestBvhAabb3DDeterministicRebuild()
        {
            FastRandom random = new(4242);
            BoundingBox3D[] boxes = MakeScene(random, 400);
            using BvhAabb3D first = new();
            using BvhAabb3D second = new();
            first.Build(boxes);
            second.Build(boxes);

            for (int i = 0; i < 500; i++)
            {
                Vector3 origin = random.NextVector3(-130, 130);
                Vector3 direction = random.NextVector3(-8, 8);
                bool hitA = first.RayCastClosest(origin, direction, out int indexA, out float tA);
                bool hitB = second.RayCastClosest(origin, direction, out int indexB, out float tB);
                Assert.That(hitB, Is.EqualTo(hitA));
                Assert.That(indexB, Is.EqualTo(indexA));
                Assert.That(tB, Is.EqualTo(tA));
            }
        }
    }
}
