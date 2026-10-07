namespace Alco.Editor.Extensibility;

/// <summary>
/// One entry in an <see cref="EditorMenu"/>: a clickable item with its label, or a
/// separator (the shared <see cref="Separator"/> instance).
/// </summary>
public sealed class EditorMenuEntry
{
    private EditorMenuEntry()
    {
        Label = string.Empty;
    }

    /// <summary>Creates an item entry.</summary>
    public EditorMenuEntry(string label, EditorMenuItem item)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(item);
        Label = label;
        Item = item;
    }

    /// <summary>The shared separator entry.</summary>
    public static EditorMenuEntry Separator { get; } = new();

    /// <summary>The item label (the tail of the registration path); empty for separators.</summary>
    public string Label { get; }

    /// <summary>The item, or null when the entry is a separator.</summary>
    public EditorMenuItem? Item { get; }

    /// <summary>Whether the entry is a separator.</summary>
    public bool IsSeparator => Item == null;
}
