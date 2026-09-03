using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Editor.Extensibility;
using Alco.Engine;
using Alco.ImGUI;
using Alco.IO;

namespace Alco.Editor;

/// <summary>
/// The editor shell: main menu bar and a strict two-pane layout — the asset browser on
/// the left, the document area (a tab bar with one tab per open asset) on the right,
/// separated by a draggable splitter. The layout is fixed; only the split position and
/// the browser's visibility can change. While no project is open (an untitled project),
/// the shell is replaced by the startup screen (<see cref="StartupScreenPanel"/>).
/// <para/>
/// ImGui content is emitted from <see cref="DoUI"/>, which must be called between
/// <see cref="ImGUISystem"/>'s frame begin and render — that is, from the game update
/// phase (<c>EditorEngine.OnUpdate</c>). This system's own <c>OnUpdate</c> runs before
/// the ImGui frame starts and must never emit ImGui calls.
/// </summary>
public sealed class EditorSystem : BaseEngineSystem
{
    private const float DefaultLeftPanelWidth = 280f;
    private const float MinLeftPanelWidth = 160f;
    private const float MinDocumentAreaWidth = 240f;
    private const float SplitterThickness = 6f;

    /// <summary>Editor window title pattern; takes the project name ({0}).</summary>
    public const string WindowTitleFormat = "Alco Editor - {0}";

    /// <summary>
    /// The title of the main menu bar's panel menu; floating panels registered in the
    /// <see cref="PanelRegistry"/> get their toggle items appended to its end.
    /// </summary>
    public const string WindowMenuTitle = "Window";

    /// <summary>Application name under which editor preferences are stored (app-local).</summary>
    private const string PreferenceApplication = "Alco.Editor";

    /// <summary>Preference key holding the recently opened projects.</summary>
    private const string RecentProjectsKey = "recent-projects";

    private readonly GameEngine _engine;
    private readonly EditorContext _context;
    private readonly EditorRegistry _registry;
    private readonly AssetBrowserPanel _assetBrowser;
    private readonly DocumentManager _documents;
    private readonly ProjectOpener _projectOpener;
    private readonly RecentProjects _recentProjects;
    private readonly StartupScreenPanel _startupScreen;

    private float _leftPanelWidth = DefaultLeftPanelWidth;
    private bool _assetBrowserOpen = true;
    private bool _openProjectDialogRequested;
    private bool _openProjectConfirm;
    private string _pendingProjectPath = string.Empty;
    private string _projectOpenError = string.Empty;

    /// <summary>
    /// Creates the editor system. Requires <see cref="ImGUISystem"/> to be registered
    /// first (the ImGui context must exist).
    /// </summary>
    /// <param name="engine">The editor engine.</param>
    /// <param name="project">The project to edit.</param>
    /// <param name="modules">Editor modules registered after the built-in defaults;
    /// a later module can override an earlier registration.</param>
    public EditorSystem(GameEngine engine, AlcoProject project, IReadOnlyList<IEditorModule>? modules = null)
    {
        _engine = engine;
        _context = new EditorContext(engine, project);

        if (!project.IsUntitled)
        {
            SetIniFilename(Path.Combine(project.ProjectDirectory, "imgui.ini"));
        }
        BuiltInImGUIStyle.ApplyAlcoStyle();

        // The registry is the composition root: the built-in module registers the
        // editor's defaults first; the caller's modules then add (or override)
        // extensions. The shell itself is available as a service because the menu
        // delegates resolve it lazily (they run at draw time, after construction).
        _registry = new EditorRegistry(_context);
        _registry.Services.Register<EditorSystem>(this);
        new BuiltInEditorModule().Register(_registry);
        if (modules != null)
        {
            foreach (IEditorModule module in modules)
            {
                module.Register(_registry);
            }
        }

        // Meta saves from asset documents trigger watcher hot reloads; without a
        // reloader for meta types the asset system would throw on an async-void path.
        engine.AssetSystem.RegisterAssetHotReloader(new MetaHotReloader(_registry.MetaTypes));

        _documents = new DocumentManager(_context, _registry.Documents);
        _assetBrowser = new AssetBrowserPanel(_context, _documents, _registry.AssetTemplates);

        // The opener performs the project's initial asset mount and owns the mounted
        // sources for later project switches.
        _context.ProjectChanged += OnProjectChanged;
        _projectOpener = new ProjectOpener(_context, _documents);

        // Recent projects live in the engine's app-local preference storage.
        _recentProjects = engine.LoadPreference<RecentProjects>(PreferenceApplication, RecentProjectsKey);
        _startupScreen = new StartupScreenPanel(_recentProjects, RequestOpenProjectDialog,
            path => TryOpenProject(path, discardUnsaved: false, out _));
        RecordProjectOpened(project);
    }

    /// <summary>The project open in the editor.</summary>
    public AlcoProject Project => _context.Project;

    /// <summary>Shared editor services (project, engine).</summary>
    public EditorContext Context => _context;

