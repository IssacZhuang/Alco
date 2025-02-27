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

        public uint3(uint x, uint y, uint z)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
        }

        public uint3(int x, int y, int z)
        {
            this.X = (uint)x;
            this.Y = (uint)y;
            this.Z = (uint)z;
        }

        public uint3(float x, float y, float z)
        {
            this.X = (uint)x;
            this.Y = (uint)y;
            this.Z = (uint)z;
        }

        public uint3(uint2 value, uint z)
        {
            this.X = value.X;
            this.Y = value.Y;
            this.Z = z;
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
        public static implicit operator Vector3(uint3 a)
        {
            return new Vector3((float)a.X, (float)a.Y, (float)a.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator uint3(Vector3 a)
        {
            return new uint3((uint)a.X, (uint)a.Y, (uint)a.Z);
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z})";
        }
    }
}
