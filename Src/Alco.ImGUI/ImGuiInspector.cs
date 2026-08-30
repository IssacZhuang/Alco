using System;
using System.Numerics;

namespace Alco.ImGUI;

/// <summary>
/// The ImGui-backed reference implementation of <see cref="IInspector"/>:
/// forwards every widget to the vendored static <see cref="ImGui"/> binding
/// (through its ReadOnlySpan overloads, keeping label handling allocation-free).
/// Requires an active ImGUI context (see <see cref="ImGUISystem"/>) on the
/// calling thread.
/// </summary>
public sealed class ImGuiInspector : IInspector
{
    /// <inheritdoc />
    public void Text(ReadOnlySpan<char> text) => ImGui.Text(text);

    /// <inheritdoc />
    public void Separator() => ImGui.Separator();

    /// <inheritdoc />
    public bool CollapsingHeader(ReadOnlySpan<char> label) => ImGui.CollapsingHeader(label);

    /// <inheritdoc />
    public bool EditFloat(ReadOnlySpan<char> label, ref float value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity)
        => ImGui.DragFloat(label, ref value, speed, min, max);

    /// <inheritdoc />
    public bool EditFloat2(ReadOnlySpan<char> label, ref Vector2 value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity)
        => ImGui.DragFloat2(label, ref value, speed, min, max);

    /// <inheritdoc />
    public bool EditFloat3(ReadOnlySpan<char> label, ref Vector3 value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity)
        => ImGui.DragFloat3(label, ref value, speed, min, max);

    /// <inheritdoc />
    public bool EditFloat4(ReadOnlySpan<char> label, ref Vector4 value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity)
        => ImGui.DragFloat4(label, ref value, speed, min, max);

    /// <inheritdoc />
    public bool EditInt(ReadOnlySpan<char> label, ref int value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue)
        => ImGui.DragInt(label, ref value, speed, min, max);

    /// <inheritdoc />
    public bool EditInt2(ReadOnlySpan<char> label, ref int2 value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue)
    {
        // The binding exposes the native N-int widgets as a ref to the first of
        // N contiguous ints; stage the components through stack memory.
        Span<int> components = stackalloc int[2] { value.X, value.Y };
        bool edited = ImGui.DragInt2(label, ref components[0], speed, min, max);
        if (edited)
        {
            value = new int2(components[0], components[1]);
        }

        return edited;
    }

    /// <inheritdoc />
    public bool EditInt3(ReadOnlySpan<char> label, ref int3 value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue)
    {
        Span<int> components = stackalloc int[3] { value.X, value.Y, value.Z };
        bool edited = ImGui.DragInt3(label, ref components[0], speed, min, max);
        if (edited)
        {
            value = new int3(components[0], components[1], components[2]);
        }

        return edited;
    }

    /// <inheritdoc />
    public bool EditInt4(ReadOnlySpan<char> label, ref int4 value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue)
    {
        Span<int> components = stackalloc int[4] { value.X, value.Y, value.Z, value.W };
        bool edited = ImGui.DragInt4(label, ref components[0], speed, min, max);
        if (edited)
        {
            value = new int4(components[0], components[1], components[2], components[3]);
        }

        return edited;
    }

    /// <inheritdoc />
    public bool SliderFloat(ReadOnlySpan<char> label, ref float value, float min, float max)
        => ImGui.SliderFloat(label, ref value, min, max);

    /// <inheritdoc />
    public bool SliderInt(ReadOnlySpan<char> label, ref int value, int min, int max)
        => ImGui.SliderInt(label, ref value, min, max);

    /// <inheritdoc />
    public bool Checkbox(ReadOnlySpan<char> label, ref bool value)
        => ImGui.Checkbox(label, ref value);

    /// <inheritdoc />
    public bool InputText(ReadOnlySpan<char> label, ref string value, uint maxLength = 256)
        => ImGui.InputText(label, ref value, maxLength);

    /// <inheritdoc />
    public bool Combo(ReadOnlySpan<char> label, ref int selectedIndex, ReadOnlySpan<string> items)
    {
        // The native combo consumes a char*[]; materialize the span (the only
        // allocating widget in this backend — combos are rare per frame).
        return ImGui.Combo(label, ref selectedIndex, items.ToArray(), items.Length);
    }

    /// <inheritdoc />
    public bool Combo<T>(ReadOnlySpan<char> label, ref T value) where T : struct, Enum
        => ImGui.Combo(label, ref value);

    /// <inheritdoc />
    public bool ColorEdit3(ReadOnlySpan<char> label, ref Vector3 color)
        => ImGui.ColorEdit3(label, ref color, ImGuiColorEditFlags.None);

    /// <inheritdoc />
    public bool ColorEdit4(ReadOnlySpan<char> label, ref Vector4 color, bool hdr = false)
        => ImGui.ColorEdit4(label, ref color, hdr ? ImGuiColorEditFlags.HDR : ImGuiColorEditFlags.None);
}
