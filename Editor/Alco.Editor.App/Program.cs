using Alco;
using Alco.Engine;
using Alco.Graphics;

namespace Alco.Editor.App;

/// <summary>
/// Entry point of the Alco editor. A <c>.alco</c> project path given as the positional
/// command line argument is opened directly; without one the editor starts with no
/// project and shows the startup screen (open button + recent projects).
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

        string title = project.IsUntitled
            ? "Alco Editor"
            : string.Format(EditorSystem.WindowTitleFormat, project.Name);
        GameEngineSetting setting = new()
        {
            StopWhenError = true,
            View = new ViewSetting(1600, 900, title),
            Graphics = GraphicsSetting.Default with
            {
                Backend = GraphicsBackend.WGPUVulkan,
            },
        };

        using (EditorEngine engine = new(setting, project, apiPort, enableApi))
        {
            engine.Run();
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

    /// <summary>
    /// Loads the project given on the command line, or falls back to an untitled
    /// in-memory project rooted at the current directory, which makes the editor show
    /// its startup screen.
    /// </summary>
    private static AlcoProject ResolveProject(string? projectArg)
    {
        return projectArg != null
            ? AlcoProject.Load(projectArg)
            : AlcoProject.CreateUntitled(Directory.GetCurrentDirectory());
    }
}
