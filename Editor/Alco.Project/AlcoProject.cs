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

    public string ProjectFilePath { get; }
    public string ProjectDirectory { get; }
    public AlcoProjectConfig Config { get; }
    public AssetDatabase AssetDatabase { get; }
    public TypeDatabase TypeDatabase { get; }
    public IReadOnlyList<IFileSource> FileSources => _fileSources;


    public AlcoProject(GameEngine engine, string projectFilePath)
    {
        _engine = engine;

        ProjectFilePath = projectFilePath;
        ProjectDirectory = Path.GetDirectoryName(projectFilePath) ?? throw new FileNotFoundException("Project directory not found");
        Config = JsonSerializer.Deserialize<AlcoProjectConfig>(File.ReadAllText(projectFilePath)) ?? throw new FileNotFoundException("Alco.Project.json not found");
        TypeDatabase = new TypeDatabase();
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
        TypeDatabase.Dispose();
    }


}
