using System;
using System.Numerics;

namespace Alco;

/// <summary>
/// A do-nothing <see cref="IInspector"/>: every widget renders nothing and
/// reports no edit. Lets code run <see cref="IInspectable.Inspect"/> paths
/// without any UI (headless tools, validation, tests).
/// </summary>
public sealed class NullInspector : IInspector
{
    /// <summary>The shared stateless no-op instance.</summary>
    public static readonly NullInspector Instance = new();

    private NullInspector()
    {
    }

    /// <inheritdoc />
    public void Text(ReadOnlySpan<char> text)
    {
    }

    /// <inheritdoc />
    public void Separator()
    {
    }

    /// <inheritdoc />
    public bool CollapsingHeader(ReadOnlySpan<char> label) => false;

    /// <inheritdoc />
    public bool DragFloat(ReadOnlySpan<char> label, ref float value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity) => false;

    /// <inheritdoc />
    public bool EditFloat2(ReadOnlySpan<char> label, ref Vector2 value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity) => false;

    /// <inheritdoc />
    public bool EditFloat3(ReadOnlySpan<char> label, ref Vector3 value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity) => false;

    /// <inheritdoc />
    public bool EditFloat4(ReadOnlySpan<char> label, ref Vector4 value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity) => false;

    /// <inheritdoc />
    public bool DragInt(ReadOnlySpan<char> label, ref int value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue) => false;

    /// <inheritdoc />
    public bool EditInt2(ReadOnlySpan<char> label, ref int2 value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue) => false;

    /// <inheritdoc />
    public bool EditInt3(ReadOnlySpan<char> label, ref int3 value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue) => false;

    /// <inheritdoc />
    public bool EditInt4(ReadOnlySpan<char> label, ref int4 value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue) => false;

    /// <inheritdoc />
    public bool SliderFloat(ReadOnlySpan<char> label, ref float value, float min, float max) => false;

    /// <inheritdoc />
    public bool SliderInt(ReadOnlySpan<char> label, ref int value, int min, int max) => false;

    /// <inheritdoc />
    public bool Checkbox(ReadOnlySpan<char> label, ref bool value) => false;

    /// <inheritdoc />
    public bool InputText(ReadOnlySpan<char> label, ref string value, uint maxLength = 256) => false;

    /// <inheritdoc />
    public bool Combo(ReadOnlySpan<char> label, ref int selectedIndex, ReadOnlySpan<string> items) => false;

    /// <inheritdoc />
    public bool ColorEdit3(ReadOnlySpan<char> label, ref Vector3 color) => false;

    /// <inheritdoc />
    public bool ColorEdit4(ReadOnlySpan<char> label, ref Vector4 color, bool hdr = false) => false;
}
