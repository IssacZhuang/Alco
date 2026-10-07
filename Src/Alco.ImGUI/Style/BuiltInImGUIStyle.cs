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

    /// <summary>
    /// Applies an Alco dark theme to the current ImGui style.
    /// The look is aligned with the Infernux editor: a near-black neutral
    /// palette with square, flat frames, thin borders, and a teal accent
    /// (#35C2A1) used for checks, sliders, selections, and hover feedback.
    /// </summary>
    public static void ApplyAlcoStyle()
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero)
        {
            return;
        }

        ImGuiStylePtr style = ImGui.GetStyle();

        // Flat, square layout language.
        style.WindowPadding = new Vector2(10.0f, 10.0f);
        style.FramePadding = new Vector2(8.0f, 3.0f);
        style.CellPadding = new Vector2(4.0f, 4.0f);
        style.ItemSpacing = new Vector2(8.0f, 6.0f);
        style.ItemInnerSpacing = new Vector2(6.0f, 4.0f);
        style.IndentSpacing = 18.0f;
        style.ScrollbarSize = 8.0f;
        style.GrabMinSize = 6.0f;

        style.WindowRounding = 0.0f;
        style.ChildRounding = 0.0f;
        style.FrameRounding = 0.0f;
        style.PopupRounding = 0.0f;
        style.ScrollbarRounding = 0.0f;
        style.GrabRounding = 0.0f;
        style.TabRounding = 0.0f;

        style.WindowBorderSize = 1.0f;
        style.ChildBorderSize = 1.0f;
        style.PopupBorderSize = 1.0f;
        style.FrameBorderSize = 1.0f;
        style.TabBorderSize = 0.0f;
        style.TabBarBorderSize = 1.0f;

        // Semantic roles; every widget color below is composed from these.
        Vector4 accent = new(0.208f, 0.761f, 0.631f, 1.00f); // Alco teal #35C2A1
        Vector4 bg = new(0.098f, 0.098f, 0.098f, 1.00f);
        Vector4 surf = new(0.125f, 0.125f, 0.125f, 1.00f);
        Vector4 raised = new(0.150f, 0.150f, 0.150f, 1.00f);
        Vector4 hover = new(0.165f, 0.165f, 0.165f, 1.00f);
        Vector4 text = new(0.812f, 0.812f, 0.812f, 1.00f);
        Vector4 border = new(0.184f, 0.184f, 0.184f, 1.00f);
        Vector4 transparent = new(0.0f, 0.0f, 0.0f, 0.0f);

        // Text
        style.Colors[(int)ImGuiCol.Text] = text;
        style.Colors[(int)ImGuiCol.TextDisabled] = Mix(text, bg, 0.62f);
        style.Colors[(int)ImGuiCol.TextLink] = accent;
        style.Colors[(int)ImGuiCol.TextSelectedBg] = WithAlpha(accent, 0.35f);

        // Backgrounds
        style.Colors[(int)ImGuiCol.WindowBg] = bg;
        style.Colors[(int)ImGuiCol.ChildBg] = surf;
        style.Colors[(int)ImGuiCol.PopupBg] = WithAlpha(raised, 0.98f);
        style.Colors[(int)ImGuiCol.FrameBg] = raised;
        style.Colors[(int)ImGuiCol.FrameBgHovered] = Mix(hover, accent, 0.12f);
        style.Colors[(int)ImGuiCol.FrameBgActive] = Mix(surf, accent, 0.22f);

        // Title / menu
        style.Colors[(int)ImGuiCol.TitleBg] = bg;
        style.Colors[(int)ImGuiCol.TitleBgActive] = bg;
        style.Colors[(int)ImGuiCol.TitleBgCollapsed] = WithAlpha(bg, 0.75f);
        style.Colors[(int)ImGuiCol.MenuBarBg] = bg;

        // Scrollbar
        style.Colors[(int)ImGuiCol.ScrollbarBg] = transparent;
        style.Colors[(int)ImGuiCol.ScrollbarGrab] = Mix(surf, text, 0.14f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = Mix(surf, text, 0.26f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = WithAlpha(accent, 0.70f);

        // Accent widgets
        style.Colors[(int)ImGuiCol.CheckMark] = accent;
        style.Colors[(int)ImGuiCol.SliderGrab] = WithAlpha(accent, 0.88f);
        style.Colors[(int)ImGuiCol.SliderGrabActive] = accent;
        style.Colors[(int)ImGuiCol.NavCursor] = transparent;

        // Buttons — surface with an accent-tinted hover/active for clear feedback.
        style.Colors[(int)ImGuiCol.Button] = surf;
        style.Colors[(int)ImGuiCol.ButtonHovered] = Mix(surf, accent, 0.26f);
        style.Colors[(int)ImGuiCol.ButtonActive] = Mix(surf, accent, 0.40f);

        // Headers / selectables — the primary "pick an item" feedback.
        style.Colors[(int)ImGuiCol.Header] = hover;
        style.Colors[(int)ImGuiCol.HeaderHovered] = Mix(hover, accent, 0.28f);
        style.Colors[(int)ImGuiCol.HeaderActive] = Mix(hover, accent, 0.42f);

        // Borders / separators
        style.Colors[(int)ImGuiCol.Border] = border;
        style.Colors[(int)ImGuiCol.BorderShadow] = transparent;
        style.Colors[(int)ImGuiCol.Separator] = border;
        style.Colors[(int)ImGuiCol.SeparatorHovered] = WithAlpha(accent, 0.60f);
        style.Colors[(int)ImGuiCol.SeparatorActive] = WithAlpha(accent, 0.80f);

        // Resize grip
        style.Colors[(int)ImGuiCol.ResizeGrip] = transparent;
        style.Colors[(int)ImGuiCol.ResizeGripHovered] = WithAlpha(accent, 0.30f);
        style.Colors[(int)ImGuiCol.ResizeGripActive] = WithAlpha(accent, 0.50f);

        // Tabs
        style.Colors[(int)ImGuiCol.Tab] = bg;
        style.Colors[(int)ImGuiCol.TabHovered] = hover;
        style.Colors[(int)ImGuiCol.TabSelected] = surf;
        style.Colors[(int)ImGuiCol.TabSelectedOverline] = accent;
        style.Colors[(int)ImGuiCol.TabDimmed] = bg;
        style.Colors[(int)ImGuiCol.TabDimmedSelected] = surf;
        style.Colors[(int)ImGuiCol.TabDimmedSelectedOverline] = WithAlpha(accent, 0.60f);

        // Docking
        style.Colors[(int)ImGuiCol.DockingPreview] = WithAlpha(accent, 0.25f);
        style.Colors[(int)ImGuiCol.DockingEmptyBg] = Mix(bg, new Vector4(0.0f, 0.0f, 0.0f, 1.0f), 0.40f);

        // Plots
        style.Colors[(int)ImGuiCol.PlotLines] = Mix(surf, text, 0.45f);
        style.Colors[(int)ImGuiCol.PlotLinesHovered] = accent;
        style.Colors[(int)ImGuiCol.PlotHistogram] = text;
        style.Colors[(int)ImGuiCol.PlotHistogramHovered] = accent;

        // Drag-drop
        style.Colors[(int)ImGuiCol.DragDropTarget] = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);

        // Nav / modal
        style.Colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1.0f, 1.0f, 1.0f, 0.70f);
        style.Colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.80f, 0.80f, 0.80f, 0.20f);
        style.Colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.56f);

        // Tables
        style.Colors[(int)ImGuiCol.TableHeaderBg] = surf;
        style.Colors[(int)ImGuiCol.TableBorderStrong] = border;
        style.Colors[(int)ImGuiCol.TableBorderLight] = Mix(bg, border, 0.50f);
        style.Colors[(int)ImGuiCol.TableRowBg] = transparent;
        style.Colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(1.0f, 1.0f, 1.0f, 0.02f);
    }

    /// <summary>Linearly interpolates two colors component-wise, including alpha.</summary>
    private static Vector4 Mix(Vector4 a, Vector4 b, float t)
    {
        return new Vector4(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t,
            a.W + (b.W - a.W) * t);
    }

    /// <summary>Returns the color with its alpha replaced.</summary>
    private static Vector4 WithAlpha(Vector4 color, float alpha)
    {
        return new Vector4(color.X, color.Y, color.Z, alpha);
    }
}

