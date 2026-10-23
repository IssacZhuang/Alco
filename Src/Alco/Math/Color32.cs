using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Alco;

/// <summary>
/// A color in the 8-bit format, which can only be used for SDR color
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Color32
{
    /// <summary>
    /// A fully opaque white color.
    /// </summary>
    public static readonly Color32 White = new(255, 255, 255, 255);
    /// <summary>
    /// A fully opaque black color.
    /// </summary>
    public static readonly Color32 Black = new(0, 0, 0, 255);
    /// <summary>
    /// A fully transparent black color.
    /// </summary>
    public static readonly Color32 Transparent = new(0, 0, 0, 0);

    public const float Inv255 = 1f / 255f;
    public byte R;
    public byte G;
    public byte B;
    public byte A;

    /// <summary>
    /// Creates a color from its RGBA components.
    /// </summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="a">The alpha component.</param>
    public Color32(byte r, byte g, byte b, byte a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>
    /// Creates a fully opaque color from its RGB components.
    /// </summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    public Color32(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
        A = 255;
    }

    /// <summary>
    /// Try to parse a hex color string to a ColorFloat
    /// </summary>
    /// <param name="hex">The hex color string</param>
    /// <param name="color">The parsed color</param>
    /// <returns>True if the parsing is successful, false otherwise</returns>
    public static bool TryParse(ReadOnlySpan<char> hex, out Color32 color)
    {
        if (hex.Length <= 0)
        {
            color = default;
            return false;
        }
        if (hex[0] == '#')
        {
            hex = hex.Slice(1);
        }
        if (hex.Length == 6)
        {
            uint uintColor = uint.Parse(hex, NumberStyles.HexNumber);
            uintColor = (uintColor << 8) | 0xFF;
            color = (Color32)uintColor;
            return true;
        }
        if (hex.Length == 8)
        {
            uint uintColor = uint.Parse(hex, NumberStyles.HexNumber);
            color = (Color32)uintColor;
            return true;
        }
        color = default;
        return false;
    }

    /// <summary>
    /// Packs the color into a 32-bit RGBA value.
    /// </summary>
    /// <param name="color">The color to pack.</param>
    public static implicit operator uint (Color32 color)
    {
        return (uint)(color.R << 24 | color.G << 16 | color.B << 8 | color.A);
    }

    /// <summary>
    /// Creates a color from a packed 32-bit RGBA value.
    /// </summary>
    /// <param name="color">The packed value.</param>
    // convet hex color in 8 digit to ColorFloat (rgba)
    public static implicit operator Color32(uint color)
    {
        return new Color32((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color);
    }

    /// <summary>
    /// Creates a fully opaque color from the low 24 bits of an integer as RGB.
    /// </summary>
    /// <param name="color">The packed RGB value.</param>
    // convet hex color in 6 digit to ColorFloat (rgb with a = 1)
    public static implicit operator Color32(int color)
    {
        return new Color32((byte)(color >> 16), (byte)(color >> 8), (byte)color);
    }

    // + - * /
    /// <summary>
    /// Adds two colors component-wise.
    /// </summary>
    /// <param name="a">The first color.</param>
    /// <param name="b">The second color.</param>
    /// <returns>The component-wise sum.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 operator +(Color32 a, Color32 b)
    {
        return new Color32((byte)(a.R + b.R), (byte)(a.G + b.G), (byte)(a.B + b.B), (byte)(a.A + b.A));
    }

    /// <summary>
    /// Subtracts two colors component-wise.
    /// </summary>
    /// <param name="a">The first color.</param>
    /// <param name="b">The color to subtract.</param>
    /// <returns>The component-wise difference.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 operator -(Color32 a, Color32 b)
    {
        return new Color32((byte)(a.R - b.R), (byte)(a.G - b.G), (byte)(a.B - b.B), (byte)(a.A - b.A));
    }

    /// <summary>
    /// Multiplies two colors component-wise.
    /// </summary>
    /// <param name="a">The first color.</param>
    /// <param name="b">The second color.</param>
    /// <returns>The component-wise product.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 operator *(Color32 a, Color32 b)
    {
        return new Color32((byte)(a.R * b.R), (byte)(a.G * b.G), (byte)(a.B * b.B), (byte)(a.A * b.A));
    }

    /// <summary>
    /// Divides two colors component-wise.
    /// </summary>
    /// <param name="a">The dividend color.</param>
    /// <param name="b">The divisor color.</param>
    /// <returns>The component-wise quotient.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 operator /(Color32 a, Color32 b)
    {
        return new Color32((byte)(a.R / b.R), (byte)(a.G / b.G), (byte)(a.B / b.B), (byte)(a.A / b.A));
    }

    /// <summary>
    /// Converts this color to a float color in the range [0, 1].
    /// </summary>
    /// <param name="color">The color to convert.</param>
    public static implicit operator ColorFloat(Color32 color)
    {
        return new ColorFloat(color.R * Inv255, color.G * Inv255, color.B * Inv255, color.A * Inv255);
    }

    /// <summary>
    /// Converts this color to an RGBA vector in the range [0, 1].
    /// </summary>
    /// <param name="color">The color to convert.</param>
    public static implicit operator Vector4(Color32 color)
    {
        return new Vector4(color.R * Inv255, color.G * Inv255, color.B * Inv255, color.A * Inv255);
    }

    /// <summary>
    /// Linearly interpolates between two colors, clamping each channel to [0, 255].
    /// </summary>
    /// <param name="a">The start color.</param>
    /// <param name="b">The end color.</param>
    /// <param name="t">The interpolation factor in the range [0, 1].</param>
    /// <returns>The interpolated color.</returns>
    public static Color32 Lerp(Color32 a, Color32 b, float t)
    {
        float r = a.R + (b.R - a.R) * t;
        float g = a.G + (b.G - a.G) * t;
        float bl = a.B + (b.B - a.B) * t;
        float al = a.A + (b.A - a.A) * t;

        return new Color32(
            (byte)Math.Clamp(r, 0, 255),
            (byte)Math.Clamp(g, 0, 255),
            (byte)Math.Clamp(bl, 0, 255),
            (byte)Math.Clamp(al, 0, 255)
        );
    }
}