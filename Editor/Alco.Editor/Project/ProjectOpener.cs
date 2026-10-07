using Alco.IO;

namespace Alco.Editor;

/// <summary>
/// Opens a different <see cref="AlcoProject"/> in a running editor: the new project
/// file is loaded and validated first (a failure leaves the current project
/// untouched), then open documents are closed, the mounted asset sources are swapped,
/// and the new project is published through <see cref="EditorContext.SetProject"/>.
/// The opener also performs the initial mount of the project the editor starts with,
/// so mounting and unmounting stay symmetric in one owner.
/// <para/>
/// Must be used from the engine main thread: it touches documents, the asset system
/// and editor state.
/// </summary>
public sealed class ProjectOpener
{
    private readonly EditorContext _context;
    private readonly DocumentManager _documents;
    private readonly List<IFileSource> _mountedSources = new();

    /// <summary>
    /// Creates the opener and mounts the editor's initial project onto the asset
    /// system.
    /// </summary>
    public ProjectOpener(EditorContext context, DocumentManager documents)
    {
        _context = context;
        _documents = documents;
        _mountedSources.AddRange(ProjectAssetMount.Mount(context.Project, context.AssetSystem));
    }

    /// <summary>
    /// Switches the editor to the project stored in <paramref name="path"/>. Opening
    /// the project that is already open succeeds without doing anything.
    /// </summary>
    /// <param name="path">Path of the <c>.alco</c> project file.</param>
    /// <param name="discardUnsaved">When true, unsaved document changes are discarded instead of blocking the switch.</param>
    /// <param name="error">Failure description (unsaved changes or a load failure); empty on success.</param>
    /// <returns>True when the editor now has the requested project open.</returns>
    public bool TryOpen(string path, bool discardUnsaved, out string error)
    {
        AlcoProject project;
        try
        {
            project = AlcoProject.Load(path);
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }

        if (string.Equals(project.FilePath, _context.Project.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            error = string.Empty;
            return true;
        }

        if (!_documents.CloseAll(discardUnsaved))
        {
            error = "Unsaved changes in: " + string.Join(", ", GetDirtyDocumentPaths());
            return false;
        }

        ProjectAssetMount.Unmount(_mountedSources, _context.AssetSystem);
        _mountedSources.Clear();

        _context.SetProject(project);
        _mountedSources.AddRange(ProjectAssetMount.Mount(project, _context.AssetSystem));

        error = string.Empty;
        return true;
    }

    private string GetDirtyDocumentPaths()
    {
        var builder = new System.Text.StringBuilder();
        foreach (AssetDocument document in _documents.Documents)
        {
            if (!document.IsDirty)
            {
                continue;
            }
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }
            builder.Append(document.AssetPath);
        }
        return builder.ToString();
    }
}
