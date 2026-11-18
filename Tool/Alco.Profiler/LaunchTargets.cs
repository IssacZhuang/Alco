using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Alco.Profiler;

/// <summary>Resolves profiler launch targets and starts the application directly.</summary>
internal static class LaunchTargets
{
    private const string Configuration = "Debug";
    private const string ProjectProperties = "TargetFramework,TargetFrameworks,TargetFrameworkIdentifier,TargetPlatformIdentifier,"
        + "OutputType,IsTestProject,RunCommand,RunArguments,RunWorkingDirectory,DOTNET_HOST_PATH,NETCoreSdkVersion";
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Finds a previously built Game executable near the profiler or current directory.</summary>
    public static string? FindDefaultPath()
    {
        string? root = FindGameRoot(Environment.CurrentDirectory) ?? FindGameRoot(AppContext.BaseDirectory);
        if (root == null)
        {
            return null;
        }

        string executable = OperatingSystem.IsWindows() ? "Application.exe" : "Application";
        string rid = RuntimeInformation.RuntimeIdentifier;
        string[] directories =
        [
            Path.Combine(root, "Bin", "Debug", "Application", rid),
            Path.Combine(root, "Bin", "Debug", "Application"),
            Path.Combine(root, "Bin", "Release", "Application", rid),
            Path.Combine(root, "Bin", "Release", "Application"),
            // The published game can be NativeAOT and have no EventPipe endpoint.
            Path.Combine(root, "Bin", "Release", "Publish", rid),
        ];

        return directories.Select(directory => Path.Combine(directory, executable)).FirstOrDefault(File.Exists);
    }

