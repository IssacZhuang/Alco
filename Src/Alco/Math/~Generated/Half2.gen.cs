//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct Half2
    {
        public Half X;
        public Half Y;

        public Half2(Half value)
        {
            this.X = value;
            this.Y = value;
        }

        public Half2(int value)
        {
            this.X = (Half)value;
            this.Y = (Half)value;
        }

        public Half2(uint value)
        {
            this.X = (Half)value;
            this.Y = (Half)value;
        }

        public Half2(float value)
        {
            this.X = (Half)value;
            this.Y = (Half)value;
        }

        public Half2(Half x, Half y)
        {
            this.X = x;
            this.Y = y;
        }

        public Half2(int x, int y)
        {
            this.X = (Half)x;
            this.Y = (Half)y;
        }

        public Half2(uint x, uint y)
        {
            this.X = (Half)x;
            this.Y = (Half)y;
        }

        public Half2(float x, float y)
        {
            this.X = (Half)x;
            this.Y = (Half)y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 operator +(Half2 a, Half2 b)
        {
            return new Half2(a.X + b.X, a.Y + b.Y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 operator -(Half2 a, Half2 b)
        {
            return new Half2(a.X - b.X, a.Y - b.Y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 operator *(Half2 a, Half2 b)
        {
            return new Half2(a.X * b.X, a.Y * b.Y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 operator /(Half2 a, Half2 b)
        {
            return new Half2(a.X / b.X, a.Y / b.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector2(Half2 a)
        {
            return new Vector2((float)a.X, (float)a.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Half2(Vector2 a)
        {
            return new Half2((Half)a.X, (Half)a.Y);
        }

        public override string ToString()
        {
            return $"({X}, {Y})";
        }
    }
}
