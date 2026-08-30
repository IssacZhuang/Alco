using Alco.ImGUI;

namespace Alco.Editor;

/// <summary>
/// Base class of an open asset editor tab. One document owns one ImGui window that
/// docks into the central node on first use; the window label stays stable
/// (<c>name###doc:path</c>) so dock layouts persist across sessions. Documents over
/// referenced (non-owned) assets are read-only: controls must be disabled and
/// <see cref="Save"/> refuses to write.
/// </summary>
public abstract class AssetDocument : AutoDisposable
{
    private bool _firstUse = true;

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

    /// <summary>The stable ImGui window name (dock layouts key on it).</summary>
    public string WindowName { get; }

    /// <summary>Whether the asset belongs to a referenced (read-only) entry.</summary>
    public bool IsReadOnly { get; }

    /// <summary>Whether the document holds unsaved modifications.</summary>
    public bool IsDirty { get; protected set; }

    /// <summary>Whether the tab is still open; cleared by the window close button.</summary>
    public bool IsOpen { get; private set; } = true;

    /// <summary>Draws the document window. Called once per frame by the document manager.</summary>
    public void DrawWindow()
    {
        if (_firstUse)
        {
            _firstUse = false;
            if (Context.CentralDockId != 0)
            {
                ImGui.SetNextWindowDockID(Context.CentralDockId, ImGuiCond.FirstUseEver);
            }
        }

        bool open = IsOpen;
        bool visible = ImGui.Begin(WindowName, ref open);
        IsOpen = open;
        if (visible)
        {
            if (IsReadOnly)
            {
                ImGui.TextDisabled("Referenced asset — read-only.");
                ImGui.Separator();
            }
            DrawContent();
        }
        ImGui.End();
    }

    /// <summary>Draws the document's content inside its window.</summary>
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

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
    }
}
