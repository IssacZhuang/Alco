namespace Alco.Editor.Extensibility;

/// <summary>The three tabbed regions of the editor workspace.</summary>
public enum EditorDockRegion
{
    /// <summary>The main document region above the bottom region.</summary>
    Top,

    /// <summary>The region below the main document region.</summary>
    Bottom,

    /// <summary>The full-height region on the right.</summary>
    Right,
}

/// <summary>
/// A dockable editor window registered by an <see cref="IEditorModule"/>. The shell
/// owns its tab, visibility and placement; implementations draw only their content.
/// Implement <see cref="IDisposable"/> when the window owns resources that must be
/// released when the editor shuts down. Hiding or moving a window does not dispose it.
/// </summary>
public interface IEditorWindow
{
    /// <summary>A stable, unique identifier used for layout persistence and ImGui IDs.</summary>
    string Id { get; }

    /// <summary>The visible tab and Window menu title.</summary>
    string Title { get; }

    /// <summary>The region used on first registration and after resetting the layout.</summary>
    EditorDockRegion DefaultRegion { get; }

    /// <summary>Whether the window is visible in the default layout.</summary>
    bool IsOpenByDefault => true;

    /// <summary>Draws content inside the selected tab, without creating a host window.</summary>
    /// <param name="context">The current project and shared editor services.</param>
    void Draw(EditorContext context);
}

/// <summary>A content callback registered as a dockable editor window.</summary>
public sealed class EditorWindow : IEditorWindow
{
    private readonly Action<EditorContext> _draw;

    /// <summary>Creates a window with a stable ID, visible title and content renderer.</summary>
    public EditorWindow(string id, string title, Action<EditorContext> draw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(draw);
        Id = id;
        Title = title;
        _draw = draw;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public string Title { get; }

    /// <inheritdoc />
    public EditorDockRegion DefaultRegion { get; init; } = EditorDockRegion.Top;

    /// <inheritdoc />
    public bool IsOpenByDefault { get; init; } = true;

    /// <inheritdoc />
    public void Draw(EditorContext context) => _draw(context);
}

/// <summary>Dockable windows contributed by editor modules, in registration order.</summary>
public sealed class WindowRegistry
{
    private readonly List<IEditorWindow> _windows = new();

    /// <summary>The registered windows in tab submission order.</summary>
    public IReadOnlyList<IEditorWindow> Windows => _windows;

    /// <summary>Registers a window. IDs must be nonempty and unique within the editor.</summary>
    /// <param name="window">The window instance owned by the editor for its lifetime.</param>
    public void Register(IEditorWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(window.Id);
        if (!Enum.IsDefined(window.DefaultRegion))
        {
            throw new ArgumentOutOfRangeException(nameof(window), "The default region must be a workspace region.");
        }
        if (_windows.Any(existing => existing.Id == window.Id))
        {
            throw new ArgumentException($"An editor window with ID '{window.Id}' is already registered.", nameof(window));
        }
        _windows.Add(window);
    }
}
