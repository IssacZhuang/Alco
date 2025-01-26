using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Alco.Graphics;

[StructLayout(LayoutKind.Sequential)]
public struct Color32
{
    public const float Inv255 = 1f / 255f;
    public byte R;
    public byte G;
    public byte B;
    public byte A;

    public Color32(byte r, byte g, byte b, byte a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public Color32(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
        A = 255;
    }

    public static implicit operator uint (Color32 color)
    {
        return (uint)(color.R << 24 | color.G << 16 | color.B << 8 | color.A);
    }

    // convet hex color in 8 digit to ColorFloat (rgba)
    public static implicit operator Color32(uint color)
    {
        return new Color32((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color);
    }

    // convet hex color in 6 digit to ColorFloat (rgb with a = 1)
    public static implicit operator Color32(int color)
    {
        return new Color32((byte)(color >> 16), (byte)(color >> 8), (byte)color);
    }

    // + - * /
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 operator +(Color32 a, Color32 b)
    {
        return new Color32((byte)(a.R + b.R), (byte)(a.G + b.G), (byte)(a.B + b.B), (byte)(a.A + b.A));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 operator -(Color32 a, Color32 b)
    {
        return new Color32((byte)(a.R - b.R), (byte)(a.G - b.G), (byte)(a.B - b.B), (byte)(a.A - b.A));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 operator *(Color32 a, Color32 b)
    {
        return new Color32((byte)(a.R * b.R), (byte)(a.G * b.G), (byte)(a.B * b.B), (byte)(a.A * b.A));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 operator /(Color32 a, Color32 b)
    {
        return new Color32((byte)(a.R / b.R), (byte)(a.G / b.G), (byte)(a.B / b.B), (byte)(a.A / b.A));
    }

    public static implicit operator ColorFloat(Color32 color)
    {
        return new ColorFloat(color.R * Inv255, color.G * Inv255, color.B * Inv255, color.A * Inv255);
    }

    public static implicit operator Vector4(Color32 color)
    {
        return new Vector4(color.R * Inv255, color.G * Inv255, color.B * Inv255, color.A * Inv255);
    }
}