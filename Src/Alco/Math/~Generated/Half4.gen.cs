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

        public Half4(Half X, Half Y, Half Z, Half W)
        {
            this.X = X;
            this.Y = Y;
            this.Z = Z;
            this.W = W;
        }

        public Half4(int X, int Y, int Z, int W)
        {
            this.X = (Half)X;
            this.Y = (Half)Y;
            this.Z = (Half)Z;
            this.W = (Half)W;
        }

        public Half4(uint X, uint Y, uint Z, uint W)
        {
            this.X = (Half)X;
            this.Y = (Half)Y;
            this.Z = (Half)Z;
            this.W = (Half)W;
        }

        public Half4(float X, float Y, float Z, float W)
        {
            this.X = (Half)X;
            this.Y = (Half)Y;
            this.Z = (Half)Z;
            this.W = (Half)W;
        }

        public Half4(Half2 value, Half Z, Half W)
        {
            this.X = value.X;
            this.Y = value.Y;
            this.Z = Z;
            this.W = W;
        }

        public Half4(Half3 value, Half W)
        {
            this.X = value.X;
            this.Y = value.Y;
            this.Z = value.Z;
            this.W = W;
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
        /// <summary>
        /// Negates the specified Half4 value.
        /// </summary>
        /// <param name="a">The value to negate.</param>
        /// <returns>A new Half4 with all components negated.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 operator -(Half4 a)
        {
            return new Half4(-a.X, -a.Y, -a.Z, -a.W);
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
