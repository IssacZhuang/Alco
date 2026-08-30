using System.Collections.Generic;
using System.Numerics;
using Alco;

namespace TestFramework;

/// <summary>
/// A scripted <see cref="IInspector"/> for tests: it records every widget call
/// (as <c>"Widget:label"</c> strings in <see cref="Calls"/>) and applies
/// label-keyed scripted edits registered through <see cref="Edit"/> — the
/// widget whose label matches reports "edited" once and writes the scripted
/// value through its <c>ref</c> parameter. Enum combos are scripted with the
/// enum value itself.
/// </summary>
public sealed class InspectorRecorder : IInspector
{
    private readonly Dictionary<string, object> _scriptedEdits = new();

    /// <summary>The widget calls made so far, in call order.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>
    /// Registers the value the widget with <paramref name="label"/> reports as
    /// edited the next time it is called.
    /// </summary>
    public InspectorRecorder Edit(string label, object value)
    {
        _scriptedEdits[label] = value;
        return this;
    }

    /// <inheritdoc />
    public void Text(ReadOnlySpan<char> text) => Calls.Add("Text");

    /// <inheritdoc />
    public void Separator() => Calls.Add("Separator");

    /// <inheritdoc />
    public bool CollapsingHeader(ReadOnlySpan<char> label)
    {
        Calls.Add($"CollapsingHeader:{label.ToString()}");
        return false;
    }

    /// <inheritdoc />
    public bool EditFloat(ReadOnlySpan<char> label, ref float value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity)
        => Take("EditFloat", label, ref value);

    /// <inheritdoc />
    public bool EditFloat2(ReadOnlySpan<char> label, ref Vector2 value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity)
        => Take("EditFloat2", label, ref value);

    /// <inheritdoc />
    public bool EditFloat3(ReadOnlySpan<char> label, ref Vector3 value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity)
        => Take("EditFloat3", label, ref value);

    /// <inheritdoc />
    public bool EditFloat4(ReadOnlySpan<char> label, ref Vector4 value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity)
        => Take("EditFloat4", label, ref value);

    /// <inheritdoc />
    public bool EditInt(ReadOnlySpan<char> label, ref int value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue)
        => Take("EditInt", label, ref value);

    /// <inheritdoc />
    public bool EditInt2(ReadOnlySpan<char> label, ref int2 value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue)
    {
        int[] components = [value.X, value.Y];
        bool edited = Take("EditInt2", label, ref components[0]);
        if (edited)
        {
            value = new int2(components[0], components[1]);
        }

        return edited;
    }

    /// <inheritdoc />
    public bool EditInt3(ReadOnlySpan<char> label, ref int3 value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue)
    {
        int[] components = [value.X, value.Y, value.Z];
        bool edited = Take("EditInt3", label, ref components[0]);
        if (edited)
        {
            value = new int3(components[0], components[1], components[2]);
        }

        return edited;
    }

    /// <inheritdoc />
    public bool EditInt4(ReadOnlySpan<char> label, ref int4 value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue)
    {
        int[] components = [value.X, value.Y, value.Z, value.W];
        bool edited = Take("EditInt4", label, ref components[0]);
        if (edited)
        {
            value = new int4(components[0], components[1], components[2], components[3]);
        }

        return edited;
    }

    /// <inheritdoc />
    public bool SliderFloat(ReadOnlySpan<char> label, ref float value, float min, float max)
        => Take("SliderFloat", label, ref value);

    /// <inheritdoc />
    public bool SliderInt(ReadOnlySpan<char> label, ref int value, int min, int max)
        => Take("SliderInt", label, ref value);

    /// <inheritdoc />
    public bool Checkbox(ReadOnlySpan<char> label, ref bool value)
        => Take("Checkbox", label, ref value);

    /// <inheritdoc />
    public bool InputText(ReadOnlySpan<char> label, ref string value, uint maxLength = 256)
        => Take("InputText", label, ref value);

    /// <inheritdoc />
    public bool Combo(ReadOnlySpan<char> label, ref int selectedIndex, ReadOnlySpan<string> items)
        => Take("Combo", label, ref selectedIndex);

    /// <inheritdoc />
    public bool Combo<T>(ReadOnlySpan<char> label, ref T value) where T : struct, Enum
        => Take("Combo", label, ref value);

    /// <inheritdoc />
    public bool ColorEdit3(ReadOnlySpan<char> label, ref Vector3 color)
        => Take("ColorEdit3", label, ref color);

    /// <inheritdoc />
    public bool ColorEdit4(ReadOnlySpan<char> label, ref Vector4 color, bool hdr = false)
        => Take("ColorEdit4", label, ref color);

    private bool Take<T>(string widget, ReadOnlySpan<char> label, ref T value)
    {
        string labelString = label.ToString();
        Calls.Add($"{widget}:{labelString}");
        if (!_scriptedEdits.TryGetValue(labelString, out object? scripted) || scripted is not T typed)
        {
            return false;
        }

        _scriptedEdits.Remove(labelString);
        value = typed;
        return true;
    }
}