    /// <summary>The manager owning the open asset documents.</summary>
    public DocumentManager Documents => _documents;

    /// <summary>Restores the default panel split (Window &gt; Reset Layout).</summary>
    public void RequestResetLayout() => _leftPanelWidth = DefaultLeftPanelWidth;

    /// <summary>Whether the asset browser pane is visible (the Window &gt; Asset Browser toggle).</summary>
    public bool AssetBrowserOpen
    {
        get => _assetBrowserOpen;
        set => _assetBrowserOpen = value;
    }

    /// <summary>
    /// Opens another <c>.alco</c> project in this editor session: closes open
    /// documents (kept open when they have unsaved changes and
    /// <paramref name="discardUnsaved"/> is false), swaps the mounted asset sources
    /// and retargets the UI. Used by both the File menu and the agent API.
    /// </summary>
    /// <param name="path">Path of the <c>.alco</c> project file.</param>
    /// <param name="discardUnsaved">Whether to discard unsaved document changes.</param>
    /// <param name="error">Failure description; empty on success.</param>
    /// <returns>True when the requested project is now open.</returns>
    public bool TryOpenProject(string path, bool discardUnsaved, out string error)
    {
        return _projectOpener.TryOpen(path, discardUnsaved, out error);
    }

    /// <summary>Schedules the platform file picker for choosing a project to open.</summary>
    public void RequestOpenProjectDialog() => _openProjectDialogRequested = true;

    /// <summary>
    /// Reacts to a project switch: points ImGui's layout persistence at the new
    /// project directory, updates the window title and records the recent-project entry.
    /// </summary>
    private void OnProjectChanged(AlcoProject project)
    {
        if (!project.IsUntitled)
        {
            SetIniFilename(Path.Combine(project.ProjectDirectory, "imgui.ini"));
        }
        RecordProjectOpened(project);
        _engine.MainView.Title = string.Format(WindowTitleFormat, project.Name);
    }

    /// <summary>
    /// Adds the project to the recent list and persists it through the engine preference
    /// system (app-local). Untitled projects are not recorded.
    /// </summary>
    private void RecordProjectOpened(AlcoProject project)
    {
        if (project.IsUntitled)
        {
            return;
        }
        _recentProjects.OnProjectOpened(project.FilePath!);
        _engine.SavePreference(PreferenceApplication, RecentProjectsKey, _recentProjects);
    }

    /// <summary>
    /// Emits all editor ImGui content for the current frame. Call from the game update
    /// phase only (see the class remarks).
    /// </summary>
    public void DoUI(float delta)
    {
        DrawMainMenuBar();

        // The file picker continuation resumes on the main thread through the game
        // synchronization context. Handle both outside the menu bar so the
        // confirmation popup shares this ID stack level.
        if (_openProjectDialogRequested)
        {
            _openProjectDialogRequested = false;
            OpenProjectDialog();
        }
        DrawOpenProjectModal();

        // No project open (untitled placeholder): show the startup screen instead of
        // the editor shell.
        if (_context.Project.IsUntitled)
        {
            _startupScreen.DrawContent();
            return;
        }

        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(viewport.WorkSize, ImGuiCond.Always);

        // Borderless host pinned to the work area; all editor UI lives inside it so no
        // window can be moved, resized or torn off.
        const ImGuiWindowFlags hostFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoDocking;

        bool hostOpen = true;
        if (ImGui.Begin("##editor_host", ref hostOpen, hostFlags))
        {
            if (_assetBrowserOpen)
            {
                if (ImGui.BeginChild("##asset_browser", new Vector2(_leftPanelWidth, 0f), ImGuiChildFlags.Borders))
                {
                    _assetBrowser.DrawContent();
                }
                ImGui.EndChild();

                ImGui.SameLine(0f, 0f);
                DrawSplitter();
                ImGui.SameLine(0f, 0f);
            }

            if (ImGui.BeginChild("##document_area"))
            {
                _documents.DrawDocuments();
            }
            ImGui.EndChild();
        }
        ImGui.End();

        // Floating panels registered by modules draw after the fixed shell layout.
        IReadOnlyList<IEditorPanel> panels = _registry.Panels.Panels;
        for (int i = 0; i < panels.Count; i++)
        {
            if (panels[i].IsOpen)
            {
                panels[i].Draw(_context);
            }
        }
    }

    /// <summary>Draws the visible, draggable divider between the two panes.</summary>
    private void DrawSplitter()
    {
        ImGui.InvisibleButton("##layout_splitter", new Vector2(SplitterThickness, -1f));
        bool hovered = ImGui.IsItemHovered();
        bool active = ImGui.IsItemActive();
        if (active)
        {
            // Left + splitter + remaining = full width; cap the left pane so the
            // document area always keeps its minimum width.
            float remaining = ImGui.GetContentRegionAvail().X;
            float maxLeft = _leftPanelWidth + SplitterThickness + remaining - MinDocumentAreaWidth;
            _leftPanelWidth = Math.Clamp(
                _leftPanelWidth + ImGui.GetIO().MouseDelta.X,
                MinLeftPanelWidth,
                Math.Max(MinLeftPanelWidth, maxLeft));
        }
        if (hovered || active)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        }

