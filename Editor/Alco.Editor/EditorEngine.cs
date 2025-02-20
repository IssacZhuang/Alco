using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Alco.Engine;
using Alco.IO;
using Avalonia.Threading;

namespace Alco.Editor;

/// <summary>
/// The engine for the editor.
/// </summary>
public class EditorEngine : GameEngine
{
    public const string CacheFolderName = ".cache";

    private readonly List<string> _allFilesInProject = new();
    private readonly object _allFilesInProjectLock = new();
    private FileSystemWatcher? _projectWatcher;

    public string CacheDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CacheFolderName);

    /// <summary>
    /// an independent asset system for the editor cache.
    /// </summary>
    public AssetSystem Cache { get; }

    public bool IsProjectOpen => ProjectDirectory != null;
    public string? ProjectDirectory { get; private set; }

    public IReadOnlyList<string> AllFilesInProject
    {
        get => _allFilesInProject;
    }

    /// <summary>
    /// Event triggered when the files in the project are updated.
    /// </summary>
    public event Action? OnFilesInProjectUpdated;

    public EditorEngine(GameEngineSetting setting) : base(setting)
    {
        //create cache folder if not exists
        if (!Directory.Exists(CacheDirectory))
        {
            Directory.CreateDirectory(CacheDirectory);
        }

        Cache = new AssetSystem(this, 0, true);
        DirectoryFileSource cacheSource = new(CacheDirectory);
        Cache.AddFileSource(cacheSource);

        AssetLoaderConfig configLoader = new AssetLoaderConfig(Cache);
        Cache.RegisterAssetLoader(configLoader);
        AssetEncoderConfig configEncoder = new AssetEncoderConfig();
        Cache.RegisterAssetEncoder(configEncoder);

    }

    /// <summary>
    /// Opens a project at the specified path.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <exception cref="InvalidOperationException">Thrown when a project is already open.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the project directory does not exist.</exception>
    public void OpenProject(string projectPath)
    {
        if (IsProjectOpen)
        {
            throw new InvalidOperationException("Project is already open, close the current project first");
        }

        if (!Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException($"Project directory not found: {projectPath}");
        }

        try
        {
            ProjectDirectory = projectPath;
            UpdateAllFilesInProject();
            SetupProjectWatcher();

            Log.Info($"Project opened: {projectPath}");
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
        if (_projectWatcher != null)
        {
            _projectWatcher.EnableRaisingEvents = false;
            _projectWatcher.Dispose();
            _projectWatcher = null;
        }

        ProjectDirectory = null;
        _allFilesInProject.Clear();

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
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            UpdateAllFilesInProject();
            OnFilesInProjectUpdated?.Invoke();
        });
    }

    private void UpdateAllFilesInProject()
    {
        lock (_allFilesInProjectLock)
        {
            if (ProjectDirectory == null)
            {
                return;
            }
            try
            {
                _allFilesInProject.Clear();
                foreach (var file in Directory.EnumerateFiles(ProjectDirectory, "*", SearchOption.AllDirectories))
                {
                    _allFilesInProject.Add(FixPath(Path.GetRelativePath(ProjectDirectory, file)));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to update project files: {ex.Message}");
                throw;
            }
        }
    }

    private static string FixPath(string path)
    {
        return path.Replace('\\', '/');
    }

}
