//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct Half3
    {
        public Half X;
        public Half Y;
        public Half Z;

        /// <summary>
        /// A Half3 with all components set to zero.
        /// </summary>
        public static readonly Half3 Zero = new Half3(0, 0, 0);

        /// <summary>
        /// A Half3 with all components set to one.
        /// </summary>
        public static readonly Half3 One = new Half3(1, 1, 1);

        /// <summary>
        /// A unit vector with the X component set to one and all other components set to zero.
        /// </summary>
        public static readonly Half3 UnitX = new Half3(1, 0, 0);

        /// <summary>
        /// A unit vector with the Y component set to one and all other components set to zero.
        /// </summary>
        public static readonly Half3 UnitY = new Half3(0, 1, 0);

        /// <summary>
        /// A unit vector with the Z component set to one and all other components set to zero.
        /// </summary>
        public static readonly Half3 UnitZ = new Half3(0, 0, 1);

        public Half3(Half value)
        {
            this.X = value;
            this.Y = value;
            this.Z = value;
        }

        public Half3(int value)
        {
            this.X = (Half)value;
            this.Y = (Half)value;
            this.Z = (Half)value;
        }

        public Half3(uint value)
        {
            this.X = (Half)value;
            this.Y = (Half)value;
            this.Z = (Half)value;
        }

        public Half3(float value)
        {
            this.X = (Half)value;
            this.Y = (Half)value;
            this.Z = (Half)value;
        }

        public Half3(Half X, Half Y, Half Z)
        {
            this.X = X;
            this.Y = Y;
            this.Z = Z;
        }

        public Half3(int X, int Y, int Z)
        {
            this.X = (Half)X;
            this.Y = (Half)Y;
            this.Z = (Half)Z;
        }

        public Half3(uint X, uint Y, uint Z)
        {
            this.X = (Half)X;
            this.Y = (Half)Y;
            this.Z = (Half)Z;
        }

        public Half3(float X, float Y, float Z)
        {
            this.X = (Half)X;
            this.Y = (Half)Y;
            this.Z = (Half)Z;
        }

        public Half3(Half2 value, Half Z)
        {
            this.X = value.X;
            this.Y = value.Y;
            this.Z = Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 operator +(Half3 a, Half3 b)
        {
            return new Half3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 operator -(Half3 a, Half3 b)
        {
            return new Half3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }
        /// <summary>
        /// Negates the specified Half3 value.
        /// </summary>
        /// <param name="a">The value to negate.</param>
        /// <returns>A new Half3 with all components negated.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 operator -(Half3 a)
        {
            return new Half3(-a.X, -a.Y, -a.Z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 operator *(Half3 a, Half3 b)
        {
            return new Half3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 operator /(Half3 a, Half3 b)
        {
            return new Half3(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector3(Half3 a)
        {
            return new Vector3((float)a.X, (float)a.Y, (float)a.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Half3(Vector3 a)
        {
            return new Half3((Half)a.X, (Half)a.Y, (Half)a.Z);
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z})";
        }
    }
}
