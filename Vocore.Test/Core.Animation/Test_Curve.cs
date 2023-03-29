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
            int pointCount = 100000;
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
            float[] x = { -3, 8, 16 };
            float[] y = { 3, 16, 0 };
            var curveHermite = new CurveHermite(x, y);
            var curveLinear = new CurveLinear(x, y);
            var curveCached = new CurveCache(curveHermite,1);
            CurveDrawer.Draw(curveLinear, "Linear");
            CurveDrawer.Draw(curveHermite, "Hermite");
            CurveDrawer.Draw(curveCached, "Hermite Cache");
        }
    }
}

