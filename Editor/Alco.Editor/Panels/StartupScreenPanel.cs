using System.Numerics;
using Alco.ImGUI;

namespace Alco.Editor;

/// <summary>
/// The editor's startup screen, drawn while no project is open: an "Open Project..."
/// button plus the recent projects list (most recently opened first). Clicking an entry
/// opens that project; entries whose project file no longer exists are hidden.
/// </summary>
public sealed class StartupScreenPanel
{
    private const float CardWidth = 440f;

    private readonly RecentProjects _recentProjects;
    private readonly Action _openProjectDialog;
    private readonly Action<string> _openProject;

    /// <summary>Creates the startup screen.</summary>
    /// <param name="recentProjects">Recent projects to list.</param>
    /// <param name="openProjectDialog">Shows the platform project file picker.</param>
    /// <param name="openProject">Opens the project at the given path.</param>
    public StartupScreenPanel(RecentProjects recentProjects, Action openProjectDialog, Action<string> openProject)
    {
        _recentProjects = recentProjects;
        _openProjectDialog = openProjectDialog;
        _openProject = openProject;
    }

    /// <summary>Draws the startup screen as a card centered over the main viewport.</summary>
    public void DrawContent()
    {
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        Vector2 center = viewport.WorkPos + viewport.WorkSize * 0.5f;

        // Fixed-width, content-height card, re-centered every frame.
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSizeConstraints(new Vector2(CardWidth, 0f), new Vector2(CardWidth, viewport.WorkSize.Y));
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize;

        if (!ImGui.Begin("##startup_screen", flags))
        {
            ImGui.End();
            return;
        }

        ImGui.SetWindowFontScale(1.5f);
        ImGui.TextUnformatted("Alco Editor");
        ImGui.SetWindowFontScale(1f);
        ImGui.TextDisabled("Open a project to start editing.");

        ImGui.Spacing();
        if (ImGui.Button("Open Project...", new Vector2(-1f, 0f)))
        {
            _openProjectDialog();
        }

        ImGui.Spacing();
        ImGui.SeparatorText("Recent Projects");
        DrawRecentList();

        ImGui.End();
    }

    /// <summary>Draws existing recent projects as two-line rows (name over file path).</summary>
    private void DrawRecentList()
    {
        List<RecentProjectEntry> entries = new();
        foreach (RecentProjectEntry entry in _recentProjects.Entries)
        {
            if (File.Exists(entry.Path))
            {
                entries.Add(entry);
            }
        }
        if (entries.Count == 0)
        {
            ImGui.TextDisabled("No recent projects.");
            return;
        }
        entries.Sort((a, b) => b.OpenedUtc.CompareTo(a.OpenedUtc));

        ImGuiStylePtr style = ImGui.GetStyle();
        float line = ImGui.GetTextLineHeight();
        Vector2 rowSize = new(ImGui.GetContentRegionAvail().X, line * 2f + style.ItemSpacing.Y * 2f);
        Vector2 textOffset = new(style.FramePadding.X, style.ItemSpacing.Y);
        uint nameColor = ImGui.GetColorU32(ImGuiCol.Text);
        uint pathColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);

        for (int i = 0; i < entries.Count; i++)
        {
            RecentProjectEntry entry = entries[i];
            ImGui.PushID(i);

            Vector2 rowMin = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton("##recent", rowSize))
            {
                _openProject(entry.Path);
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(entry.Path);
                ImGui.TextUnformatted("Last opened: " + entry.OpenedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                ImGui.EndTooltip();
                ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMin + rowSize,
                    ImGui.GetColorU32(ImGuiCol.FrameBgHovered));
            }
            ImGui.GetWindowDrawList().AddText(rowMin + textOffset, nameColor,
                Path.GetFileNameWithoutExtension(entry.Path));
            ImGui.GetWindowDrawList().AddText(new Vector2(rowMin.X, rowMin.Y + textOffset.Y + line), pathColor,
                entry.Path);

            ImGui.PopID();
        }
    }
}
