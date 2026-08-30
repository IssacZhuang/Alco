using Alco.ImGUI;

namespace Alco.Editor;

/// <summary>
/// Base class of an open asset editor tab. One document owns one tab in the document
/// area's tab bar; the tab label stays stable (<c>name###doc:path</c>). Documents over
/// referenced (non-owned) assets are read-only: controls must be disabled and
/// <see cref="Save"/> refuses to write.
/// </summary>
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
    /// </summary>
    public void DrawTabItem(bool setSelected)
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
    }

    /// <summary>Draws the document's content inside its tab.</summary>
    protected abstract void DrawContent();

    /// <summary>
    /// Saves the document. The base implementation does nothing; editing documents
    /// override it and must refuse to write when <see cref="IsReadOnly"/> is set.
    /// </summary>
    public virtual void Save()
    {
    }

    /// <summary>Marks the document as modified.</summary>
    protected void MarkDirty() => IsDirty = true;

    /// <summary>
    /// Agent tools contributed by this document while it is open. Each returned
    /// instance's <c>[AgentFunction]</c> methods are registered into the editor's agent
    /// API when the document opens and unregistered when it closes. Method names should
    /// carry an asset-type prefix (e.g. <c>Material_SetBaseColor</c>) so they cannot
    /// collide with the editor's base tools. The base implementation contributes none:
    /// text-format assets are better edited by writing the file directly and letting
    /// hot reload update the preview.
    /// </summary>
    public virtual IEnumerable<object> CreateAgentTools() => Enumerable.Empty<object>();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
    }
}
