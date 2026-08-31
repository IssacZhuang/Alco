using System.Numerics;
using Alco.ImGUI;
using Alco.Particles;

namespace Alco.Editor;

/// <summary>
/// Immediate-mode editor for a particle scalar-over-life curve (a list of
/// <see cref="ParticleScalarKey"/>): a framed plot of the curve sampled through
/// <see cref="ParticleOverLifeBake.EvaluateCurve"/> (the same evaluation the GPU bake
/// uses), draggable 2D key points, double-click to insert a key, and a time/value
/// editor row for the selected key. The value axis auto-ranges to the keys (always
/// including the 0 and 1 reference lines). Edits the key list in place.
/// </summary>
public static class CurveEditor
{
    private const float PlotHeight = 90f;
    private const float PointRadius = 4.5f;
    private const int PreviewSteps = 64;

    /// <summary>The selected key per widget id (references survive list reordering).</summary>
    private static readonly Dictionary<uint, ParticleScalarKey?> s_selection = new();

    /// <summary>
    /// Draws the curve editor for <paramref name="keys"/>, editing it in place.
    /// </summary>
    /// <param name="id">A unique ImGui id for this editor instance.</param>
    /// <param name="keys">The curve keys (must contain at least one key).</param>
    /// <returns>True when the keys were modified this frame.</returns>
    public static bool Draw(string id, List<ParticleScalarKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            ImGui.TextDisabled("(a curve needs at least one key)");
            return false;
        }

        bool changed = false;
        float width = Math.Max(ImGui.GetContentRegionAvail().X, 60f);
        Vector2 origin = ImGui.GetCursorScreenPos();
        Vector2 size = new(width, PlotHeight);

        // Value range: fit the keys, always showing the 0 and 1 references.
        float vMin = 0f, vMax = 1f;
        foreach (ParticleScalarKey k in keys)
        {
            vMin = Math.Min(vMin, k.Value);
            vMax = Math.Max(vMax, k.Value);
        }
        float padding = (vMax - vMin) * 0.1f + 0.001f;
        vMin -= padding;
        vMax += padding;

        DrawPlot(origin, size, vMin, vMax, keys);

        ImGui.InvisibleButton(id, size);
        uint uid = ImGui.GetID(id);
        bool hovered = ImGui.IsItemHovered();
        Vector2 mouse = ImGui.GetIO().MousePos;

