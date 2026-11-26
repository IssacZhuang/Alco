using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alco;

namespace Alco.Test
{
    /// <summary>Tests <see cref="ChunkTable{TChunk}"/> and <see cref="ChunkReadCursor{TChunk}"/>.</summary>
    [TestFixture]
    public class TestChunkTable
    {
        private sealed class DummyChunk(int2 coord, int value)
        {
            public readonly int2 Coord = coord;
            public readonly int Value = value;
        }

        private static uint ComputeSlotMask(int2 coord, int capacity)
        {
            ulong z = ((ulong)(uint)coord.X << 32) | (uint)coord.Y;
            z ^= z >> 30;
            z *= 0xbf58476d1ce4e5b9UL;
            z ^= z >> 27;
            z *= 0x94d049bb133111ebUL;
            z ^= z >> 31;
            return (uint)z & (uint)(capacity - 1);
        }

        private static List<int2> FindCollidingCoords(int count, int capacity, uint targetBucket = 0)
        {
            List<int2> result = new(count);
            for (int y = -500; y < 500 && result.Count < count; y++)
            {
                for (int x = -500; x < 500 && result.Count < count; x++)
                {
                    int2 coord = new(x, y);
                    if (ComputeSlotMask(coord, capacity) == targetBucket)
                    {
                        result.Add(coord);
                    }
                }
            }

            return result;
        }

        /// <summary>Verifies CRUD operations on positive, negative, and extreme coordinates.</summary>
        [Test]
        public void Crud_HandlesSignedAndExtremeCoordinates()
        {
            ChunkTable<DummyChunk> table = new();
            int2[] coords =
            [
                new(0, 0),
                new(-1, -1),
                new(-1024, 2048),
                new(int.MinValue, int.MaxValue),
                new(int.MaxValue, int.MinValue),
                new(int.MinValue, int.MinValue),
            ];

            for (int i = 0; i < coords.Length; i++)
            {
                DummyChunk chunk = new(coords[i], i + 10);
                Assert.That(table.TryAdd(coords[i], chunk), Is.True);
                Assert.That(table.TryAdd(coords[i], chunk), Is.False);
            }

            Assert.That(table.Count, Is.EqualTo(coords.Length));

            for (int i = 0; i < coords.Length; i++)
            {
                Assert.That(table.TryGet(coords[i], out DummyChunk? got), Is.True);
                Assert.That(got!.Value, Is.EqualTo(i + 10));
            }

            DummyChunk replacement = new(coords[3], 999);
            table.Set(coords[3], replacement);
            Assert.That(table.TryGet(coords[3], out DummyChunk? updated), Is.True);
            Assert.That(updated!.Value, Is.EqualTo(999));
            Assert.That(table.Count, Is.EqualTo(coords.Length));

            int factoryCalls = 0;
            DummyChunk existing = table.GetOrAdd(coords[3], c =>
            {
                factoryCalls++;
                return new DummyChunk(c, -1);
            });
            Assert.That(factoryCalls, Is.EqualTo(0));
            Assert.That(existing, Is.SameAs(replacement));

            Assert.That(table.TryRemove(coords[1], out DummyChunk? removed), Is.True);
            Assert.That(removed!.Coord, Is.EqualTo(coords[1]));
            Assert.That(table.Contains(coords[1]), Is.False);
            Assert.That(table.TryRemove(coords[1], out _), Is.False);
            Assert.That(table.Count, Is.EqualTo(coords.Length - 1));

            table.Clear();
            Assert.That(table.Count, Is.EqualTo(0));
            Assert.That(table.Contains(coords[0]), Is.False);
        }

