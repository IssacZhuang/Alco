using Alco.Editor.Extensibility;

namespace Alco.Editor;

/// <summary>Persisted placement and selection of a registered editor window.</summary>
public sealed class EditorWindowPlacement
{
    /// <summary>The stable registration ID of the window.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The region containing the window's tab.</summary>
    public EditorDockRegion Region { get; set; }

    /// <summary>Whether the window is visible.</summary>
    public bool IsOpen { get; set; } = true;

    /// <summary>Whether this is the selected tab in its region.</summary>
    public bool IsSelected { get; set; }
}

/// <summary>
/// Serializable workspace state independent of ImGui and document ownership. Unknown
/// registrations and invalid saved values are reconciled against the current modules.
/// </summary>
public sealed class EditorWorkspaceLayout
{
    /// <summary>The minimum persisted fraction occupied by a resizable side region.</summary>
    public const float MinimumRegionRatio = 0.1f;

    /// <summary>The maximum persisted fraction occupied by a resizable side region.</summary>
    public const float MaximumRegionRatio = 0.8f;

    /// <summary>The initial fraction of workspace width reserved for the right region.</summary>
    public const float DefaultRightWidthRatio = 0.26f;

    /// <summary>The initial fraction of left-side height reserved for the bottom region.</summary>
    public const float DefaultBottomHeightRatio = 0.3f;

    /// <summary>The fraction of workspace width reserved for the right region.</summary>
    public float RightWidthRatio { get; set; } = DefaultRightWidthRatio;

    /// <summary>The fraction of left-side height reserved for the bottom region.</summary>
    public float BottomHeightRatio { get; set; } = DefaultBottomHeightRatio;

    /// <summary>The saved window placements, keyed by their stable IDs.</summary>
    public List<EditorWindowPlacement> Windows { get; set; } = new();

    /// <summary>Restores all registered windows to their default visibility and region.</summary>
    public void Reset(WindowRegistry registry)
    {
        RightWidthRatio = DefaultRightWidthRatio;
        BottomHeightRatio = DefaultBottomHeightRatio;
        Windows = new List<EditorWindowPlacement>();
        Reconcile(registry);
    }

    /// <summary>Validates saved state and adds defaults for newly registered windows.</summary>
    public void Reconcile(WindowRegistry registry)
    {
        RightWidthRatio = NormalizeRatio(RightWidthRatio, DefaultRightWidthRatio);
        BottomHeightRatio = NormalizeRatio(BottomHeightRatio, DefaultBottomHeightRatio);
        var placements = new List<EditorWindowPlacement>();
        foreach (IEditorWindow window in registry.Windows)
        {
            EditorWindowPlacement? saved = Windows?.Find(item => item != null && item.Id == window.Id);
            placements.Add(new EditorWindowPlacement
            {
                Id = window.Id,
                Region = saved != null && Enum.IsDefined(saved.Region) ? saved.Region : window.DefaultRegion,
                IsOpen = saved?.IsOpen ?? window.IsOpenByDefault,
                IsSelected = saved?.IsSelected ?? false,
            });
        }
        Windows = placements;
        NormalizeSelection();
    }

    /// <summary>Finds the placement of a registered window.</summary>
    public bool TryGetWindow(string id, out EditorWindowPlacement placement)
    {
        placement = Windows.Find(window => window.Id == id)!;
        return placement != null;
    }

    /// <summary>Moves a registered window, opens it and selects its tab without recreating it.</summary>
    public bool TryMove(string id, EditorDockRegion region)
    {
        if (!Enum.IsDefined(region) || !TryGetWindow(id, out EditorWindowPlacement placement))
        {
            return false;
        }
        placement.Region = region;
        placement.IsOpen = true;
        Select(placement);
        NormalizeSelection();
        return true;
    }

    /// <summary>Opens and selects a registered window, or hides it without closing its content.</summary>
    public bool TrySetOpen(string id, bool open)
    {
        if (!TryGetWindow(id, out EditorWindowPlacement placement))
        {
            return false;
        }
        placement.IsOpen = open;
        if (open)
        {
            Select(placement);
        }
        NormalizeSelection();
        return true;
    }

    /// <summary>Selects an open window in its current region.</summary>
    public bool TrySelect(string id)
    {
        if (!TryGetWindow(id, out EditorWindowPlacement placement) || !placement.IsOpen)
        {
            return false;
        }
        Select(placement);
        return true;
    }

    private void Select(EditorWindowPlacement selected)
    {
        foreach (EditorWindowPlacement window in Windows)
        {
            if (window.Region == selected.Region)
            {
                window.IsSelected = window == selected;
            }
        }
    }

    private void NormalizeSelection()
    {
        foreach (EditorWindowPlacement window in Windows)
        {
            if (!window.IsOpen)
            {
                window.IsSelected = false;
            }
        }
        foreach (EditorDockRegion region in Enum.GetValues<EditorDockRegion>())
        {
            EditorWindowPlacement? selected = Windows.Find(window => window.Region == region && window.IsOpen && window.IsSelected)
                ?? Windows.Find(window => window.Region == region && window.IsOpen);
            if (selected != null)
            {
                Select(selected);
            }
        }
    }

    private static float NormalizeRatio(float ratio, float fallback) =>
        float.IsFinite(ratio) ? Math.Clamp(ratio, MinimumRegionRatio, MaximumRegionRatio) : fallback;
}
