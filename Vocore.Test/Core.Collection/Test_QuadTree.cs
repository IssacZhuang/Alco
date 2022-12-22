using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Vocore.Test.Core.Collection
{
    internal class Test_QuadTree
    {
        [Test("QuadTree RangeQuery")]
        public void RangeQuery()
        {
            // Create a QuadTree with a bound of (-10, -10) to (10, 10)
            QuadTree<object> quadTree = new QuadTree<object>(new Bound2 { center = Vector2.zero, size = new Vector2(20, 20) });

            // Generate 10000 random points within the bound of the QuadTree
            List<Vector2> points = new List<Vector2>();
            System.Random rand = new System.Random(123);
            for (int i = 0; i < 500000; i++)
            {
                points.Add(new Vector2((float)rand.NextDouble() * 20 - 10, (float)rand.NextDouble() * 20 - 10));
            }

            Stopwatch timer = new Stopwatch();
            timer.Start();

            // Add the points to the QuadTree
            foreach (var point in points)
            {
                quadTree.Add(point, new object());
            }

            timer.Stop();
            TestUtility.PrintBlue(TestUtility.TEXT_TIME_COST + ": quad tree build |" + timer.ElapsedMilliseconds);
            int results = 0;
            timer.Restart();
            // Query the QuadTree for points within a radius of 5

            results = quadTree.RangeQuery(Vector2.zero, 5).Count();
            

            timer.Stop();
            TestUtility.PrintBlue(TestUtility.TEXT_TIME_COST + ": quad tree query |" + timer.ElapsedMilliseconds);

            TestUtility.Assert(results != points.Count(p => (p - Vector2.zero).magnitude <= 5), "failed queried: " + results, "success queried: " + results);
        }
    }
}
