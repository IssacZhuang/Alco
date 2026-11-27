using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco.Test
{
    /// <summary>
    /// Cross-validates <see cref="DynamicBvhAabb3D"/> against a double-precision 3D brute-force reference
    /// across incremental inserts, removals, bounds updates, 30-bit Morton <c>BulkBuild</c>, visitors, and <c>maxT</c>.
    /// </summary>
    public class TestDynamicBvhAabb3D
    {
        private struct CollectingVisitor : IBvhAabb3DLeafVisitor
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

        private struct ClosestVisitor : IBvhAabb3DRayClosestVisitor
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

        private static double BoxEntry(in BoundingBox3D b, Vector3 origin, Vector3 displacement, double maxT)
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
            if (tmax >= Math.Max(tmin, 0.0) && tmin <= maxT)
            {
                return tmin;
            }
            return double.NaN;
        }

        private static void CrossValidate(DynamicBvhAabb3D tree, List<(int Handle, BoundingBox3D Box, int Payload)> live, FastRandom rng)
        {
            List<int> actual = new();
            List<int> expected = new();

            for (int i = 0; i < 60; i++)
            {
                Vector3 p = new(rng.NextFloat(-100f, 100f), rng.NextFloat(-100f, 100f), rng.NextFloat(-100f, 100f));
                actual.Clear();
                expected.Clear();
                tree.QueryPoint(p, actual);
                for (int j = 0; j < live.Count; j++)
                {
                    BoundingBox3D b = live[j].Box;
                    if (b.Min.X <= p.X && p.X <= b.Max.X &&
                        b.Min.Y <= p.Y && p.Y <= b.Max.Y &&
                        b.Min.Z <= p.Z && p.Z <= b.Max.Z)
                    {
                        expected.Add(live[j].Payload);
                    }
                }
                actual.Sort();
                expected.Sort();
                Assert.That(actual, Is.EqualTo(expected));
            }

            for (int i = 0; i < 60; i++)
            {
                Vector3 c = new(rng.NextFloat(-90f, 90f), rng.NextFloat(-90f, 90f), rng.NextFloat(-90f, 90f));
                Vector3 e = new(rng.NextFloat(0.5f, 12f), rng.NextFloat(0.5f, 12f), rng.NextFloat(0.5f, 12f));
                BoundingBox3D q = new(c - e, c + e);
                actual.Clear();
                expected.Clear();
                tree.OverlapAabb(q, actual);
                for (int j = 0; j < live.Count; j++)
                {
                    BoundingBox3D b = live[j].Box;
                    if (b.Min.X <= q.Max.X && q.Min.X <= b.Max.X &&
                        b.Min.Y <= q.Max.Y && q.Min.Y <= b.Max.Y &&
                        b.Min.Z <= q.Max.Z && q.Min.Z <= b.Max.Z)
                    {
                        expected.Add(live[j].Payload);
                    }
                }
                actual.Sort();
                expected.Sort();
                Assert.That(actual, Is.EqualTo(expected));
            }

            for (int i = 0; i < 120; i++)
            {
                Vector3 origin = new(rng.NextFloat(-110f, 110f), rng.NextFloat(-110f, 110f), rng.NextFloat(-110f, 110f));
                Vector3 disp = new(rng.NextFloat(-120f, 120f), rng.NextFloat(-120f, 120f), rng.NextFloat(-120f, 120f));
                float maxT = i % 3 == 0 ? 1.5f : 1f;

                bool refHit = false;
                double refT = double.PositiveInfinity;
                for (int j = 0; j < live.Count; j++)
                {
                    double entry = BoxEntry(live[j].Box, origin, disp, maxT);
                    if (!double.IsNaN(entry) && entry < refT)
                    {
                        refT = entry;
                        refHit = true;
                    }
                }

                bool got = tree.RayCastClosest(origin, disp, maxT, out _, out float hitT);
                Assert.That(got, Is.EqualTo(refHit));
                if (refHit)
                {
                    Assert.That(hitT, Is.EqualTo((float)refT).Within(1e-3f));
                    Assert.That(tree.RayCastAny(origin, disp, maxT), Is.True);
                }
            }
        }

        /// <summary>Verifies 3D incremental edits, BulkBuild, visitors, and Clear reuse.</summary>
        [Test(Description = "DynamicBvhAabb3D exact Insert, Remove, UpdateBounds, BulkBuild, Clear")]
        public void TestDynamicBvh3DFuzzExactAndBulkBuild()
        {
            FastRandom rng = new(7331);
            DynamicBvhAabb3D tree = new(initialCapacity: 8, fattenMargin: 0f);
            List<(int Handle, BoundingBox3D Box, int Payload)> live = new();

            int nextPayload = 1;
            for (int i = 0; i < 200; i++)
            {
                Vector3 c = new(rng.NextFloat(-90f, 90f), rng.NextFloat(-90f, 90f), rng.NextFloat(-90f, 90f));
                Vector3 h = new(rng.NextFloat(0.2f, 6f), rng.NextFloat(0.2f, 6f), rng.NextFloat(0.2f, 6f));
                BoundingBox3D box = new(c - h, c + h);
                int payload = nextPayload++;
                int handle = tree.Insert(box, payload);
                live.Add((handle, box, payload));
            }
            CrossValidate(tree, live, rng);

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
                    Vector3 c = new(rng.NextFloat(-90f, 90f), rng.NextFloat(-90f, 90f), rng.NextFloat(-90f, 90f));
                    Vector3 h = new(rng.NextFloat(0.2f, 6f), rng.NextFloat(0.2f, 6f), rng.NextFloat(0.2f, 6f));
                    BoundingBox3D newBox = new(c - h, c + h);
                    Assert.That(tree.UpdateBounds(live[idx].Handle, newBox), Is.True);
                    live[idx] = (live[idx].Handle, newBox, live[idx].Payload);
                }
                for (int i = 0; i < 40; i++)
                {
                    Vector3 c = new(rng.NextFloat(-90f, 90f), rng.NextFloat(-90f, 90f), rng.NextFloat(-90f, 90f));
                    Vector3 h = new(rng.NextFloat(0.2f, 6f), rng.NextFloat(0.2f, 6f), rng.NextFloat(0.2f, 6f));
                    BoundingBox3D box = new(c - h, c + h);
                    int payload = nextPayload++;
                    int handle = tree.Insert(box, payload);
                    live.Add((handle, box, payload));
                }
                CrossValidate(tree, live, rng);
            }

            BoundingBox3D[] bulkBoxes = new BoundingBox3D[live.Count];
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
            CrossValidate(tree, live, rng);

            List<int> hitItems = new();
            CollectingVisitor stopTwo = new(hitItems, stopAfter: 2);
            tree.VisitOverlapAabb(new BoundingBox3D(new Vector3(-200f), new Vector3(200f)), ref stopTwo);
            Assert.That(hitItems.Count, Is.EqualTo(2));

            ClosestVisitor closest = new() { BestT = 1.5f };
            tree.VisitRayClosest(new Vector3(-110f, 0f, 0f), new Vector3(220f, 0f, 0f), 1.5f, ref closest);

            tree.Clear();
            Assert.That(tree.Count, Is.EqualTo(0));
            Assert.That(tree.RayCastAny(Vector3.Zero, Vector3.One), Is.False);
        }

        /// <summary>Verifies 3D AABB fatten margin absorbs small moves and origin-inside ray returns negative entry t.</summary>
        [Test(Description = "DynamicBvhAabb3D fatten margin and origin-inside negative entry t")]
        public void TestDynamicBvh3DFattenMarginAndOriginInsideRay()
        {
            DynamicBvhAabb3D tree = new(initialCapacity: 8, fattenMargin: 0.5f);
            int h0 = tree.Insert(new BoundingBox3D(new Vector3(-1f), new Vector3(1f)), 42);
            Assert.That(tree.UpdateBounds(h0, new BoundingBox3D(new Vector3(-0.7f), new Vector3(1.2f))), Is.False);
            Assert.That(tree.UpdateBounds(h0, new BoundingBox3D(new Vector3(4f), new Vector3(6f))), Is.True);

            Assert.That(tree.RayCastClosest(new Vector3(5f), new Vector3(2f, 0f, 0f), 1f, out int hitPayload, out float hitT), Is.True);
            Assert.That(hitPayload, Is.EqualTo(42));
            Assert.That(hitT, Is.LessThan(0f));
        }
    }
}
