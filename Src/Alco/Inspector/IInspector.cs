using System;
using System.Numerics;

namespace Alco;

/// <summary>
/// A UI-agnostic, immediate-mode parameter editing contract.
/// Widgets are called every frame while a row should stay visible, edits occur in place through ref parameters, and the label doubles as the stable widget identifier within a panel.
/// </summary>
public interface IInspector
{
    /// <summary>Draws a read-only text line (titles, diagnostics, non-editable values).</summary>
    void Text(ReadOnlySpan<char> text);

    /// <summary>Draws a horizontal separator line.</summary>
    void Separator();

    /// <summary>
    /// Draws a collapsible section header.
    /// Returns true while the section is expanded; draw the section content
    /// right after the call when it is.
    /// </summary>
    bool CollapsingHeader(ReadOnlySpan<char> label);

    /// <summary>
    /// Drags a float value. <paramref name="speed"/> scales the drag delta;
    /// <paramref name="min"/>/<paramref name="max"/> clamp the value when
    /// <paramref name="min"/> &lt; <paramref name="max"/> (the defaults are unbounded).
    /// </summary>
    bool EditFloat(ReadOnlySpan<char> label, ref float value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity);

    /// <summary>
    /// Edits a <see cref="Vector2"/> value; <paramref name="min"/>/<paramref name="max"/>
    /// clamp every component when <paramref name="min"/> &lt; <paramref name="max"/>.
    /// </summary>
    bool EditFloat2(ReadOnlySpan<char> label, ref Vector2 value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity);

    /// <summary>
    /// Edits a <see cref="Vector3"/> value; <paramref name="min"/>/<paramref name="max"/>
    /// clamp every component when <paramref name="min"/> &lt; <paramref name="max"/>.
    /// </summary>
    bool EditFloat3(ReadOnlySpan<char> label, ref Vector3 value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity);

    /// <summary>
    /// Edits a <see cref="Vector4"/> value; <paramref name="min"/>/<paramref name="max"/>
    /// clamp every component when <paramref name="min"/> &lt; <paramref name="max"/>.
    /// </summary>
    bool EditFloat4(ReadOnlySpan<char> label, ref Vector4 value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity);

    /// <summary>
    /// Drags an int value. <paramref name="speed"/> scales the drag delta;
    /// <paramref name="min"/>/<paramref name="max"/> clamp the value when
    /// <paramref name="min"/> &lt; <paramref name="max"/> (the defaults are unbounded).
    /// </summary>
    bool EditInt(ReadOnlySpan<char> label, ref int value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue);

    /// <summary>
    /// Edits an <see cref="int2"/> value; <paramref name="min"/>/<paramref name="max"/>
    /// clamp every component when <paramref name="min"/> &lt; <paramref name="max"/>.
    /// </summary>
    bool EditInt2(ReadOnlySpan<char> label, ref int2 value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue);

    /// <summary>
    /// Edits an <see cref="int3"/> value; <paramref name="min"/>/<paramref name="max"/>
    /// clamp every component when <paramref name="min"/> &lt; <paramref name="max"/>.
    /// </summary>
    bool EditInt3(ReadOnlySpan<char> label, ref int3 value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue);

    /// <summary>
    /// Edits an <see cref="int4"/> value; <paramref name="min"/>/<paramref name="max"/>
    /// clamp every component when <paramref name="min"/> &lt; <paramref name="max"/>.
    /// </summary>
    bool EditInt4(ReadOnlySpan<char> label, ref int4 value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue);

    /// <summary>Drags a float value restricted to the inclusive [<paramref name="min"/>, <paramref name="max"/>] range.</summary>
    bool SliderFloat(ReadOnlySpan<char> label, ref float value, float min, float max);

    /// <summary>Drags an int value restricted to the inclusive [<paramref name="min"/>, <paramref name="max"/>] range.</summary>
    bool SliderInt(ReadOnlySpan<char> label, ref int value, int min, int max);

    /// <summary>Draws a toggle checkbox.</summary>
    bool Checkbox(ReadOnlySpan<char> label, ref bool value);

    /// <summary>
    /// Draws a single-line text input; <paramref name="maxLength"/> bounds the
    /// accepted input length (the UI buffers the text internally).
    /// </summary>
    bool InputText(ReadOnlySpan<char> label, ref string value, uint maxLength = 256);

    /// <summary>
    /// Draws a combo box selecting one entry of <paramref name="items"/>;
    /// <paramref name="selectedIndex"/> is the displayed/selected entry index.
    /// </summary>
    bool Combo(ReadOnlySpan<char> label, ref int selectedIndex, ReadOnlySpan<string> items);

    /// <summary>
    /// Draws a combo box over an enum value; the entries are the enum member
    /// names in declaration order.
    /// </summary>
    bool Combo<T>(ReadOnlySpan<char> label, ref T value) where T : struct, Enum;

    /// <summary>Draws an RGB color editor (alpha kept unchanged).</summary>
    bool ColorEdit3(ReadOnlySpan<char> label, ref Vector3 color);

    /// <summary>Draws an RGBA color editor; <paramref name="hdr"/> enables editing values above 1.</summary>
    bool ColorEdit4(ReadOnlySpan<char> label, ref Vector4 color, bool hdr = false);
}
