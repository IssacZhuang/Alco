//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct Half3
    {
        public Half x;
        public Half y;
        public Half z;

        public Half3(Half value)
        {
            this.x = value;
            this.y = value;
            this.z = value;
        }

        public Half3(int value)
        {
            this.x = (Half)value;
            this.y = (Half)value;
            this.z = (Half)value;
        }

        public Half3(uint value)
        {
            this.x = (Half)value;
            this.y = (Half)value;
            this.z = (Half)value;
        }

        public Half3(float value)
        {
            this.x = (Half)value;
            this.y = (Half)value;
            this.z = (Half)value;
        }

        public Half3(Half x, Half y, Half z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public Half3(int x, int y, int z)
        {
            this.x = (Half)x;
            this.y = (Half)y;
            this.z = (Half)z;
        }

        public Half3(uint x, uint y, uint z)
        {
            this.x = (Half)x;
            this.y = (Half)y;
            this.z = (Half)z;
        }

        public Half3(float x, float y, float z)
        {
            this.x = (Half)x;
            this.y = (Half)y;
            this.z = (Half)z;
        }

        public Half3(Half2 value, Half z)
        {
            this.x = value.x;
            this.y = value.y;
            this.z = z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 operator +(Half3 a, Half3 b)
        {
            return new Half3(a.x + b.x, a.y + b.y, a.z + b.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 operator -(Half3 a, Half3 b)
        {
            return new Half3(a.x - b.x, a.y - b.y, a.z - b.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 operator *(Half3 a, Half3 b)
        {
            return new Half3(a.x * b.x, a.y * b.y, a.z * b.z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 operator /(Half3 a, Half3 b)
        {
            return new Half3(a.x / b.x, a.y / b.y, a.z / b.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector3(Half3 a)
        {
            return new Vector3((float)a.x, (float)a.y, (float)a.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Half3(Vector3 a)
        {
            return new Half3((Half)a.X, (Half)a.Y, (Half)a.Z);
        }

        public override string ToString()
        {
            return $"({x}, {y}, {z})";
        }
    }
}
