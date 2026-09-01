using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Engine;
using Alco.ImGUI;
using Alco.IO;

namespace Alco.Editor;

/// <summary>
/// The editor shell: main menu bar and a strict two-pane layout — the asset browser on
/// the left, the document area (a tab bar with one tab per open asset) on the right,
/// separated by a draggable splitter. The layout is fixed; only the split position and
/// the browser's visibility can change.
/// <para/>
/// ImGui content is emitted from <see cref="DoUI"/>, which must be called between
/// <see cref="ImGUISystem"/>'s frame begin and render — that is, from the game update
/// phase (<c>EditorGame.OnUpdate</c>). This system's own <c>OnUpdate</c> runs before
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

    private readonly GameEngine _engine;
    private readonly EditorContext _context;
    private readonly AssetBrowserPanel _assetBrowser;
    private readonly DocumentManager _documents;
    private readonly ProjectOpener _projectOpener;

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
    public EditorSystem(GameEngine engine, AlcoProject project)
    {
        _engine = engine;
        _context = new EditorContext(engine, project);

        if (!project.IsUntitled)
        {
            SetIniFilename(Path.Combine(project.ProjectDirectory, "imgui.ini"));
        }
        BuiltInImGUIStyle.ApplyAlcoStyle();

        // Meta saves from asset documents trigger watcher hot reloads; without a
        // reloader for meta types the asset system would throw on an async-void path.
        engine.AssetSystem.RegisterAssetHotReloader(new MetaHotReloader());

        _documents = new DocumentManager(_context);
        _assetBrowser = new AssetBrowserPanel(_context, _documents);

        // The opener performs the project's initial asset mount and owns the mounted
        // sources for later project switches.
        _context.ProjectChanged += OnProjectChanged;
        _projectOpener = new ProjectOpener(_context, _documents);
        if (_context.Project.FilePath != null)
        {
            RecentProjectStore.Save(_context.Project.FilePath);
        }
    }

    /// <summary>The project open in the editor.</summary>
    public AlcoProject Project => _context.Project;

    /// <summary>Shared editor services (project, engine).</summary>
    public EditorContext Context => _context;

    /// <summary>The manager owning the open asset documents.</summary>
    public DocumentManager Documents => _documents;

    /// <summary>Restores the default panel split (Window &gt; Reset Layout).</summary>
    public void RequestResetLayout() => _leftPanelWidth = DefaultLeftPanelWidth;

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
    /// project directory and updates the window title.
    /// </summary>
    private void OnProjectChanged(AlcoProject project)
    {
        if (!project.IsUntitled)
        {
            SetIniFilename(Path.Combine(project.ProjectDirectory, "imgui.ini"));
            RecentProjectStore.Save(project.FilePath!);
        }
        _engine.MainView.Title = string.Format(WindowTitleFormat, project.Name);
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

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("Open Project...", string.Empty))
            {
                RequestOpenProjectDialog();
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Save", "Ctrl+S", false, _documents.ActiveDocument is { IsDirty: true, IsReadOnly: false }))
            {
                _documents.SaveActive();
            }
            if (ImGui.MenuItem("Exit", "Esc"))
            {
                _engine.Stop();
            }
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Window"))
        {
            if (ImGui.MenuItem("Asset Browser", string.Empty, _assetBrowserOpen))
            {
                _assetBrowserOpen = !_assetBrowserOpen;
            }
            if (ImGui.MenuItem("Reset Layout"))
            {
                RequestResetLayout();
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
