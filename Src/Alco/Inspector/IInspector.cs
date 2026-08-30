using System;
using System.Numerics;

namespace Alco;

/// <summary>
/// A UI-agnostic, immediate-mode parameter editing contract (an "inspector").
/// <para>
/// Engine code (assets, components, render node descriptors, ...) depends only
/// on this interface — defined in the dependency-free base module — to expose
/// parameters for interactive editing, so the engine itself never references a
/// UI toolkit. Concrete editors implement the interface on top of their own
/// toolkit; the reference implementation is <c>ImGuiInspector</c> (Alco.ImGUI).
/// </para>
/// <para>
/// Contract rules:
/// <list type="bullet">
/// <item>Immediate mode: call the widgets every frame while the parameter rows
/// should stay visible.</item>
/// <item>Edits happen in place through the <c>ref</c> parameter; a widget
/// returns true only when the user edited the value during this frame (the
/// <c>ref</c> value already holds the new value when it does).</item>
/// <item>The label doubles as the stable widget identifier; keep it unique
/// within one panel.</item>
/// <item>Text parameters are spans (<see cref="ReadOnlySpan{T}"/>): labels are
/// usually literals and must not be stored by implementations — widgets draw
/// immediately.</item>
/// <item>Scalar drag widgets are <c>Drag*</c>; multi-component vector editors
/// are <c>Edit*</c>; drag bounds default to unbounded; sliders require a range.</item>
/// </list>
/// </para>
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
    bool DragFloat(ReadOnlySpan<char> label, ref float value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity);

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
    bool DragInt(ReadOnlySpan<char> label, ref int value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue);

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
    /// names in declaration order (cached per enum type).
    /// </summary>
    bool Combo<T>(ReadOnlySpan<char> label, ref T value) where T : struct, Enum
    {
        string[] names = EnumCache<T>.Names;
        int index = -1;
        string current = value.ToString();
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], current, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0 || !Combo(label, ref index, names))
        {
            return false;
        }

        value = EnumCache<T>.Values[index];
        return true;
    }

    /// <summary>Draws an RGB color editor (alpha kept unchanged).</summary>
    bool ColorEdit3(ReadOnlySpan<char> label, ref Vector3 color);

    /// <summary>Draws an RGBA color editor; <paramref name="hdr"/> enables editing values above 1.</summary>
    bool ColorEdit4(ReadOnlySpan<char> label, ref Vector4 color, bool hdr = false);
}

/// <summary>
/// Per-enum-type cached member names and values backing
/// <see cref="IInspector.Combo{T}"/>.
/// </summary>
file sealed class EnumCache<T> where T : struct, Enum
{
    /// <summary>The enum member names in declaration order.</summary>
    public static readonly string[] Names = Enum.GetNames<T>();

    /// <summary>The enum member values in declaration order (aligned with <see cref="Names"/>).</summary>
    public static readonly T[] Values = Enum.GetValues<T>();
}