    /// <summary>Checks whether the path has a C# project extension.</summary>
    public static bool IsProject(string path) => string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase);

    /// <summary>Checks whether the path has a solution extension.</summary>
    public static bool IsSolution(string path) => Path.GetExtension(path).ToLowerInvariant() is ".sln" or ".slnx";

    /// <summary>Lists the solution's absolute C# project paths or reports why it could not be read.</summary>
    public static async Task<(string[] Projects, string? Error)> ReadSolutionAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsSolution(path) || !File.Exists(path))
        {
            return ([], "Select an existing .sln or .slnx file.");
        }

        string solution = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(solution)!;
        var (output, error) = await RunDotNetAsync(directory, "Could not read solution", token, "sln", solution, "list").ConfigureAwait(false);
        if (output == null)
        {
            return ([], error);
        }

        // The heading is localized; only project path lines are relevant.
        string[] projects = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(IsProject)
            .Select(project => Path.GetFullPath(project.Replace('\\', Path.DirectorySeparatorChar), directory))
            .ToArray();
        return projects.Length == 0 ? ([], "This solution contains no C# projects.") : (projects, null);
    }

    /// <summary>Starts an application or a solution's startup project and transfers ownership of the process.</summary>
    public static async Task<((Process Process, string Name)? Application, string? Error)> StartAsync(
        string path, string? startupProject, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(path))
        {
            return (null, $"The selected file does not exist: {path}");
        }

        string target = Path.GetFullPath(path);
        string? solution = null;
        if (IsSolution(target))
        {
            solution = target;
            var (projects, error) = await ReadSolutionAsync(solution, token).ConfigureAwait(false);
            if (error != null)
            {
                return (null, error);
            }

            if (startupProject == null)
            {
                if (projects.Length != 1)
                {
                    return (null, "This solution has multiple projects. Choose a startup project.");
                }

                target = projects[0];
            }
            else
            {
                try
                {
                    target = Path.GetFullPath(startupProject, Path.GetDirectoryName(solution)!);
                }
                catch (Exception exception) when (exception is ArgumentException or IOException)
                {
                    return (null, $"Invalid startup project path: {exception.Message}");
                }

                if (!projects.Any(project => project.Equals(target, PathComparison)))
                {
                    return (null, $"The startup project is not in the selected solution: {startupProject}");
                }
            }

            if (!File.Exists(target))
            {
                return (null, $"The startup project does not exist: {target}");
            }
        }
        else if (startupProject != null)
        {
            return (null, "A startup project can only be specified for a solution.");
        }

        var (process, launchError) = await StartResolvedAsync(target, solution, token).ConfigureAwait(false);
        return process == null ? (null, launchError) : ((process, Path.GetFileNameWithoutExtension(target)), null);
    }

    private static async Task<(Process? Process, string? Error)> StartResolvedAsync(string target, string? solutionPath, CancellationToken token)
    {
        if (!IsProject(target))
        {
            return StartProcess(target, string.Empty, GetExecutableWorkingDirectory(target), token);
        }

        string projectDirectory = Path.GetDirectoryName(target)!;
        string? solutionDirectory = solutionPath == null ? null : Path.GetDirectoryName(Path.GetFullPath(solutionPath));
        string sdkDirectory = solutionDirectory ?? projectDirectory;
        var (properties, error) = await ReadProjectAsync(target, sdkDirectory, null, false, token).ConfigureAwait(false);
        if (error != null)
        {
            return (null, error);
        }

        string? framework = null;
        string projectName = Path.GetFileName(target);
        string[] frameworks = GetProperty(properties, "TargetFrameworks").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (frameworks.Length != 0)
        {
            string? firstFailure = null;
            foreach (string candidate in frameworks)
            {
                var (candidateProperties, candidateError) = await ReadProjectAsync(target, sdkDirectory, candidate, false, token).ConfigureAwait(false);
                string? reason = candidateError ?? GetUnsupportedReason(candidateProperties, projectName);
                if (reason == null)
                {
                    framework = candidate;
                    properties = candidateProperties;
                    break;
                }

                firstFailure ??= reason;
            }

            if (framework == null)
            {
                return (null, $"No runnable target framework in {projectName}. {firstFailure}");
            }
        }
        else if (GetUnsupportedReason(properties, projectName) is string reason)
        {
            return (null, reason);
        }

        string[] buildArguments = framework == null ? [] : ["--framework", framework];
        var (_, buildError) = await RunDotNetAsync(sdkDirectory, $"Could not build {projectName}", token,
            ["build", target, "--configuration", Configuration, "--nologo", "--tl:off", "--disable-build-servers", .. buildArguments]).ConfigureAwait(false);
        if (buildError != null)
        {
            return (null, buildError);
        }

        // Restore can add imports (including test SDKs); read launch properties again after the build.
        string sdkVersion = GetProperty(properties, "NETCoreSdkVersion").Split('-')[0];
        bool computeRunArguments = Version.TryParse(sdkVersion, out Version? sdk) && sdk.Major >= 9;
        (properties, error) = await ReadProjectAsync(target, sdkDirectory, framework, computeRunArguments, token).ConfigureAwait(false);
        error ??= GetUnsupportedReason(properties, projectName);
        if (error != null)
        {
            return (null, error);
        }

        string configuredDirectory = GetProperty(properties, "RunWorkingDirectory");
        string directory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? solutionDirectory ?? GetProjectWorkingDirectory(target, projectDirectory)
            : Path.GetFullPath(configuredDirectory, projectDirectory);
        if (!Directory.Exists(directory))
        {
            return (null, $"The launch working directory does not exist: {directory}");
        }

        string command = GetProperty(properties, "RunCommand").Trim().Trim('"');
        if (command.Length == 0)
        {
            return (null, $"The .NET SDK did not provide a run command for {projectName}.");
        }

        string arguments = GetProperty(properties, "RunArguments");
        string commandName = Path.GetFileNameWithoutExtension(command).ToLowerInvariant();
        if (commandName == "dotnet")
        {
            // The muxer can host the app itself, but "dotnet run" and other SDK commands launch another process.
            if (!RunsManagedAssembly(arguments))
            {
                return (null, $"{projectName} does not launch a managed assembly directly. Select its built executable instead.");
            }

            string host = GetProperty(properties, "DOTNET_HOST_PATH");
            if (command == Path.GetFileName(command) && File.Exists(host))
            {
                command = host;
            }
        }
        else if (commandName is "cmd" or "powershell" or "pwsh" or "sh" or "bash" or "zsh" or "fish" or "open" or "xcrun" or "mono"
            || Path.GetExtension(command).ToLowerInvariant() is ".bat" or ".cmd" or ".ps1" or ".sh")
        {
            return (null, $"{projectName} uses a launcher wrapper. Select the built application executable instead.");
        }

        string projectRelativeCommand = Path.GetFullPath(command, projectDirectory);
        if (File.Exists(projectRelativeCommand))
        {
            command = projectRelativeCommand;
        }
        else if (command != Path.GetFileName(command))
        {
            command = Path.GetFullPath(command, directory);
        }

        return StartProcess(command, arguments, directory, token);
    }

    private static string? GetUnsupportedReason(JsonElement properties, string projectName)
    {
        if (string.Equals(GetProperty(properties, "IsTestProject"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return $"{projectName} is a test project. Choose an application project instead.";
        }

        if (GetProperty(properties, "OutputType").ToLowerInvariant() is not ("exe" or "winexe"))
        {
            return $"{projectName} is not executable (OutputType must be Exe or WinExe).";
        }

        string platform = GetProperty(properties, "TargetPlatformIdentifier");
        bool supportsPlatform = platform.ToLowerInvariant() switch
        {
            "" => true,
            "windows" => OperatingSystem.IsWindows(),
            "linux" => OperatingSystem.IsLinux(),
            "macos" => OperatingSystem.IsMacOS(),
            _ => false,
        };
        return GetProperty(properties, "TargetFrameworkIdentifier") == ".NETCoreApp" && supportsPlatform
            ? null
            : $"{projectName} targets {GetProperty(properties, "TargetFramework")}, not a desktop CoreCLR framework on this platform.";
    }

    private static async Task<(JsonElement Properties, string? Error)> ReadProjectAsync(string path, string directory,
        string? framework, bool computeRunArguments, CancellationToken token)
    {
        string[] frameworkArguments = framework == null ? [] : [$"-property:TargetFramework={framework}"];
        string[] runArguments = computeRunArguments ? ["-getTargetResult:ComputeRunArguments"] : [];
        var (output, error) = await RunDotNetAsync(directory, $"Could not evaluate {Path.GetFileName(path)}", token,
            ["msbuild", path, "-nologo", "-verbosity:quiet", $"-property:Configuration={Configuration}", $"-getProperty:{ProjectProperties}",
                .. frameworkArguments, .. runArguments]).ConfigureAwait(false);
        if (output == null)
        {
            return (default, error);
        }

        try
        {
            // MSBuild may print warnings before the property JSON.
            int start = output.LastIndexOf("\n{", StringComparison.Ordinal) + 1;
            using JsonDocument document = JsonDocument.Parse(output.AsMemory(start));
            if (document.RootElement.TryGetProperty("Properties", out JsonElement properties))
            {
                return (properties.Clone(), null);
            }
        }
        catch (JsonException)
        {
        }

        return (default, $"The .NET SDK did not return readable project properties for {Path.GetFileName(path)}.");
    }

    private static string GetProperty(JsonElement properties, string name) => properties.GetProperty(name).GetString() ?? string.Empty;

    private static bool RunsManagedAssembly(string arguments)
    {
        ReadOnlySpan<char> first = arguments.AsSpan().TrimStart();
        if (first.IsEmpty)
        {
            return false;
        }

        if (first[0] == '"')
        {
            first = first[1..];
            int end = first.IndexOf('"');
            if (end < 0)
            {
                return false;
            }

            first = first[..end];
        }
        else
        {
            int end = first.IndexOfAny(' ', '\t');
            first = end < 0 ? first : first[..end];
        }

        return first.Equals("exec", StringComparison.Ordinal) || first.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static (Process? Process, string? Error) StartProcess(string command, string arguments, string directory, CancellationToken token)
    {
        var startInfo = new ProcessStartInfo(command, arguments) { UseShellExecute = false, WorkingDirectory = directory };
        token.ThrowIfCancellationRequested();
        try
        {
            Process? process = Process.Start(startInfo);
            return process != null ? (process, null) : (null, "The application did not start a process.");
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or ArgumentException)
        {
            return (null, $"Could not launch the application: {exception.Message}");
        }
    }

    private static async Task<(string? Output, string? Error)> RunDotNetAsync(string directory, string operation,
        CancellationToken token, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = directory,
        };
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_CLI_FORCE_UTF8_ENCODING"] = "1";
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        token.ThrowIfCancellationRequested();
        try
        {
            using Process? helper = Process.Start(startInfo);
            if (helper == null)
            {
                return (null, $"{operation}: dotnet did not start.");
            }

            using StreamReader output = helper.StandardOutput;
            using StreamReader error = helper.StandardError;
            Task<string[]> read = Task.WhenAll(output.ReadToEndAsync(token), error.ReadToEndAsync(token));
            try
            {
                await helper.WaitForExitAsync(token).ConfigureAwait(false);
                string[] streams = await read.WaitAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (helper.ExitCode == 0)
                {
                    return (streams[0], null);
                }

                string[] diagnostics = string.Join('\n', streams).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                string detail = diagnostics.FirstOrDefault(line => line.Contains(": error ", StringComparison.OrdinalIgnoreCase))
                    ?? diagnostics.LastOrDefault() ?? $"dotnet exited with code {helper.ExitCode}.";
                return (null, $"{operation}: {detail}");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                try
                {
                    helper.Kill(entireProcessTree: true);
                }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or AggregateException)
                {
                    // The SDK helper may already have exited.
                }

                throw;
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode is 2 or 3)
        {
            return (null, $"{operation}: dotnet was not found. Install the .NET SDK and add it to PATH.");
        }
        catch (Exception exception) when (exception is Win32Exception or IOException)
        {
            return (null, $"{operation}: {exception.Message}");
        }
    }

    private static string GetExecutableWorkingDirectory(string executable)
    {
        string directory = Path.GetDirectoryName(executable)!;
        string? root = FindGameRoot(directory);
        if (root == null)
        {
            return directory;
        }

        string relative = Path.GetRelativePath(Path.Combine(root, "Bin"), directory);
        return relative.Split(Path.DirectorySeparatorChar) is ["Debug" or "Release", "Application", ..] ? root : directory;
    }

    private static string GetProjectWorkingDirectory(string project, string projectDirectory)
    {
        string? root = FindGameRoot(projectDirectory);
        return root != null && project.Equals(Path.Combine(root, "Src", "Application", "Application.csproj"), PathComparison)
            ? root
            : projectDirectory;
    }

    private static string? FindGameRoot(string path)
    {
        for (DirectoryInfo? directory = new(path); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Game.slnx")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
