using Alco;
using Alco.Engine;
using Alco.Graphics;

namespace Alco.Editor.App;

/// <summary>
/// Entry point of the Alco editor. Project resolution order:
/// <list type="number">
/// <item>The path given as the positional command line argument (a <c>.alco</c> file).</item>
/// <item>The first <c>*.alco</c> file found walking up from the current directory
/// (the engine is usually embedded as a submodule next to the game project's
/// <c>.alco</c> file).</item>
/// <item>The bundled <c>Demo.alco</c> next to the executable.</item>
/// <item>An in-memory untitled project rooted at the current directory.</item>
/// </list>
/// </summary>
internal static class Program
{
    internal static int Main(string[] args)
    {
        if (!TryParseArguments(args, out string? projectArg, out int apiPort, out bool enableApi))
        {
            return 1;
        }

        AlcoProject project;
        try
        {
            project = ResolveProject(projectArg);
        }
        catch (Exception exception)
        {
            Log.Error(exception.Message);
            return 1;
        }

        Log.Info("Editor project: ", project.Name);

        GameEngineSetting setting = new()
        {
            StopWhenError = true,
            View = new ViewSetting(1600, 900, $"Alco Editor - {project.Name}"),
            Graphics = GraphicsSetting.Default with
            {
                Backend = GraphicsBackend.WGPUVulkan,
            },
        };

        using (EditorGame game = new(setting, project, apiPort, enableApi))
        {
            game.Run();
        }

        GC.Collect();
        GC.WaitForFullGCComplete();
        AllocationTracker.CheckAllocated();
        return 0;
    }

    /// <summary>
    /// Parses the command line: an optional positional <c>.alco</c> project path,
    /// <c>--api-port=N</c> (agent API port, default 52200) and <c>--no-api</c>
    /// (disable the agent API server).
    /// </summary>
    private static bool TryParseArguments(string[] args, out string? projectArg, out int apiPort, out bool enableApi)
    {
        projectArg = null;
        apiPort = 52200;
        enableApi = true;

        foreach (string arg in args)
        {
            if (arg.Equals("--no-api", StringComparison.OrdinalIgnoreCase))
            {
                enableApi = false;
            }
            else if (arg.StartsWith("--api-port=", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(arg["--api-port=".Length..], out apiPort) || apiPort <= 0 || apiPort > 65535)
                {
                    Log.Error($"Invalid --api-port value: {arg}");
                    return false;
                }
            }
            else if (projectArg == null)
            {
                projectArg = arg;
            }
            else
            {
                Log.Error($"Unexpected argument: {arg}");
                return false;
            }
        }

        return true;
    }

    /// <summary>Resolves the project to open, following the documented search order.</summary>
    private static AlcoProject ResolveProject(string? projectArg)
    {
        if (projectArg != null)
        {
            return AlcoProject.Load(projectArg);
        }

        string? fromDisk = FindProjectFileUpwards(Directory.GetCurrentDirectory(), "*.alco");
        if (fromDisk != null)
        {
            return AlcoProject.Load(fromDisk);
        }

        string demoPath = Path.Combine(AppContext.BaseDirectory, "Demo.alco");
        if (File.Exists(demoPath))
        {
            return AlcoProject.Load(demoPath);
        }

        return AlcoProject.CreateUntitled(Directory.GetCurrentDirectory());
    }

    /// <summary>Walks up the directory tree looking for a project file; null when none exists.</summary>
    private static string? FindProjectFileUpwards(string startDirectory, string searchPattern)
    {
        string? current = Path.GetFullPath(startDirectory);
        while (current != null)
        {
            string[] matches = Directory.GetFiles(current, searchPattern);
            if (matches.Length > 0)
            {
                return matches[0];
            }

            current = Directory.GetParent(current)?.FullName;
        }

        return null;
    }
}
