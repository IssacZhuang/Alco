using System.Runtime.CompilerServices;

namespace SandboxUtils;

/// <summary>Path helpers for locating the solution, project, and asset folders from sandbox code.</summary>
public static class Utils
{
    /// <summary>Finds the folder containing the solution file by walking up from the current directory.</summary>
    /// <returns>The absolute solution folder path.</returns>
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

    /// <summary>Gets the engine's built-in source assets folder.</summary>
    /// <returns>The absolute path to the built-in assets folder.</returns>
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

    /// <summary>Finds the folder containing the caller's project file.</summary>
    /// <param name="path">Caller file path supplied automatically by the compiler.</param>
    /// <returns>The absolute project folder path.</returns>
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

    /// <summary>Gets the caller project's assets folder.</summary>
    /// <param name="path">Caller file path supplied automatically by the compiler.</param>
    /// <returns>The absolute path to the project's assets folder.</returns>
    public static string GetProjectAssetsPath([CallerFilePath] string? path = null)
    {
        return Path.Combine(GetProjectPath(path), "Assets");
    }
}
