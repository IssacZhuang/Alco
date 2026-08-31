using System.Numerics;
using Alco.ImGUI;
using Alco.Particles;

namespace Alco.Editor;

/// <summary>
/// Immediate-mode editor for a particle color-over-life gradient (a list of
/// <see cref="ParticleColorKey"/>): a preview bar sampled through
/// <see cref="ParticleOverLifeBake.EvaluateGradient"/> (the same evaluation the GPU
/// bake uses), draggable key handles on a strip below it, click-on-bar to insert a
/// key, and a color/time editor row for the selected key. Edits the key list in
/// place; times are clamped to [0, 1] as the bake does.
/// </summary>
public static class GradientEditor
{
    private const float BarHeight = 18f;
    private const float HandleHeight = 10f;
    private const float HandleHalfWidth = 5f;
    private const int PreviewSteps = 64;

    /// <summary>The selected key per widget id (references survive list reordering).</summary>
    private static readonly Dictionary<uint, ParticleColorKey?> s_selection = new();

    /// <summary>
    /// Draws the gradient editor for <paramref name="keys"/>, editing it in place.
    /// </summary>
    /// <param name="id">A unique ImGui id for this editor instance.</param>
    /// <param name="keys">The gradient keys (must contain at least one key).</param>
    /// <returns>True when the keys were modified this frame.</returns>
    public static bool Draw(string id, List<ParticleColorKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            ImGui.TextDisabled("(a gradient needs at least one key)");
            return false;
        }

        bool changed = false;
        float width = Math.Max(ImGui.GetContentRegionAvail().X, 60f);
        Vector2 barPos = ImGui.GetCursorScreenPos();
        Vector2 size = new(width, BarHeight + HandleHeight);

        DrawBar(barPos, width, keys);

        // One interaction zone over bar + handles: click selects/inserts, drag moves.
        ImGui.InvisibleButton(id, size);
        uint uid = ImGui.GetID(id);
        bool hovered = ImGui.IsItemHovered();
        Vector2 mouse = ImGui.GetIO().MousePos;

        ParticleColorKey? selected = s_selection.TryGetValue(uid, out ParticleColorKey? key) && key != null && keys.Contains(key)
            ? key
            : null;

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            float time = MouseTime(mouse.X, barPos.X, width);
            selected = HitKey(keys, mouse, barPos, width);
            if (selected == null)
            {
                selected = new ParticleColorKey { Time = time, Color = ParticleOverLifeBake.EvaluateGradient(keys, time) };
                keys.Add(selected);
                changed = true;
            }
            s_selection[uid] = selected;
        }
        else if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            ParticleColorKey? hit = HitKey(keys, mouse, barPos, width);
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
            float time = MouseTime(mouse.X, barPos.X, width);
            if (selected.Time != time)
            {
                selected.Time = time;
                changed = true;
            }
        }

        DrawHandles(barPos, width, keys, selected);

        // Selected key row: color, time, delete.
        if (selected != null)
        {
            ImGui.PushID("selected");
            ImGui.SetNextItemWidth(-110f);
            ColorFloat color = selected.Color;
            if (ImGui.ColorEdit4("##color", ref color))
            {
                selected.Color = color;
                changed = true;
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(60f);
            float time = selected.Time;
            if (ImGui.DragFloat("##time", ref time, 0.005f, 0f, 1f, "%.2f"))
            {
                selected.Time = time;
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
                ImGui.SetTooltip("Delete the selected key (or right-click its handle)");
            }
            ImGui.PopID();
        }
        else
        {
            ImGui.TextDisabled("Click the bar to add a key; drag handles to move them.");
        }

        return changed;
    }

    /// <summary>Draws the gradient preview bar, one column per sample.</summary>
    private static void DrawBar(Vector2 barPos, float width, List<ParticleColorKey> keys)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float step = width / PreviewSteps;
        for (int i = 0; i < PreviewSteps; i++)
        {
            ColorFloat color = ParticleOverLifeBake.EvaluateGradient(keys, (float)i / (PreviewSteps - 1));
            Vector2 min = new(barPos.X + i * step, barPos.Y);
            Vector2 max = new(barPos.X + (i + 1) * step + 0.5f, barPos.Y + BarHeight);
            drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(color.value));
        }
        drawList.AddRect(barPos, barPos + new Vector2(width, BarHeight), 0xFF808080);
    }

    /// <summary>Draws the key handles below the bar; the selected key is highlighted.</summary>
    private static void DrawHandles(Vector2 barPos, float width, List<ParticleColorKey> keys, ParticleColorKey? selected)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float y = barPos.Y + BarHeight;
        foreach (ParticleColorKey key in keys)
        {
            float x = barPos.X + Math.Clamp(key.Time, 0f, 1f) * width;
            uint color = key == selected ? 0xFFFFFFFF : 0xFFB0B0B0;
            drawList.AddRectFilled(new Vector2(x - HandleHalfWidth, y), new Vector2(x + HandleHalfWidth, y + HandleHeight), color);
            drawList.AddRect(new Vector2(x - HandleHalfWidth, y), new Vector2(x + HandleHalfWidth, y + HandleHeight), 0xFF202020);
        }
    }

    /// <summary>Returns the key whose handle contains the mouse position, or null.</summary>
    private static ParticleColorKey? HitKey(List<ParticleColorKey> keys, Vector2 mouse, Vector2 barPos, float width)
    {
        float y = barPos.Y + BarHeight;
        if (mouse.Y < barPos.Y || mouse.Y > y + HandleHeight)
        {
            return null;
        }
        foreach (ParticleColorKey key in keys)
        {
            float x = barPos.X + Math.Clamp(key.Time, 0f, 1f) * width;
            if (Math.Abs(mouse.X - x) <= HandleHalfWidth)
            {
                return key;
            }
        }
        return null;
    }

    /// <summary>The clamped normalized time under a mouse x position.</summary>
    private static float MouseTime(float mouseX, float barX, float width)
    {
        return Math.Clamp((mouseX - barX) / width, 0f, 1f);
    }
}
