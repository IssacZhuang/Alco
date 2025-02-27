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

        public int2(int X, int Y)
        {
            this.X = X;
            this.Y = Y;
        }

        public int2(uint X, uint Y)
        {
            this.X = (int)X;
            this.Y = (int)Y;
        }

        public int2(float X, float Y)
        {
            this.X = (int)X;
            this.Y = (int)Y;
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
