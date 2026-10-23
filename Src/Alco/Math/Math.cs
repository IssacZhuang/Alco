using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        public const double DegToRad_Dbl = Math.PI / 180;
        /// <summary>
        /// The factor that converts degrees to radians.
        /// </summary>
        public const float DegToRad = (float)DegToRad_Dbl;
        public const double RadToDeg_Dbl = 180 / Math.PI;
        /// <summary>
        /// The factor that converts radians to degrees.
        /// </summary>
        public const float RadToDeg = (float)RadToDeg_Dbl;

        public const double PI_Dbl = Math.PI;
        /// <summary>
        /// The ratio of a circle's circumference to its diameter.
        /// </summary>
        public const float PI = (float)PI_Dbl;
    }
}