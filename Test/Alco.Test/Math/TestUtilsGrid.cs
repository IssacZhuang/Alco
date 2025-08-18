using System;
using System.Collections.Generic;
using System.Numerics;
using Alco;

namespace Alco.Test
{
    [TestFixture]
    public class TestUtilsGrid
    {
        [Test]
        public void Bresenham_IncludesEndpoints_And_CountIsMaxDxDyPlusOne()
        {
            // Horizontal
            var list = new List<int2>();
            UtilsGrid.GetBresenhamLine(list, new int2(2, 5), new int2(7, 5));
            Assert.That(list[0], Is.EqualTo(new int2(2, 5)));
            Assert.That(list[^1], Is.EqualTo(new int2(7, 5)));
            Assert.That(list.Count, Is.EqualTo(Math.Max(Math.Abs(7 - 2), Math.Abs(5 - 5)) + 1));

            // Vertical
            list.Clear();
            UtilsGrid.GetBresenhamLine(list, new int2(-3, -1), new int2(-3, 4));
            Assert.That(list[0], Is.EqualTo(new int2(-3, -1)));
            Assert.That(list[^1], Is.EqualTo(new int2(-3, 4)));
            Assert.That(list.Count, Is.EqualTo(Math.Max(Math.Abs(0), Math.Abs(4 - (-1))) + 1));

            // Diagonal 45°
            list.Clear();
            UtilsGrid.GetBresenhamLine(list, new int2(0, 0), new int2(5, 5));
            Assert.That(list[0], Is.EqualTo(new int2(0, 0)));
            Assert.That(list[^1], Is.EqualTo(new int2(5, 5)));
            Assert.That(list.Count, Is.EqualTo(6));
        }

        [Test]
        public void Bresenham_SpanMatchesList_And_RespectsCapacity()
        {
            var list = new List<int2>();
            var start = new int2(0, 0);
            var end = new int2(7, 3);
            UtilsGrid.GetBresenhamLine(list, start, end);

            Span<int2> buf = stackalloc int2[64];
            int written = UtilsGrid.GetBresenhamLine(buf, start, end);
            Assert.That(written, Is.EqualTo(list.Count));
            for (int i = 0; i < written; i++)
            {
                Assert.That(buf[i], Is.EqualTo(list[i]));
            }

            // Capacity limit: ensure no overflow and truncation occurs
            Span<int2> small = stackalloc int2[3];
            int truncated = UtilsGrid.GetBresenhamLine(small, start, end);
            Assert.That(truncated, Is.EqualTo(small.Length));
        }

        [Test]
        public void Supercover_BasicShapes_EndpointsAndCounts()
        {
            var list = new List<int2>();

            // Horizontal fractional
            Vector2 s = new Vector2(0.2f, 1.7f);
            Vector2 e = new Vector2(4.9f, 1.7f);
            UtilsGrid.GetSupercoverLine(list, s, e);
            Assert.That(list[0], Is.EqualTo(new int2(0, 1)));
            Assert.That(list[^1], Is.EqualTo(new int2(4, 1)));
            Assert.That(list.Count, Is.EqualTo(Math.Abs(4 - 0) + 1));

            // Vertical fractional
            s = new Vector2(-2.4f, -1.1f);
            e = new Vector2(-2.4f, 3.9f);
            list.Clear();
            UtilsGrid.GetSupercoverLine(list, s, e);
            Assert.That(list[0], Is.EqualTo(new int2(-3, -2)));
            Assert.That(list[^1], Is.EqualTo(new int2(-3, 3)));
            Assert.That(list.Count, Is.EqualTo(Math.Abs(3 - (-2)) + 1));

            // Diagonal fractional (slope 1)
            s = new Vector2(0.2f, 0.2f);
            e = new Vector2(3.8f, 3.8f);
            list.Clear();
            UtilsGrid.GetSupercoverLine(list, s, e);
            Assert.That(list[0], Is.EqualTo(new int2(0, 0)));
            Assert.That(list[^1], Is.EqualTo(new int2(3, 3)));
            Assert.That(list.Count, Is.EqualTo(Math.Abs(3 - 0) + 1));
        }

        [Test]
        public void Supercover_SpanMatchesList_And_RespectsCapacity()
        {
            Vector2 start = new Vector2(0.3f, 0.7f);
            Vector2 end = new Vector2(6.9f, 2.1f);
            var list = new List<int2>();
            UtilsGrid.GetSupercoverLine(list, start, end);

            Span<int2> buf = stackalloc int2[64];
            int written = UtilsGrid.GetSupercoverLine(buf, start, end);
            Assert.That(written, Is.EqualTo(list.Count));
            for (int i = 0; i < written; i++)
            {
                Assert.That(buf[i], Is.EqualTo(list[i]));
            }

            Span<int2> small = stackalloc int2[2];
            int truncated = UtilsGrid.GetSupercoverLine(small, start, end);
            Assert.That(truncated, Is.EqualTo(small.Length));
        }
    }
}

