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

        public Half2(Half X, Half Y)
        {
            this.X = X;
            this.Y = Y;
        }

        public Half2(int X, int Y)
        {
            this.X = (Half)X;
            this.Y = (Half)Y;
        }

        public Half2(uint X, uint Y)
        {
            this.X = (Half)X;
            this.Y = (Half)Y;
        }

        public Half2(float X, float Y)
        {
            this.X = (Half)X;
            this.Y = (Half)Y;
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
