using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vocore.Animation
{
    public class CurveCubicBezier: ICurve
    {
        const int NEWTON_ITERATIONS = 4;
        const float NEWTON_MIN_SLOPE = 0.001f;
        const float SUBDIVISION_PRECISION = 0.0000001f;
        const int SUBDIVISION_MAX_ITERATIONS = 10;

        const int K_SPLINE_TABLE_SIZE = 11;
        const float K_SAMPLE_STEP_SIZE = 1f / (K_SPLINE_TABLE_SIZE - 1f);

        private float _x1, _y1, _x2, _y2;
        private float[] _mSampleValues;

        private static float A(float aA1, float aA2) { return 1f - 3f * aA2 + 3f * aA1; }
        private static float B(float aA1, float aA2) { return 3f * aA2 - 6f * aA1; }
        private static float C(float aA1) { return 3f * aA1; }
         private static float CalcBezier(float aT, float aA1, float aA2) { return ((A(aA1, aA2) * aT + B(aA1, aA2)) * aT + C(aA1)) * aT; }
         private static float GetSlope(float aT, float aA1, float aA2) { return 3f * A(aA1, aA2) * aT * aT + 2f * B(aA1, aA2) * aT + C(aA1); }

        public CurveCubicBezier(float x1, float y1, float x2, float y2)
        {
            _x1 = x1;
            _y1 = y1;
            _x2 = x2;
            _y2 = y2;
            _mSampleValues = new float[K_SPLINE_TABLE_SIZE];
            if (x1 != y1 || x2 != y2)
            {
                for (int i = 0; i < K_SPLINE_TABLE_SIZE; ++i)
                {
                    _mSampleValues[i] = CalcBezier(i * K_SAMPLE_STEP_SIZE, _x1, _x2);
                }
            }
        }

        public int PointsCount
        {
            get
            {
                return 4;
            }
        }

        public IEnumerable<Vector2> Points
        {
            get
            {
                return new Vector2[] { new Vector2(0, 0), new Vector2(_x1, _y1), new Vector2(_x2, _y2), new Vector2(1, 1) };
            }
        }

        public float Evaluate(float x)
        {
            x = Mathf.Clamp01(x);
            if (_x1 == _y1 && _x2 == _y2)
            {
                return x;
            }
            if (x == 0f)
            {
                return 0f;
            }
            if (x == 1f)
            {
                return 1f;
            }
            return CalcBezier(GetTForX(x), _y1, _y2);
        }

        private float GetTForX(float aX)
        {
            float intervalStart = 0.0f;
            int currentSample = 1;
            int lastSample = K_SPLINE_TABLE_SIZE;

            while (currentSample != lastSample && _mSampleValues[currentSample] <= aX)
            {
                currentSample++;
                intervalStart += K_SAMPLE_STEP_SIZE;
            }
            currentSample--;

            float dist = (aX - _mSampleValues[currentSample]) / (_mSampleValues[currentSample + 1] - _mSampleValues[currentSample]);
            float guessForT = intervalStart + dist * K_SAMPLE_STEP_SIZE;

            float initialSlope = GetSlope(guessForT, _x1, _x2);

            if (initialSlope >= NEWTON_MIN_SLOPE)
            {
                return NewtonRaphsonIterate(aX, guessForT, _x1, _x2);
            }
            else if (initialSlope == 0.0f)
            {
                return guessForT;
            }
            else
            {
                return BinarySearch(aX, intervalStart, intervalStart + K_SAMPLE_STEP_SIZE, _x1, _x2);
            }
        }

        private static float BinarySearch(float aX, float aA, float aB, float mX1, float mX2)
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
    }
}