        /// <summary>
        /// Verifies radix overflow fallback, tombstone invariance (no duplicate entries on Set/TryAdd/GetOrAdd),
        /// and clean removal from overflow.
        /// </summary>
        [Test]
        public void OverflowAndTombstones_PreserveUniquenessAndLookupBounds()
        {
            ChunkTable<DummyChunk> table = new(64);
            // 20 colliding keys < growth threshold (64 * 5 / 8 = 40), so capacity stays 64 and 12 overflow.
            List<int2> keys = FindCollidingCoords(20, 64, targetBucket: 7);
            Assert.That(keys.Count, Is.EqualTo(20));

            for (int i = 0; i < keys.Count; i++)
            {
                Assert.That(table.TryAdd(keys[i], new DummyChunk(keys[i], i)), Is.True);
            }

            Assert.That(table.Capacity, Is.EqualTo(64));
            Assert.That(table.OverflowCount, Is.EqualTo(12));

            // Create tombstones in the main 8-slot probe window while keys[8..19] remain in OverflowRoot.
            Assert.That(table.TryRemove(keys[0], out _), Is.True);
            Assert.That(table.TryRemove(keys[3], out _), Is.True);
            Assert.That(table.Count, Is.EqualTo(18));

            // TryAdd on an overflow key must fail; Set must replace without creating a duplicate in the tombstone slot.
            Assert.That(table.TryAdd(keys[15], new DummyChunk(keys[15], 500)), Is.False);
            DummyChunk updatedOverflow = new(keys[15], 777);
            table.Set(keys[15], updatedOverflow);
            Assert.That(table.Count, Is.EqualTo(18));

            int factoryCount = 0;
            DummyChunk gotOverflow = table.GetOrAdd(keys[15], c =>
            {
                factoryCount++;
                return new DummyChunk(c, -1);
            });
            Assert.That(factoryCount, Is.EqualTo(0));
            Assert.That(gotOverflow, Is.SameAs(updatedOverflow));

            // Enumerator must visit each live key exactly once.
            Dictionary<int2, int> enumerated = new();
            foreach (KeyValuePair<int2, DummyChunk> kvp in table)
            {
                Assert.That(enumerated.TryAdd(kvp.Key, kvp.Value.Value), Is.True, $"Duplicate key {kvp.Key}");
            }
            Assert.That(enumerated.Count, Is.EqualTo(18));
            Assert.That(enumerated[keys[15]], Is.EqualTo(777));

            // Remove all remaining keys (including all overflow entries) and verify overflow drains to 0.
            for (int i = 0; i < keys.Count; i++)
            {
                bool expectedRemoved = i != 0 && i != 3;
                Assert.That(table.TryRemove(keys[i], out _), Is.EqualTo(expectedRemoved));
            }
            Assert.That(table.Count, Is.EqualTo(0));
            Assert.That(table.OverflowCount, Is.EqualTo(0));
        }

        /// <summary>Verifies table growth, tombstone compaction, and shrink retain all live entries.</summary>
        [Test]
        public void GrowthAndShrink_RetainAllLiveEntries()
        {
            ChunkTable<DummyChunk> table = new(64);
            Dictionary<int2, DummyChunk> expected = new();

            for (int i = 0; i < 300; i++)
            {
                int2 coord = new(i * 3 - 150, i * 5 - 300);
                DummyChunk chunk = new(coord, i);
                table.Set(coord, chunk);
                expected[coord] = chunk;
            }

            Assert.That(table.Capacity, Is.GreaterThanOrEqualTo(512));
            Assert.That(table.Count, Is.EqualTo(expected.Count));

            // Remove most entries to trigger shrink back to MinCapacity (64).
            int idx = 0;
            List<int2> toRemove = new();
            foreach (int2 key in expected.Keys)
            {
                if (idx++ < 290)
                {
                    toRemove.Add(key);
                }
            }

            foreach (int2 key in toRemove)
            {
                Assert.That(table.TryRemove(key, out _), Is.True);
                expected.Remove(key);
            }

            Assert.That(table.Capacity, Is.EqualTo(64));
            Assert.That(table.Count, Is.EqualTo(expected.Count));
            foreach (KeyValuePair<int2, DummyChunk> kvp in expected)
            {
                Assert.That(table.TryGet(kvp.Key, out DummyChunk? actual), Is.True);
                Assert.That(actual, Is.SameAs(kvp.Value));
            }
        }

