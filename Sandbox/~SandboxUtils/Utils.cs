using System.Runtime.CompilerServices;

namespace SandboxUtils;

public static class Utils
{
    public static string GetSolutionFolder()
    {
        string? current = Directory.GetCurrentDirectory();
        while (current != null)
        {
            if (Directory.GetFiles(current, "*.sln").Length > 0 || Directory.GetFiles(current, "*.slnx").Length > 0)
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        throw new Exception("Solution file not found");
    }

    public static string GetBuiltInAssetsPath()
    {
        return Path.Combine(GetSolutionFolder(), "Src", "Alco.Engine", "Assets");
    }

    /// <summary>
    /// The source asset folder of the rendering library (built-in shaders),
    /// for development-time hot reload watchers.
    /// </summary>
    public static string GetRenderingAssetsPath()
    {
        return Path.Combine(GetSolutionFolder(), "Src", "Alco.Rendering", "Assets");
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
