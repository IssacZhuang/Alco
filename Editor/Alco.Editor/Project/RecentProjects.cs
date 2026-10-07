namespace Alco.Editor;

/// <summary>
/// The editor's recently opened projects, persisted as a JSON preference through the
/// engine's preference system (an app-local file next to the editor executable).
/// <see cref="Entries"/> is kept most-recently-opened first and capped at
/// <see cref="MaxCount"/>.
/// </summary>
public sealed class RecentProjects
{
    /// <summary>Maximum number of remembered projects; older entries are dropped.</summary>
    public const int MaxCount = 10;

    /// <summary>Remembered projects, most recently opened first.</summary>
    public List<RecentProjectEntry> Entries { get; set; } = new();

    /// <summary>
    /// Records that the given project file was just opened: moves any existing entry
    /// for the path to the front with a fresh timestamp, otherwise prepends a new one,
    /// and truncates the list to <see cref="MaxCount"/>.
    /// </summary>
    /// <param name="projectPath">Path of the <c>.alco</c> project file.</param>
    public void OnProjectOpened(string projectPath)
    {
        string fullPath = Path.GetFullPath(projectPath);
        Entries.RemoveAll(entry => string.Equals(entry.Path, fullPath, StringComparison.OrdinalIgnoreCase));
        Entries.Insert(0, new RecentProjectEntry { Path = fullPath, OpenedUtc = DateTime.UtcNow });
        if (Entries.Count > MaxCount)
        {
            Entries.RemoveRange(MaxCount, Entries.Count - MaxCount);
        }
    }
}

/// <summary>One recently opened project: its file path and when it was last opened.</summary>
public sealed class RecentProjectEntry
{
    /// <summary>Absolute path of the <c>.alco</c> project file.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>When the project was last opened (UTC).</summary>
    public DateTime OpenedUtc { get; set; }
}
