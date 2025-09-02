using Alco.GUI;

namespace Alco.ImGUI;

public static class UINodeExtension
{

    public static void DrawDebugInspectorWithWindow(this UINode node, ReadOnlySpan<char> title, ref bool isOpen)
    {
        ImGui.Begin(title, ref isOpen);
        DrawDebugInspector(node);
        ImGui.End();
    }

    public static void DrawDebugInspectorWithWindow(this UINode node, ReadOnlySpan<char> title)
    {
        ImGui.Begin(title);
        DrawDebugInspector(node);
        ImGui.End();
    }


    public static void DrawDebugInspector(this UINode node)
    {
        //todo
    }
}