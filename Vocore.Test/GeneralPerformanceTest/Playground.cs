using Vocore.Animation;

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
            CurveDrawer.Draw(curve, 0f, 16);
        }
    }

}

