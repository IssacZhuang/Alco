//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct int3
    {
        public int X;
        public int Y;
        public int Z;

        /// <summary>
        /// A int3 with all components set to zero.
        /// </summary>
        public static readonly int3 Zero = new int3(0, 0, 0);

        /// <summary>
        /// A int3 with all components set to one.
        /// </summary>
        public static readonly int3 One = new int3(1, 1, 1);

        /// <summary>
        /// A unit vector with the X component set to one and all other components set to zero.
        /// </summary>
        public static readonly int3 UnitX = new int3(1, 0, 0);

        /// <summary>
        /// A unit vector with the Y component set to one and all other components set to zero.
        /// </summary>
        public static readonly int3 UnitY = new int3(0, 1, 0);

        /// <summary>
        /// A unit vector with the Z component set to one and all other components set to zero.
        /// </summary>
        public static readonly int3 UnitZ = new int3(0, 0, 1);

        public int3(int value)
        {
            this.X = value;
            this.Y = value;
            this.Z = value;
        }

        public int3(uint value)
        {
            this.X = (int)value;
            this.Y = (int)value;
            this.Z = (int)value;
        }

        public int3(float value)
        {
            this.X = (int)value;
            this.Y = (int)value;
            this.Z = (int)value;
        }

        public int3(int X, int Y, int Z)
        {
            this.X = X;
            this.Y = Y;
            this.Z = Z;
        }

        public int3(uint X, uint Y, uint Z)
        {
            this.X = (int)X;
            this.Y = (int)Y;
            this.Z = (int)Z;
        }

        public int3(float X, float Y, float Z)
        {
            this.X = (int)X;
            this.Y = (int)Y;
            this.Z = (int)Z;
        }

        public int3(int2 value, int Z)
        {
            this.X = value.X;
            this.Y = value.Y;
            this.Z = Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 operator +(int3 a, int3 b)
        {
            return new int3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 operator -(int3 a, int3 b)
        {
            return new int3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }
        /// <summary>
        /// Negates the specified int3 value.
        /// </summary>
        /// <param name="a">The value to negate.</param>
        /// <returns>A new int3 with all components negated.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 operator -(int3 a)
        {
            return new int3(-a.X, -a.Y, -a.Z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 operator *(int3 a, int3 b)
        {
            return new int3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 operator /(int3 a, int3 b)
        {
            return new int3(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(int3 a, int3 b)
        {
            return a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(int3 a, int3 b)
        {
            return !(a == b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector3(int3 a)
        {
            return new Vector3((float)a.X, (float)a.Y, (float)a.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int3(Vector3 a)
        {
            return new int3((int)a.X, (int)a.Y, (int)a.Z);
        }

        public override bool Equals(object? obj)
        {
            return obj is int3 other && this == other;
        }

        public bool Equals(int3 other)
        {
            return this == other;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z);
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z})";
        }
    }
}
