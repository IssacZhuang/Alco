//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct Half4
    {
        public Half x;
        public Half y;
        public Half z;
        public Half w;

        public Half4(Half value)
        {
            this.x = value;
            this.y = value;
            this.z = value;
            this.w = value;
        }

        public Half4(int value)
        {
            this.x = (Half)value;
            this.y = (Half)value;
            this.z = (Half)value;
            this.w = (Half)value;
        }

        public Half4(uint value)
        {
            this.x = (Half)value;
            this.y = (Half)value;
            this.z = (Half)value;
            this.w = (Half)value;
        }

        public Half4(float value)
        {
            this.x = (Half)value;
            this.y = (Half)value;
            this.z = (Half)value;
            this.w = (Half)value;
        }

        public Half4(Half x, Half y, Half z, Half w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public Half4(int x, int y, int z, int w)
        {
            this.x = (Half)x;
            this.y = (Half)y;
            this.z = (Half)z;
            this.w = (Half)w;
        }

        public Half4(uint x, uint y, uint z, uint w)
        {
            this.x = (Half)x;
            this.y = (Half)y;
            this.z = (Half)z;
            this.w = (Half)w;
        }

        public Half4(float x, float y, float z, float w)
        {
            this.x = (Half)x;
            this.y = (Half)y;
            this.z = (Half)z;
            this.w = (Half)w;
        }

        public Half4(Half2 value, Half z, Half w)
        {
            this.x = value.x;
            this.y = value.y;
            this.z = z;
            this.w = w;
        }

        public Half4(Half3 value, Half w)
        {
            this.x = value.x;
            this.y = value.y;
            this.z = value.z;
            this.w = w;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 operator +(Half4 a, Half4 b)
        {
            return new Half4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 operator -(Half4 a, Half4 b)
        {
            return new Half4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 operator *(Half4 a, Half4 b)
        {
            return new Half4(a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 operator /(Half4 a, Half4 b)
        {
            return new Half4(a.x / b.x, a.y / b.y, a.z / b.z, a.w / b.w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector4(Half4 a)
        {
            return new Vector4((float)a.x, (float)a.y, (float)a.z, (float)a.w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Half4(Vector4 a)
        {
            return new Half4((Half)a.X, (Half)a.Y, (Half)a.Z, (Half)a.W);
        }

        public override string ToString()
        {
            return $"({x}, {y}, {z}, {w})";
        }
    }
}
