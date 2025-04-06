using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        public const double DegToRad_Dbl = Math.PI / 180;
        public const float DegToRad = (float)DegToRad_Dbl;
        public const double RadToDeg_Dbl = 180 / Math.PI;
        public const float RadToDeg = (float)RadToDeg_Dbl;

        public const double PI_Dbl = Math.PI;
        public const float PI = (float)PI_Dbl;
    }
}