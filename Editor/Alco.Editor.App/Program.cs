using Alco;
using Alco.Engine;
using Alco.Graphics;

namespace Alco.Editor.App;

/// <summary>
/// Entry point of the Alco editor. Project resolution order:
/// <list type="number">
/// <item>The path given as the first command line argument (a <c>.alco</c> file).</item>
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
        AlcoProject project;
        try
        {
            project = ResolveProject(args);
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

        using (EditorGame game = new(setting, project))
        {
            game.Run();
        }

        GC.Collect();
        GC.WaitForFullGCComplete();
        AllocationTracker.CheckAllocated();
        return 0;
    }

    /// <summary>Resolves the project to open, following the documented search order.</summary>
    private static AlcoProject ResolveProject(string[] args)
    {
        if (args.Length > 0)
        {
            return AlcoProject.Load(args[0]);
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
