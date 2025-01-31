//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct Half2
    {
        public Half x;
        public Half y;

        public Half2(Half value)
        {
            this.x = value;
            this.y = value;
        }

        public Half2(int value)
        {
            this.x = (Half)value;
            this.y = (Half)value;
        }

        public Half2(uint value)
        {
            this.x = (Half)value;
            this.y = (Half)value;
        }

        public Half2(float value)
        {
            this.x = (Half)value;
            this.y = (Half)value;
        }

        public Half2(Half x, Half y)
        {
            this.x = x;
            this.y = y;
        }

        public Half2(int x, int y)
        {
            this.x = (Half)x;
            this.y = (Half)y;
        }

        public Half2(uint x, uint y)
        {
            this.x = (Half)x;
            this.y = (Half)y;
        }

        public Half2(float x, float y)
        {
            this.x = (Half)x;
            this.y = (Half)y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 operator +(Half2 a, Half2 b)
        {
            return new Half2(a.x + b.x, a.y + b.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 operator -(Half2 a, Half2 b)
        {
            return new Half2(a.x - b.x, a.y - b.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 operator *(Half2 a, Half2 b)
        {
            return new Half2(a.x * b.x, a.y * b.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 operator /(Half2 a, Half2 b)
        {
            return new Half2(a.x / b.x, a.y / b.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector2(Half2 a)
        {
            return new Vector2((float)a.x, (float)a.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Half2(Vector2 a)
        {
            return new Half2((Half)a.X, (Half)a.Y);
        }

        public override string ToString()
        {
            return $"({x}, {y})";
        }
    }
}
