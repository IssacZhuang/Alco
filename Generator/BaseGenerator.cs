public abstract class BaseGenerator
{
    public static readonly string SolutionFolder = GetSolutionFolder();

    public abstract string OutputFolder { get; }

    public abstract void Generate();

    protected void WriteFile(string path, string content)
    {
        string fullPath = Path.Combine(SolutionFolder, OutputFolder, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    protected void ClearFolder()
    {
        string fullPath = Path.Combine(SolutionFolder, OutputFolder);
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, true);
        }
    }

    private static string GetSolutionFolder()
    {
        string? current = Directory.GetCurrentDirectory();
        while (current != null)
        {
            string[] files = Directory.GetFiles(current, "*.sln");
            if (files.Length > 0)
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        throw new Exception("Solution file not found");
    }
}