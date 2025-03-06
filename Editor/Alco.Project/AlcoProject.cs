using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.IO;

namespace Alco.Project;

public class AlcoProject
{
    public string FullPath { get; }
    public AlcoProjectConfig Config { get; }

    public AlcoProject(string projectFilePath)
    {
        FullPath = projectFilePath;
        Config = JsonSerializer.Deserialize<AlcoProjectConfig>(File.ReadAllText(projectFilePath)) ?? throw new FileNotFoundException("Alco.Project.json not found");
    }

    public CSharpProject OpenCSharpProject()
    {
        //get same filename that ends with .csproj in same directory
        string csharpProjectPath = Path.Combine(FullPath, $"{Path.GetFileNameWithoutExtension(FullPath)}.csproj");

        if (!File.Exists(csharpProjectPath))
        {
            throw new FileNotFoundException($"C# project file not found at {csharpProjectPath}");
        }

        return new CSharpProject(csharpProjectPath);
    }
}
