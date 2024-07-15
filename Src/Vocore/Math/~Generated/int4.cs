//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public struct int4
    {
        public int x;
        public int y;
        public int z;
        public int w;

        public int4(int value)
        {
            this.x = value;
            this.y = value;
            this.z = value;
            this.w = value;
        }

        public int4(int x, int y, int z, int w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public int4(int2 value, int z, int w)
        {
            this.x = value.x;
            this.y = value.y;
            this.z = z;
            this.w = w;
        }

        public int4(int3 value, int w)
        {
            this.x = value.x;
            this.y = value.y;
            this.z = value.z;
            this.w = w;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 operator +(int4 a, int4 b)
        {
            return new int4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 operator -(int4 a, int4 b)
        {
            return new int4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 operator *(int4 a, int4 b)
        {
            return new int4(a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 operator /(int4 a, int4 b)
        {
            return new int4(a.x / b.x, a.y / b.y, a.z / b.z, a.w / b.w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector4(int4 a)
        {
            return new Vector4(a.x, a.y, a.z, a.w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int4(Vector4 a)
        {
            return new int4((int)a.X, (int)a.Y, (int)a.Z, (int)a.W);
        }

        public override string ToString()
        {
            return $"({x}, {y}, {z}, {w})";
        }
    }
}
