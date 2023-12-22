using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public struct Rotation2D
    {
        /// <summary>
        /// Sin
        /// </summary>
        public float s;
        /// <summary>
        /// Cos
        /// </summary>
        public float c;

        public static Rotation2D Identity => new Rotation2D(0, 1);

        public Rotation2D(float radian)
        {
            math.sincos(radian, out s, out c);
        }

        public Rotation2D(float sin, float cos)
        {
            s = sin;
            c = cos;
        }

        public static Rotation2D CreateByDegree(float degree)
        {
            return new Rotation2D(math.radians(degree));
        }

    }
}