using Vocore;

namespace Vocore.Test
{
    public class Playground
    {
        //some temp code for testing
        [Test("Playground")]
        public void Test()
        {
            //create a cubic spline curve
            float[] x = { 0, 8, 16 };
            float[] y = { 0, 16, 0 };
            var curve = new CurveCubicHermite(x, y);
            TestUtility.PrintBlue(curve.Evaluate(18));
            var curveCached = new CurveCache(curve,1);
            CurveDrawer.Draw(curveCached);
        }
    }

}

