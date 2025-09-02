using Alco.GUI;
using System.Numerics;

namespace Alco.ImGUI;

public static class UINodeExtension
{
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