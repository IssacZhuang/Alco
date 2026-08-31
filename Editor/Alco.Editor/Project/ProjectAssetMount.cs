using Alco.IO;

namespace Alco.Editor;

/// <summary>
/// Mounts an <see cref="AlcoProject"/> onto an <see cref="AssetSystem"/>: owned roots as
/// hot-reloading watcher sources (priority 10, shadowing referenced entries of the same
/// path), referenced entries as plain read-only sources (directories) or single-file
/// sources (file entries). Missing entries are skipped silently, matching the engine's
/// optional-root convention.
/// </summary>
public static class ProjectAssetMount
{
    /// <summary>
    /// Mounts all of the project's asset roots and referenced entries.
    /// </summary>
    /// <param name="project">The project to mount.</param>
    /// <param name="assetSystem">The asset system to mount onto.</param>
    /// <returns>The mounted sources, in mount order (for later removal/disposal).</returns>
    public static IReadOnlyList<IFileSource> Mount(AlcoProject project, AssetSystem assetSystem)
    {
        var mounted = new List<IFileSource>();

        foreach (string root in project.GetAbsoluteAssetRoots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }
            var source = new DirectoryWatcherFileSource(root, assetSystem);
            assetSystem.AddFileSource(source);
            mounted.Add(source);
        }

        foreach (string entry in project.ReferencedAssets)
        {
            string absoluteEntry = project.GetAbsolutePath(entry);
            IFileSource? source = null;
            if (Directory.Exists(absoluteEntry))
            {
                source = new DirectoryFileSource(absoluteEntry);
            }
            else if (File.Exists(absoluteEntry))
            {
                source = new SingleFileSource(project.ProjectDirectory, entry);
            }

            if (source != null)
            {
                assetSystem.AddFileSource(source);
                mounted.Add(source);
            }
        }

        return mounted;
    }

    /// <summary>
    /// Removes and disposes sources previously returned by <see cref="Mount"/>,
    /// stopping their file watchers. Used when the editor switches projects.
    /// </summary>
    /// <param name="mounted">The sources returned by <see cref="Mount"/>.</param>
    /// <param name="assetSystem">The asset system the sources were mounted onto.</param>
    public static void Unmount(IReadOnlyList<IFileSource> mounted, AssetSystem assetSystem)
    {
        for (int i = 0; i < mounted.Count; i++)
        {
            assetSystem.RemoveFileSource(mounted[i]);
            (mounted[i] as IDisposable)?.Dispose();
        }
    }
}
