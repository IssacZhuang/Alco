using Alco.ImGUI;
using Alco.IO;

namespace Alco.Editor;

/// <summary>
/// The asset browser panel (fixed left pane): a directory tree over every file the
/// asset system serves (owned roots and referenced entries merged), with read-only
/// markers for referenced assets. Double-clicking a file opens it as a document tab.
/// The tree rebuilds whenever <see cref="AssetSystem.Version"/> changes (hot reload,
/// mount changes) or on demand via the refresh button.
/// </summary>
public sealed class AssetBrowserPanel
{
    private readonly EditorContext _context;
    private readonly DocumentManager _documents;

    private int _lastAssetVersion = -1;
    private Node _root = new Node();
    private string _filter = string.Empty;
    private string? _selectedPath;

    /// <summary>Creates the panel.</summary>
    public AssetBrowserPanel(EditorContext context, DocumentManager documents)
    {
        _context = context;
        _documents = documents;
    }

    /// <summary>Draws the panel content inside the left pane's child region.</summary>
    public void DrawContent()
    {
        DrawToolbar();
        ImGui.Separator();

        if (ImGui.BeginChild("##asset_tree"))
        {
            if (_filter.Length > 0)
            {
                DrawFilteredList();
            }
            else
            {
                EnsureTree();
                foreach (KeyValuePair<string, Node> child in _root.Directories)
                {
                    DrawNode(child.Value, depth: 0);
                }
                foreach (string file in _root.Files)
                {
                    DrawFile(file);
                }
            }
        }
        ImGui.EndChild();
    }

    private void DrawToolbar()
    {
        // New-asset creation (owned projects only: referenced roots are read-only).
        bool canCreate = _context.Project.GetAbsoluteAssetRoots().Count > 0;
        ImGui.BeginDisabled(!canCreate);
        if (ImGui.SmallButton("+##new_asset"))
        {
            ImGui.OpenPopup("##new_asset_popup");
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Create a new asset");
        }
        if (ImGui.BeginPopup("##new_asset_popup"))
        {
            if (ImGui.Selectable("Particle Effect 2D (.afx)"))
            {
                CreateParticleEffect(ParticleEffectTemplates.Effect2D, "NewEffect2D");
            }
            if (ImGui.Selectable("Particle Effect 3D (.afx)"))
            {
                CreateParticleEffect(ParticleEffectTemplates.Effect3D, "NewEffect3D");
            }
            ImGui.EndPopup();
        }
        ImGui.SameLine();

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputTextWithHint("##filter", "Filter assets...", ref _filter, 256))
        {
            // applied next frame through DrawFilteredList
        }
    }

    /// <summary>
    /// Writes a new particle effect from a template into the selected file's directory
    /// (or the first owned root) under a unique name and opens it in its document.
    /// </summary>
    private void CreateParticleEffect(string template, string baseName)
    {
        string directory = string.Empty;
        if (_selectedPath is { } selected && _context.Project.IsOwnedAsset(selected))
        {
            directory = (Path.GetDirectoryName(selected) ?? string.Empty).Replace('\\', '/');
        }

        string relativePath = ParticleEffectTemplates.GetUniqueAssetPath(_context, directory, baseName);
        string absolutePath = Path.Combine(
            _context.Project.GetAbsoluteAssetRoots()[0],
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllText(absolutePath, template);

        _selectedPath = relativePath;
        _documents.Open(relativePath);
    }

    private void EnsureTree()
    {
        int version = _context.AssetSystem.Version;
        if (version == _lastAssetVersion)
        {
            return;
        }
        _lastAssetVersion = version;
        RebuildTree();
    }

    private void RebuildTree()
    {
        _root = new Node();
        foreach (string assetPath in _context.AssetSystem.AllAssetNames)
        {
            // .meta sidecars belong to their main asset and are not listed separately
            if (assetPath.EndsWith(FileExt.Meta, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] segments = assetPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            Node node = _root;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (!node.Directories.TryGetValue(segments[i], out Node? child))
                {
                    child = new Node { Name = segments[i] };
                    node.Directories.Add(segments[i], child);
                }
                node = child;
            }
            node.Files.Add(assetPath);
        }
        _root.SortFiles();
    }

    private void DrawNode(Node node, int depth)
    {
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (depth == 0)
        {
            ImGui.SetNextItemOpen(true, ImGuiCond.Once);
        }
        if (ImGui.TreeNodeEx(node.Name + "##" + NodePath(node), flags))
        {
            foreach (KeyValuePair<string, Node> child in node.Directories)
            {
                DrawNode(child.Value, depth + 1);
            }
            foreach (string file in node.Files)
            {
                DrawFile(file);
            }
            ImGui.TreePop();
        }
    }

    private void DrawFile(string assetPath)
    {
        string fileName = Path.GetFileName(assetPath);
        bool selected = string.Equals(_selectedPath, assetPath, StringComparison.OrdinalIgnoreCase);
        bool readOnly = !_context.Project.IsOwnedAsset(assetPath);

        if (readOnly)
        {
            // matches ImGui's default TextDisabled color
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1f));
        }

        if (ImGui.Selectable(fileName + "##" + assetPath, selected))
        {
            _selectedPath = assetPath;
        }

        if (readOnly)
        {
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Referenced asset (read-only)");
            }
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            _documents.Open(assetPath);
        }
    }

    private void DrawFilteredList()
    {
        foreach (string assetPath in _context.AssetSystem.AllAssetNames)
        {
            if (assetPath.EndsWith(FileExt.Meta, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!assetPath.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            DrawFile(assetPath);
        }
    }

    // Nodes currently do not know their full path; derive a stable id suffix from the name.
    // (Tree node labels already differ per level because children are pushed under their parent id stack.)
    private static string NodePath(Node node) => node.Name;

    /// <summary>One directory of the in-memory asset tree.</summary>
    private sealed class Node
    {
        public string Name = string.Empty;
        public readonly SortedDictionary<string, Node> Directories = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Files = new();

        public void SortFiles()
        {
            Files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, Node> child in Directories)
            {
                child.Value.SortFiles();
            }
        }
    }
}
