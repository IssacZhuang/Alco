using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.Engine;
using Alco.IO;

namespace Alco.Project;

public partial class AlcoProject: AutoDisposable
{
    private readonly List<IFileSource> _fileSources = [];
    private readonly GameEngine _engine;
    private readonly TypeDatabase _typeDatabase;
    private FileSystemWatcher _projectWatcher;

    public string ProjectFilePath { get; }
    public string ProjectDirectory { get; }
    public AlcoProjectConfig Config { get; }
    public AssetDatabase AssetDatabase { get; }
    public IReadOnlyList<IFileSource> FileSources => _fileSources;

    /// <summary>
    /// Event triggered when the files in the project are updated.
    /// </summary>
    public event Action<FileSystemEventArgs>? OnFilesInProjectUpdated;

    public AlcoProject(GameEngine engine, string projectFilePath)
    {
        _engine = engine;

        ProjectFilePath = projectFilePath;
        ProjectDirectory = Path.GetDirectoryName(projectFilePath) ?? throw new FileNotFoundException("Project directory not found");
        Config = JsonSerializer.Deserialize<AlcoProjectConfig>(File.ReadAllText(projectFilePath)) ?? throw new FileNotFoundException("Alco.Project.json not found");
        _typeDatabase = new TypeDatabase();
        AssetDatabase = new AssetDatabase(engine.Assets);

        foreach (var fileSource in Config.AssetsPaths)
        {
            string fullPath = Path.Combine(ProjectDirectory, fileSource);
            if (Directory.Exists(fullPath))
            {
                _fileSources.Add(new DirectoryFileSource(fullPath));
            }
        }

        AssetDatabase.UpdateConfigMeta(CancellationToken.None);

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
        _typeDatabase.Dispose();
        _projectWatcher.EnableRaisingEvents = false;
        _projectWatcher.Dispose();
    }

    private void OnProjectFileChanged(object sender, FileSystemEventArgs e)
    {
        OnFilesInProjectUpdated?.Invoke(e);
    }
}
