using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Alco.Editor.Extensibility;
using Alco.ImGUI;

namespace Alco.Editor;

/// <summary>
/// Hosts registered windows in three resizable tabbed regions. Window lifetime and
/// document lifetime are independent of tab visibility and placement.
/// </summary>
public sealed class EditorWorkspace : IDisposable
{
    private const float SplitterThickness = 6f;
    private const string PreferenceApplication = "Alco.Editor";
    private const string WindowPayload = "ALCO_EDITOR_WINDOW";
    private readonly EditorContext _context;
    private readonly WindowRegistry _registry;
    private readonly HashSet<EditorDockRegion> _selectionRequests = new();
    private string _preferenceKey;
    private string? _pendingMove;
    private EditorDockRegion _pendingRegion;
    private string? _pendingClose;
    private double _saveAt = double.PositiveInfinity;
    private bool _disposed;

    /// <summary>Creates a workspace and restores the current project's saved layout.</summary>
    public EditorWorkspace(EditorContext context, WindowRegistry registry)
    {
        _context = context;
        _registry = registry;
        _preferenceKey = GetPreferenceKey(context.Project);
        Layout = LoadLayout();
        RequestSelections();
        _context.ProjectChanged += OnProjectChanged;
    }

    /// <summary>The current placements, selections and splitter proportions.</summary>
    public EditorWorkspaceLayout Layout { get; private set; }

    /// <summary>Shows and selects a registered window without recreating its content.</summary>
    public bool TryShow(string id) => TrySetOpen(id, true);

    /// <summary>Changes visibility without disposing the window or its documents.</summary>
    public bool TrySetOpen(string id, bool open)
    {
        if (!Layout.TrySetOpen(id, open))
        {
            return false;
        }
        RequestSelections();
        ScheduleSave();
        return true;
    }

    /// <summary>Moves a window into a region, making it visible and selected.</summary>
    public bool TryMove(string id, EditorDockRegion region)
    {
        if (!Layout.TryMove(id, region))
        {
            return false;
        }
        RequestSelections();
        ScheduleSave();
        return true;
    }

    /// <summary>Restores default window placements, visibility and splitter sizes.</summary>
    public void ResetLayout()
    {
        Layout.Reset(_registry);
        RequestSelections();
        ScheduleSave();
    }

    /// <summary>Draws the workspace inside the current ImGui host window.</summary>
    public void Draw()
    {
        if (Layout.Windows.Count != _registry.Windows.Count)
        {
            Layout.Reconcile(_registry);
            RequestSelections();
        }
        Vector2 origin = ImGui.GetCursorScreenPos();
        Vector2 size = Vector2.Max(ImGui.GetContentRegionAvail(), new Vector2(32f));
        float availableWidth = Math.Max(2f, size.X - SplitterThickness);
        float rightWidth = ClampExtent(availableWidth * Layout.RightWidthRatio, availableWidth, 220f);
        float leftWidth = availableWidth - rightWidth;
        float availableHeight = Math.Max(2f, size.Y - SplitterThickness);
        float bottomHeight = ClampExtent(availableHeight * Layout.BottomHeightRatio, availableHeight, 120f);
        float topHeight = availableHeight - bottomHeight;

        DrawRegion(EditorDockRegion.Top, origin, new Vector2(leftWidth, topHeight));
        DrawRegion(EditorDockRegion.Bottom, origin + new Vector2(0f, topHeight + SplitterThickness), new Vector2(leftWidth, bottomHeight));
        DrawRegion(EditorDockRegion.Right, origin + new Vector2(leftWidth + SplitterThickness, 0f), new Vector2(rightWidth, size.Y));
        DrawSplitter("##workspace_vertical", origin + new Vector2(leftWidth, 0f),
            new Vector2(SplitterThickness, size.Y), true, availableWidth, rightWidth);
        DrawSplitter("##workspace_horizontal", origin + new Vector2(0f, topHeight),
            new Vector2(leftWidth, SplitterThickness), false, availableHeight, bottomHeight);

        if (_pendingMove != null)
        {
            TryMove(_pendingMove, _pendingRegion);
            _pendingMove = null;
        }
        if (_pendingClose != null)
        {
            TrySetOpen(_pendingClose, false);
            _pendingClose = null;
        }
        if (ImGui.GetTime() >= _saveAt)
        {
            SaveLayout();
        }
    }

