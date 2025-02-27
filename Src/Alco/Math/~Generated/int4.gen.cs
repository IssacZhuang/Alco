//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct int4
    {
        public int X;
        public int Y;
        public int Z;
        public int W;

        public int4(int value)
        {
            this.X = value;
            this.Y = value;
            this.Z = value;
            this.W = value;
        }

        public int4(uint value)
        {
            this.X = (int)value;
            this.Y = (int)value;
            this.Z = (int)value;
            this.W = (int)value;
        }

        public int4(float value)
        {
            this.X = (int)value;
            this.Y = (int)value;
            this.Z = (int)value;
            this.W = (int)value;
        }

        public int4(int x, int y, int z, int w)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
            this.W = w;
        }

        public int4(uint x, uint y, uint z, uint w)
        {
            this.X = (int)x;
            this.Y = (int)y;
            this.Z = (int)z;
            this.W = (int)w;
        }

        public int4(float x, float y, float z, float w)
        {
            this.X = (int)x;
            this.Y = (int)y;
            this.Z = (int)z;
            this.W = (int)w;
        }

        public int4(int2 value, int z, int w)
        {
            this.X = value.X;
            this.Y = value.Y;
            this.Z = z;
            this.W = w;
        }

        public int4(int3 value, int w)
        {
            this.X = value.X;
            this.Y = value.Y;
            this.Z = value.Z;
            this.W = w;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 operator +(int4 a, int4 b)
        {
            return new int4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 operator -(int4 a, int4 b)
        {
            return new int4(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 operator *(int4 a, int4 b)
        {
            return new int4(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 operator /(int4 a, int4 b)
        {
            return new int4(a.X / b.X, a.Y / b.Y, a.Z / b.Z, a.W / b.W);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector4(int4 a)
        {
            return new Vector4((float)a.X, (float)a.Y, (float)a.Z, (float)a.W);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int4(Vector4 a)
        {
            return new int4((int)a.X, (int)a.Y, (int)a.Z, (int)a.W);
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z}, {W})";
        }
    }
}
