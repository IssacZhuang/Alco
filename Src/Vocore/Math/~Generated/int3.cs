//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public struct int3
    {
        public int x;
        public int y;
        public int z;

        public int3(int value)
        {
            this.x = value;
            this.y = value;
            this.z = value;
        }

        public int3(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public int3(int2 value, int z)
        {
            this.x = value.x;
            this.y = value.y;
            this.z = z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 operator +(int3 a, int3 b)
        {
            return new int3(a.x + b.x, a.y + b.y, a.z + b.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 operator -(int3 a, int3 b)
        {
            return new int3(a.x - b.x, a.y - b.y, a.z - b.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 operator *(int3 a, int3 b)
        {
            return new int3(a.x * b.x, a.y * b.y, a.z * b.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 operator /(int3 a, int3 b)
        {
            return new int3(a.x / b.x, a.y / b.y, a.z / b.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector3(int3 a)
        {
            return new Vector3(a.x, a.y, a.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int3(Vector3 a)
        {
            return new int3((int)a.X, (int)a.Y, (int)a.Z);
        }

        public override string ToString()
        {
            return $"({x}, {y}, {z})";
        }
    }
}
