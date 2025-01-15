using System.Runtime.CompilerServices;

namespace SandboxUtils;

public static class Utils
{
    public static string GetSolutionFolder()
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

    public static string GetBuiltInAssetsPath()
    {
        return Path.Combine(GetSolutionFolder(), "Src", "Vocore.Engine", "Assets");
    }

    public static string GetProjectPath([CallerFilePath] string? path = null)
    {
        //find .csproj file
        string? current = Path.GetDirectoryName(path);
        while (current != null)
        {
            string[] files = Directory.GetFiles(current, "*.csproj");
            if (files.Length > 0)
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        throw new Exception("Project file not found");
    }

    public static string GetProjectAssetsPath([CallerFilePath] string? path = null)
    {
        return Path.Combine(GetProjectPath(path), "Assets");
    }
}
