using System.ComponentModel;
using System.Text;
using Alco.Engine;
using Alco.AgentControlProtocol;

namespace Alco.Editor;

/// <summary>
/// The editor's base agent tools, always available while the agent API runs:
/// document open/close/save, project switching, asset listing and layout control.
/// Frame screenshots and script execution come from the agent control host's built-in
/// tools. Asset-type-specific tools are
/// contributed by the open documents themselves (<see cref="AssetDocument.CreateAgentTools"/>).
/// Tools run on the engine main thread unless marked otherwise.
/// </summary>
[AgentTools]
public sealed class EditorBaseTools
{
    private const int MaxListedAssets = 200;

    private readonly GameEngine _engine;
    private readonly EditorSystem _editor;

    /// <summary>Creates the base tool set.</summary>
    public EditorBaseTools(GameEngine engine, EditorSystem editor)
    {
        _engine = engine;
        _editor = editor;
    }

    [AgentFunction]
    [Description("Opens an asset as an editor document tab (or focuses it when already open). Unknown extensions open in a read-only info view. Referenced assets open read-only.")]
    public string OpenAsset(
        [Description("Asset-system-relative path, e.g. 'Materials/Dirt.amat'. Use ListAssets to discover paths.")] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "error: path is empty.";
        }
        if (!_engine.AssetSystem.IsFileExist(path))
        {
            return $"error: asset not found: {path}";
        }

        _editor.Documents.Open(path);
        AssetDocument? document = _editor.Documents.FindOpen(path);
        return document == null
            ? $"error: failed to open {path}"
            : $"opened {document.AssetPath} ({document.GetType().Name}{(document.IsReadOnly ? ", read-only" : string.Empty)})";
    }

    [AgentFunction]
    [Description("Closes the editor document tab of an open asset. Unsaved changes are discarded.")]
    public string CloseAsset(
        [Description("Asset-system-relative path of the open document.")] string path)
    {
        return _editor.Documents.Close(path)
            ? $"closed {path}"
            : $"error: no open document for {path}";
    }

    [AgentFunction]
    [Description("Saves the editor document of an open asset. Documents of referenced (read-only) assets cannot be saved.")]
    public string SaveAsset(
        [Description("Asset-system-relative path of the open document.")] string path)
    {
        AssetDocument? document = _editor.Documents.FindOpen(path);
        if (document == null)
        {
            return $"error: no open document for {path}";
        }
        if (document.IsReadOnly)
        {
            return $"error: {path} is a referenced asset and read-only";
        }

        return _editor.Documents.Save(path)
            ? $"saved {path}"
            : $"error: failed to save {path}";
    }

    [AgentFunction]
    [Description("Lists the project's assets (sidecar .meta files excluded), each tagged as owned (editable) or referenced (read-only).")]
    public string ListAssets(
        [Description("Optional case-insensitive substring filter on the asset path.")] string filter = "")
    {
        AlcoProject project = _editor.Project;
        var builder = new StringBuilder();
        int listed = 0;
        int skipped = 0;
        foreach (string name in _engine.AssetSystem.AllAssetNames)
        {
            if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (listed >= MaxListedAssets)
            {
                skipped++;
                continue;
            }

            builder.Append(name)
                .Append(project.IsOwnedAsset(name) ? " [owned]" : " [referenced]")
                .Append('\n');
            listed++;
        }

        if (listed == 0)
        {
            return filter.Length > 0 ? $"no assets matching '{filter}'" : "no assets";
        }
        if (skipped > 0)
        {
            builder.Append($"... and {skipped} more (truncated at {MaxListedAssets}; use a filter)\n");
        }
        return builder.ToString();
    }

    [AgentFunction]
    [Description("Lists the currently open asset documents with their dirty and read-only state.")]
    public string ListOpenDocuments()
    {
        IReadOnlyList<AssetDocument> documents = _editor.Documents.Documents;
        if (documents.Count == 0)
        {
            return "no open documents";
        }

        var builder = new StringBuilder();
        foreach (AssetDocument document in documents)
        {
            builder.Append(document.AssetPath);
            if (document.IsDirty)
            {
                builder.Append(" (dirty)");
            }
            if (document.IsReadOnly)
            {
                builder.Append(" (read-only)");
            }
            builder.Append('\n');
        }
        return builder.ToString();
    }

    [AgentFunction]
    [Description("Restores the editor's default panel split (asset browser left, documents right).")]
    public string ResetLayout()
    {
        _editor.RequestResetLayout();
        return "layout reset scheduled";
    }

    [AgentFunction]
    [Description("Opens a different Alco project in the editor, replacing the currently open one: all open asset documents are closed, the project's asset roots are remounted, and ListAssets/GetProjectInfo afterwards reflect the new project.")]
    public string OpenProject(
        [Description("Path of the .alco project file (absolute, or relative to the editor process's working directory).")] string path,
        [Description("When true, unsaved changes in open documents are discarded instead of failing the call.")] bool discardChanges = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "error: path is empty.";
        }
        return _editor.TryOpenProject(path, discardChanges, out string error)
            ? $"opened project: {path}"
            : $"error: {error}";
    }

    [AgentFunction]
    [Description("Describes the open Alco project: name, directories, owned asset roots and referenced asset entries.")]
    public string GetProjectInfo()
    {
        AlcoProject project = _editor.Project;
        var builder = new StringBuilder();
        builder.Append("name: ").Append(project.Name).Append('\n');
        builder.Append("projectDirectory: ").Append(project.ProjectDirectory).Append('\n');
        builder.Append("projectFile: ").Append(project.FilePath ?? "(untitled)").Append('\n');
        builder.Append("assetRoots:\n");
        foreach (string root in project.AssetsPaths)
        {
            builder.Append("  ").Append(root).Append('\n');
        }
        builder.Append("referencedAssets:\n");
        foreach (string entry in project.ReferencedAssets)
        {
            builder.Append("  ").Append(entry).Append('\n');
        }
        return builder.ToString();
    }
}
