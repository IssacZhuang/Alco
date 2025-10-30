//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct uint4
    {
        public uint X;
        public uint Y;
        public uint Z;
        public uint W;

        /// <summary>
        /// A uint4 with all components set to zero.
        /// </summary>
        public static readonly uint4 Zero = new uint4(0u, 0u, 0u, 0u);

        /// <summary>
        /// A uint4 with all components set to one.
        /// </summary>
        public static readonly uint4 One = new uint4(1u, 1u, 1u, 1u);

        /// <summary>
        /// A unit vector with the X component set to one and all other components set to zero.
        /// </summary>
        public static readonly uint4 UnitX = new uint4(1u, 0u, 0u, 0u);

        /// <summary>
        /// A unit vector with the Y component set to one and all other components set to zero.
        /// </summary>
        public static readonly uint4 UnitY = new uint4(0u, 1u, 0u, 0u);

        /// <summary>
        /// A unit vector with the Z component set to one and all other components set to zero.
        /// </summary>
        public static readonly uint4 UnitZ = new uint4(0u, 0u, 1u, 0u);

        /// <summary>
        /// A unit vector with the W component set to one and all other components set to zero.
        /// </summary>
        public static readonly uint4 UnitW = new uint4(0u, 0u, 0u, 1u);

        public uint4(uint value)
        {
            this.X = value;
            this.Y = value;
            this.Z = value;
            this.W = value;
        }

        public uint4(int value)
        {
            this.X = (uint)value;
            this.Y = (uint)value;
            this.Z = (uint)value;
            this.W = (uint)value;
        }

        public uint4(float value)
        {
            this.X = (uint)value;
            this.Y = (uint)value;
            this.Z = (uint)value;
            this.W = (uint)value;
        }

        public uint4(uint X, uint Y, uint Z, uint W)
        {
            this.X = X;
            this.Y = Y;
            this.Z = Z;
            this.W = W;
        }

        public uint4(int X, int Y, int Z, int W)
        {
            this.X = (uint)X;
            this.Y = (uint)Y;
            this.Z = (uint)Z;
            this.W = (uint)W;
        }

        public uint4(float X, float Y, float Z, float W)
        {
            this.X = (uint)X;
            this.Y = (uint)Y;
            this.Z = (uint)Z;
            this.W = (uint)W;
        }

        public uint4(uint2 value, uint Z, uint W)
        {
            this.X = value.X;
            this.Y = value.Y;
            this.Z = Z;
            this.W = W;
        }

        public uint4(uint3 value, uint W)
        {
            this.X = value.X;
            this.Y = value.Y;
            this.Z = value.Z;
            this.W = W;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint4 operator +(uint4 a, uint4 b)
        {
            return new uint4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint4 operator -(uint4 a, uint4 b)
        {
            return new uint4(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint4 operator *(uint4 a, uint4 b)
        {
            return new uint4(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint4 operator /(uint4 a, uint4 b)
        {
            return new uint4(a.X / b.X, a.Y / b.Y, a.Z / b.Z, a.W / b.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(uint4 a, uint4 b)
        {
            return a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.W == b.W;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(uint4 a, uint4 b)
        {
            return !(a == b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector4(uint4 a)
        {
            return new Vector4((float)a.X, (float)a.Y, (float)a.Z, (float)a.W);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator uint4(Vector4 a)
        {
            return new uint4((uint)a.X, (uint)a.Y, (uint)a.Z, (uint)a.W);
        }

        public override bool Equals(object obj)
        {
            return obj is uint4 other && this == other;
        }

        public bool Equals(uint4 other)
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
