using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// Represents a 2D rotation using sine and cosine values.
    /// </summary>
    public struct Rotation2D:IEquatable<Rotation2D>
    {
        /// <summary>
        /// The sine component of the rotation.
        /// </summary>
        public float S;
        /// <summary>
        /// The cosine component of the rotation.
        /// </summary>
        public float C;

        /// <summary>
        /// Gets the identity rotation (no rotation).
        /// </summary>
        public static Rotation2D Identity => new Rotation2D(0, 1);

        /// <summary>
        /// Creates a rotation from an angle in radians.
        /// </summary>
        /// <param name="radian">The angle in radians.</param>
        public Rotation2D(float radian)
        {
            math.sincos(radian, out S, out C);
        }

        /// <summary>
        /// Creates a rotation from sine and cosine values.
        /// </summary>
        /// <param name="sin">The sine value.</param>
        /// <param name="cos">The cosine value.</param>
        public Rotation2D(float sin, float cos)
        {
            S = sin;
            C = cos;
        }

        /// <summary>
        /// Converts the rotation to an angle in radians.
        /// </summary>
        /// <returns>The angle in radians.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly float ToRadian()
        {
            return math.atan2(S, C);
        }

        /// <summary>
        /// Converts the rotation to an angle in degrees.
        /// </summary>
        /// <returns>The angle in degrees.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly float ToDegree()
        {
            return math.degrees(ToRadian());
        }

        /// <summary>
        /// Creates a rotation from an angle in degrees.
        /// </summary>
        /// <param name="degree">The angle in degrees.</param>
        /// <returns>A new Rotation2D instance.</returns>
        public static Rotation2D FromDegree(float degree)
        {
            return new Rotation2D(math.radians(degree));
        }

        /// <summary>
        /// Compares two Rotation2D instances for equality.
        /// </summary>
        /// <param name="a">The first rotation.</param>
        /// <param name="b">The second rotation.</param>
        /// <returns>True if the rotations are equal; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Rotation2D a, Rotation2D b)
        {
            return a.S == b.S && a.C == b.C;
        }

        /// <summary>
        /// Compares two Rotation2D instances for inequality.
        /// </summary>
        /// <param name="a">The first rotation.</param>
        /// <param name="b">The second rotation.</param>
        /// <returns>True if the rotations are not equal; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Rotation2D a, Rotation2D b)
        {
            return a.S != b.S || a.C != b.C;
        }

        /// <summary>
        /// Multiplies two rotations.
        /// </summary>
        /// <param name="q">The first rotation.</param>
        /// <param name="r">The second rotation.</param>
        /// <returns>The combined rotation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D operator *(Rotation2D q, Rotation2D r)
        {
            return new Rotation2D(q.C * r.S + q.S * r.C, q.C * r.C - q.S * r.S);
        }

        /// <summary>
        /// Divides one rotation by another.
        /// </summary>
        /// <param name="a">The dividend rotation.</param>
        /// <param name="b">The divisor rotation.</param>
        /// <returns>The result of the division.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D operator /(Rotation2D a, Rotation2D b)
        {
            return a * new Rotation2D(-b.S, b.C);//mutiply inverse b
        }

        /// <summary>
        /// Linearly interpolates between two rotations.
        /// </summary>
        /// <param name="a">The first rotation.</param>
        /// <param name="b">The second rotation.</param>
        /// <param name="t">The interpolation factor (between 0 and 1).</param>
        /// <returns>The interpolated rotation.</returns>
        public static Rotation2D Lerp(Rotation2D a, Rotation2D b, float t)
        {
            return new Rotation2D(math.lerp(a.S, b.S, t), math.lerp(a.C, b.C, t));
        }

        /// <summary>
        /// Spherically interpolates between two rotations.
        /// </summary>
        /// <param name="a">The first rotation.</param>
        /// <param name="b">The second rotation.</param>
        /// <param name="t">The interpolation factor (between 0 and 1).</param>
        /// <returns>The interpolated rotation.</returns>
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

        /// <summary>
        /// Determines whether this rotation is equal to another rotation.
        /// </summary>
        /// <param name="other">The rotation to compare with this rotation.</param>
        /// <returns>True if the rotations are equal; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Rotation2D other)
        {
            return this == other;
        }

        /// <summary>
        /// Determines whether this rotation is equal to the specified object.
        /// </summary>
        /// <param name="obj">The object to compare with this rotation.</param>
        /// <returns>True if the specified object is equal to this rotation; otherwise, false.</returns>
        public override bool Equals(object? obj)
        {
            return obj is Rotation2D d && this == d;
        }

        /// <summary>
        /// Returns the hash code for this rotation.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override int GetHashCode()
        {
            return S.GetHashCode() ^ C.GetHashCode();
        }

        /// <summary>
        /// Returns a string representation of this rotation.
        /// </summary>
        /// <returns>A string in the format "&lt;S, C&gt;".</returns>
        public override string ToString()
        {
            return $"<{S}, {C}>";
        }
    }
}