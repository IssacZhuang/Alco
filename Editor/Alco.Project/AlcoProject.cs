using System.Text.Json;
using Alco.Engine;
using Alco.IO;

namespace Alco.Project;

public partial class AlcoProject: AutoDisposable, IAssetSystemHost
{
    private readonly TypeDatabase _typeDatabase;
    private readonly FileSystemWatcher _projectWatcher;
    //individual asset system for this project
    private readonly AssetSystem _assetSystem;
    private readonly AssetDatabase _assetDatabase;
    private readonly GameEngine _engine;

    public string ProjectFilePath { get; }
    public string ProjectDirectory { get; }
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

        string fullPath = Path.Combine(ProjectDirectory, Config.AssetPath);
        if (Directory.Exists(fullPath))
        {
            _assetSystem.AddFileSource(new DirectoryFileSource(fullPath));
        }
        else
        {
            Log.Error($"Asset path {fullPath} not found");
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

    

    public CSharpProject OpenCSharpProject()
    {
        //get same filename that ends with .csproj in same directory
        string csharpProjectPath = Path.Combine(ProjectFilePath, $"{Path.GetFileNameWithoutExtension(ProjectFilePath)}.csproj");

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
    }

    private void OnProjectFileChanged(object sender, FileSystemEventArgs e)
    {
        OnFilesInProjectUpdated?.Invoke(e);
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
