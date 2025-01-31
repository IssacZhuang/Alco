using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        public static readonly Half FP16Zero = (Half)0.0f;
        public static readonly Half FP16One = (Half)1.0f;
        public static readonly Half FP16TORADIANS = (Half)TORADIANS;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half min(Half a, Half b)
        {
            return a < b ? a : b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half max(Half a, Half b)
        {
            return a > b ? a : b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half abs(Half a)
        {
            return a < FP16Zero ? -a : a;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half select(Half a, Half b, bool test)
        {
            return test ? b : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half sign(Half x)
        {
            return (x > FP16Zero ? FP16One : FP16Zero) - (x < FP16Zero ? FP16One : FP16Zero);
        }



        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half clamp(Half a, Half min, Half max)
        {
            return a < min ? min : (a > max ? max : a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half floor(Half a)
        {
            return (Half)MathF.Floor((float)a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half ceil(Half a)
        {
            return (Half)MathF.Ceiling((float)a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half round(Half a)
        {
            return (Half)MathF.Round((float)a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half pow(Half a, Half b)
        {
            return (Half)MathF.Pow((float)a, (float)b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half sqrt(Half a)
        {
            return (Half)MathF.Sqrt((float)a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half log(Half a)
        {
            return (Half)MathF.Log((float)a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half log(Half a, Half b)
        {
            return (Half)MathF.Log((float)a, (float)b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half log2(Half a)
        {
            return (Half)MathF.Log((float)a, 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half log10(Half a)
        {
            return (Half)MathF.Log10((float)a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half radians(Half a)
        {
            return a * FP16TORADIANS;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half lerp(Half a, Half b, Half t)
        {
            return a + (b - a) * t;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half acos(Half a)
        {
            return (Half)MathF.Acos((float)a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half asin(Half a)
        {
            return (Half)MathF.Asin((float)a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half atan(Half a)
        {
            return (Half)MathF.Atan((float)a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half atan2(Half a, Half b)
        {
            return (Half)MathF.Atan2((float)a, (float)b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half cos(Half a)
        {
            return (Half)MathF.Cos((float)a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half sin(Half a)
        {
            return (Half)MathF.Sin((float)a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half tan(Half a)
        {
            return (Half)MathF.Tan((float)a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half cosh(Half a)
        {
            return (Half)MathF.Cosh((float)a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half sinh(Half a)
        {
            return (Half)MathF.Sinh((float)a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half tanh(Half a)
        {
            return (Half)MathF.Tanh((float)a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void sincos(Half a, out Half s, out Half c)
        {
            s = sin(a);
            c = cos(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static Half asHalf(uint a)
        {
            return *(Half*)&a;
        }

    }
}

