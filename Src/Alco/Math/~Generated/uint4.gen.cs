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

        public uint4(uint x, uint y, uint z, uint w)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
            this.W = w;
        }

        public uint4(int x, int y, int z, int w)
        {
            this.X = (uint)x;
            this.Y = (uint)y;
            this.Z = (uint)z;
            this.W = (uint)w;
        }

        public uint4(float x, float y, float z, float w)
        {
            this.X = (uint)x;
            this.Y = (uint)y;
            this.Z = (uint)z;
            this.W = (uint)w;
        }

        public uint4(uint2 value, uint z, uint w)
        {
            this.X = value.X;
            this.Y = value.Y;
            this.Z = z;
            this.W = w;
        }

        public uint4(uint3 value, uint w)
        {
            this.X = value.X;
            this.Y = value.Y;
            this.Z = value.Z;
            this.W = w;
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
        public static implicit operator Vector4(uint4 a)
        {
            return new Vector4((float)a.X, (float)a.Y, (float)a.Z, (float)a.W);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator uint4(Vector4 a)
        {
            return new uint4((uint)a.X, (uint)a.Y, (uint)a.Z, (uint)a.W);
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z}, {W})";
        }
    }
}