    /// <summary>Draws visibility and placement commands inside the shell's Window menu.</summary>
    public void DrawWindowMenu()
    {
        for (int i = 0; i < _registry.Windows.Count; i++)
        {
            IEditorWindow window = _registry.Windows[i];
            if (!Layout.TryGetWindow(window.Id, out EditorWindowPlacement placement))
            {
                continue;
            }
            ImGui.PushID(window.Id);
            if (ImGui.MenuItem(window.Title, string.Empty, placement.IsOpen))
            {
                TrySetOpen(window.Id, !placement.IsOpen);
            }
            ImGui.PopID();
        }
        if (!ImGui.BeginMenu("Move Window"))
        {
            return;
        }
        for (int i = 0; i < _registry.Windows.Count; i++)
        {
            IEditorWindow window = _registry.Windows[i];
            ImGui.PushID(window.Id);
            if (ImGui.BeginMenu(window.Title))
            {
                DrawMoveMenu(window.Id, false);
                ImGui.EndMenu();
            }
            ImGui.PopID();
        }
        ImGui.EndMenu();
    }

    /// <summary>Saves the layout and releases resources owned by contributed windows.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _context.ProjectChanged -= OnProjectChanged;
        SaveLayout();
        foreach (IEditorWindow window in _registry.Windows)
        {
            if (window is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private void DrawRegion(EditorDockRegion region, Vector2 position, Vector2 size)
    {
        ImGui.SetCursorScreenPos(position);
        ImGui.PushID((int)region);
        if (ImGui.BeginChild("##workspace_region", size, ImGuiChildFlags.Borders,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            bool hasWindows = false;
            string? requestedId = _selectionRequests.Contains(region)
                ? Layout.Windows.Find(window => window.Region == region && window.IsSelected)?.Id
                : null;
            if (ImGui.BeginTabBar("##workspace_tabs", ImGuiTabBarFlags.FittingPolicyScroll))
            {
                for (int i = 0; i < _registry.Windows.Count; i++)
                {
                    IEditorWindow window = _registry.Windows[i];
                    if (!Layout.TryGetWindow(window.Id, out EditorWindowPlacement placement)
                        || placement.Region != region || !placement.IsOpen)
                    {
                        continue;
                    }
                    hasWindows = true;
                    bool open = true;
                    ImGuiTabItemFlags flags = requestedId == window.Id
                        ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                    bool visible = ImGui.BeginTabItem($"{window.Title}###workspace:{window.Id}", ref open, flags);
                    SetDragSource(window);
                    if (ImGui.BeginPopupContextItem($"##window_menu:{window.Id}"))
                    {
                        DrawMoveMenu(window.Id, true);
                        ImGui.Separator();
                        if (ImGui.MenuItem("Hide Window"))
                        {
                            _pendingClose = window.Id;
                        }
                        ImGui.EndPopup();
                    }
                    if (!open)
                    {
                        _pendingClose = window.Id;
                    }
                    if (!visible)
                    {
                        continue;
                    }
                    if (requestedId == window.Id)
                    {
                        _selectionRequests.Remove(region);
                    }
                    if (requestedId == null && !placement.IsSelected)
                    {
                        Layout.TrySelect(window.Id);
                        ScheduleSave();
                    }
                    ImGui.PushID(window.Id);
                    if (ImGui.BeginChild("##window_content", Vector2.Zero))
                    {
                        window.Draw(_context);
                    }
                    ImGui.EndChild();
                    ImGui.PopID();
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
            if (!hasWindows)
            {
                ImGui.TextDisabled($"{region} region");
                ImGui.TextWrapped("Drop a window tab here, or open a window from the Window menu.");
            }
        }
        ImGui.EndChild();
        AcceptDrop(region);
        ImGui.PopID();
    }

    private void DrawMoveMenu(string id, bool defer)
    {
        if (!Layout.TryGetWindow(id, out EditorWindowPlacement placement))
        {
            return;
        }
        DrawRegionItem(EditorDockRegion.Top);
        DrawRegionItem(EditorDockRegion.Bottom);
        DrawRegionItem(EditorDockRegion.Right);

        void DrawRegionItem(EditorDockRegion region)
        {
            if (!ImGui.MenuItem($"Move to {region}", string.Empty, placement.Region == region))
            {
                return;
            }
            if (defer)
            {
                _pendingMove = id;
                _pendingRegion = region;
                return;
            }
            TryMove(id, region);
        }
    }

    private static unsafe void SetDragSource(IEditorWindow window)
    {
        if (!ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoDisableHover))
        {
            return;
        }
        int byteCount = Encoding.UTF8.GetByteCount(window.Id);
        Span<byte> bytes = byteCount <= 512 ? stackalloc byte[byteCount] : new byte[byteCount];
        Encoding.UTF8.GetBytes(window.Id, bytes);
        fixed (byte* data = bytes)
        {
            ImGui.SetDragDropPayload(WindowPayload, (IntPtr)data, (uint)byteCount);
        }
        ImGui.TextUnformatted(window.Title);
        ImGui.TextDisabled("Drop in Top, Bottom or Right");
        ImGui.EndDragDropSource();
    }

    private unsafe void AcceptDrop(EditorDockRegion region)
    {
        if (!ImGui.BeginDragDropTarget())
        {
            return;
        }
        ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(WindowPayload);
        if (payload.NativePtr != null && payload.IsDelivery() && payload.DataSize > 0)
        {
            _pendingMove = Encoding.UTF8.GetString((byte*)payload.Data, payload.DataSize);
            _pendingRegion = region;
        }
        ImGui.EndDragDropTarget();
    }

    private void DrawSplitter(string id, Vector2 position, Vector2 size, bool vertical, float available, float extent)
    {
        ImGui.SetCursorScreenPos(position);
        ImGui.InvisibleButton(id, size);
        bool hovered = ImGui.IsItemHovered();
        bool active = ImGui.IsItemActive();
        if (active)
        {
            float delta = vertical ? ImGui.GetIO().MouseDelta.X : ImGui.GetIO().MouseDelta.Y;
            float ratio = ClampExtent(extent - delta, available, vertical ? 220f : 120f) / available;
            if (vertical)
            {
                Layout.RightWidthRatio = ratio;
            }
            else
            {
                Layout.BottomHeightRatio = ratio;
            }
            ScheduleSave();
        }
        if (hovered || active)
        {
            ImGui.SetMouseCursor(vertical ? ImGuiMouseCursor.ResizeEW : ImGuiMouseCursor.ResizeNS);
        }
        uint color = ImGui.GetColorU32(active ? ImGuiCol.SeparatorActive : hovered ? ImGuiCol.SeparatorHovered : ImGuiCol.Border);
        Vector2 start = position + (vertical ? new Vector2(SplitterThickness / 2f, 0f) : new Vector2(0f, SplitterThickness / 2f));
        Vector2 end = start + (vertical ? new Vector2(0f, size.Y) : new Vector2(size.X, 0f));
        ImGui.GetWindowDrawList().AddLine(start, end, color, active ? 2f : 1f);
    }

    private static float ClampExtent(float extent, float available, float minimum)
    {
        float effectiveMinimum = Math.Min(minimum, available * 0.35f);
        float minExtent = Math.Max(effectiveMinimum, available * EditorWorkspaceLayout.MinimumRegionRatio);
        float maxExtent = Math.Min(available - effectiveMinimum, available * EditorWorkspaceLayout.MaximumRegionRatio);
        return Math.Clamp(extent, minExtent, maxExtent);
    }

    private void OnProjectChanged(AlcoProject project)
    {
        SaveLayout();
        _preferenceKey = GetPreferenceKey(project);
        Layout = LoadLayout();
        RequestSelections();
    }

    private EditorWorkspaceLayout LoadLayout()
    {
        EditorWorkspaceLayout layout = _context.Engine.LoadPreference<EditorWorkspaceLayout>(PreferenceApplication, _preferenceKey);
        layout.Reconcile(_registry);
        return layout;
    }

    private void RequestSelections()
    {
        _selectionRequests.Add(EditorDockRegion.Top);
        _selectionRequests.Add(EditorDockRegion.Bottom);
        _selectionRequests.Add(EditorDockRegion.Right);
    }

    private void ScheduleSave() => _saveAt = ImGui.GetTime() + 0.5;

    private void SaveLayout()
    {
        _context.Engine.SavePreference(PreferenceApplication, _preferenceKey, Layout);
        _saveAt = double.PositiveInfinity;
    }

    private static string GetPreferenceKey(AlcoProject project)
    {
        string identity = Path.GetFullPath(project.FilePath ?? project.ProjectDirectory);
        if (OperatingSystem.IsWindows())
        {
            identity = identity.ToUpperInvariant();
        }
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return $"workspace-v1-{hash[..16]}";
    }
}
