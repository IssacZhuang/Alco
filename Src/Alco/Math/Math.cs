using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        public const double TO_RADIANS_DBL = Math.PI / 180;
        public const float TO_RADIANS = (float)TO_RADIANS_DBL;
        public const double TO_DEGREES_DBL = 180 / Math.PI;
        public const float TO_DEGREES = (float)TO_DEGREES_DBL;

        public const double PI_DBL = Math.PI;
        public const float PI = (float)PI_DBL;
    }
}