namespace Alco.Editor.Extensibility;

/// <summary>
/// A clickable item in an editor menu. The label comes from the tail of the
/// registration path (<see cref="MenuRegistry.AddItem"/>), so it is not repeated here.
/// The shortcut text is display-only — the actual key handling stays where it always
/// was (e.g. Ctrl+S in the document manager, Esc in the editor game).
/// </summary>
public sealed class EditorMenuItem
{
    /// <summary>Creates the item.</summary>
    /// <param name="execute">The action invoked when the item is clicked.</param>
    public EditorMenuItem(Action execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        Execute = execute;
    }

    /// <summary>The shortcut text shown right-aligned (display only).</summary>
    public string Shortcut { get; init; } = string.Empty;

    /// <summary>The enabled-state query; the item is always enabled when null.</summary>
    public Func<bool>? IsEnabled { get; init; }

    /// <summary>The checked-state query; the item is never checked when null.</summary>
    public Func<bool>? IsChecked { get; init; }

    /// <summary>The action invoked when the item is clicked.</summary>
    public Action Execute { get; }
}
