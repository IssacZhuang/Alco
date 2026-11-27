using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco.Test
{
    /// <summary>
    /// Cross-validates <see cref="ChunkedBvhAabb2D"/> across both <see cref="ChunkedTlasMode2D"/> modes,
    /// pre/post <see cref="ChunkedBvhAabb2D.CommitDirty"/> parity, intra/cross-chunk <c>UpdateBounds</c>,
    /// <see cref="ChunkedBvhAabb2D.UnloadChunk"/>, generational handle invalidation, and multi-chunk early-out visitors.
    /// </summary>
    public class TestChunkedBvhAabb2D
    {
        private struct StopAfterVisitor : IBvhAabb2DLeafVisitor
        {
            public int Visits;
            public readonly int Limit;

            public StopAfterVisitor(int limit) => Limit = limit;

            public bool OnLeaf(int payload)
            {
                Visits++;
                return Visits < Limit;
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

        private static void VerifyAllQueries(
            ChunkedBvhAabb2D index,
            Dictionary<int, BoundingBox2D> expectedBoxes,
            FastRandom rng)
        {
            List<int> actual = new();
            List<int> expected = new();

            for (int i = 0; i < 60; i++)
            {
                Vector2 p = rng.NextVector2(-150f, 150f);
                actual.Clear();
                expected.Clear();
                index.QueryPoint(p, actual);
                foreach (KeyValuePair<int, BoundingBox2D> kv in expectedBoxes)
                {
                    BoundingBox2D b = kv.Value;
                    if (b.Min.X <= p.X && p.X <= b.Max.X && b.Min.Y <= p.Y && p.Y <= b.Max.Y)
                    {
                        expected.Add(kv.Key);
                    }
                }
                actual.Sort();
                expected.Sort();
                Assert.That(actual, Is.EqualTo(expected));
            }

            for (int i = 0; i < 60; i++)
            {
                Vector2 c = rng.NextVector2(-140f, 140f);
                Vector2 e = rng.NextVector2(0.5f, 25f);
                BoundingBox2D q = new(c - e, c + e);
                actual.Clear();
                expected.Clear();
                index.OverlapAabb(q, actual);
                foreach (KeyValuePair<int, BoundingBox2D> kv in expectedBoxes)
                {
                    BoundingBox2D b = kv.Value;
                    if (b.Min.X <= q.Max.X && q.Min.X <= b.Max.X && b.Min.Y <= q.Max.Y && q.Min.Y <= b.Max.Y)
                    {
                        expected.Add(kv.Key);
                    }
                }
                actual.Sort();
                expected.Sort();
                Assert.That(actual, Is.EqualTo(expected));
            }

            for (int i = 0; i < 100; i++)
            {
                Vector2 origin = rng.NextVector2(-160f, 160f);
                Vector2 disp = rng.NextVector2(-180f, 180f);
                float maxT = i % 4 == 0 ? 1.5f : 1f;

                bool refHit = false;
                double refT = double.PositiveInfinity;
                foreach (KeyValuePair<int, BoundingBox2D> kv in expectedBoxes)
                {
                    double entry = BoxEntry(kv.Value, origin, disp, maxT);
                    if (!double.IsNaN(entry) && entry < refT)
                    {
                        refT = entry;
                        refHit = true;
                    }
                }

                bool got = index.RayCastClosest(origin, disp, maxT, out _, out float hitT);
                Assert.That(got, Is.EqualTo(refHit));
                if (refHit)
                {
                    Assert.That(hitT, Is.EqualTo((float)refT).Within(1e-3f));
                    Assert.That(index.RayCastAny(origin, disp, maxT), Is.True);
                }
            }
        }

        /// <summary>Verifies both TLAS modes, pre-commit visibility, flat-to-tree threshold transitions, and UpdateBounds.</summary>
        [Test(Description = "ChunkedBvhAabb2D fuzz cross-validation in MortonSimd and Dynamic modes")]
        public void TestChunkedBvh2DFuzzBothTlasModesAndPreCommitParity()
        {
            foreach (ChunkedTlasMode2D mode in new[] { ChunkedTlasMode2D.MortonSimd, ChunkedTlasMode2D.Dynamic })
            {
                FastRandom rng = new(4455);
                using ChunkedBvhAabb2D index = new(chunkSize: 32, treeBuildThreshold: 8, tlasMode: mode);
                Dictionary<int, BoundingBox2D> refMap = new();
                List<(ChunkedEntryHandle2D Handle, int Payload)> live = new();

                int nextPayload = 1;
                for (int round = 0; round < 3; round++)
                {
                    for (int i = 0; i < 120; i++)
                    {
                        Vector2 c = rng.NextVector2(-120f, 120f);
                        Vector2 e = rng.NextVector2(0.3f, 8f);
                        BoundingBox2D box = new(c - e, c + e);
                        int p = nextPayload++;
                        ChunkedEntryHandle2D h = index.Add(box, p);
                        live.Add((h, p));
                        refMap[p] = box;
                    }

                    // Verify BEFORE CommitDirty
                    VerifyAllQueries(index, refMap, rng);

                    index.CommitDirty();
                    Assert.That(index.DirtyChunkCount, Is.EqualTo(0));
                    VerifyAllQueries(index, refMap, rng);

                    // Intra-chunk shrink and cross-chunk migration updates
                    for (int i = 0; i < 30 && live.Count > 0; i++)
                    {
                        int idx = rng.NextInt(0, live.Count);
                        (ChunkedEntryHandle2D handle, int payload) = live[idx];
                        Vector2 c = rng.NextVector2(-140f, 140f);
                        Vector2 e = rng.NextVector2(0.2f, 5f);
                        BoundingBox2D updated = new(c - e, c + e);
                        Assert.That(index.UpdateBounds(handle, updated), Is.True);
                        refMap[payload] = updated;
                    }

                    // Removals before and after commit
                    for (int i = 0; i < 25 && live.Count > 0; i++)
                    {
                        int idx = rng.NextInt(0, live.Count);
                        (ChunkedEntryHandle2D handle, int payload) = live[idx];
                        Assert.That(index.Remove(handle), Is.True);
                        Assert.That(index.Remove(handle), Is.False, "stale handle removal must fail safely");
                        refMap.Remove(payload);
                        live.RemoveAt(idx);
                    }

                    VerifyAllQueries(index, refMap, rng);
                    index.CommitDirty();
                    Assert.That(index.DirtyChunkCount, Is.EqualTo(0));
                    VerifyAllQueries(index, refMap, rng);
                }

                index.Clear();
                Assert.That(index.EntryCount, Is.EqualTo(0));
                Assert.That(index.ChunkCount, Is.EqualTo(0));
                Assert.That(index.DirtyChunkCount, Is.EqualTo(0));
                Assert.That(index.NativeMemoryBytes, Is.EqualTo(0));
            }
        }

        /// <summary>Verifies shrinking loose bounds, UnloadChunk, stale handle rejection, and multi-chunk early-out.</summary>
        [Test(Description = "ChunkedBvhAabb2D UnloadChunk, shrinking loose bounds, and multi-chunk early-out")]
        public void TestChunkedBvh2DUnloadBoundsShrinkAndMultiChunkEarlyOut()
        {
            using ChunkedBvhAabb2D index = new(chunkSize: 32, treeBuildThreshold: 4, tlasMode: ChunkedTlasMode2D.MortonSimd);

            // Chunk (0, 0): one wide box and one small interior box; shrink wide box inside chunk
            BoundingBox2D wide = new(new Vector2(2f, 2f), new Vector2(28f, 28f));
            BoundingBox2D inner = new(new Vector2(10f, 10f), new Vector2(14f, 14f));
            ChunkedEntryHandle2D hWide = index.Add(wide, 100);
            ChunkedEntryHandle2D hInner = index.Add(inner, 101);
            index.CommitDirty();

            BoundingBox2D shrunk = new(new Vector2(8f, 8f), new Vector2(12f, 12f));
            Assert.That(index.UpdateBounds(hWide, shrunk), Is.True);
            Assert.That(index.DirtyChunkCount, Is.EqualTo(1));
            Assert.That(index.TryGetChunkBounds(new int2(0, 0), out BoundingBox2D tight), Is.True);
            Assert.That(tight, Is.EqualTo(BoundingBox2D.Merge(shrunk, inner)));

            // Add more chunks across negative and distant positive space for multi-chunk early-out and unload
            ChunkedEntryHandle2D hFar0 = index.Add(new BoundingBox2D(new Vector2(40f, 5f), new Vector2(45f, 15f)), 200);
            ChunkedEntryHandle2D hFar1 = index.Add(new BoundingBox2D(new Vector2(70f, 5f), new Vector2(75f, 15f)), 201);
            ChunkedEntryHandle2D hNeg = index.Add(new BoundingBox2D(new Vector2(-50f, -50f), new Vector2(-40f, -40f)), 300);
            index.CommitDirty();
            Assert.That(index.DirtyChunkCount, Is.EqualTo(0));
            Assert.That(index.ChunkCount, Is.EqualTo(4));

            StopAfterVisitor stopTwo = new(limit: 2);
            index.VisitOverlapAabb(new BoundingBox2D(new Vector2(-100f, -100f), new Vector2(100f, 100f)), ref stopTwo);
            Assert.That(stopTwo.Visits, Is.EqualTo(2), "early-out across chunks must stop immediately after limit");

            StopAfterVisitor stopOneRay = new(limit: 1);
            index.VisitRayAll(new Vector2(0f, 10f), new Vector2(100f, 0f), 1f, ref stopOneRay);
            Assert.That(stopOneRay.Visits, Is.EqualTo(1));

            // Unload chunk (0, 0) removes its 2 entries and invalidates handles hWide & hInner
            List<int> unloadedPayloads = new();
            int removed = index.UnloadChunk(new int2(0, 0), p => unloadedPayloads.Add(p));
            Assert.That(removed, Is.EqualTo(2));
            CollectionAssert.AreEquivalent(new[] { 100, 101 }, unloadedPayloads);
            Assert.That(index.ContainsChunk(new int2(0, 0)), Is.False);
            Assert.That(index.Remove(hWide), Is.False);
            Assert.That(index.UpdateBounds(hInner, shrunk), Is.False);
            Assert.That(index.TryGetPayload(hInner, out _), Is.False);

            // Reusing slot after unload does not alias the stale handle hWide
            ChunkedEntryHandle2D replacement = index.Add(new BoundingBox2D(new Vector2(1f, 1f), new Vector2(2f, 2f)), 999);
            Assert.That(index.Remove(hWide), Is.False);
            Assert.That(index.TryGetPayload(replacement, out int gotPayload), Is.True);
            Assert.That(gotPayload, Is.EqualTo(999));

            Assert.That(index.TryGetPayload(hFar0, out _), Is.True);
            Assert.That(index.TryGetPayload(hFar1, out _), Is.True);
            Assert.That(index.TryGetPayload(hNeg, out _), Is.True);
        }
    }
}
