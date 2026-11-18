using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Alco.Profiler;

internal static class Program
{
    private const string ApiTokenVariable = "ALCO_PROFILER_API_TOKEN";
    private const string Usage = """
        Usage: Alco.Profiler [--launch PATH [--project PATH]] [--api-port PORT]
          --launch PATH    Launch an executable, .csproj, .sln, or .slnx without using the picker.
          --project PATH   Startup .csproj (absolute or relative to the solution directory).
                           Required if the solution has multiple C# projects.
          --api-port PORT  Enable Agent Control on 127.0.0.1. Requires ALCO_PROFILER_API_TOKEN.
                           Use Authorization: Bearer <token> with GET /tools and POST /tools/{name}.
          --help           Show this help.
        """;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--help" or "-h"])
        {
            Console.WriteLine(Usage);
            return 0;
        }

        if (!TryParseArguments(args, out var options, out string error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(Usage);
            return 2;
        }

        string? apiToken = null;
        if (options.ApiPort.HasValue)
        {
            apiToken = Environment.GetEnvironmentVariable(ApiTokenVariable);
            if (string.IsNullOrEmpty(apiToken) || apiToken.Any(static c => c is < '!' or > '~'))
            {
                Console.Error.WriteLine($"Set {ApiTokenVariable} to a non-empty token with no spaces before enabling Agent Control.");
                return 2;
            }

            // Do not pass the control credential to SDK builds or profiled applications.
            Environment.SetEnvironmentVariable(ApiTokenVariable, null);
        }

        CaptureOverlay.PrepareInput();
        var builder = AppBuilder.Configure<ProfilerApp>()
            .UsePlatformDetect()
            .SetupWithClassicDesktopLifetime([]);
        var desktop = (ClassicDesktopStyleApplicationLifetime)builder.Instance!.ApplicationLifetime!;
        ProfilerAgentControl? agent = null;
        if (options.ApiPort is int port && !ProfilerAgentControl.TryStart(desktop, port, apiToken!, out agent, out error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        using (agent)
        {
            if (options.LaunchPath is string path && desktop.MainWindow is LaunchWindow launcher)
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    if (await launcher.LaunchAsync(path, options.StartupProject) is string launchError)
                    {
                        Console.Error.WriteLine(launchError);
                    }
                });
            }

            return desktop.Start();
        }
    }

    private static bool TryParseArguments(string[] args, out (string? LaunchPath, string? StartupProject, int? ApiPort) options,
        out string error)
    {
        options = default;
        for (int i = 0; i < args.Length; i++)
        {
            string argument = args[i];
            int equals = argument.IndexOf('=');
            string name = (equals < 0 ? argument : argument[..equals]).ToLowerInvariant();
            if (name is not ("--launch" or "--project" or "--api-port"))
            {
                error = $"Unknown option: {argument}";
                return false;
            }

            string? value = equals >= 0 ? argument[(equals + 1)..]
                : i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = $"{name} requires a value.";
                return false;
            }

            switch (name)
            {
                case "--launch" when options.LaunchPath == null:
                    options.LaunchPath = value;
                    break;
                case "--project" when options.StartupProject == null:
                    options.StartupProject = value;
                    break;
                case "--api-port" when options.ApiPort == null:
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
                    {
                        error = "--api-port must be a port from 1 to 65535.";
                        return false;
                    }

                    options.ApiPort = port;
                    break;
                default:
                    error = $"{name} can only be specified once.";
                    return false;
            }
        }

        if (options.StartupProject != null && (options.LaunchPath == null || !LaunchTargets.IsSolution(options.LaunchPath)))
        {
            error = "Use --project with --launch pointing to a .sln or .slnx file.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

internal sealed class ProfilerApp : Application
{
    /// <inheritdoc />
    public override void Initialize() => ImGuiTheme.Apply(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateLauncher(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static LaunchWindow CreateLauncher(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var launcher = new LaunchWindow();
        launcher.ApplicationLaunched += (target, name) =>
        {
            var analysis = new MainWindow(target, name);
            analysis.ChooseApplicationRequested += () =>
            {
                LaunchWindow next = CreateLauncher(desktop);
                desktop.MainWindow = next;
                next.Show();
                analysis.Close();
            };
            desktop.MainWindow = analysis;
            analysis.Show();
            launcher.Close();
        };
        return launcher;
    }
}
