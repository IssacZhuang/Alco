using System;
using System.Collections.Generic;

using System.Diagnostics;

namespace Alco.Test
{
    public class TestCurve
    {
        [Test(Description = "linear vs hermite vs cache performance")]
        public void TestCurvePerformance()
        {
            int pointCount = 1000000;
            float[] t = new float[pointCount];
            float[] value = new float[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                t[i] = i;
                value[i] = i;
            }

            var curveLinear = new CurveLinear(t, value);
            var curveHermite = new CurveHermite(t, value);
            var curveCache = new CurveCache(curveHermite);

            Stopwatch sw = new Stopwatch();
            sw.Start();
            for (int i = 0; i < pointCount; i++)
            {
                curveLinear.Evaluate(i);
            }
            sw.Stop();
            TestContext.WriteLine("Linear: " + sw.ElapsedMilliseconds);

            sw.Restart();
            for (int i = 0; i < pointCount; i++)
            {
                curveHermite.Evaluate(i);
            }
            sw.Stop();
            TestContext.WriteLine("Hermite: " + sw.ElapsedMilliseconds);

            sw.Restart();
            for (int i = 0; i < pointCount; i++)
            {
                curveCache.Evaluate(i);
            }
            sw.Stop();
            TestContext.WriteLine("Cache: " + sw.ElapsedMilliseconds);
        }

    }
}

