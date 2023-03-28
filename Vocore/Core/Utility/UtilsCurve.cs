using System;

namespace Vocore
{
    public class UtilsCurve
    {
        const int NEWTON_ITERATIONS = 4;
        const float NEWTON_MIN_SLOPE = 0.001f;
        const float SUBDIVISION_PRECISION = 0.0000001f;
        const int SUBDIVISION_MAX_ITERATIONS = 10;

        const int kSplineTableSize = 11;
        const float kSampleStepSize = 1f / (kSplineTableSize - 1f);

        private static float A(float aA1, float aA2) { return 1f - 3f * aA2 + 3f * aA1; }
        private static float B(float aA1, float aA2) { return 3f * aA2 - 6f * aA1; }
        private static float C(float aA1) { return 3f * aA1; }

        private static float CalcBezier(float aT, float aA1, float aA2) { return ((A(aA1, aA2) * aT + B(aA1, aA2)) * aT + C(aA1)) * aT; }

        private static float GetSlope(float aT, float aA1, float aA2) { return 3f * A(aA1, aA2) * aT * aT + 2f * B(aA1, aA2) * aT + C(aA1); }

        private static float BinarySubdivide(float aX, float aA, float aB, float mX1, float mX2)
        {
            float currentX, currentT;
            int i = 0;
            do
            {
                currentT = aA + (aB - aA) / 2f;
                currentX = CalcBezier(currentT, mX1, mX2) - aX;
                if (currentX > 0f)
                {
                    aB = currentT;
                }
                else
                {
                    aA = currentT;
                }
                i++;
            } while (Math.Abs(currentX) > SUBDIVISION_PRECISION && i < SUBDIVISION_MAX_ITERATIONS);
            return currentT;
        }

        private static float NewtonRaphsonIterate(float aX, float aGuessT, float mX1, float mX2)
        {
            for (int i = 0; i < NEWTON_ITERATIONS; i++)
            {
                float currentSlope = GetSlope(aGuessT, mX1, mX2);
                if (currentSlope == 0f)
                {
                    return aGuessT;
                }
                float currentX = CalcBezier(aGuessT, mX1, mX2) - aX;
                aGuessT -= currentX / currentSlope;
            }
            return aGuessT;
        }

        private static float LinearEasing(float x)
        {
            return x;
        }

        /// <summary>
        /// Generate a Bizer curve function that is used for the t value of lerp.
        /// </summary>
        public static Func<float, float> GenerateBizerLerpCurve(float mX1, float mY1, float mX2, float mY2)
        {
            if (!(0 <= mX1 && mX1 <= 1 && 0 <= mX2 && mX2 <= 1))
            {
                throw new Exception("the value of the x must be in [0, 1]");
            }
            if (mX1 == mY1 && mX2 == mY2)
            {
                return LinearEasing;
            }

            float[] sampleValues = new float[kSplineTableSize];

            for (int i = 0; i < kSplineTableSize; i++)
            {
                sampleValues[i] = CalcBezier(i * kSampleStepSize, mX1, mX2);
            }

            float GetTForX(float aX)
            {
                float intervalStart = 0.0f;
                int currentSample = 1;
                int lastSample = kSplineTableSize;

                while (currentSample != lastSample && sampleValues[currentSample] <= aX)
                {
                    currentSample++;
                    intervalStart += kSampleStepSize;
                }
                currentSample--;

                float dist = (aX - sampleValues[currentSample]) / (sampleValues[currentSample + 1] - sampleValues[currentSample]);
                float guessForT = intervalStart + dist * kSampleStepSize;

                float initialSlope = GetSlope(guessForT, mX1, mX2);

                if (initialSlope >= NEWTON_MIN_SLOPE)
                {
                    return NewtonRaphsonIterate(aX, guessForT, mX1, mX2);
                }
                else if (initialSlope == 0.0f)
                {
                    return guessForT;
                }
                else
                {
                    return BinarySubdivide(aX, intervalStart, intervalStart + kSampleStepSize, mX1, mX2);
                }
            }

            return (float x) =>
            {
                if (x == 0 || x == 1)
                {
                    return x;
                }
                return CalcBezier(GetTForX(x), mY1, mY2);
            };
        }
    }
}


