using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alco.Engine;
using Alco.IO;
using Alco.Project;
using Alco.Project.Mcp;
using Avalonia.Threading;

namespace Alco.Editor;

/// <summary>
/// The engine for the editor.
/// </summary>
public class EditorEngine : GameEngine, IAlcoProjectContext
{
    private readonly List<DirectoryFileSource> _fileSources = new();
    private readonly Lock _lockProjectFiles = new();
    private readonly Lock _lockProject = new();
    private volatile AlcoProject? _project;

    public bool IsProjectOpen => _project != null;
    public string? ProjectDirectory => _project?.ProjectDirectory;
    public AlcoProject? Project => _project;

    /// <summary>
    /// Event triggered when the project is opened.
    /// </summary>
    public event Action<AlcoProject>? OnProjectOpened;

    /// <summary>
    /// Event triggered when the project is closed.
    /// </summary>
    public event Action<AlcoProject>? OnProjectClosed;

    /// <summary>
    /// The json serializer options for the game engine, which is used for the config loading
    /// </summary>
    public JsonSerializerOptions ConfigSerializeOption { get; } = new();

    public EditorEngine(GameEngineSetting setting) : base(setting)
    {
        ConfigSerializeOption = CreateConfigSerializeOption();
        AssetSystem.RegisterAssetEncoder(new AssetEncoderConfig(ConfigSerializeOption));
    }

    /// <summary>
    /// Open a project at the specified path.
    /// </summary>
    /// <param name="projectFilePath">The path to the project directory.</param>
    /// <exception cref="InvalidOperationException">Thrown when a project is already open.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the project directory does not exist.</exception>
    public Task OpenProjectAsync(string projectFilePath)
    {
        return Task.Run(() => OpenProject(projectFilePath));
    }

    /// <summary>
    /// Open a project at the specified path.
    /// </summary>
    /// <param name="projectFilePath">The path to the project directory.</param>
    /// <exception cref="InvalidOperationException">Thrown when a project is already open.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the project directory does not exist.</exception>
    public void OpenProject(string projectFilePath)
    {
        using (_lockProject.EnterScope())
        {
            OpenProjectCore(projectFilePath);
        }
    }

    private void OpenProjectCore(string projectFilePath)
    {
        if (!File.Exists(projectFilePath))
        {
            throw new FileNotFoundException($"Project file not found: {projectFilePath}");
        }

        string directory = Path.GetDirectoryName(projectFilePath) ?? throw new InvalidOperationException("Project directory not found");

        if (IsProjectOpen)
        {
            CloseProjectCore();
        }
        try
        {
            _project = new AlcoProject(projectFilePath, this);
            if (OnProjectOpened != null)
            {
                Dispatcher.UIThread.Invoke(() => OnProjectOpened(_project));
            }

            Log.Info($"Project opened: {projectFilePath}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to open project: {ex.Message}");
            CloseProject();
            throw;
        }
    }

    public void CloseProject()
    {
        using (_lockProject.EnterScope())
        {
            CloseProjectCore();
        }
    }

    private void CloseProjectCore()
    {
        if (_project == null) return;

        if (OnProjectClosed != null)
        {
            Dispatcher.UIThread.Invoke(() => OnProjectClosed(_project));
        }

        try
        {
            _project.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to dispose project: {ex}");
        }

        _project = null;
        Log.Info("Project closed");
    }

    public override void PostToMainThread(SendOrPostCallback action, object? state)
    {
        Dispatcher.UIThread.Invoke(() => action(state));
    }
    private JsonSerializerOptions CreateConfigSerializeOption()
    {
        var options = new JsonSerializerOptions()
        {
            TypeInfoResolver = new PolymorphicJsonTypeResolver([typeof(Configable)]),
            WriteIndented = true,
            Converters = { }
        };

        foreach (var converter in CreateDefaultJsonConverters())
        {
            options.Converters.Add(converter);
        }

        options.MakeReadOnly();

        return options;
    }



    private static string FixPath(string path)
    {
        return path.Replace('\\', '/');
    }

}
