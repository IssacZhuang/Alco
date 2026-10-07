using System.Numerics;
using Alco.ImGUI;
using Alco.IO;

namespace Alco.Editor;

/// <summary>
/// Reusable asset picker widget: an input field for the asset path plus a browse
/// button that opens a picker popup anchored below the field. The popup contains a
/// search box over a browser: with an empty search it shows the directory tree pruned
/// to branches that contain assets of the requested type; with a non-empty search it
/// shows the matching asset paths as a flat list. Clicking an entry writes its path
/// into the field and closes the popup. The input field and the browse button also
/// accept an asset dropped from the asset browser (<see cref="EditorDragDrop.AssetPayload"/>),
/// taking the same assignment path as a popup pick. The filtered tree is cached and rebuilt when
/// the asset system version or the type filter changes. One instance per field: each
/// instance keeps its own search text and tree cache.
/// </summary>
public sealed class AssetPicker
{
    private string _search = string.Empty;
    private int _lastAssetVersion = -1;
    private Type? _lastAssetType;
    private HashSet<string> _allowedExtensions = new(StringComparer.OrdinalIgnoreCase);
    private Node _root = new();

    /// <summary>
    /// Draws the input field plus browse button and runs the picker popup.
    /// The input width can be controlled with <c>ImGui.SetNextItemWidth</c> before
    /// calling this method (the input field is the first widget drawn).
    /// </summary>
    /// <param name="context">Editor context providing the asset system.</param>
    /// <param name="id">ImGui id of the input field (e.g. <c>"##path"</c>); the other
    /// picker widget ids are derived from it, so it must be unique in scope.</param>
    /// <param name="assetPath">The selected asset path, edited in place.</param>
    /// <param name="assetType">Target asset type; only assets a registered loader can
    /// load as this type are offered. Null offers every asset (minus .meta sidecars).
    /// When no loader handles the type, every asset is offered as a fallback.</param>
    /// <returns>True when the path changed this frame.</returns>
    public bool Draw(EditorContext context, string id, ref string assetPath, Type? assetType = null)
    {
        bool changed = ImGui.InputText(id, ref assetPath, 256);
        if (EditorDragDrop.TryAcceptAsset(out string dropped))
        {
            // Same assignment path as a popup pick.
            assetPath = dropped;
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("...##" + id))
        {
            OpenPopupUnderLastItem(id);
        }
        if (!changed && EditorDragDrop.TryAcceptAsset(out dropped))
        {
            assetPath = dropped;
            changed = true;
        }

        changed |= DrawPopup(context, id, ref assetPath, assetType);
        return changed;
    }

    /// <summary>Anchors the popup just below the browse button, clamped to the viewport work area.</summary>
    private static void OpenPopupUnderLastItem(string id)
    {
        Vector2 itemMin = ImGui.GetItemRectMin();
        Vector2 itemMax = ImGui.GetItemRectMax();

        const float width = 420f;
        const float height = 320f;
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        Vector2 workMin = viewport.WorkPos;
        Vector2 workMax = workMin + viewport.WorkSize;

        Vector2 pos = new(itemMin.X, itemMax.Y + ImGui.GetStyle().ItemSpacing.Y);
        if (pos.Y + height > workMax.Y)
        {
            pos.Y = itemMin.Y - ImGui.GetStyle().ItemSpacing.Y - height;
        }
        pos.X = Math.Clamp(pos.X, workMin.X, Math.Max(workMin.X, workMax.X - width));
        pos.Y = Math.Clamp(pos.Y, workMin.Y, Math.Max(workMin.Y, workMax.Y - height));

        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSize(new Vector2(width, height), ImGuiCond.Appearing);
        ImGui.OpenPopup(id + "_popup");
    }

    /// <summary>Draws the search box and the file browser; returns true when an entry was picked.</summary>
    private bool DrawPopup(EditorContext context, string id, ref string assetPath, Type? assetType)
    {
        if (!ImGui.BeginPopup(id + "_popup", ImGuiWindowFlags.NoMove))
        {
            return false;
        }

        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
        }
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##search", "Search assets...", ref _search, 256);
        ImGui.Separator();

        EnsureTree(context, assetType);

