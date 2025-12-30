//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct Half2
    {
        public Half X;
        public Half Y;

        /// <summary>
        /// A Half2 with all components set to zero.
        /// </summary>
        public static readonly Half2 Zero = new Half2(0, 0);

        /// <summary>
        /// A Half2 with all components set to one.
        /// </summary>
        public static readonly Half2 One = new Half2(1, 1);

        /// <summary>
        /// A unit vector with the X component set to one and all other components set to zero.
        /// </summary>
        public static readonly Half2 UnitX = new Half2(1, 0);

        /// <summary>
        /// A unit vector with the Y component set to one and all other components set to zero.
        /// </summary>
        public static readonly Half2 UnitY = new Half2(0, 1);

        public Half2(Half value)
        {
            this.X = value;
            this.Y = value;
        }

        public Half2(int value)
        {
            this.X = (Half)value;
            this.Y = (Half)value;
        }

        public Half2(uint value)
        {
            this.X = (Half)value;
            this.Y = (Half)value;
        }

        public Half2(float value)
        {
            this.X = (Half)value;
            this.Y = (Half)value;
        }

        public Half2(Half X, Half Y)
        {
            this.X = X;
            this.Y = Y;
        }

        public Half2(int X, int Y)
        {
            this.X = (Half)X;
            this.Y = (Half)Y;
        }

        public Half2(uint X, uint Y)
        {
            this.X = (Half)X;
            this.Y = (Half)Y;
        }

        public Half2(float X, float Y)
        {
            this.X = (Half)X;
            this.Y = (Half)Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 operator +(Half2 a, Half2 b)
        {
            return new Half2(a.X + b.X, a.Y + b.Y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 operator -(Half2 a, Half2 b)
        {
            return new Half2(a.X - b.X, a.Y - b.Y);
        }
        /// <summary>
        /// Negates the specified Half2 value.
        /// </summary>
        /// <param name="a">The value to negate.</param>
        /// <returns>A new Half2 with all components negated.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 operator -(Half2 a)
        {
            return new Half2(-a.X, -a.Y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 operator *(Half2 a, Half2 b)
        {
            return new Half2(a.X * b.X, a.Y * b.Y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 operator /(Half2 a, Half2 b)
        {
            return new Half2(a.X / b.X, a.Y / b.Y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Half2 a, Half2 b)
        {
            return a.X == b.X && a.Y == b.Y;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Half2 a, Half2 b)
        {
            return !(a == b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector2(Half2 a)
        {
            return new Vector2((float)a.X, (float)a.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Half2(Vector2 a)
        {
            return new Half2((Half)a.X, (Half)a.Y);
        }

        public override bool Equals(object? obj)
        {
            return obj is Half2 other && this == other;
        }

        public bool Equals(Half2 other)
        {
            return this == other;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y);
        }

        public override string ToString()
        {
            return $"({X}, {Y})";
        }
    }
}
