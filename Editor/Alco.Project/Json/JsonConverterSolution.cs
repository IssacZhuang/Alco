using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Build.Construction;

namespace Alco.Project;

public class JsonConverterSolution : JsonConverter<DotnetSolution>
{
    public string? ProjectPath { get; }
    public JsonConverterSolution(string? projectPath = null)
    {
        ProjectPath = projectPath;
    }

    public override DotnetSolution? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string path = reader.GetString() ?? throw new JsonException("Solution file path is null");
        string fullPath = ProjectPath == null ? path : Path.Combine(ProjectPath, path);
        SolutionFile solutionFile = SolutionFile.Parse(fullPath);
        return new DotnetSolution(solutionFile, fullPath);
    }

    public override void Write(Utf8JsonWriter writer, DotnetSolution value, JsonSerializerOptions options)
    {
        string relativePath = ProjectPath == null ? value.FilePath : Path.GetRelativePath(ProjectPath, value.FilePath);
        writer.WriteStringValue(relativePath);
    }
}
