using Alco.GUI;
using Alco;
using System.Numerics;

namespace Alco.ImGUI;

public static class UINodeExtension
{
    /// <summary>
    /// Draw a highlight outline around the given <see cref="UINode"/> using its <see cref="UINode.RenderTransform"/>.
    /// Coordinate conversion is handled from Alco UI space (origin center, Y up) to ImGui screen space (origin top-left, Y down).
    /// </summary>
    /// <param name="node">The UI node to highlight.</param>
    /// <param name="thickness">Outline thickness in pixels.</param>
    /// <param name="color">RGBA color. If null, uses yellow (1,1,0,1).</param>
    public static void DrawHighlight(this UINode node, float thickness = 2f, Vector4? color = null)
    {
        // Compute the four corners in Alco UI world space
        Transform2D rt = node.RenderTransform;
        Vector2 center = rt.Position;
        Vector2 half = rt.Scale * 0.5f;

        Vector2 o0 = new Vector2(-half.X, -half.Y);
        Vector2 o1 = new Vector2(half.X, -half.Y);
        Vector2 o2 = new Vector2(half.X, half.Y);
        Vector2 o3 = new Vector2(-half.X, half.Y);

        // Rotate offsets by node rotation
        o0 = math.rotate(o0, rt.Rotation);
        o1 = math.rotate(o1, rt.Rotation);
        o2 = math.rotate(o2, rt.Rotation);
        o3 = math.rotate(o3, rt.Rotation);

        Vector2 p0 = center + o0;
        Vector2 p1 = center + o1;
        Vector2 p2 = center + o2;
        Vector2 p3 = center + o3;

        // Convert to ImGui screen space (absolute coordinates)
        // Use UI root size as virtual resolution; final screen pixels are the ImGui main viewport size.
        Vector2 canvasSize = node.GetRoot().Size;   // virtual units (W,H)
        Vector2 viewportPos = ImGui.GetMainViewport().Pos;   // top-left in screen pixels
        Vector2 viewportSize = ImGui.GetMainViewport().Size; // size in screen pixels

        float sx = viewportSize.X / canvasSize.X; // pixel per virtual unit (X)
        float sy = viewportSize.Y / canvasSize.Y; // pixel per virtual unit (Y)

        static Vector2 ToImGui(Vector2 world, Vector2 vpPos, Vector2 vpSize, float sx, float sy)
        {
            float x = vpPos.X + vpSize.X * 0.5f + world.X * sx;
            float y = vpPos.Y + vpSize.Y * 0.5f - world.Y * sy;
            return new Vector2(x, y);
        }

        Vector2 q0 = ToImGui(p0, viewportPos, viewportSize, sx, sy);
        Vector2 q1 = ToImGui(p1, viewportPos, viewportSize, sx, sy);
        Vector2 q2 = ToImGui(p2, viewportPos, viewportSize, sx, sy);
        Vector2 q3 = ToImGui(p3, viewportPos, viewportSize, sx, sy);

        // Draw on foreground so it's visible above all windows
        ImDrawListPtr dl = ImGui.GetForegroundDrawList();
        uint col = ImGui.GetColorU32(color ?? new Vector4(1f, 1f, 0f, 1f));

        dl.PathClear();
        dl.PathLineTo(q0);
        dl.PathLineTo(q1);
        dl.PathLineTo(q2);
        dl.PathLineTo(q3);
        dl.PathStroke(col, ImDrawFlags.Closed, thickness);
    }

