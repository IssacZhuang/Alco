using System;
using System.Collections.Generic;
using System.Numerics;
using Alco;

namespace Alco.Test
{
    /// <summary>Tests <see cref="IntBounds"/>, <see cref="ChunkUtility"/>, and <see cref="ChunkSliceEnumerator"/>.</summary>
    [TestFixture]
    public class TestChunkUtility
    {
        /// <summary>Verifies <see cref="IntBounds"/> containment, chunk intersection, union, and conversions.</summary>
        [Test]
        public void IntBounds_OperationsAndChunkIntersection_WorkAcrossSignedCoordinates()
        {
            IntBounds empty = new(5, 5, 5, 8);
            Assert.That(empty.IsEmpty, Is.True);
            Assert.That(empty.Width, Is.EqualTo(0));
            Assert.That(empty.Height, Is.EqualTo(3));
            Assert.That(empty.IntersectsChunk(int2.Zero), Is.False);
            Assert.That(empty.ToChunkBounds(), Is.EqualTo(default(IntBounds)));
            Assert.That(empty.ToRectInt(), Is.EqualTo(default(RectInt)));

            IntBounds bounds = new(-33, -1, 33, 1);
            Assert.That(bounds.IsEmpty, Is.False);
            Assert.That(bounds.Width, Is.EqualTo(66));
            Assert.That(bounds.Height, Is.EqualTo(2));
            Assert.That(bounds.Contains(-33, -1), Is.True);
            Assert.That(bounds.Contains(32, 0), Is.True);
            Assert.That(bounds.Contains(new int2(33, 0)), Is.False);
            Assert.That(bounds.Contains(new int2(0, 1)), Is.False);

            // Spans X chunks [-2, 1] => [-2, 2) and Y chunks [-1, 0] => [-1, 1)
            Assert.That(bounds.ToChunkBounds(), Is.EqualTo(new IntBounds(-2, -1, 2, 1)));
            Assert.That(bounds.IntersectsChunk(new int2(-2, -1)), Is.True);
            Assert.That(bounds.IntersectsChunk(new int2(1, 0)), Is.True);
            Assert.That(bounds.IntersectsChunk(new int2(2, 0)), Is.False);
            Assert.That(bounds.IntersectsChunk(new int2(0, 1)), Is.False);
            Assert.That(bounds.IntersectsChunk(new int2(-3, 0)), Is.False);

            IntBounds fromChunk = IntBounds.FromChunk(new int2(-1, 2));
            Assert.That(fromChunk, Is.EqualTo(new IntBounds(-32, 64, 0, 96)));

            IntBounds union = empty.Union(fromChunk).Union(new IntBounds(10, 10, 20, 20));
            Assert.That(union, Is.EqualTo(new IntBounds(-32, 10, 20, 96)));

            RectInt rect = bounds.ToRectInt();
            Assert.That(IntBounds.FromRectInt(rect), Is.EqualTo(bounds));
            Assert.That(bounds.ContainsBounds(new IntBounds(-33, -1, 0, 1)), Is.True);
            Assert.That(bounds.ContainsBounds(new IntBounds(-34, -1, 0, 1)), Is.False);
            Assert.That(bounds.ContainsBounds(empty), Is.True);
        }

