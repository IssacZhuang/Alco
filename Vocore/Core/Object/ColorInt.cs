using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Vocore
{
    public struct ColorInt
    {
        public int r;
        public int g;
        public int b;
        public int a;

        public ColorInt(int r, int g, int b, int a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public ColorInt(int r, int g, int b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            a = 255;
        }

        public ColorInt(Color color)
        {
            r = (int)(color.r * 255);
            g = (int)(color.g * 255);
            b = (int)(color.b * 255);
            a = (int)(color.a * 255);
        }

        public Color ToColor()
        {
            return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        }

        //overriding operators
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorInt operator +(ColorInt a, ColorInt b)
        {
            return new ColorInt(a.r + b.r, a.g + b.g, a.b + b.b, a.a + b.a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorInt operator -(ColorInt a, ColorInt b)
        {
            return new ColorInt(a.r - b.r, a.g - b.g, a.b - b.b, a.a - b.a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorInt operator *(ColorInt a, ColorInt b)
        {
            return new ColorInt(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorInt operator /(ColorInt a, ColorInt b)
        {
            return new ColorInt(a.r / b.r, a.g / b.g, a.b / b.b, a.a / b.a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorInt operator *(ColorInt a, int b)
        {
            return new ColorInt(a.r * b, a.g * b, a.b * b, a.a * b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorInt operator /(ColorInt a, int b)
        {
            return new ColorInt(a.r / b, a.g / b, a.b / b, a.a / b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorInt operator *(ColorInt a, float b)
        {
            return new ColorInt((int)(a.r * b), (int)(a.g * b), (int)(a.b * b), (int)(a.a * b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorInt operator /(ColorInt a, float b)
        {
            return new ColorInt((int)(a.r / b), (int)(a.g / b), (int)(a.b / b), (int)(a.a / b));
        }
    }
}