//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct uint4
    {
        public uint x;
        public uint y;
        public uint z;
        public uint w;

        public uint4(uint value)
        {
            this.x = value;
            this.y = value;
            this.z = value;
            this.w = value;
        }

        public uint4(int value)
        {
            this.x = (uint)value;
            this.y = (uint)value;
            this.z = (uint)value;
            this.w = (uint)value;
        }

        public uint4(float value)
        {
            this.x = (uint)value;
            this.y = (uint)value;
            this.z = (uint)value;
            this.w = (uint)value;
        }

        public uint4(uint x, uint y, uint z, uint w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public uint4(int x, int y, int z, int w)
        {
            this.x = (uint)x;
            this.y = (uint)y;
            this.z = (uint)z;
            this.w = (uint)w;
        }

        public uint4(float x, float y, float z, float w)
        {
            this.x = (uint)x;
            this.y = (uint)y;
            this.z = (uint)z;
            this.w = (uint)w;
        }

        public uint4(uint2 value, uint z, uint w)
        {
            this.x = value.x;
            this.y = value.y;
            this.z = z;
            this.w = w;
        }

        public uint4(uint3 value, uint w)
        {
            this.x = value.x;
            this.y = value.y;
            this.z = value.z;
            this.w = w;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint4 operator +(uint4 a, uint4 b)
        {
            return new uint4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint4 operator -(uint4 a, uint4 b)
        {
            return new uint4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint4 operator *(uint4 a, uint4 b)
        {
            return new uint4(a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint4 operator /(uint4 a, uint4 b)
        {
            return new uint4(a.x / b.x, a.y / b.y, a.z / b.z, a.w / b.w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector4(uint4 a)
        {
            return new Vector4(a.x, a.y, a.z, a.w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator uint4(Vector4 a)
        {
            return new uint4((uint)a.X, (uint)a.Y, (uint)a.Z, (uint)a.W);
        }

        public override string ToString()
        {
            return $"({x}, {y}, {z}, {w})";
        }
    }
}
