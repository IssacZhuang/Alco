using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public struct int2
    {
        public int x;
        public int y;

        public int2(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public int2(int v)
        {
            this.x = v;
            this.y = v;
        }

        public static int2 operator +(int2 a, int2 b)
        {
            return new int2(a.x + b.x, a.y + b.y);
        }

        public static int2 operator -(int2 a, int2 b)
        {
            return new int2(a.x - b.x, a.y - b.y);
        }

        public static int2 operator *(int2 a, int2 b)
        {
            return new int2(a.x * b.x, a.y * b.y);
        }

        public static int2 operator /(int2 a, int2 b)
        {
            return new int2(a.x / b.x, a.y / b.y);
        }

        //to vector2

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector2(int2 a)
        {
            return new System.Numerics.Vector2(a.x, a.y);
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