        /// <summary>Verifies <see cref="ChunkReadCursor{TChunk}"/> caching semantics and enumerator termination.</summary>
        [Test]
        public void ReadCursorAndEnumerator_WorkAtBoundaries()
        {
            ChunkTable<DummyChunk> table = new();
            int2 minCoord = new(int.MinValue, int.MinValue);
            DummyChunk minChunk = new(minCoord, 42);
            table.Set(minCoord, minChunk);

            ChunkReadCursor<DummyChunk> cursor = table.CreateReadCursor();
            Assert.That(cursor.TryGet(minCoord, out DummyChunk? gotMin), Is.True);
            Assert.That(gotMin, Is.SameAs(minChunk));
            Assert.That(cursor.TryGet(minCoord, out DummyChunk? gotMinCached), Is.True);
            Assert.That(gotMinCached, Is.SameAs(minChunk));

            int2 missCoord = new(99, -99);
            Assert.That(cursor.TryGet(missCoord, out _), Is.False);
            Assert.That(cursor.TryGet(missCoord, out _), Is.False);

            DummyChunk newChunk = new(missCoord, 7);
            table.Set(missCoord, newChunk);
            cursor.Reset();
            Assert.That(cursor.TryGet(missCoord, out DummyChunk? gotAfterReset), Is.True);
            Assert.That(gotAfterReset, Is.SameAs(newChunk));

            ChunkTable<DummyChunk>.Enumerator enumerator = table.GetEnumerator();
            int count = 0;
            while (enumerator.MoveNext())
            {
                count++;
            }
            Assert.That(count, Is.EqualTo(2));
            Assert.That(enumerator.MoveNext(), Is.False);
            Assert.That(enumerator.MoveNext(), Is.False);
        }

        /// <summary>
        /// Exercises single-writer mutations (including forced collisions, rehash, and shrink) concurrently with
        /// multiple lock-free reader threads.
        /// </summary>
        [Test]
        public void ConcurrentSwmr_ReadsNeverTearOrFailDuringRehashAndOverflow()
        {
            ChunkTable<DummyChunk> table = new(64);

            // Pinned keys remain present throughout the entire test (including colliding keys).
            List<int2> pinned = FindCollidingCoords(12, 64, targetBucket: 19);
            for (int i = 0; i < pinned.Count; i++)
            {
                table.Set(pinned[i], new DummyChunk(pinned[i], i + 1000));
            }

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(1.5));
            Exception? readerFailure = null;

            Task[] readers = new Task[4];
            for (int r = 0; r < readers.Length; r++)
            {
                int readerId = r;
                readers[r] = Task.Run(() =>
                {
                    try
                    {
                        ChunkReadCursor<DummyChunk> cursor = table.CreateReadCursor();
                        int step = 0;
                        while (!cts.IsCancellationRequested)
                        {
                            int2 pin = pinned[step % pinned.Count];
                            if (!table.TryGet(pin, out DummyChunk? pinnedChunk) || pinnedChunk.Coord != pin)
                            {
                                Interlocked.CompareExchange(ref readerFailure,
                                    new InvalidOperationException($"Pinned key missing or mismatched: {pin}"), null);
                                return;
                            }

                            int2 churn = new((step + readerId * 17) % 256 - 128, (step * 3) % 64 - 32);
                            if (cursor.TryGet(churn, out DummyChunk? churnChunk) && churnChunk.Coord != churn)
                            {
                                Interlocked.CompareExchange(ref readerFailure,
                                    new InvalidOperationException($"Torn read for {churn}: got {churnChunk.Coord}"), null);
                                return;
                            }

                            if ((step & 7) == 0)
                            {
                                cursor.Reset();
                            }

                            step++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.CompareExchange(ref readerFailure, ex, null);
                    }
                });
            }

            Task writer = Task.Run(() =>
            {
                int iter = 0;
                while (!cts.IsCancellationRequested)
                {
                    for (int i = 0; i < 200; i++)
                    {
                        int2 c = new(i - 100, (iter + i) % 32 - 16);
                        table.Set(c, new DummyChunk(c, i));
                    }

                    for (int i = 0; i < 200; i++)
                    {
                        int2 c = new(i - 100, (iter + i) % 32 - 16);
                        table.TryRemove(c, out _);
                    }

                    iter++;
                }
            });

            Task.WaitAll([writer, .. readers]);
            Assert.That(readerFailure, Is.Null);
        }
    }
}
