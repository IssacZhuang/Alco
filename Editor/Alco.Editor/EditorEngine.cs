using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Alco.Engine;
using Alco.IO;
using Alco.Project;
using Avalonia.Threading;

namespace Alco.Editor;

/// <summary>
/// The engine for the editor.
/// </summary>
public class EditorEngine : GameEngine
{
    private readonly List<DirectoryFileSource> _fileSources = new();
    private readonly Lock _lockProjectFiles = new();
    private readonly Lock _lockProject = new();
    private FileSystemWatcher? _projectWatcher;
    private volatile AlcoProject? _project;

    public bool IsProjectOpen => _project != null;
    public string? ProjectDirectory => _project?.ProjectDirectory;
    public AlcoProject? Project => _project;



    /// <summary>
    /// Event triggered when the files in the project are updated.
    /// </summary>
    public event Action? OnFilesInProjectUpdated;

    /// <summary>
    /// Event triggered when the project is opened.
    /// </summary>
    public event Action? OnProjectOpened;

    /// <summary>
    /// Event triggered when the project is closed.
    /// </summary>
    public event Action? OnProjectClosed;

    public EditorEngine(GameEngineSetting setting) : base(setting)
    {
        
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
            _project = new AlcoProject(this, projectFilePath);
            foreach (var fileSource in _project.FileSources)
            {
                Assets.AddFileSource(fileSource);
            }
            SetupProjectWatcher();
            if (OnProjectOpened != null)
            {
                Dispatcher.UIThread.InvokeAsync(OnProjectOpened);
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
        
        if (_projectWatcher != null)
        {
            _projectWatcher.EnableRaisingEvents = false;
            _projectWatcher.Dispose();
            _projectWatcher = null;
        }

        foreach (var fileSource in _project.FileSources)
        {
            Assets.RemoveFileSource(fileSource);
        }

        if (OnProjectClosed != null)
        {
            Dispatcher.UIThread.InvokeAsync(OnProjectClosed);
        }

        Log.Info("Project closed");
    }

    private void SetupProjectWatcher()
    {
        if (ProjectDirectory == null) return;

        _projectWatcher = new FileSystemWatcher
        {
            Path = ProjectDirectory,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            Filter = "*.*",
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        _projectWatcher.Created += OnProjectFileChanged;
        _projectWatcher.Deleted += OnProjectFileChanged;
        _projectWatcher.Renamed += OnProjectFileChanged;
    }

    private void OnProjectFileChanged(object sender, FileSystemEventArgs e)
    {
        if (ProjectDirectory == null) return;
        Dispatcher.UIThread.Invoke(() =>
        {
            OnFilesInProjectUpdated?.Invoke();
        });
    }

    private static string FixPath(string path)
    {
        return path.Replace('\\', '/');
    }

}
