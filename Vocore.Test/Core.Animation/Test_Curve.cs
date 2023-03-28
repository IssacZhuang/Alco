using System;
using System.Collections.Generic;

using System.Diagnostics;

namespace Vocore.Test
{
    public class Test_Curve
    {
        [Test("Test linear vs hermite vs cache performance")]
        public void Test_CurvePerformance()
        {
            int pointCount = 10000;
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
            TestUtility.PrintBlue("Linear: " + sw.ElapsedMilliseconds);

            sw.Restart();
            for (int i = 0; i < pointCount; i++)
            {
                curveHermite.Evaluate(i);
            }
            sw.Stop();
            TestUtility.PrintBlue("Hermite: " + sw.ElapsedMilliseconds);

            sw.Restart();

            for (int i = 0; i < pointCount; i++)
            {
                curveCache.Evaluate(i);
            }
            sw.Stop();
            TestUtility.PrintBlue("Cache: " + sw.ElapsedMilliseconds);
        }

        [Test("Draw curve")]
        public void Test_DrawCurve()
        {
            //create a cubic spline curve
            float[] x = { 0, 8, 16 };
            float[] y = { 0, 16, 0 };
            var curve = new CurveHermite(x, y);
            var curveCached = new CurveCache(curve,1);
            CurveDrawer.Draw(curveCached);
        }
    }
}

