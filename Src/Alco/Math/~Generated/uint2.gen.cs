//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct uint2
    {
        public uint x;
        public uint y;

        public uint2(uint value)
        {
            this.x = value;
            this.y = value;
        }

        public uint2(int value)
        {
            this.x = (uint)value;
            this.y = (uint)value;
        }

        public uint2(float value)
        {
            this.x = (uint)value;
            this.y = (uint)value;
        }

        public uint2(uint x, uint y)
        {
            this.x = x;
            this.y = y;
        }

        public uint2(int x, int y)
        {
            this.x = (uint)x;
            this.y = (uint)y;
        }

        public uint2(float x, float y)
        {
            this.x = (uint)x;
            this.y = (uint)y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint2 operator +(uint2 a, uint2 b)
        {
            return new uint2(a.x + b.x, a.y + b.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint2 operator -(uint2 a, uint2 b)
        {
            return new uint2(a.x - b.x, a.y - b.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint2 operator *(uint2 a, uint2 b)
        {
            return new uint2(a.x * b.x, a.y * b.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint2 operator /(uint2 a, uint2 b)
        {
            return new uint2(a.x / b.x, a.y / b.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector2(uint2 a)
        {
            return new Vector2(a.x, a.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator uint2(Vector2 a)
        {
            return new uint2((uint)a.X, (uint)a.Y);
        }

        public override string ToString()
        {
            return $"({x}, {y})";
        }
    }
}