        ParticleScalarKey? selected = s_selection.TryGetValue(uid, out ParticleScalarKey? key) && key != null && keys.Contains(key)
            ? key
            : null;

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            selected = HitKey(keys, mouse, origin, size, vMin, vMax);
            s_selection[uid] = selected;
        }
        else if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            selected = new ParticleScalarKey
            {
                Time = Math.Clamp((mouse.X - origin.X) / size.X, 0f, 1f),
                Value = Math.Clamp(ValueAt(mouse.Y, origin.Y, size.Y, vMin, vMax), vMin, vMax),
            };
            keys.Add(selected);
            s_selection[uid] = selected;
            changed = true;
        }
        else if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            ParticleScalarKey? hit = HitKey(keys, mouse, origin, size, vMin, vMax);
            if (hit != null && keys.Count > 1)
            {
                keys.Remove(hit);
                if (selected == hit)
                {
                    selected = null;
                    s_selection[uid] = null;
                }
                changed = true;
            }
        }

        if (selected != null && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            float time = Math.Clamp((mouse.X - origin.X) / size.X, 0f, 1f);
            float value = ValueAt(mouse.Y, origin.Y, size.Y, vMin, vMax);
            if (selected.Time != time || selected.Value != value)
            {
                selected.Time = time;
                selected.Value = value;
                changed = true;
            }
        }

        DrawPoints(origin, size, vMin, vMax, keys, selected);

        // Selected key row: time, value, delete.
        if (selected != null)
        {
            ImGui.PushID("selected");
            ImGui.SetNextItemWidth(80f);
            float time = selected.Time;
            if (ImGui.DragFloat("Time##time", ref time, 0.005f, 0f, 1f, "%.2f"))
            {
                selected.Time = time;
                changed = true;
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80f);
            float value = selected.Value;
            if (ImGui.DragFloat("Value##value", ref value, 0.01f))
            {
                selected.Value = value;
                changed = true;
            }
            ImGui.SameLine();
            ImGui.BeginDisabled(keys.Count <= 1);
            if (ImGui.SmallButton("X"))
            {
                keys.Remove(selected);
                s_selection[uid] = null;
                changed = true;
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Delete the selected key (or right-click its point)");
            }
            ImGui.PopID();
        }
        else
        {
            ImGui.TextDisabled("Double-click to add a key; drag points to move them.");
        }

        return changed;
    }

    /// <summary>Draws the frame, the 0/1 reference lines and the sampled curve.</summary>
    private static void DrawPlot(Vector2 origin, Vector2 size, float vMin, float vMax, List<ParticleScalarKey> keys)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(origin, origin + size, 0xFF282828);
        drawList.AddRect(origin, origin + size, 0xFF808080);

        // Reference lines at value 0 and 1.
        foreach (float reference in new[] { 0f, 1f })
        {
            float y = origin.Y + size.Y * (1f - (reference - vMin) / (vMax - vMin));
            drawList.AddLine(new Vector2(origin.X, y), new Vector2(origin.X + size.X, y), 0xFF505050);
        }

        Vector2 previous = PointPos(origin, size, vMin, vMax, 0f, ParticleOverLifeBake.EvaluateCurve(keys, 0f));
        for (int i = 1; i < PreviewSteps; i++)
        {
            float time = (float)i / (PreviewSteps - 1);
            Vector2 point = PointPos(origin, size, vMin, vMax, time, ParticleOverLifeBake.EvaluateCurve(keys, time));
            drawList.AddLine(previous, point, 0xFF60C0FF, 1.5f);
            previous = point;
        }
    }

    /// <summary>Draws the key points; the selected key is highlighted.</summary>
    private static void DrawPoints(Vector2 origin, Vector2 size, float vMin, float vMax, List<ParticleScalarKey> keys, ParticleScalarKey? selected)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        foreach (ParticleScalarKey key in keys)
        {
            Vector2 point = PointPos(origin, size, vMin, vMax, Math.Clamp(key.Time, 0f, 1f), key.Value);
            drawList.AddCircleFilled(point, PointRadius, key == selected ? 0xFFFFFFFF : 0xFFB0B0B0);
            drawList.AddCircle(point, PointRadius, 0xFF202020);
        }
    }

    /// <summary>Returns the key whose point is near the mouse position, or null.</summary>
    private static ParticleScalarKey? HitKey(List<ParticleScalarKey> keys, Vector2 mouse, Vector2 origin, Vector2 size, float vMin, float vMax)
    {
        foreach (ParticleScalarKey key in keys)
        {
            Vector2 point = PointPos(origin, size, vMin, vMax, Math.Clamp(key.Time, 0f, 1f), key.Value);
            if (Vector2.DistanceSquared(mouse, point) <= (PointRadius + 2f) * (PointRadius + 2f))
            {
                return key;
            }
        }
        return null;
    }

    /// <summary>The canvas position of a (time, value) pair.</summary>
    private static Vector2 PointPos(Vector2 origin, Vector2 size, float vMin, float vMax, float time, float value)
    {
        return new Vector2(
            origin.X + time * size.X,
            origin.Y + size.Y * (1f - (Math.Clamp(value, vMin, vMax) - vMin) / (vMax - vMin)));
    }

    /// <summary>The value under a mouse y position (not clamped to the visible range).</summary>
    private static float ValueAt(float mouseY, float originY, float height, float vMin, float vMax)
    {
        return vMin + (1f - (mouseY - originY) / height) * (vMax - vMin);
    }
}
