using System.Runtime.CompilerServices;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Vocore.Graphics;

[StructLayout(LayoutKind.Sequential)]
public struct ColorFloat
{
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

    //overload operator

    public static implicit operator Vector4(ColorFloat color) => color.value;
    public static implicit operator ColorFloat(Vector4 color) => new ColorFloat { value = color };

    // + - * /
    public static ColorFloat operator +(ColorFloat a, ColorFloat b) => new ColorFloat { value = a.value + b.value };
    public static ColorFloat operator -(ColorFloat a, ColorFloat b) => new ColorFloat { value = a.value - b.value };
    public static ColorFloat operator *(ColorFloat a, ColorFloat b) => new ColorFloat { value = a.value * b.value };
    public static ColorFloat operator /(ColorFloat a, ColorFloat b) => new ColorFloat { value = a.value / b.value };

}