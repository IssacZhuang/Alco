//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct int4
    {
        public int X;
        public int Y;
        public int Z;
        public int W;

        /// <summary>
        /// A int4 with all components set to zero.
        /// </summary>
        public static readonly int4 Zero = new int4(0, 0, 0, 0);

        /// <summary>
        /// A int4 with all components set to one.
        /// </summary>
        public static readonly int4 One = new int4(1, 1, 1, 1);

        /// <summary>
        /// A unit vector with the X component set to one and all other components set to zero.
        /// </summary>
        public static readonly int4 UnitX = new int4(1, 0, 0, 0);

        /// <summary>
        /// A unit vector with the Y component set to one and all other components set to zero.
        /// </summary>
        public static readonly int4 UnitY = new int4(0, 1, 0, 0);

        /// <summary>
        /// A unit vector with the Z component set to one and all other components set to zero.
        /// </summary>
        public static readonly int4 UnitZ = new int4(0, 0, 1, 0);

        /// <summary>
        /// A unit vector with the W component set to one and all other components set to zero.
        /// </summary>
        public static readonly int4 UnitW = new int4(0, 0, 0, 1);

        public int4(int value)
        {
            this.X = value;
            this.Y = value;
            this.Z = value;
            this.W = value;
        }

        public int4(uint value)
        {
            this.X = (int)value;
            this.Y = (int)value;
            this.Z = (int)value;
            this.W = (int)value;
        }

        public int4(float value)
        {
            this.X = (int)value;
            this.Y = (int)value;
            this.Z = (int)value;
            this.W = (int)value;
        }

        public int4(int X, int Y, int Z, int W)
        {
            this.X = X;
            this.Y = Y;
            this.Z = Z;
            this.W = W;
        }

        public int4(uint X, uint Y, uint Z, uint W)
        {
            this.X = (int)X;
            this.Y = (int)Y;
            this.Z = (int)Z;
            this.W = (int)W;
        }

        public int4(float X, float Y, float Z, float W)
        {
            this.X = (int)X;
            this.Y = (int)Y;
            this.Z = (int)Z;
            this.W = (int)W;
        }

        public int4(int2 value, int Z, int W)
        {
            this.X = value.X;
            this.Y = value.Y;
            this.Z = Z;
            this.W = W;
        }

        public int4(int3 value, int W)
        {
            this.X = value.X;
            this.Y = value.Y;
            this.Z = value.Z;
            this.W = W;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 operator +(int4 a, int4 b)
        {
            return new int4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 operator -(int4 a, int4 b)
        {
            return new int4(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
        }
        /// <summary>
        /// Negates the specified int4 value.
        /// </summary>
        /// <param name="a">The value to negate.</param>
        /// <returns>A new int4 with all components negated.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 operator -(int4 a)
        {
            return new int4(-a.X, -a.Y, -a.Z, -a.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 operator *(int4 a, int4 b)
        {
            return new int4(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 operator /(int4 a, int4 b)
        {
            return new int4(a.X / b.X, a.Y / b.Y, a.Z / b.Z, a.W / b.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(int4 a, int4 b)
        {
            return a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.W == b.W;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(int4 a, int4 b)
        {
            return !(a == b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector4(int4 a)
        {
            return new Vector4((float)a.X, (float)a.Y, (float)a.Z, (float)a.W);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int4(Vector4 a)
        {
            return new int4((int)a.X, (int)a.Y, (int)a.Z, (int)a.W);
        }

        public override bool Equals(object obj)
        {
            return obj is int4 other && this == other;
        }

        public bool Equals(int4 other)
        {
            return this == other;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z, W);
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z}, {W})";
        }
    }
}
