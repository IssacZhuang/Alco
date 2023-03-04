using System;
using Vocore;

namespace Vocore.Test
{
    public class Test_Bezier
    {
        [Test("test bezier curve")]
        public void Test_BezierCurve()
        {
            float mX1 = 0f;
            float mY1 = 1f;
            float mX2 = 0f;
            float mY2 = 1f;

            var bizerCurveFunc = CurveUtility.GenerateBizerCurve(mX1, mY1, mX2, mY2);

            // Check if function returns a value between 0 and 1 for x in the range [0, 1]
            for (float x = 0; x <= 1; x += 0.09f)
            {
                float result = bizerCurveFunc(x);
                Console.WriteLine("x: {0}, y: {1}", x, result);
            }
            float result2 = bizerCurveFunc(1);
            Console.WriteLine("x: {0}, y: {1}", 1, result2);
        }
    }

}

