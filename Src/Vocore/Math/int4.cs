using System;

namespace Vocore
{
    public struct int4
    {
        public int x;
        public int y;
        public int z;
        public int w;

        public int4(int x, int y, int z, int w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public int4(int v)
        {
            this.x = v;
            this.y = v;
            this.z = v;
            this.w = v;
        }

        public static int4 operator +(int4 a, int4 b)
        {
            return new int4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
        }

        public static int4 operator -(int4 a, int4 b)
        {
            return new int4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);
        }

        public static int4 operator *(int4 a, int4 b)
        {
            return new int4(a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w);
        }

        public static int4 operator /(int4 a, int4 b)
        {
            return new int4(a.x / b.x, a.y / b.y, a.z / b.z, a.w / b.w);
        }
    }
}