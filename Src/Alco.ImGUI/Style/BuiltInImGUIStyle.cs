using System.Numerics;

namespace Alco.ImGUI;

/// <summary>
/// Provides built-in styles for ImGui.
/// </summary>
public static class BuiltInImGUIStyle
{
    /// <summary>
    /// Applies a Visual Studio 2026-inspired dark theme to the current ImGui style.
    /// This style features deep dark backgrounds with vibrant blue accents.
    /// </summary>
    public static void ApplyVisualStudio2026Style()
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero)
        {
            return;
        }

        ImGuiStylePtr style = ImGui.GetStyle();
        
        style.WindowRounding = 0.0f;
        style.ChildRounding = 0.0f;
        style.FrameRounding = 2.0f;
        style.GrabRounding = 2.0f;
        style.PopupRounding = 0.0f;
        style.ScrollbarRounding = 2.0f;
        style.TabRounding = 2.0f;
        
        style.WindowBorderSize = 1.0f;
        style.ChildBorderSize = 1.0f;
        style.PopupBorderSize = 1.0f;
        style.FrameBorderSize = 0.0f;
        style.TabBorderSize = 0.0f;

        style.Colors[(int)ImGuiCol.Text] = new Vector4(0.95f, 0.95f, 0.95f, 1.00f);
        style.Colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.55f, 0.55f, 0.55f, 1.00f);
        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.11f, 0.11f, 0.11f, 1.00f);
        style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.11f, 0.11f, 0.11f, 0.00f);
        style.Colors[(int)ImGuiCol.PopupBg] = new Vector4(0.14f, 0.14f, 0.14f, 1.00f);
        style.Colors[(int)ImGuiCol.Border] = new Vector4(0.25f, 0.25f, 0.26f, 1.00f);
        style.Colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
        style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.20f, 0.20f, 0.22f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.25f, 0.25f, 0.27f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.29f, 0.29f, 0.31f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.15f, 0.15f, 0.15f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.18f, 0.18f, 0.19f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.15f, 0.15f, 0.15f, 1.00f);
        style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.18f, 0.18f, 0.19f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.24f, 0.24f, 0.25f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.31f, 0.31f, 0.32f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.37f, 0.37f, 0.38f, 1.00f);
        style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(0.53f, 0.41f, 0.88f, 1.00f);
        style.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.53f, 0.41f, 0.88f, 1.00f);
        style.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.59f, 0.48f, 0.90f, 1.00f);
        style.Colors[(int)ImGuiCol.Button] = new Vector4(0.42f, 0.36f, 0.90f, 1.00f);
        style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.50f, 0.45f, 0.95f, 1.00f);
        style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.35f, 0.30f, 0.80f, 1.00f);
        style.Colors[(int)ImGuiCol.Header] = new Vector4(0.20f, 0.20f, 0.22f, 1.00f);
        style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.53f, 0.41f, 0.88f, 0.80f);
        style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.53f, 0.41f, 0.88f, 1.00f);
        style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.25f, 0.25f, 0.26f, 1.00f);
        style.Colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.53f, 0.41f, 0.88f, 1.00f);
        style.Colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.59f, 0.48f, 0.90f, 1.00f);
        style.Colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.42f, 0.36f, 0.90f, 0.20f);
        style.Colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.42f, 0.36f, 0.90f, 0.67f);
        style.Colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.42f, 0.36f, 0.90f, 0.95f);
        style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.18f, 0.18f, 0.19f, 1.00f);
        style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.53f, 0.41f, 0.88f, 0.80f);
        style.Colors[(int)ImGuiCol.TabSelected] = new Vector4(0.53f, 0.41f, 0.88f, 1.00f);
        style.Colors[(int)ImGuiCol.TabDimmed] = new Vector4(0.15f, 0.15f, 0.15f, 1.00f);
        style.Colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.22f, 0.20f, 0.25f, 1.00f);
        style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(0.61f, 0.61f, 0.61f, 1.00f);
        style.Colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(1.00f, 0.43f, 0.35f, 1.00f);
        style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.53f, 0.41f, 0.88f, 1.00f);
        style.Colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(0.59f, 0.48f, 0.90f, 1.00f);
        style.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.53f, 0.41f, 0.88f, 0.35f);
        style.Colors[(int)ImGuiCol.DragDropTarget] = new Vector4(1.00f, 1.00f, 0.00f, 0.90f);
        style.Colors[(int)ImGuiCol.NavCursor] = new Vector4(0.53f, 0.41f, 0.88f, 1.00f);
        style.Colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1.00f, 1.00f, 1.00f, 0.70f);
        style.Colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.80f, 0.80f, 0.80f, 0.20f);
        style.Colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.80f, 0.80f, 0.80f, 0.35f);
    }

    /// <summary>
    /// Applies a Visual Studio Code-inspired dark theme to the current ImGui style.
    /// This style features the classic VS Code Dark+ colors.
    /// </summary>
    public static void ApplyVisualStudioCodeStyle()
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero)
        {
            return;
        }

        ImGuiStylePtr style = ImGui.GetStyle();

        style.WindowRounding = 0.0f;
        style.ChildRounding = 0.0f;
        style.FrameRounding = 0.0f;
        style.GrabRounding = 0.0f;
        style.PopupRounding = 0.0f;
        style.ScrollbarRounding = 0.0f;
        style.TabRounding = 0.0f;

        style.WindowBorderSize = 1.0f;
        style.ChildBorderSize = 1.0f;
        style.PopupBorderSize = 1.0f;
        style.FrameBorderSize = 0.0f;
        style.TabBorderSize = 0.0f;

        style.Colors[(int)ImGuiCol.Text] = new Vector4(0.95f, 0.95f, 0.95f, 1.00f);
        style.Colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.55f, 0.55f, 0.55f, 1.00f);
        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
        style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.12f, 0.12f, 0.12f, 0.00f);
        style.Colors[(int)ImGuiCol.PopupBg] = new Vector4(0.15f, 0.15f, 0.15f, 1.00f);
        style.Colors[(int)ImGuiCol.Border] = new Vector4(0.27f, 0.27f, 0.27f, 1.00f);
        style.Colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
        style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.24f, 0.24f, 0.24f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.30f, 0.30f, 0.30f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.35f, 0.35f, 0.35f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
        style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.19f, 0.19f, 0.19f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.31f, 0.31f, 0.31f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.41f, 0.41f, 0.41f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.51f, 0.51f, 0.51f, 1.00f);
        style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(0.00f, 0.48f, 0.80f, 1.00f);
        style.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.00f, 0.48f, 0.80f, 1.00f);
        style.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.00f, 0.58f, 0.90f, 1.00f);
        style.Colors[(int)ImGuiCol.Button] = new Vector4(0.05f, 0.39f, 0.61f, 1.00f);
        style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.07f, 0.47f, 0.73f, 1.00f);
        style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.04f, 0.31f, 0.49f, 1.00f);
        style.Colors[(int)ImGuiCol.Header] = new Vector4(0.17f, 0.18f, 0.18f, 1.00f);
        style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.20f, 0.20f, 0.21f, 1.00f);
        style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.00f, 0.48f, 0.80f, 1.00f);
        style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.27f, 0.27f, 0.27f, 1.00f);
        style.Colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.00f, 0.48f, 0.80f, 1.00f);
        style.Colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.00f, 0.48f, 0.80f, 1.00f);
        style.Colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.00f, 0.48f, 0.80f, 0.20f);
        style.Colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.00f, 0.48f, 0.80f, 0.67f);
        style.Colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.00f, 0.48f, 0.80f, 0.95f);
        style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.18f, 0.18f, 0.18f, 1.00f);
        style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
        style.Colors[(int)ImGuiCol.TabSelected] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
        style.Colors[(int)ImGuiCol.TabDimmed] = new Vector4(0.18f, 0.18f, 0.18f, 1.00f);
        style.Colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
        style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(0.61f, 0.61f, 0.61f, 1.00f);
        style.Colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(1.00f, 0.43f, 0.35f, 1.00f);
        style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.00f, 0.48f, 0.80f, 1.00f);
        style.Colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(0.00f, 0.58f, 0.90f, 1.00f);
        style.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.16f, 0.35f, 0.62f, 1.00f);
        style.Colors[(int)ImGuiCol.DragDropTarget] = new Vector4(1.00f, 1.00f, 0.00f, 0.90f);
        style.Colors[(int)ImGuiCol.NavCursor] = new Vector4(0.00f, 0.48f, 0.80f, 1.00f);
        style.Colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1.00f, 1.00f, 1.00f, 0.70f);
        style.Colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.80f, 0.80f, 0.80f, 0.20f);
        style.Colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.80f, 0.80f, 0.80f, 0.35f);
    }
}

