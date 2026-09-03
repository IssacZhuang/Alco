namespace Alco.Editor.Extensibility;

/// <summary>
/// A toggleable floating editor panel. Registered panels appear as checkable items in
/// the Window menu and are drawn (while open) after the shell's fixed two-pane layout.
/// </summary>
public interface IEditorPanel
{
    /// <summary>The panel title (Window menu label and window title).</summary>
    string Title { get; }

    /// <summary>Whether the panel is currently open; toggled from the Window menu.</summary>
    bool IsOpen { get; set; }

    /// <summary>Draws the panel's ImGui content (called only while open).</summary>
    /// <param name="context">The shared editor services.</param>
    void Draw(EditorContext context);
}

/// <summary>
/// The floating panels registered by editor modules, in registration order.
/// </summary>
public sealed class PanelRegistry
{
    private readonly List<IEditorPanel> _panels = new();

    /// <summary>The registered panels, in registration order.</summary>
    public IReadOnlyList<IEditorPanel> Panels => _panels;

    /// <summary>Registers a floating panel.</summary>
    public void Register(IEditorPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        _panels.Add(panel);
    }
}
