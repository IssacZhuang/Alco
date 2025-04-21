using System.Text.Json;
using Alco.Engine;
using Alco.IO;

namespace Alco.Project;

/// <summary>
/// Represents an Alco game engine project and handles asset system integration.
/// This class manages project files, watches for file changes, and provides access to the asset system.
/// </summary>
public partial class AlcoProject: AutoDisposable, IAssetSystemHost
{
    private readonly TypeDatabase _typeDatabase;
    private readonly FileSystemWatcher _projectWatcher;
    private readonly FileSystemWatcher? _assetsWatcher;
    //individual asset system for this project
    private readonly AssetSystem _assetSystem;
    private readonly AssetDatabase _assetDatabase;
    private readonly GameEngine _engine;

    /// <summary>
    /// Gets the full path to the project file.
    /// </summary>
    public string ProjectFilePath { get; }

    /// <summary>
    /// Gets the directory containing the project.
    /// </summary>
    public string ProjectDirectory { get; }

    /// <summary>
    /// Gets the configuration for the Alco project.
    /// </summary>
    public AlcoProjectConfig Config { get; }

    /// <summary>
    /// Event triggered when the files in the project are updated.
    /// </summary>
    public event Action<FileSystemEventArgs>? OnFilesInProjectUpdated;

    public AlcoProject(string projectFilePath, GameEngine engine)
    {
        _engine = engine;
        ProjectFilePath = projectFilePath;
        ProjectDirectory = Path.GetDirectoryName(projectFilePath) ?? throw new FileNotFoundException("Project directory not found");
        Config = JsonSerializer.Deserialize<AlcoProjectConfig>(File.ReadAllText(projectFilePath)) ?? throw new FileNotFoundException("Alco.Project.json not found");
        _typeDatabase = new TypeDatabase();
        _assetSystem = new AssetSystem(this);

        // add file sources from this project

        string assetsPath = Path.Combine(ProjectDirectory, Config.AssetPath);
        if (Directory.Exists(assetsPath))
        {
            _assetSystem.AddFileSource(new DirectoryFileSource(assetsPath));

            // Setup assets watcher
            _assetsWatcher = new FileSystemWatcher
            {
                Path = assetsPath,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                Filter = "*.*",
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _assetsWatcher.Created += OnAssetFileChanged;
            _assetsWatcher.Deleted += OnAssetFileDeleted;
            _assetsWatcher.Changed += OnAssetFileChanged;
            _assetsWatcher.Renamed += OnAssetFileRenamed;
        }
        else
        {
            Log.Error($"Asset path {assetsPath} not found");
        }

        foreach (var fileSource in engine.CreateDefaultFileSources())
        {
            _assetSystem.AddFileSource(fileSource);
        }

        foreach (var assetLoader in engine.CreateDefaultAssetLoaders())
        {
            _assetSystem.RegisterAssetLoader(assetLoader);
        }

        foreach (var assetEncoder in engine.CreateDefaultAssetEncoders()){
            _assetSystem.RegisterAssetEncoder(assetEncoder);
        }

        _assetDatabase = new AssetDatabase(_assetSystem);

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



    /// <summary>
    /// Opens the C# project associated with this Alco project.
    /// </summary>
    /// <returns>A CSharpProject instance representing the opened project.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the C# project file is not found.</exception>
    public CSharpProject OpenCSharpProject()
    {
        //get same filename that ends with .csproj in same directory
        string csharpProjectPath = Path.Combine(ProjectDirectory, $"{Path.GetFileNameWithoutExtension(ProjectFilePath)}.csproj");

        if (!File.Exists(csharpProjectPath))
        {
            throw new FileNotFoundException($"C# project file not found at {csharpProjectPath}");
        }

        return new CSharpProject(csharpProjectPath);
    }

    protected override void Dispose(bool disposing)
    {
        _onDispose?.Invoke();
        _typeDatabase.Dispose();
        _projectWatcher.EnableRaisingEvents = false;
        _projectWatcher.Dispose();

        if (_assetsWatcher != null)
        {
            _assetsWatcher.EnableRaisingEvents = false;
            _assetsWatcher.Dispose();
        }
    }

    private void OnProjectFileChanged(object sender, FileSystemEventArgs e)
    {
        OnFilesInProjectUpdated?.Invoke(e);
    }

    private void OnAssetFileChanged(object sender, FileSystemEventArgs e)
    {
        string relativePath = GetRelativeAssetPath(e.FullPath);
        Log.Info($"Asset file modified: {relativePath}");
        _assetDatabase.MarkAsChanged(relativePath);
    }

    private void OnAssetFileDeleted(object sender, FileSystemEventArgs e)
    {
        string relativePath = GetRelativeAssetPath(e.FullPath);
        Log.Info($"Asset file deleted: {relativePath}");
        _assetSystem.MarkEntriesDirty();
        _assetDatabase.MarkAsDelete(relativePath);
    }

    private void OnAssetFileRenamed(object sender, RenamedEventArgs e)
    {
        string oldRelativePath = GetRelativeAssetPath(e.OldFullPath);
        string newRelativePath = GetRelativeAssetPath(e.FullPath);
        Log.Info($"Asset file renamed: {oldRelativePath} to {newRelativePath}");
        _assetSystem.MarkEntriesDirty();
        _assetDatabase.MarkAsDelete(oldRelativePath);
        _assetDatabase.MarkAsCreate(newRelativePath);
    }

    private string GetRelativeAssetPath(string fullPath)
    {
        string assetsPath = Path.Combine(ProjectDirectory, Config.AssetPath);
        return fullPath.Replace(assetsPath, "").TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');
    }

    // asset system intergration
    // impl IAssetSystemHost

    private Action? _onDispose;

    event Action IAssetSystemHost.OnDispose
    {
        add
        {
            _onDispose += value;
        }

        remove
        {
            _onDispose -= value;
        }
    }

    void IAssetSystemHost.LogInfo(ReadOnlySpan<char> message)
    {
        Log.Info(message);
    }

    void IAssetSystemHost.LogWarning(ReadOnlySpan<char> message)
    {
        Log.Warning(message);
    }

    void IAssetSystemHost.LogError(ReadOnlySpan<char> message)
    {
        Log.Error(message);
    }

    void IAssetSystemHost.LogSuccess(ReadOnlySpan<char> message)
    {
        Log.Success(message);
    }

    void IAssetSystemHost.PostToMainThread(Action action)
    {
        _engine.PostToMainThread(action);
    }
}
