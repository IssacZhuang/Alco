//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct int2
    {
        public int X;
        public int Y;

        public int2(int value)
        {
            this.X = value;
            this.Y = value;
        }

        public int2(uint value)
        {
            this.X = (int)value;
            this.Y = (int)value;
        }

        public int2(float value)
        {
            this.X = (int)value;
            this.Y = (int)value;
        }

        public int2(int x, int y)
        {
            this.X = x;
            this.Y = y;
        }

        public int2(uint x, uint y)
        {
            this.X = (int)x;
            this.Y = (int)y;
        }

        public int2(float x, float y)
        {
            this.X = (int)x;
            this.Y = (int)y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 operator +(int2 a, int2 b)
        {
            return new int2(a.X + b.X, a.Y + b.Y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 operator -(int2 a, int2 b)
        {
            return new int2(a.X - b.X, a.Y - b.Y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 operator *(int2 a, int2 b)
        {
            return new int2(a.X * b.X, a.Y * b.Y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 operator /(int2 a, int2 b)
        {
            return new int2(a.X / b.X, a.Y / b.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector2(int2 a)
        {
            return new Vector2((float)a.X, (float)a.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int2(Vector2 a)
        {
            return new int2((int)a.X, (int)a.Y);
        }

        public override string ToString()
        {
            return $"({X}, {Y})";
        }
    }
}
