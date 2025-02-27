//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct Half4
    {
        public Half X;
        public Half Y;
        public Half Z;
        public Half W;

        public Half4(Half value)
        {
            this.X = value;
            this.Y = value;
            this.Z = value;
            this.W = value;
        }

        public Half4(int value)
        {
            this.X = (Half)value;
            this.Y = (Half)value;
            this.Z = (Half)value;
            this.W = (Half)value;
        }

        public Half4(uint value)
        {
            this.X = (Half)value;
            this.Y = (Half)value;
            this.Z = (Half)value;
            this.W = (Half)value;
        }

        public Half4(float value)
        {
            this.X = (Half)value;
            this.Y = (Half)value;
            this.Z = (Half)value;
            this.W = (Half)value;
        }

        public Half4(Half x, Half y, Half z, Half w)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
            this.W = w;
        }

        public Half4(int x, int y, int z, int w)
        {
            this.X = (Half)x;
            this.Y = (Half)y;
            this.Z = (Half)z;
            this.W = (Half)w;
        }

        public Half4(uint x, uint y, uint z, uint w)
        {
            this.X = (Half)x;
            this.Y = (Half)y;
            this.Z = (Half)z;
            this.W = (Half)w;
        }

        public Half4(float x, float y, float z, float w)
        {
            this.X = (Half)x;
            this.Y = (Half)y;
            this.Z = (Half)z;
            this.W = (Half)w;
        }

        public Half4(Half2 value, Half z, Half w)
        {
            this.X = value.X;
            this.Y = value.Y;
            this.Z = z;
            this.W = w;
        }

        public Half4(Half3 value, Half w)
        {
            this.X = value.X;
            this.Y = value.Y;
            this.Z = value.Z;
            this.W = w;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 operator +(Half4 a, Half4 b)
        {
            return new Half4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 operator -(Half4 a, Half4 b)
        {
            return new Half4(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 operator *(Half4 a, Half4 b)
        {
            return new Half4(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 operator /(Half4 a, Half4 b)
        {
            return new Half4(a.X / b.X, a.Y / b.Y, a.Z / b.Z, a.W / b.W);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector4(Half4 a)
        {
            return new Vector4((float)a.X, (float)a.Y, (float)a.Z, (float)a.W);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Half4(Vector4 a)
        {
            return new Half4((Half)a.X, (Half)a.Y, (Half)a.Z, (Half)a.W);
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z}, {W})";
        }
    }
}
