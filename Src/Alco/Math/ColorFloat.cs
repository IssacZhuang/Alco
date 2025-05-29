using System.Runtime.CompilerServices;
using System.Numerics;
using System.Runtime.InteropServices;
using System;
using System.Globalization;

namespace Alco;

/// <summary>
/// A color in the float format, which can be used for HDR color
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ColorFloat
{
    public static readonly ColorFloat Black = new(0, 0, 0, 1);
    public static readonly ColorFloat White = new(1, 1, 1, 1);

    private const float invMaxByte = 1.0f / 255.0f;
    private static readonly Vector4 invMaxByteVec4 = new(invMaxByte);
    public Vector4 value;

    public ColorFloat(float r, float g, float b, float a)
    {
        value = new Vector4(r, g, b, a);
    }

    public ColorFloat(float r, float g, float b)
    {
        value = new Vector4(r, g, b, 1);
    }

    public float R
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => value.X;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => this.value.X = value;
    }

    public float G
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => value.Y;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => this.value.Y = value;
    }

    public float B
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => value.Z;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => this.value.Z = value;
    }

    public float A
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => value.W;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => this.value.W = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ColorFloat Clamp01()
    {
        return Vector4.Clamp(value, Vector4.Zero, Vector4.One);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Color32 ToColor32()
    {
        ColorFloat color = Clamp01();
        return new Color32((byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255), (byte)(color.A * 255));
    }

    /// <summary>
    /// Decompose the HDR color into a SDR color and an intensity
    /// </summary>
    /// <param name="sdrColor">The color</param>
    /// <param name="intensity">The intensity</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Decompose(out Color32 sdrColor, out float intensity)
    {
        intensity = MathF.Max(MathF.Max(1, R), MathF.Max(G, B));
        ColorFloat sdr = this.value;
        sdr.R /= intensity;
        sdr.G /= intensity;
        sdr.B /= intensity;
        sdrColor = sdr.ToColor32();
    }

    /// <summary>
    /// Try to parse a hex color string to a ColorFloat
    /// </summary>
    /// <param name="hex">The hex color string</param>
    /// <param name="color">The parsed color</param>
    /// <returns>True if the parsing is successful, false otherwise</returns>
    public static bool TryParse(ReadOnlySpan<char> hex, out ColorFloat color)
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
            color = (ColorFloat)uintColor;
            return true;
        }
        if (hex.Length == 8)
        {
            uint uintColor = uint.Parse(hex, NumberStyles.HexNumber);
            color = (ColorFloat)uintColor;
            return true;
        }
        color = default;
        return false;
    }

    //overload operator

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector4(ColorFloat color) => color.value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Color32(ColorFloat color) => color.ToColor32();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ColorFloat(Vector4 color) => new ColorFloat { value = color };

    // convet hex color in 8 digit to ColorFloat (rgba)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ColorFloat(uint color)
    {
        return invMaxByteVec4 * new Vector4(
            (color & 0xFF000000) >> 24,
            (color & 0x00FF0000) >> 16,
            (color & 0x0000FF00) >> 8,
            color & 0x000000FF
        );
    }

    // convet hex color in 6 digit to ColorFloat (rgb with a = 1)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ColorFloat(int color)
    {
        return invMaxByteVec4 * new Vector4(
            (color & 0xFF0000) >> 16,
            (color & 0x00FF00) >> 8,
            color & 0x0000FF,
            255
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Half4(ColorFloat color)
    {
        return new Half4(color.R, color.G, color.B, color.A);
    }

    public static ColorFloat Lerp(ColorFloat a, ColorFloat b, float t)
    {
        return new ColorFloat
        {
            value = Vector4.Lerp(a.value, b.value, t)
        };
    }

    // + - * /
    public static ColorFloat operator +(ColorFloat a, ColorFloat b) => new ColorFloat { value = a.value + b.value };
    public static ColorFloat operator -(ColorFloat a, ColorFloat b) => new ColorFloat { value = a.value - b.value };
    public static ColorFloat operator *(ColorFloat a, ColorFloat b) => new ColorFloat { value = a.value * b.value };
    public static ColorFloat operator /(ColorFloat a, ColorFloat b) => new ColorFloat { value = a.value / b.value };

    
}