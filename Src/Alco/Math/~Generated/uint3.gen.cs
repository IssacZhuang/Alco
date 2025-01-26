//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct uint3
    {
        public uint x;
        public uint y;
        public uint z;

        public uint3(uint value)
        {
            this.x = value;
            this.y = value;
            this.z = value;
        }

        public uint3(int value)
        {
            this.x = (uint)value;
            this.y = (uint)value;
            this.z = (uint)value;
        }

        public uint3(float value)
        {
            this.x = (uint)value;
            this.y = (uint)value;
            this.z = (uint)value;
        }

        public uint3(uint x, uint y, uint z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public uint3(int x, int y, int z)
        {
            this.x = (uint)x;
            this.y = (uint)y;
            this.z = (uint)z;
        }

        public uint3(float x, float y, float z)
        {
            this.x = (uint)x;
            this.y = (uint)y;
            this.z = (uint)z;
        }

        public uint3(uint2 value, uint z)
        {
            this.x = value.x;
            this.y = value.y;
            this.z = z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint3 operator +(uint3 a, uint3 b)
        {
            return new uint3(a.x + b.x, a.y + b.y, a.z + b.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint3 operator -(uint3 a, uint3 b)
        {
            return new uint3(a.x - b.x, a.y - b.y, a.z - b.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint3 operator *(uint3 a, uint3 b)
        {
            return new uint3(a.x * b.x, a.y * b.y, a.z * b.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint3 operator /(uint3 a, uint3 b)
        {
            return new uint3(a.x / b.x, a.y / b.y, a.z / b.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector3(uint3 a)
        {
            return new Vector3(a.x, a.y, a.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator uint3(Vector3 a)
        {
            return new uint3((uint)a.X, (uint)a.Y, (uint)a.Z);
        }

        public override string ToString()
        {
            return $"({x}, {y}, {z})";
        }
    }
}