    public static void DrawDebugTreeWithInspector(this UINode rootNode, ref UINode? selectedNode)
    {
        if (ImGui.BeginTable("ui_debug", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn("Tree", ImGuiTableColumnFlags.WidthFixed, 360.0f);
            ImGui.TableSetupColumn("Inspector", ImGuiTableColumnFlags.WidthStretch);

            // left tree
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("UI Root Tree:");
            
            // Use remaining space for tree with scrollbar
            Vector2 treeAvailableSpace = ImGui.GetContentRegionAvail();
            ImGui.BeginChild("UITreeView", new Vector2(0, treeAvailableSpace.Y - 10));
            rootNode.DrawDebugTree(ref selectedNode);
            ImGui.EndChild();

            // right inspector
            ImGui.TableSetColumnIndex(1);
            ImGui.Text("Inspector:");
            
            // Use remaining space for inspector with scrollbar
            Vector2 inspectorAvailableSpace = ImGui.GetContentRegionAvail();
            ImGui.BeginChild("UIInspector", new Vector2(0, inspectorAvailableSpace.Y - 10));
            if (selectedNode != null)
            {
                selectedNode.DrawDebugInspector();
            }
            else
            {
                ImGui.Text("Select a node on the left");
            }
            ImGui.EndChild();

            ImGui.EndTable();
        }

        if(selectedNode != null)
        {
            selectedNode.DrawHighlight(1f);
        }
    }

    public static void DrawDebugInspector(this UINode node)
    {
        FixedString128 label;
        label = "Name: ";
        label += node.Name;
        ImGui.Text(label);

        label = "Type: ";
        label += node.GetType().Name;
        ImGui.Text(label);

        // IsEnable
        bool enabled = node.IsEnable;
        if (ImGui.Checkbox("IsEnable", ref enabled))
        {
            node.IsEnable = enabled;
        }

        // Position (step 1)
        Vector2 pos = node.Position;
        if (ImGui.DragFloat2("Position", ref pos, 1f))
        {
            node.Position = pos;
        }

        // Rotation (as angle degrees)
        float angle = node.Rotation.ToDegree();
        if (ImGui.DragFloat("Rotation (deg)", ref angle, 1f))
        {
            node.Rotation = new Rotation2D(angle);
        }

        // Scale (step 0.1)
        Vector2 scale = node.Scale;
        if (ImGui.DragFloat2("Scale", ref scale, 0.1f))
        {
            node.Scale = scale;
        }

        // Size (step 1)
        Vector2 size = node.Size;
        if (ImGui.DragFloat2("Size", ref size, 1f))
        {
            node.Size = size;
        }

        // SizeDelta (step 1)
        Vector2 sizeDelta = node.SizeDelta;
        if (ImGui.DragFloat2("SizeDelta", ref sizeDelta, 1f))
        {
            node.SizeDelta = sizeDelta;
        }

        // Pivot (vector2 editable, step 0.1)
        Vector2 pivot = node.Pivot;
        if (ImGui.DragFloat2("Pivot", ref pivot, 0.1f))
        {
            node.Pivot = pivot;
        }

        // Anchor (two vectors min/max)
        Anchor anchor = node.Anchor;
        Vector2 aMin = anchor.min;
        Vector2 aMax = anchor.max;
        if (ImGui.DragFloat2("Anchor min", ref aMin, 0.1f) | ImGui.DragFloat2("Anchor max", ref aMax, 0.1f))
        {
            node.Anchor = new Anchor(aMin, aMax);
        }

        ImGui.Separator();
        ImGui.Text("RenderTransform:");
        var rt = node.RenderTransform;
        Vector2 rtPos = rt.Position;
        Vector2 rtScale = rt.Scale;
        float rtRotDeg = rt.Rotation.ToDegree();
        ImGui.BeginDisabled();
        ImGui.InputFloat2("RT Position", ref rtPos);
        ImGui.InputFloat("RT Rotation (deg)", ref rtRotDeg);
        ImGui.InputFloat2("RT Scale", ref rtScale);
        ImGui.EndDisabled();
    }

    public static void DrawDebugTree(this UINode node, ref UINode? selectedNode)
    {
        // build label with fixed string to avoid GC
        FixedString128 label = node.Name;
        if (label.Length == 0)
        {
            label = node.GetType().Name;
        }
        // append child count suffix
        FixedString32 suffix = " [";
        suffix += node.Children.Count;
        suffix += "]";
        label += suffix;

        // stable id to support toggling without GC
        int id = node.GetHashCode();
        ImGui.PushID(id);
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick;
        bool open = ImGui.TreeNodeEx(label, flags);
        if (ImGui.IsItemClicked())
        {
            selectedNode = node;
        }
        if (open)
        {
            for (int i = 0; i < node.Children.Count; i++)
            {
                node.Children[i].DrawDebugTree(ref selectedNode);
            }
            ImGui.TreePop();
        }
        ImGui.PopID();
    }

}