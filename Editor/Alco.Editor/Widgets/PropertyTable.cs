using System.Numerics;
using Alco.ImGUI;

namespace Alco.Editor;

/// <summary>
/// A two-column name/value layout for inspector panels: the label column keeps a
/// fixed width shared by every section, so labels are left-aligned and all value
/// widgets start at the same x; the value column fills the remaining width. Labels
/// longer than the column wrap to another line instead of pushing the values apart,
/// and multi-widget value cells flow onto extra lines instead of overflowing.
/// Create one instance per section in a <c>using</c> statement; the shared width
/// formula keeps the columns aligned across the whole panel. When the host window
/// skips items, the row helpers degrade to no-ops.
/// </summary>
public ref struct PropertyTable : IDisposable
{
    /// <summary>The label column width as a fraction of the available width.</summary>
    private const float LabelWidthFraction = 0.45f;

    /// <summary>The lower clamp of the label column width, in pixels.</summary>
    private const float LabelWidthMin = 110f;

    /// <summary>The upper clamp of the label column width, in pixels.</summary>
    private const float LabelWidthMax = 240f;

    /// <summary>The per-cell padding; rows end up twice this apart vertically.</summary>
    private static readonly Vector2 CellPadding = new(4f, 4f);

    /// <summary>The smallest drag field kept on one line in <see cref="Range"/> rows.</summary>
    private const float RangeDragWidthMin = 64f;

    /// <summary>The smallest two-component drag field kept on one line in <see cref="Range"/> rows.</summary>
    private const float RangeDrag2WidthMin = 110f;

    private readonly bool _open;

    /// <summary>Creates the table with its fixed label column and stretching value column.</summary>
    /// <param name="id">A unique ImGui id for the table within the current scope.</param>
    public PropertyTable(string id)
    {
        float labelWidth = Math.Clamp(ImGui.GetContentRegionAvail().X * LabelWidthFraction, LabelWidthMin, LabelWidthMax);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, CellPadding);
        _open = ImGui.BeginTable(id, 2, ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.NoPadOuterX);
        if (_open)
        {
            ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthFixed, labelWidth);
            ImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch);
        }
    }

    /// <summary>Ends the table and restores the cell padding style.</summary>
    public void Dispose()
    {
        ImGui.PopStyleVar();
        if (_open)
        {
            ImGui.EndTable();
        }
    }

    /// <summary>
    /// Starts a row: writes the label (wrapped when it exceeds the label column) and
    /// moves the cursor into the value column. The row stays open until the next row
    /// or the end of the table; pair with <see cref="EndCell"/> when drawing the value
    /// cell manually.
    /// </summary>
    /// <param name="label">Row label; also scopes the ids of the row's widgets.</param>
    public void BeginCell(string label)
    {
        ImGui.PushID(label);
        if (!_open)
        {
            return;
        }
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        float wrapWidth = ImGui.GetContentRegionAvail().X;
        if (ImGui.CalcTextSize(label).X <= wrapWidth)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(label);
        }
        else
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapWidth);
            ImGui.TextUnformatted(label);
            ImGui.PopTextWrapPos();
        }
        ImGui.TableNextColumn();
    }

    /// <summary>Closes a row started with <see cref="BeginCell"/>.</summary>
    public void EndCell()
    {
        ImGui.PopID();
    }

    /// <summary>A float drag row with the value widget filling the value column.</summary>
    public bool Drag(string label, ref float value, float speed, float min = 0f, float max = 0f)
    {
        BeginCell(label);
        ImGui.SetNextItemWidth(-1f);
        bool changed = ImGui.DragFloat("##value", ref value, speed, min, max);
        EndCell();
        return changed;
    }

    /// <summary>An int drag row with the value widget filling the value column.</summary>
    public bool Drag(string label, ref int value, float speed, int min, int max)
    {
        BeginCell(label);
        ImGui.SetNextItemWidth(-1f);
        bool changed = ImGui.DragInt("##value", ref value, speed, min, max);
        EndCell();
        return changed;
    }

    /// <summary>A Vector2 drag row with the value widget filling the value column.</summary>
    public bool Drag(string label, ref Vector2 value, float speed, float min = 0f, float max = 0f)
    {
        BeginCell(label);
        ImGui.SetNextItemWidth(-1f);
        bool changed = ImGui.DragFloat2("##value", ref value, speed, min, max);
        EndCell();
        return changed;
    }

    /// <summary>A Vector3 drag row with the value widget filling the value column.</summary>
    public bool Drag(string label, ref Vector3 value, float speed, float min = 0f, float max = 0f)
    {
        BeginCell(label);
        ImGui.SetNextItemWidth(-1f);
        bool changed = ImGui.DragFloat3("##value", ref value, speed, min, max);
        EndCell();
        return changed;
    }

    /// <summary>A slider row with the value widget filling the value column.</summary>
    public bool Slider(string label, ref float value, float min, float max)
    {
        BeginCell(label);
        ImGui.SetNextItemWidth(-1f);
        bool changed = ImGui.SliderFloat("##value", ref value, min, max);
        EndCell();
        return changed;
    }

    /// <summary>A checkbox row: the label in the label column, the box in the value column.</summary>
    public bool Checkbox(string label, ref bool value)
    {
        BeginCell(label);
        bool changed = ImGui.Checkbox("##value", ref value);
        EndCell();
        return changed;
    }

    /// <summary>An enum combo row with the value widget filling the value column.</summary>
    public bool Combo<TEnum>(string label, ref TEnum value) where TEnum : struct, Enum
    {
        BeginCell(label);
        ImGui.SetNextItemWidth(-1f);
        bool changed = ImGui.Combo("##value", ref value);
        EndCell();
        return changed;
    }

    /// <summary>A color picker row with the value widget filling the value column.</summary>
    public bool Color(string label, ref ColorFloat color)
    {
        BeginCell(label);
        ImGui.SetNextItemWidth(-1f);
        bool changed = ImGui.ColorEdit4("##value", ref color);
        EndCell();
        return changed;
    }

    /// <summary>A text input row with the value widget filling the value column.</summary>
    public bool TextField(string label, ref string value, uint maxLength)
    {
        BeginCell(label);
        ImGui.SetNextItemWidth(-1f);
        bool changed = ImGui.InputText("##value", ref value, maxLength);
        EndCell();
        return changed;
    }

    /// <summary>
    /// A min/max pair of float drags: each takes half the value column, or they stack
    /// onto separate lines when the column is too narrow for two usable fields.
    /// </summary>
    public bool Range(string label, ref float min, ref float max, float speed)
    {
        BeginCell(label);
        float avail = ImGui.GetContentRegionAvail().X;
        float caption = MathF.Max(ImGui.CalcTextSize("min").X, ImGui.CalcTextSize("max").X);
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        bool inline = avail >= (caption * 2f) + (spacing * 3f) + (RangeDragWidthMin * 2f);
        bool changed;
        if (inline)
        {
            float drag = ((avail - (caption * 2f) - (spacing * 3f)) * 0.5f);
            Caption("min");
            ImGui.SetNextItemWidth(drag);
            changed = ImGui.DragFloat("##min", ref min, speed, 0f, 0f);
            ImGui.SameLine();
            Caption("max");
            ImGui.SetNextItemWidth(drag);
            changed |= ImGui.DragFloat("##max", ref max, speed, 0f, 0f);
        }
        else
        {
            float drag = avail - caption - spacing;
            Caption("min");
            ImGui.SetNextItemWidth(drag);
            changed = ImGui.DragFloat("##min", ref min, speed, 0f, 0f);
            Caption("max");
            ImGui.SetNextItemWidth(drag);
            changed |= ImGui.DragFloat("##max", ref max, speed, 0f, 0f);
        }
        EndCell();
        return changed;
    }

    /// <summary>
    /// A min/max pair of Vector2 drags: they share the value column, or stack onto
    /// separate lines when the column is too narrow for two usable fields.
    /// </summary>
    public bool Range(string label, ref Vector2 min, ref Vector2 max, float speed)
    {
        BeginCell(label);
        float avail = ImGui.GetContentRegionAvail().X;
        float caption = MathF.Max(ImGui.CalcTextSize("min").X, ImGui.CalcTextSize("max").X);
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        bool inline = avail >= (caption * 2f) + (spacing * 3f) + (RangeDrag2WidthMin * 2f);
        bool changed;
        if (inline)
        {
            float drag = ((avail - (caption * 2f) - (spacing * 3f)) * 0.5f);
            Caption("min");
            ImGui.SetNextItemWidth(drag);
            changed = ImGui.DragFloat2("##min", ref min, speed, 0f, 0f);
            ImGui.SameLine();
            Caption("max");
            ImGui.SetNextItemWidth(drag);
            changed |= ImGui.DragFloat2("##max", ref max, speed, 0f, 0f);
        }
        else
        {
            float drag = avail - caption - spacing;
            Caption("min");
            ImGui.SetNextItemWidth(drag);
            changed = ImGui.DragFloat2("##min", ref min, speed, 0f, 0f);
            Caption("max");
            ImGui.SetNextItemWidth(drag);
            changed |= ImGui.DragFloat2("##max", ref max, speed, 0f, 0f);
        }
        EndCell();
        return changed;
    }

    /// <summary>A small caption vertically aligned with the following widget, on the same line.</summary>
    public static void Caption(string text)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(text);
        ImGui.SameLine();
    }

    /// <summary>The horizontal room a <see cref="Caption"/> occupies, including its trailing gap.</summary>
    public static float CaptionWidth(string text)
    {
        return ImGui.CalcTextSize(text).X + ImGui.GetStyle().ItemSpacing.X;
    }

    /// <summary>
    /// Breaks the current value cell onto a new line when a group of widgets
    /// <paramref name="incomingWidth"/> wide would not fit on the current one. Only
    /// valid inside an open value cell.
    /// </summary>
    public static void WrapWhenFull(float incomingWidth)
    {
        if (ImGui.GetContentRegionAvail().X < incomingWidth)
        {
            ImGui.NewLine();
        }
    }
}
