using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore.Animation
{
    public class CurveCubicSpline: ICurve
    {
        private float[] _x; // 插值点的x坐标
        private float[] _y; // 插值点的y坐标
        private float[] _a; // S(x) = a[i]*(x[i+1]-x)^3 + b[i]*(x-x[i])^3 + c[i]*(x[i+1]-x) + d[i]*(x-x[i])
        private float[] _b; // i=0,...,n-1
        private float[] _c;
        private float[] _d;

        public CurveCubicSpline(float[] x, float[] y)
        {
            int n = x.Length - 1;
            _x = x;
            _y = y;

            _a = new float[n + 1];
            _b = new float[n + 1];
            _c = new float[n + 1];
            _d = new float[n + 1];

            float[] h = new float[n];
            float[] alpha = new float[n];
            float[] l = new float[n + 1];
            float[] mu = new float[n + 1];
            float[] z = new float[n + 1];

            for (int i = 0; i < n; i++)
            {
                h[i] = x[i + 1] - x[i];
                alpha[i] = 3 * (y[i + 1] - y[i]) / h[i];
            }

            l[0] = 1;
            mu[0] = z[0] = 0;

            for (int i = 1; i < n; i++)
            {
                l[i] = 2 * (x[i + 1] - x[i - 1]) - h[i - 1] * mu[i - 1];
                mu[i] = h[i] / l[i];
                z[i] = (alpha[i - 1] - h[i - 1] * z[i - 1]) / l[i];
            }

            l[n] = 1;
            z[n] = _c[n] = 0;

            for (int j = n - 1; j >= 0; j--)
            {
                _c[j] = z[j] - mu[j] * _c[j + 1];
                _b[j] = (y[j + 1] - y[j]) / h[j] - h[j] * (_c[j + 1] + 2 * _c[j]) / 3;
                _d[j] = (_c[j + 1] - _c[j]) / (3 * h[j]);
                _a[j] = y[j];
            }
        }

        public float Evaluate(float xx)
        {
            int j = BinarySearch(_x, xx);

            if (j < 0)
            {
                j = ~j;

                if (j == 0)
                    j = 1;
                else if (j == _x.Length)
                    j = _x.Length - 1;
            }

            float dx = xx - _x[j - 1];

            return _a[j - 1] + _b[j - 1] * dx + _c[j - 1] * dx * dx + _d[j - 1] * dx * dx * dx;
        }

        private static int BinarySearch(float[] a, float x)
        {
            int lo = 0, hi = a.Length - 1;

            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;

                if (x < a[mid])
                    hi = mid - 1;
                else if (x > a[mid])
                    lo = mid + 1;
                else
                    return mid;
            }

            return ~lo;
        }
    }
}