        /// <summary>Verifies signed arithmetic chunk mapping and translation-invariant cell snapping.</summary>
        [Test]
        public void ChunkUtility_SignedMappingAndSnapping_AreTranslationInvariant()
        {
            Assert.That(ChunkUtility.CellToChunk(-33), Is.EqualTo(-2));
            Assert.That(ChunkUtility.CellToChunk(-32), Is.EqualTo(-1));
            Assert.That(ChunkUtility.CellToChunk(-1), Is.EqualTo(-1));
            Assert.That(ChunkUtility.CellToChunk(0), Is.EqualTo(0));
            Assert.That(ChunkUtility.CellToChunk(31), Is.EqualTo(0));
            Assert.That(ChunkUtility.CellToChunk(32), Is.EqualTo(1));

            Assert.That(ChunkUtility.CellToLocal(-33), Is.EqualTo(31));
            Assert.That(ChunkUtility.CellToLocal(-32), Is.EqualTo(0));
            Assert.That(ChunkUtility.CellToLocal(-1), Is.EqualTo(31));
            Assert.That(ChunkUtility.CellToLocal(0), Is.EqualTo(0));
            Assert.That(ChunkUtility.CellToLocal(32), Is.EqualTo(0));

            Assert.That(ChunkUtility.ChunkOrigin(new int2(-1, -2)), Is.EqualTo(new int2(-32, -64)));

            // Translation invariance on half-integer tie boundaries: world + 1 shifts cell by +1 everywhere.
            for (int i = -10; i <= 10; i++)
            {
                float boundary = i - 0.5f;
                Assert.That(ChunkUtility.WorldToCell(boundary), Is.EqualTo(i));
                Assert.That(ChunkUtility.CalculateBaseCell(new Vector2(boundary, boundary), int2.One), Is.EqualTo(new int2(i, i)));
            }

            // 2x2 centered at (0.5, 0.5) covers [0, 2) x [0, 2); shifted by -1 to (-0.5, -0.5) covers [-1, 1) x [-1, 1)
            Assert.That(ChunkUtility.CalculateBaseCell(new Vector2(0.5f, 0.5f), new int2(2, 2)), Is.EqualTo(new int2(0, 0)));
            Assert.That(ChunkUtility.CalculateBaseCell(new Vector2(-0.5f, -0.5f), new int2(2, 2)), Is.EqualTo(new int2(-1, -1)));
            Assert.That(ChunkUtility.CalculateBaseCell(new Vector2(-1.5f, -1.5f), new int2(2, 2)), Is.EqualTo(new int2(-2, -2)));
        }

        /// <summary>Verifies <see cref="ChunkSliceEnumerator"/> yields an exact partition of arbitrary rectangles across chunk seams.</summary>
        [TestCase(5, 5, 6, 6)]
        [TestCase(0, 0, 32, 32)]
        [TestCase(-1, -1, 1, 1)]
        [TestCase(-33, -65, 34, 65)]
        [TestCase(10, 10, 10, 12)]
        public void EnumerateSlices_CoversExactCellSetOnce(int minX, int minY, int maxX, int maxY)
        {
            IntBounds bounds = new(minX, minY, maxX, maxY);
            HashSet<int2> visited = new();

            foreach (ChunkCellSlice slice in ChunkUtility.EnumerateSlices(bounds))
            {
                Assert.That(slice.LocalMinX, Is.GreaterThanOrEqualTo(0));
                Assert.That(slice.LocalMinY, Is.GreaterThanOrEqualTo(0));
                Assert.That(slice.LocalMaxX, Is.LessThanOrEqualTo(ChunkUtility.DefaultChunkSize));
                Assert.That(slice.LocalMaxY, Is.LessThanOrEqualTo(ChunkUtility.DefaultChunkSize));
                Assert.That(slice.LocalMinX, Is.LessThan(slice.LocalMaxX));
                Assert.That(slice.LocalMinY, Is.LessThan(slice.LocalMaxY));

                for (int ly = slice.LocalMinY; ly < slice.LocalMaxY; ly++)
                {
                    for (int lx = slice.LocalMinX; lx < slice.LocalMaxX; lx++)
                    {
                        int2 world = new(slice.WorldOriginX + lx, slice.WorldOriginY + ly);
                        Assert.That(bounds.Contains(world), Is.True, $"Cell {world} should be inside {bounds}");
                        Assert.That(visited.Add(world), Is.True, $"Cell {world} visited more than once");
                    }
                }
            }

            Assert.That(visited.Count, Is.EqualTo(bounds.Width * bounds.Height));
        }
    }
}
