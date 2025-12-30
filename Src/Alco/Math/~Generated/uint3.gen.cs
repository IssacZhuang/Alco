//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct uint3
    {
        public uint X;
        public uint Y;
        public uint Z;

        /// <summary>
        /// A uint3 with all components set to zero.
        /// </summary>
        public static readonly uint3 Zero = new uint3(0u, 0u, 0u);

        /// <summary>
        /// A uint3 with all components set to one.
        /// </summary>
        public static readonly uint3 One = new uint3(1u, 1u, 1u);

        /// <summary>
        /// A unit vector with the X component set to one and all other components set to zero.
        /// </summary>
        public static readonly uint3 UnitX = new uint3(1u, 0u, 0u);

        /// <summary>
        /// A unit vector with the Y component set to one and all other components set to zero.
        /// </summary>
        public static readonly uint3 UnitY = new uint3(0u, 1u, 0u);

        /// <summary>
        /// A unit vector with the Z component set to one and all other components set to zero.
        /// </summary>
        public static readonly uint3 UnitZ = new uint3(0u, 0u, 1u);

        public uint3(uint value)
        {
            this.X = value;
            this.Y = value;
            this.Z = value;
        }

        public uint3(int value)
        {
            this.X = (uint)value;
            this.Y = (uint)value;
            this.Z = (uint)value;
        }

        public uint3(float value)
        {
            this.X = (uint)value;
            this.Y = (uint)value;
            this.Z = (uint)value;
        }

        public uint3(uint X, uint Y, uint Z)
        {
            this.X = X;
            this.Y = Y;
            this.Z = Z;
        }

        public uint3(int X, int Y, int Z)
        {
            this.X = (uint)X;
            this.Y = (uint)Y;
            this.Z = (uint)Z;
        }

        public uint3(float X, float Y, float Z)
        {
            this.X = (uint)X;
            this.Y = (uint)Y;
            this.Z = (uint)Z;
        }

        public uint3(uint2 value, uint Z)
        {
            this.X = value.X;
            this.Y = value.Y;
            this.Z = Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint3 operator +(uint3 a, uint3 b)
        {
            return new uint3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint3 operator -(uint3 a, uint3 b)
        {
            return new uint3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint3 operator *(uint3 a, uint3 b)
        {
            return new uint3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint3 operator /(uint3 a, uint3 b)
        {
            return new uint3(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(uint3 a, uint3 b)
        {
            return a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(uint3 a, uint3 b)
        {
            return !(a == b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector3(uint3 a)
        {
            return new Vector3((float)a.X, (float)a.Y, (float)a.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator uint3(Vector3 a)
        {
            return new uint3((uint)a.X, (uint)a.Y, (uint)a.Z);
        }

        public override bool Equals(object? obj)
        {
            return obj is uint3 other && this == other;
        }

        public bool Equals(uint3 other)
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
