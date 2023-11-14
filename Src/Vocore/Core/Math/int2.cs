using System;

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
    }
}