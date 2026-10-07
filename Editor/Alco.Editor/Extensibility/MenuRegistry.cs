namespace Alco.Editor.Extensibility;

/// <summary>
/// A top-level menu of the editor's main menu bar, holding its entries in
/// registration order. Created by the <see cref="MenuRegistry"/> on first use.
/// </summary>
public sealed class EditorMenu
{
    private readonly List<EditorMenuEntry> _entries = new();

    /// <summary>Creates the menu.</summary>
    public EditorMenu(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        Title = title;
    }

    /// <summary>The menu title (the head of the registration paths).</summary>
    public string Title { get; }

    /// <summary>The entries in registration order.</summary>
    public IReadOnlyList<EditorMenuEntry> Entries => _entries;

    /// <summary>Appends an entry. Called by the owning <see cref="MenuRegistry"/>.</summary>
    public void Add(EditorMenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Add(entry);
    }
}

/// <summary>
/// The editor's main menu bar, addressed by paths: the head of the path is the
/// top-level menu title, the tail is the item label (<c>"File/Open Project..."</c>).
/// Top-level menus appear in first-appearance order, entries in registration order.
/// </summary>
public sealed class MenuRegistry
{
    private readonly List<EditorMenu> _menus = new();

    /// <summary>The top-level menus, in first-appearance order.</summary>
    public IReadOnlyList<EditorMenu> Menus => _menus;

    /// <summary>Registers an item under a path like <c>"File/Save"</c>.</summary>
    /// <param name="path">The menu path: top-level title, a slash, then the item label.</param>
    /// <param name="item">The item's behavior and display state.</param>
    public void AddItem(string path, EditorMenuItem item)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(item);
        int slash = path.IndexOf('/');
        string title = slash >= 0 ? path[..slash] : path;
        string label = slash >= 0 ? path[(slash + 1)..] : path;
        GetOrCreateMenu(title).Add(new EditorMenuEntry(label, item));
    }

    /// <summary>Appends a separator to a top-level menu.</summary>
    /// <param name="menu">The top-level menu title.</param>
    public void AddSeparator(string menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        GetOrCreateMenu(menu).Add(EditorMenuEntry.Separator);
    }

    private EditorMenu GetOrCreateMenu(string title)
    {
        for (int i = 0; i < _menus.Count; i++)
        {
            if (string.Equals(_menus[i].Title, title, StringComparison.Ordinal))
            {
                return _menus[i];
            }
        }
        var menu = new EditorMenu(title);
        _menus.Add(menu);
        return menu;
    }
}
