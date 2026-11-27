using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco.Test
{
    /// <summary>
    /// Cross-validates <see cref="DynamicBvhAabb2D"/> against a double-precision brute-force reference
    /// across incremental inserts, removals, bounds updates, Morton <c>BulkBuild</c>, visitors, and <c>maxT</c>.
    /// </summary>
    public class TestDynamicBvhAabb2D
    {
        private struct CollectingVisitor : IBvhAabb2DLeafVisitor
        {
            public readonly List<int> Items;
            public readonly int StopAfter;

            public CollectingVisitor(List<int> items, int stopAfter = int.MaxValue)
            {
                Items = items;
                StopAfter = stopAfter;
            }

            public bool OnLeaf(int payload)
            {
                Items.Add(payload);
                return Items.Count < StopAfter;
            }
        }

        private struct ClosestVisitor : IBvhAabb2DRayClosestVisitor
        {
            public bool HasHit;
            public int Payload;
            public float BestT;

            public float OnLeaf(int payload, float entryT, float currentBestT)
            {
                if (entryT < BestT || (!HasHit && entryT <= currentBestT))
                {
                    HasHit = true;
                    Payload = payload;
                    BestT = entryT;
                    return entryT;
                }
                return currentBestT;
            }
        }

        private static double BoxEntry(in BoundingBox2D b, Vector2 origin, Vector2 displacement, double maxT)
        {
            double invX = displacement.X != 0 ? 1.0 / displacement.X : double.MaxValue;
            double invY = displacement.Y != 0 ? 1.0 / displacement.Y : double.MaxValue;
            double tx1 = (b.Min.X - origin.X) * invX;
            double tx2 = (b.Max.X - origin.X) * invX;
            double tmin = Math.Min(tx1, tx2);
            double tmax = Math.Max(tx1, tx2);
            double ty1 = (b.Min.Y - origin.Y) * invY;
            double ty2 = (b.Max.Y - origin.Y) * invY;
            tmin = Math.Max(tmin, Math.Min(ty1, ty2));
            tmax = Math.Min(tmax, Math.Max(ty1, ty2));
            if (tmax >= Math.Max(tmin, 0.0) && tmin <= maxT)
            {
                return tmin;
            }
            return double.NaN;
        }

        private static void CrossValidate(DynamicBvhAabb2D tree, List<(int Handle, BoundingBox2D Box, int Payload)> live, FastRandom rng, bool exactBounds)
        {
            List<int> actual = new();
            List<int> expected = new();

            for (int i = 0; i < 80; i++)
            {
                Vector2 p = rng.NextVector2(-110f, 110f);
                actual.Clear();
                expected.Clear();
                tree.QueryPoint(p, actual);
                for (int j = 0; j < live.Count; j++)
                {
                    BoundingBox2D b = live[j].Box;
                    if (b.Min.X <= p.X && p.X <= b.Max.X && b.Min.Y <= p.Y && p.Y <= b.Max.Y)
                    {
                        expected.Add(live[j].Payload);
                    }
                }
                actual.Sort();
                expected.Sort();
                if (exactBounds)
                {
                    Assert.That(actual, Is.EqualTo(expected));
                }
                else
                {
                    foreach (int exp in expected)
                    {
                        Assert.That(actual, Does.Contain(exp));
                    }
                }
            }

            for (int i = 0; i < 80; i++)
            {
                Vector2 c = rng.NextVector2(-100f, 100f);
                Vector2 e = rng.NextVector2(0.5f, 12f);
                BoundingBox2D q = new(c - e, c + e);
                actual.Clear();
                expected.Clear();
                tree.OverlapAabb(q, actual);
                for (int j = 0; j < live.Count; j++)
                {
                    BoundingBox2D b = live[j].Box;
                    if (b.Min.X <= q.Max.X && q.Min.X <= b.Max.X && b.Min.Y <= q.Max.Y && q.Min.Y <= b.Max.Y)
                    {
                        expected.Add(live[j].Payload);
                    }
                }
                actual.Sort();
                expected.Sort();
                if (exactBounds)
                {
                    Assert.That(actual, Is.EqualTo(expected));
                }
                else
                {
                    foreach (int exp in expected)
                    {
                        Assert.That(actual, Does.Contain(exp));
                    }
                }
            }

            if (exactBounds)
            {
                for (int i = 0; i < 150; i++)
                {
                    Vector2 origin = rng.NextVector2(-120f, 120f);
                    Vector2 disp = rng.NextVector2(-120f, 120f);
                    float maxT = i % 3 == 0 ? 1.5f : 1f;

                    bool refHit = false;
                    double refT = double.PositiveInfinity;
                    int refPayload = -1;
                    for (int j = 0; j < live.Count; j++)
                    {
                        double entry = BoxEntry(live[j].Box, origin, disp, maxT);
                        if (!double.IsNaN(entry) && entry < refT)
                        {
                            refT = entry;
                            refHit = true;
                            refPayload = live[j].Payload;
                        }
                    }

                    bool got = tree.RayCastClosest(origin, disp, maxT, out int hitPayload, out float hitT);
                    Assert.That(got, Is.EqualTo(refHit));
                    if (refHit && hitT < (float)refT - 1e-4f || (refHit && hitT > (float)refT + 1e-4f))
                    {
                        Assert.That(hitT, Is.EqualTo((float)refT).Within(1e-3f));
                    }
                    if (refHit)
                    {
                        Assert.That(hitPayload, Is.GreaterThanOrEqualTo(0));
                        Assert.That(tree.RayCastAny(origin, disp, maxT), Is.True);
                    }
                    else
                    {
                        Assert.That(refPayload, Is.EqualTo(-1));
                    }
                }
            }
        }

        /// <summary>Verifies exact incremental mutations with zero fatten margin.</summary>
        [Test(Description = "DynamicBvhAabb2D exact Insert, Remove, UpdateBounds, BulkBuild, Clear")]
        public void TestDynamicBvh2DFuzzExactAndBulkBuild()
        {
            FastRandom rng = new(1337);
            DynamicBvhAabb2D tree = new(initialCapacity: 8, fattenMargin: 0f);
            List<(int Handle, BoundingBox2D Box, int Payload)> live = new();

            int nextPayload = 1;
            for (int i = 0; i < 200; i++)
            {
                Vector2 c = rng.NextVector2(-100f, 100f);
                Vector2 h = rng.NextVector2(0.2f, 6f);
                BoundingBox2D box = new(c - h, c + h);
                int payload = nextPayload++;
                int handle = tree.Insert(box, payload);
                live.Add((handle, box, payload));
            }
            Assert.That(tree.Count, Is.EqualTo(200));
            CrossValidate(tree, live, rng, exactBounds: true);

            // Update bounds and removals across several rounds
            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < 40; i++)
                {
                    int idx = rng.NextInt(0, live.Count);
                    tree.Remove(live[idx].Handle);
                    live.RemoveAt(idx);
                }

                for (int i = 0; i < 50; i++)
                {
                    int idx = rng.NextInt(0, live.Count);
                    Vector2 c = rng.NextVector2(-100f, 100f);
                    Vector2 h = rng.NextVector2(0.2f, 6f);
                    BoundingBox2D newBox = new(c - h, c + h);
                    Assert.That(tree.UpdateBounds(live[idx].Handle, newBox), Is.True);
                    live[idx] = (live[idx].Handle, newBox, live[idx].Payload);
                }

                for (int i = 0; i < 40; i++)
                {
                    Vector2 c = rng.NextVector2(-100f, 100f);
                    Vector2 h = rng.NextVector2(0.2f, 6f);
                    BoundingBox2D box = new(c - h, c + h);
                    int payload = nextPayload++;
                    int handle = tree.Insert(box, payload);
                    live.Add((handle, box, payload));
                }

                Assert.That(tree.Count, Is.EqualTo(live.Count));
                CrossValidate(tree, live, rng, exactBounds: true);
            }

            // BulkBuild then continue mutating incrementally
            BoundingBox2D[] bulkBoxes = new BoundingBox2D[live.Count];
            int[] bulkPayloads = new int[live.Count];
            int[] bulkHandles = new int[live.Count];
            for (int i = 0; i < live.Count; i++)
            {
                bulkBoxes[i] = live[i].Box;
                bulkPayloads[i] = live[i].Payload;
            }
            tree.BulkBuild(bulkBoxes, bulkPayloads, bulkHandles);
            for (int i = 0; i < live.Count; i++)
            {
                live[i] = (bulkHandles[i], live[i].Box, live[i].Payload);
            }
            CrossValidate(tree, live, rng, exactBounds: true);

            // Visitors early-out & maxT / negative t when origin starts inside box
            List<int> hitItems = new();
            CollectingVisitor stopTwo = new(hitItems, stopAfter: 2);
            tree.VisitOverlapAabb(new BoundingBox2D(new Vector2(-200f), new Vector2(200f)), ref stopTwo);
            Assert.That(hitItems.Count, Is.EqualTo(2));

            ClosestVisitor closestVisitor = new() { BestT = 1.5f };
            tree.VisitRayClosest(new Vector2(-120f, 0f), new Vector2(240f, 0f), 1.5f, ref closestVisitor);
            tree.Clear();
            Assert.That(tree.Count, Is.EqualTo(0));
            Assert.That(tree.RayCastAny(Vector2.Zero, Vector2.One), Is.False);
        }

        /// <summary>Verifies AABB fattening skips redundant re-inserts while still preserving hits.</summary>
        [Test(Description = "DynamicBvhAabb2D with positive fatten margin absorbs minor motion")]
        public void TestDynamicBvh2DFattenMarginAndOriginInsideRay()
        {
            DynamicBvhAabb2D tree = new(initialCapacity: 8, fattenMargin: 0.5f);
            int h0 = tree.Insert(new BoundingBox2D(new Vector2(-1f, -1f), new Vector2(1f, 1f)), 10);
            Assert.That(tree.UpdateBounds(h0, new BoundingBox2D(new Vector2(-0.8f, -0.8f), new Vector2(1.2f, 1.2f))), Is.False);
            Assert.That(tree.UpdateBounds(h0, new BoundingBox2D(new Vector2(5f, 5f), new Vector2(7f, 7f))), Is.True);

            // Origin inside box yields negative raw entry fraction
            Assert.That(tree.RayCastClosest(new Vector2(6f, 6f), new Vector2(2f, 0f), 1f, out int hitPayload, out float hitT), Is.True);
            Assert.That(hitPayload, Is.EqualTo(10));
            Assert.That(hitT, Is.LessThan(0f));
        }
    }
}
