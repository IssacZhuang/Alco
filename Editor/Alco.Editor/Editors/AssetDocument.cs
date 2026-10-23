using Alco.ImGUI;

namespace Alco.Editor;

/// <summary>Base class of an open asset editor tab; documents over non-owned assets are read-only.</summary>
public abstract class AssetDocument : AutoDisposable
{
    /// <summary>Creates the document for the given asset-system-relative path.</summary>
    protected AssetDocument(EditorContext context, string assetPath)
    {
        Context = context;
        AssetPath = assetPath;
        IsReadOnly = !context.Project.IsOwnedAsset(assetPath);
        WindowName = $"{Path.GetFileName(assetPath)}###doc:{assetPath}";
    }

    /// <summary>Shared editor services.</summary>
    protected EditorContext Context { get; }

    /// <summary>The asset-system-relative path this document edits.</summary>
    public string AssetPath { get; }

    /// <summary>The stable ImGui tab label.</summary>
    public string WindowName { get; }

    /// <summary>Whether the asset belongs to a referenced (read-only) entry.</summary>
    public bool IsReadOnly { get; }

    /// <summary>Whether the document holds unsaved modifications.</summary>
    public bool IsDirty { get; protected set; }

    /// <summary>Whether the tab is still open; cleared by the tab close button.</summary>
    public bool IsOpen { get; private set; } = true;

    /// <summary>
    /// Draws the document as one tab inside the manager's tab bar. Called once per
    /// frame by the document manager, between <c>BeginTabBar</c>/<c>EndTabBar</c>.
    /// Returns whether the tab is the selected one (its content is visible).
    /// </summary>
    public bool DrawTabItem(bool setSelected)
    {
        bool open = IsOpen;
        ImGuiTabItemFlags flags = ImGuiTabItemFlags.None;
        if (IsDirty)
        {
            flags |= ImGuiTabItemFlags.UnsavedDocument;
        }
        if (setSelected)
        {
            flags |= ImGuiTabItemFlags.SetSelected;
        }

        bool visible = ImGui.BeginTabItem(WindowName, ref open, flags);
        IsOpen = open;
        if (visible)
        {
            if (IsReadOnly)
            {
                ImGui.TextDisabled("Referenced asset — read-only.");
                ImGui.Separator();
            }
            DrawContent();
            ImGui.EndTabItem();
        }
        return visible;
    }

    /// <summary>Reopens the tab after a close request was vetoed (unsaved-changes prompt).</summary>
    public void Reopen() => IsOpen = true;

    /// <summary>
    /// Draws the document's independent inspector in the caller's ImGui window.
    /// Widget IDs are scoped to the asset path and referenced assets cannot be edited.
    /// </summary>
    public void DrawInspector()
    {
        ImGui.PushID(AssetPath);
        try
        {
            if (IsReadOnly)
            {
                ImGui.TextDisabled("Referenced asset — read-only.");
                ImGui.Separator();
            }
            ImGui.BeginDisabled(IsReadOnly);
            try
            {
                DrawInspectorContent();
            }
            finally
            {
                ImGui.EndDisabled();
            }
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>Draws the document's content inside its tab.</summary>
    protected abstract void DrawContent();

    /// <summary>
    /// Draws the inspector's parameters inside an asset-scoped, read-only-aware UI scope.
    /// The default implementation has no editable parameters.
    /// </summary>
    protected virtual void DrawInspectorContent()
    {
        ImGui.TextDisabled("No editable parameters.");
    }

    /// <summary>
    /// Updates document state once per frame, even when its tab or inspector is hidden.
    /// Called by <see cref="DocumentManager.Update"/> after the editor windows are drawn;
    /// the default implementation does nothing.
    /// </summary>
    public virtual void Update()
    {
    }

    /// <summary>Saves the document; overrides must refuse to write when <see cref="IsReadOnly"/> is set.</summary>
    public virtual void Save()
    {
    }

    /// <summary>Marks the document as modified.</summary>
    protected void MarkDirty() => IsDirty = true;

    /// <summary>Returns agent tools contributed by this document while it is open; the base implementation returns none.</summary>
    public virtual IEnumerable<object> CreateAgentTools() => Enumerable.Empty<object>();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
    }
}