        // Center line, highlighted while hovered or dragged.
        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        float x = MathF.Round((min.X + max.X) * 0.5f) + 0.5f;
        uint color = ImGui.GetColorU32(active ? ImGuiCol.SeparatorActive
            : hovered ? ImGuiCol.SeparatorHovered
            : ImGuiCol.Border);
        ImGui.GetWindowDrawList().AddLine(new Vector2(x, min.Y), new Vector2(x, max.Y), color, active ? 2f : 1f);
    }

    private void DrawMainMenuBar()
    {
        if (!ImGui.BeginMainMenuBar())
        {
            return;
        }

        IReadOnlyList<EditorMenu> menus = _registry.Menus.Menus;
        for (int i = 0; i < menus.Count; i++)
        {
            EditorMenu menu = menus[i];
            if (!ImGui.BeginMenu(menu.Title))
            {
                continue;
            }

            IReadOnlyList<EditorMenuEntry> entries = menu.Entries;
            for (int j = 0; j < entries.Count; j++)
            {
                EditorMenuEntry entry = entries[j];
                if (entry.IsSeparator)
                {
                    ImGui.Separator();
                    continue;
                }
                EditorMenuItem item = entry.Item!;
                if (ImGui.MenuItem(entry.Label, item.Shortcut,
                        item.IsChecked?.Invoke() ?? false, item.IsEnabled?.Invoke() ?? true))
                {
                    item.Execute();
                }
            }

            // Floating panels get their toggle items at the end of the Window menu.
            if (menu.Title == WindowMenuTitle)
            {
                IReadOnlyList<IEditorPanel> panels = _registry.Panels.Panels;
                for (int j = 0; j < panels.Count; j++)
                {
                    IEditorPanel panel = panels[j];
                    bool open = panel.IsOpen;
                    if (ImGui.MenuItem(panel.Title, string.Empty, open))
                    {
                        panel.IsOpen = !open;
                    }
                }
            }
            ImGui.EndMenu();
        }

        ImGui.EndMainMenuBar();
    }

    /// <summary>
    /// Points ImGui's ini persistence at the project directory so each project keeps its
    /// own editor layout. The buffer is deliberately never freed: ImGui borrows the
    /// pointer for the whole context lifetime.
    /// </summary>
    private static unsafe void SetIniFilename(string path)
    {
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(path + '\0');
        IntPtr ptr = Marshal.AllocHGlobal(utf8.Length);
        Marshal.Copy(utf8, 0, ptr, utf8.Length);
        ImGui.GetIO().NativePtr->IniFilename = (byte*)ptr;
    }

    /// <summary>
    /// Shows the platform file picker and opens the chosen project. The awaiter captures
    /// the game synchronization context, so the picker continuation resumes on the main
    /// thread; a blocked switch (unsaved changes, load failure) is surfaced in
    /// <see cref="DrawOpenProjectModal"/>.
    /// </summary>
    private async void OpenProjectDialog()
    {
        string[] files = await _engine.MainView.OpenFilePickerAsync(
            _context.Project.ProjectDirectory,
            allowMultiple: false,
            new DialogFileFilter("Alco Project", "alco"));
        if (files.Length == 0)
        {
            return;
        }

        if (TryOpenProject(files[0], discardUnsaved: false, out string error))
        {
            return;
        }
        _pendingProjectPath = files[0];
        _projectOpenError = error;
        _openProjectConfirm = true;
    }

    /// <summary>
    /// Modal reporting a failed project open. While dirty documents block the switch,
    /// it offers a discard-and-switch button; load failures just show the error.
    /// </summary>
    private void DrawOpenProjectModal()
    {
        if (_openProjectConfirm)
        {
            _openProjectConfirm = false;
            ImGui.OpenPopup("Open Project");
        }

        bool open = true;
        if (!ImGui.BeginPopupModal("Open Project", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }

        ImGui.TextUnformatted(_projectOpenError);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        bool hasDirty = HasDirtyDocuments();
        if (hasDirty)
        {
            if (ImGui.Button("Discard and Switch", new Vector2(140f, 0f)))
            {
                TryOpenProject(_pendingProjectPath, discardUnsaved: true, out _);
                _pendingProjectPath = string.Empty;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
        }
        if (ImGui.Button(hasDirty ? "Cancel" : "OK", new Vector2(140f, 0f)))
        {
            _pendingProjectPath = string.Empty;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private bool HasDirtyDocuments()
    {
        foreach (AssetDocument document in _documents.Documents)
        {
            if (document.IsDirty)
            {
                return true;
            }
        }
        return false;
    }
}
