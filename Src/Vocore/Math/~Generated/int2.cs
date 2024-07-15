//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
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

        public int2(int x, int y)
        {
            this.x = x;
            this.y = y;
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
