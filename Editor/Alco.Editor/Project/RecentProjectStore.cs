namespace Alco.Editor;

/// <summary>
/// Best-effort memory of the last opened project, so the editor can reopen it when
/// launched without a project argument. Stored as a plain text file holding the
/// <c>.alco</c> path under the per-user local application data directory. All IO
/// failures are swallowed: remembering the project is a convenience, never a blocker.
/// </summary>
public static class RecentProjectStore
{
    /// <summary>The file holding the remembered project path.</summary>
    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Alco", "Editor", "last-project.txt");

    /// <summary>The remembered project file, or null when none is recorded or it no longer exists.</summary>
    public static string? Load()
    {
        try
        {
            string path = File.ReadAllText(StorePath).Trim();
            return path.Length > 0 && File.Exists(path) ? path : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Remembers the given <c>.alco</c> project file path.</summary>
    public static void Save(string projectPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, projectPath);
        }
        catch (Exception)
        {
            // Best-effort: a failed write must never break project opening.
        }
    }
}
