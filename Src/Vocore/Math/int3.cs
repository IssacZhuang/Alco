using System;

namespace Vocore
{
    public struct int3
    {
        public int x;
        public int y;
        public int z;

        public int3(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public int3(int v)
        {
            this.x = v;
            this.y = v;
            this.z = v;
        }

        public static int3 operator +(int3 a, int3 b)
        {
            return new int3(a.x + b.x, a.y + b.y, a.z + b.z);
        }

        public static int3 operator -(int3 a, int3 b)
        {
            return new int3(a.x - b.x, a.y - b.y, a.z - b.z);
        }

        public static int3 operator *(int3 a, int3 b)
        {
            return new int3(a.x * b.x, a.y * b.y, a.z * b.z);
        }

        public static int3 operator /(int3 a, int3 b)
        {
            return new int3(a.x / b.x, a.y / b.y, a.z / b.z);
        }
    }
}