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
    private readonly List<string> _allFilesInProject = new();
    private readonly List<DirectoryFileSource> _fileSources = new();
    private readonly Lock _lockProjectFiles = new();
    private readonly Lock _lockProject = new();
    private FileSystemWatcher? _projectWatcher;
    private volatile string? _projectDirectory;
    private volatile AlcoProject? _project;

    public bool IsProjectOpen => _projectDirectory != null;
    public string? ProjectDirectory => _projectDirectory;
    public AlcoProject? Project => _project;

    /// <summary>
    /// All files in the project.
    /// </summary>
    public IReadOnlyList<string> AllFilesInProject
    {
        get
        {
            using (_lockProjectFiles.EnterScope())
            {
                return _allFilesInProject;
            }
        }
    }

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
            _project = new AlcoProject(projectFilePath);
            _projectDirectory = directory;
            UpdateAllFilesInProject();
            AddFileSources();
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
        if (_projectWatcher != null)
        {
            _projectWatcher.EnableRaisingEvents = false;
            _projectWatcher.Dispose();
            _projectWatcher = null;
        }

        _projectDirectory = null;
        _allFilesInProject.Clear();

        RemoveFileSources();

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
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Run(UpdateAllFilesInProject);
            OnFilesInProjectUpdated?.Invoke();
        });
    }

    private void UpdateAllFilesInProject()
    {
        using (_lockProjectFiles.EnterScope())
        {
            if (ProjectDirectory == null)
            {
                return;
            }
            try
            {
                _allFilesInProject.Clear();

                var entries = new List<(string Path, bool IsDirectory)>();
                foreach (var entry in Directory.EnumerateFileSystemEntries(ProjectDirectory, "*", SearchOption.AllDirectories))
                {
                    entries.Add((entry, Directory.Exists(entry)));
                }

                entries.Sort((a, b) =>
                {
                    if (a.IsDirectory != b.IsDirectory)
                    {
                        return a.IsDirectory ? -1 : 1;
                    }
                    return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
                });

                foreach (var entry in entries)
                {
                    _allFilesInProject.Add(FixPath(Path.GetRelativePath(ProjectDirectory, entry.Path)));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to update project files: {ex}");
                throw;
            }
        }
    }

    private void AddFileSources()
    {
        if (_project == null) return;
        if (ProjectDirectory == null) return;
        RemoveFileSources();

        foreach (var file in _project.Config.AssetsPaths)
        {
            string fullPath = Path.Combine(ProjectDirectory, file);
            if (Directory.Exists(fullPath))
            {
                var fileSource = new DirectoryFileSource(fullPath);
                _fileSources.Add(fileSource);
                Assets.AddFileSource(fileSource);
            }
        }
        Assets.TryRefreshEntries();
    }

    private void RemoveFileSources()
    {
        foreach (var fileSource in _fileSources)
        {
            Assets.RemoveFileSource(fileSource);
        }
        _fileSources.Clear();
    }

    private static string FixPath(string path)
    {
        return path.Replace('\\', '/');
    }

}
