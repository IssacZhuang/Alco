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
            float[] x = { 0, 1, 2 };
            float[] y = { 0, 1, 0 };
            var curve = new CurveLinear(x, y);
            CurveDrawer.Draw(curve, 0f, 2);
        }
    }

}

