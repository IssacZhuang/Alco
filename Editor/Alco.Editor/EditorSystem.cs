using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Engine;
using Alco.ImGUI;
using Alco.IO;

namespace Alco.Editor;

/// <summary>
/// The editor shell: main menu bar, full-viewport dockspace, the default dock layout
/// (asset browser left, document area center/right), the asset browser panel and the
/// document manager that owns the open asset editors.
/// <para/>
/// ImGui content is emitted from <see cref="DoUI"/>, which must be called between
/// <see cref="ImGUISystem"/>'s frame begin and render — that is, from the game update
/// phase (<c>EditorGame.OnUpdate</c>). This system's own <c>OnUpdate</c> runs before
/// the ImGui frame starts and must never emit ImGui calls.
/// </summary>
public sealed class EditorSystem : BaseEngineSystem
{
    private readonly GameEngine _engine;
    private readonly EditorContext _context;
    private readonly AssetBrowserPanel _assetBrowser;
    private readonly DocumentManager _documents;

    private uint _dockspaceId;
    private bool _layoutPending = true;
    private bool _assetBrowserOpen = true;

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

        ImGuiIOPtr io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
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
    }

    /// <summary>The project open in the editor.</summary>
    public AlcoProject Project => _context.Project;

    /// <summary>Shared editor services (project, engine, dock state).</summary>
    public EditorContext Context => _context;

    /// <summary>The manager owning the open asset documents.</summary>
    public DocumentManager Documents => _documents;

    /// <summary>Rebuilds the default dock layout on the next frame (Window &gt; Reset Layout).</summary>
    public void RequestResetLayout() => _layoutPending = true;

    /// <summary>
    /// Emits all editor ImGui content for the current frame. Call from the game update
    /// phase only (see the class remarks).
    /// </summary>
    public void DoUI(float delta)
    {
        DrawMainMenuBar();

        _dockspaceId = ImGui.DockSpaceOverViewport();
        if (_layoutPending)
        {
            _layoutPending = false;
            BuildDefaultLayout();
        }

        _assetBrowser.Draw(ref _assetBrowserOpen);
        _documents.DrawDocuments();
    }

    private void DrawMainMenuBar()
    {
        if (!ImGui.BeginMainMenuBar())
        {
            return;
        }

        if (ImGui.BeginMenu("File"))
        {
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
                _layoutPending = true;
            }
            ImGui.EndMenu();
        }

        ImGui.EndMainMenuBar();
    }

    /// <summary>
    /// Rebuilds the default layout: asset browser docked left, all open documents in the
    /// remaining central node. Runs on first frame and on Window &gt; Reset Layout; user
    /// rearrangements persist through ImGui's ini file afterwards.
    /// </summary>
    private void BuildDefaultLayout()
    {
        if (!ImGuiDockBuilder.NodeExists(_dockspaceId))
        {
            return;
        }

        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGuiDockBuilder.RemoveNode(_dockspaceId);
        ImGuiDockBuilder.AddNode(_dockspaceId, ImGuiDockNodeFlags.None);
        ImGuiDockBuilder.SetNodePos(_dockspaceId, viewport.WorkPos);
        ImGuiDockBuilder.SetNodeSize(_dockspaceId, viewport.WorkSize);

        ImGuiDockBuilder.SplitNode(_dockspaceId, ImGuiDir.Left, 0.22f, out uint leftId, out uint centralId);
        ImGuiDockBuilder.DockWindow(AssetBrowserPanel.WindowName, leftId);
        _documents.DockAllDocuments(centralId);
        ImGuiDockBuilder.Finish(_dockspaceId);

        _context.CentralDockId = centralId;
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
}
