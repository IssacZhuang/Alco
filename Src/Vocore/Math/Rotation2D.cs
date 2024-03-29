using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public struct Rotation2D:IEquatable<Rotation2D>
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

        public static Rotation2D FromDegree(float degree)
        {
            return new Rotation2D(math.radians(degree));
        }

        // overload operator
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Rotation2D a, Rotation2D b)
        {
            return a.s == b.s && a.c == b.c;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Rotation2D a, Rotation2D b)
        {
            return a.s != b.s || a.c != b.c;
        }

        

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Rotation2D other)
        {
            return this == other;
        }

        public override bool Equals(object? obj)
        {
            return obj is Rotation2D d && this == d;
        }

        public override int GetHashCode()
        {
            return s.GetHashCode() ^ c.GetHashCode();
        }
    }
}