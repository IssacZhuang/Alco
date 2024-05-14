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
        public static Rotation2D operator *(Rotation2D q, Rotation2D r)
        {
            return new Rotation2D(q.c * r.s + q.s * r.c, q.c * r.c - q.s * r.s);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D operator /(Rotation2D a, Rotation2D b)
        {
            return a * new Rotation2D(-b.s, b.c);//mutiply inverse b
        }

        public static Rotation2D Lerp(Rotation2D a, Rotation2D b, float t)
        {
            return new Rotation2D(math.lerp(a.s, b.s, t), math.lerp(a.c, b.c, t));
        }

        public static Rotation2D Slerp(Rotation2D a, Rotation2D b, float t)
        {
            float angle = math.acos(a.c * b.c + a.s * b.s);

            if (angle == 0)
            {
                return a;
            }

            float sinAngle = math.sin(angle);
            float weightA = math.sin((1 - t) * angle) / sinAngle;
            float weightB = math.sin(t * angle) / sinAngle;

            Rotation2D result;
            result.s = weightA * a.s + weightB * b.s;
            result.c = weightA * a.c + weightB * b.c;

            float length = math.sqrt(result.s * result.s + result.c * result.c);
            result.s /= length;
            result.c /= length;

            return result;
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

        public override string ToString()
        {
            return $"<{s}, {c}>";
        }
    }
}