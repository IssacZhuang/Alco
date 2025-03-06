using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct Rotation2D:IEquatable<Rotation2D>
    {
        /// <summary>
        /// Sin
        /// </summary>
        public float S;
        /// <summary>
        /// Cos
        /// </summary>
        public float C;

        public static Rotation2D Identity => new Rotation2D(0, 1);

        public Rotation2D(float radian)
        {
            math.sincos(radian, out S, out C);
        }

        public Rotation2D(float sin, float cos)
        {
            S = sin;
            C = cos;
        }

        public static Rotation2D FromDegree(float degree)
        {
            return new Rotation2D(math.radians(degree));
        }

        // overload operator
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Rotation2D a, Rotation2D b)
        {
            return a.S == b.S && a.C == b.C;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Rotation2D a, Rotation2D b)
        {
            return a.S != b.S || a.C != b.C;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D operator *(Rotation2D q, Rotation2D r)
        {
            return new Rotation2D(q.C * r.S + q.S * r.C, q.C * r.C - q.S * r.S);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D operator /(Rotation2D a, Rotation2D b)
        {
            return a * new Rotation2D(-b.S, b.C);//mutiply inverse b
        }

        public static Rotation2D Lerp(Rotation2D a, Rotation2D b, float t)
        {
            return new Rotation2D(math.lerp(a.S, b.S, t), math.lerp(a.C, b.C, t));
        }

        public static Rotation2D Slerp(Rotation2D a, Rotation2D b, float t)
        {
            float angle = math.acos(a.C * b.C + a.S * b.S);

            if (angle == 0)
            {
                return a;
            }

            float sinAngle = math.sin(angle);
            float weightA = math.sin((1 - t) * angle) / sinAngle;
            float weightB = math.sin(t * angle) / sinAngle;

            Rotation2D result;
            result.S = weightA * a.S + weightB * b.S;
            result.C = weightA * a.C + weightB * b.C;

            float length = math.sqrt(result.S * result.S + result.C * result.C);
            result.S /= length;
            result.C /= length;

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
            return S.GetHashCode() ^ C.GetHashCode();
        }

        public override string ToString()
        {
            return $"<{S}, {C}>";
        }
    }
}