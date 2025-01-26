//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct int2
    {
        public int x;
        public int y;

        public int2(int value)
        {
            this.x = value;
            this.y = value;
        }

        public int2(uint value)
        {
            this.x = (int)value;
            this.y = (int)value;
        }

        public int2(float value)
        {
            this.x = (int)value;
            this.y = (int)value;
        }

        public int2(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public int2(uint x, uint y)
        {
            this.x = (int)x;
            this.y = (int)y;
        }

        public int2(float x, float y)
        {
            this.x = (int)x;
            this.y = (int)y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 operator +(int2 a, int2 b)
        {
            return new int2(a.x + b.x, a.y + b.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 operator -(int2 a, int2 b)
        {
            return new int2(a.x - b.x, a.y - b.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 operator *(int2 a, int2 b)
        {
            return new int2(a.x * b.x, a.y * b.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 operator /(int2 a, int2 b)
        {
            return new int2(a.x / b.x, a.y / b.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector2(int2 a)
        {
            return new Vector2(a.x, a.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int2(Vector2 a)
        {
            return new int2((int)a.X, (int)a.Y);
        }

        public override string ToString()
        {
            return $"({x}, {y})";
        }
    }
}
