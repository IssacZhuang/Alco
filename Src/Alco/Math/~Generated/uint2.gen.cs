//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct uint2
    {
        public uint X;
        public uint Y;

        public uint2(uint value)
        {
            this.X = value;
            this.Y = value;
        }

        public uint2(int value)
        {
            this.X = (uint)value;
            this.Y = (uint)value;
        }

        public uint2(float value)
        {
            this.X = (uint)value;
            this.Y = (uint)value;
        }

        public uint2(uint X, uint Y)
        {
            this.X = X;
            this.Y = Y;
        }

        public uint2(int X, int Y)
        {
            this.X = (uint)X;
            this.Y = (uint)Y;
        }

        public uint2(float X, float Y)
        {
            this.X = (uint)X;
            this.Y = (uint)Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint2 operator +(uint2 a, uint2 b)
        {
            return new uint2(a.X + b.X, a.Y + b.Y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint2 operator -(uint2 a, uint2 b)
        {
            return new uint2(a.X - b.X, a.Y - b.Y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint2 operator *(uint2 a, uint2 b)
        {
            return new uint2(a.X * b.X, a.Y * b.Y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint2 operator /(uint2 a, uint2 b)
        {
            return new uint2(a.X / b.X, a.Y / b.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector2(uint2 a)
        {
            return new Vector2((float)a.X, (float)a.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator uint2(Vector2 a)
        {
            return new uint2((uint)a.X, (uint)a.Y);
        }

        public override string ToString()
        {
            return $"({X}, {Y})";
        }
    }
}
