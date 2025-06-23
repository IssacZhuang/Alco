using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Alco;

/// <summary>
/// Represents padding values for all four edges: left, top, right, and bottom.
/// Commonly used for UI layout and 9-slice sprite rendering.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Padding : IEquatable<Padding>
{
    /// <summary>
    /// A Padding4 with all values set to zero.
    /// </summary>
    public static readonly Padding Zero = new(0, 0, 0, 0);

    /// <summary>
    /// Internal Vector4 value for SIMD optimization (X=Left, Y=Top, Z=Right, W=Bottom).
    /// </summary>
    public Vector4 value;

    /// <summary>
    /// Initializes a new instance of Padding4 with the same value for all edges.
    /// </summary>
    /// <param name="all">The value to use for all edges.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Padding(float all)
    {
        value = new Vector4(all);
    }

    /// <summary>
    /// Initializes a new instance of Padding4 with horizontal and vertical values.
    /// </summary>
    /// <param name="horizontal">The value to use for left and right edges.</param>
    /// <param name="vertical">The value to use for top and bottom edges.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Padding(float horizontal, float vertical)
    {
        value = new Vector4(horizontal, vertical, horizontal, vertical);
    }

    /// <summary>
    /// Initializes a new instance of Padding4 with individual values for each edge.
    /// </summary>
    /// <param name="left">The left padding value.</param>
    /// <param name="top">The top padding value.</param>
    /// <param name="right">The right padding value.</param>
    /// <param name="bottom">The bottom padding value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Padding(float left, float top, float right, float bottom)
    {
        value = new Vector4(left, top, right, bottom);
    }

    /// <summary>
    /// The left padding value.
    /// </summary>
    public float Left
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => value.X;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => this.value.X = value;
    }

    /// <summary>
    /// The top padding value.
    /// </summary>
    public float Top
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => value.Y;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => this.value.Y = value;
    }

    /// <summary>
    /// The right padding value.
    /// </summary>
    public float Right
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => value.Z;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => this.value.Z = value;
    }

    /// <summary>
    /// The bottom padding value.
    /// </summary>
    public float Bottom
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => value.W;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => this.value.W = value;
    }

    /// <summary>
    /// Gets the total horizontal padding (left + right).
    /// </summary>
    public float Horizontal
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Left + Right;
    }

    /// <summary>
    /// Gets the total vertical padding (top + bottom).
    /// </summary>
    public float Vertical
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Top + Bottom;
    }

    /// <summary>
    /// Implicitly converts a Padding4 to a Vector4.
    /// </summary>
    /// <param name="padding">The Padding4 to convert.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector4(Padding padding) => padding.value;

    /// <summary>
    /// Implicitly converts a Vector4 to a Padding4.
    /// </summary>
    /// <param name="vector">The Vector4 to convert.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Padding(Vector4 vector) => new Padding { value = vector };

    /// <summary>
    /// Adds two Padding4 values.
    /// </summary>
    /// <param name="left">The first padding.</param>
    /// <param name="right">The second padding.</param>
    /// <returns>The sum of the two paddings.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Padding operator +(Padding left, Padding right)
        => new Padding { value = left.value + right.value };

    /// <summary>
    /// Subtracts one Padding4 from another.
    /// </summary>
    /// <param name="left">The first padding.</param>
    /// <param name="right">The second padding.</param>
    /// <returns>The difference of the two paddings.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Padding operator -(Padding left, Padding right)
        => new Padding { value = left.value - right.value };

    /// <summary>
    /// Multiplies a Padding4 by a scalar value.
    /// </summary>
    /// <param name="padding">The padding to multiply.</param>
    /// <param name="scalar">The scalar value.</param>
    /// <returns>The scaled padding.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Padding operator *(Padding padding, float scalar)
        => new Padding { value = padding.value * scalar };

    /// <summary>
    /// Multiplies a Padding4 by a scalar value.
    /// </summary>
    /// <param name="scalar">The scalar value.</param>
    /// <param name="padding">The padding to multiply.</param>
    /// <returns>The scaled padding.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Padding operator *(float scalar, Padding padding)
        => new Padding { value = scalar * padding.value };

    /// <summary>
    /// Divides a Padding4 by a scalar value.
    /// </summary>
    /// <param name="padding">The padding to divide.</param>
    /// <param name="scalar">The scalar value.</param>
    /// <returns>The divided padding.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Padding operator /(Padding padding, float scalar)
        => new Padding { value = padding.value / scalar };

    /// <summary>
    /// Checks if two Padding4 values are equal.
    /// </summary>
    /// <param name="left">The first padding.</param>
    /// <param name="right">The second padding.</param>
    /// <returns>True if the paddings are equal; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Padding left, Padding right)
        => left.value == right.value;

    /// <summary>
    /// Checks if two Padding4 values are not equal.
    /// </summary>
    /// <param name="left">The first padding.</param>
    /// <param name="right">The second padding.</param>
    /// <returns>True if the paddings are not equal; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Padding left, Padding right)
        => left.value != right.value;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Padding other) => value == other.value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Padding other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => value.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => $"Padding4(Left={Left}, Top={Top}, Right={Right}, Bottom={Bottom})";
}