        bool picked = false;
        if (ImGui.BeginChild("##list"))
        {
            if (_search.Length == 0)
            {
                foreach (KeyValuePair<string, Node> child in _root.Directories)
                {
                    if (DrawNode(child.Value, depth: 0, ref assetPath))
                    {
                        picked = true;
                    }
                }
                foreach (string file in _root.Files)
                {
                    if (DrawFileEntry(file, Path.GetFileName(file) + "##" + file, ref assetPath, showPathTooltip: true))
                    {
                        picked = true;
                    }
                }
                if (_root.Directories.Count == 0 && _root.Files.Count == 0)
                {
                    ImGui.TextDisabled("No assets of this type.");
                }
            }
            else
            {
                int matches = 0;
                if (DrawSearchMatches(_root, ref assetPath, ref matches))
                {
                    picked = true;
                }
                if (matches == 0)
                {
                    ImGui.TextDisabled("No matching assets.");
                }
            }
        }
        ImGui.EndChild();

        if (picked)
        {
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
        return picked;
    }

    /// <summary>Draws one file entry and writes the picked path into <paramref name="currentPath"/>; returns true when it was clicked.</summary>
    private bool DrawFileEntry(string assetPath, string label, ref string currentPath, bool showPathTooltip)
    {
        bool selected = string.Equals(currentPath, assetPath, StringComparison.OrdinalIgnoreCase);
        if (ImGui.Selectable(label, selected))
        {
            currentPath = assetPath;
            return true;
        }
        if (showPathTooltip && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(assetPath);
        }
        return false;
    }

    /// <summary>
    /// Recursively draws the files of the filtered tree whose path contains the search
    /// text as a flat, sorted list; writes a picked path into
    /// <paramref name="currentPath"/> and returns true when one of them was picked.
    /// </summary>
    private bool DrawSearchMatches(Node node, ref string currentPath, ref int matches)
    {
        bool picked = false;
        foreach (KeyValuePair<string, Node> child in node.Directories)
        {
            if (DrawSearchMatches(child.Value, ref currentPath, ref matches))
            {
                picked = true;
            }
        }
        foreach (string file in node.Files)
        {
            if (!file.Contains(_search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            matches++;
            if (DrawFileEntry(file, file, ref currentPath, showPathTooltip: false))
            {
                picked = true;
            }
        }
        return picked;
    }

    /// <summary>Draws one directory of the filtered tree; returns true when a file inside was picked.</summary>
    private bool DrawNode(Node node, int depth, ref string currentPath)
    {
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (depth == 0)
        {
            ImGui.SetNextItemOpen(true, ImGuiCond.Once);
        }
        if (!ImGui.TreeNodeEx(node.Name + "##" + node.Name, flags))
        {
            return false;
        }

        bool picked = false;
        foreach (KeyValuePair<string, Node> child in node.Directories)
        {
            if (DrawNode(child.Value, depth + 1, ref currentPath))
            {
                picked = true;
            }
        }
        foreach (string file in node.Files)
        {
            if (DrawFileEntry(file, Path.GetFileName(file) + "##" + file, ref currentPath, showPathTooltip: true))
            {
                picked = true;
            }
        }
        ImGui.TreePop();
        return picked;
    }

    /// <summary>
    /// Rebuilds the filtered tree when the asset system version or the requested type
    /// changed. Loaders registered mid-session without an entry refresh are not picked
    /// up until the next rebuild — loader registration is a start-time event in practice.
    /// </summary>
    private void EnsureTree(EditorContext context, Type? assetType)
    {
        int version = context.AssetSystem.Version;
        if (version == _lastAssetVersion && ReferenceEquals(assetType, _lastAssetType))
        {
            return;
        }
        _lastAssetVersion = version;
        _lastAssetType = assetType;

        // An empty set (null type, or no loader handles the type) means "no filter".
        if (assetType != null)
        {
            IReadOnlySet<string> extensions = context.AssetSystem.GetExtensionsForType(assetType);
            if (extensions.Count > 0)
            {
                _allowedExtensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                _allowedExtensions.Clear();
            }
        }
        else
        {
            _allowedExtensions.Clear();
        }

        RebuildTree(context);
    }

    /// <summary>Builds the directory tree over the assets matching the current extension filter.</summary>
    private void RebuildTree(EditorContext context)
    {
        _root = new Node();
        foreach (string assetPath in context.AssetSystem.AllAssetNames)
        {
            // .meta sidecars belong to their main asset and are not listed separately
            if (assetPath.EndsWith(FileExt.Meta, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (_allowedExtensions.Count > 0
                && !_allowedExtensions.Contains(Path.GetExtension(assetPath)))
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

    /// <summary>One directory of the filtered asset tree.</summary>
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
