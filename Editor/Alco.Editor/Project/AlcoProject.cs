using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Editor;

/// <summary>
/// An Alco editor project — the <c>.alco</c> JSON file that associates owned asset
/// roots (editable by the editor) with referenced external assets such as engine
/// built-ins (usable, but read-only). All paths inside the file are relative to the
/// directory containing the file. This is an editor-only concept; game runtimes do
/// not consume it.
/// </summary>
public sealed class AlcoProject
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>The project display name; defaults to the file name when omitted.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Owned asset roots (directories relative to the project directory). The editor may
    /// create/modify assets under these roots; they are mounted as hot-reloading sources
    /// that shadow referenced entries of the same path.
    /// </summary>
    [JsonConverter(typeof(JsonConverterStringList))]
    public List<string> AssetsPaths { get; set; } = new();

    /// <summary>
    /// Referenced asset entries (directories or single files, relative to the project
    /// directory) — typically engine built-in assets. They resolve in the editor like
    /// project assets but are read-only.
    /// </summary>
    [JsonConverter(typeof(JsonConverterStringList))]
    public List<string> ReferencedAssets { get; set; } = new();

    /// <summary>Absolute directory that relative paths resolve against (the project file's directory).</summary>
    [JsonIgnore]
    public string ProjectDirectory { get; private set; } = string.Empty;

    /// <summary>Absolute path of the backing <c>.alco</c> file; null for untitled in-memory projects.</summary>
    [JsonIgnore]
    public string? FilePath { get; private set; }

    /// <summary>Whether this project exists only in memory (no backing file).</summary>
    [JsonIgnore]
    public bool IsUntitled => FilePath == null;

    /// <summary>File extension of Alco project files.</summary>
    public const string Extension = ".alco";

    /// <summary>
    /// Loads a project from a <c>.alco</c> file. Reads case-insensitively and accepts
    /// both single strings and arrays for the path lists.
    /// </summary>
    /// <param name="path">Path to the <c>.alco</c> file.</param>
    /// <returns>The loaded project.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not a <c>.alco</c> file or failed to parse.</exception>
    public static AlcoProject Load(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Project file not found: {fullPath}", fullPath);
        }
        if (!string.Equals(Path.GetExtension(fullPath), Extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Not an Alco project file (expected {Extension}): {fullPath}");
        }

        AlcoProject? project;
        try
        {
            project = JsonSerializer.Deserialize<AlcoProject>(File.ReadAllText(fullPath), JsonOptions);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Failed to parse project file {fullPath}: {e.Message}");
        }
        if (project == null)
        {
            throw new InvalidDataException($"Failed to parse project file: {fullPath}");
        }

        project.ProjectDirectory = Path.GetDirectoryName(fullPath)!;
        project.FilePath = fullPath;
        if (string.IsNullOrWhiteSpace(project.Name))
        {
            project.Name = Path.GetFileNameWithoutExtension(fullPath);
        }
        return project;
    }

    /// <summary>
    /// Creates an in-memory project whose single owned root is the given directory.
    /// </summary>
    /// <param name="rootDirectory">The directory serving as the untitled project's root.</param>
    public static AlcoProject CreateUntitled(string rootDirectory)
    {
        return new AlcoProject
        {
            Name = "Untitled",
            ProjectDirectory = Path.GetFullPath(rootDirectory),
            AssetsPaths = new List<string> { "." },
        };
    }

    /// <summary>
    /// Writes the project back to its backing file (camelCase, indented).
    /// Untitled projects must use <see cref="Save(string)"/>.
    /// </summary>
    public void Save()
    {
        if (FilePath == null)
        {
            throw new InvalidOperationException("Untitled projects have no backing file; use Save(path).");
        }
        Save(FilePath);
    }

    /// <summary>
    /// Writes the project to the given file, making it the backing file.
    /// </summary>
    /// <param name="path">Destination <c>.alco</c> path.</param>
    public void Save(string path)
    {
        string fullPath = Path.GetFullPath(path);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(this, JsonOptions));
        FilePath = fullPath;
        ProjectDirectory = Path.GetDirectoryName(fullPath)!;
    }

    /// <summary>Resolves a project-relative path to a normalized absolute path ('/' separators, no trailing slash).</summary>
    public string GetAbsolutePath(string projectRelativePath)
    {
        return Normalize(Path.GetFullPath(Path.Combine(ProjectDirectory, projectRelativePath)));
    }

    /// <summary>Absolute paths of all owned asset roots, in declaration order.</summary>
    public IReadOnlyList<string> GetAbsoluteAssetRoots()
    {
        var roots = new List<string>(AssetsPaths.Count);
        foreach (string path in AssetsPaths)
        {
            roots.Add(GetAbsolutePath(path));
        }
        return roots;
    }

    /// <summary>Absolute paths of all referenced entries (directories or files), in declaration order.</summary>
    public IReadOnlyList<string> GetAbsoluteReferencedPaths()
    {
        var entries = new List<string>(ReferencedAssets.Count);
        foreach (string path in ReferencedAssets)
        {
            entries.Add(GetAbsolutePath(path));
        }
        return entries;
    }

    /// <summary>Whether the asset with the given asset-system-relative path is owned (editable) by this project.</summary>
    public bool IsOwnedAsset(string relativePath) => TryGetOwnedAbsolutePath(relativePath, out _);

    /// <summary>
    /// Resolves an asset-system-relative path to a file under an owned root.
    /// Roots are checked in declaration order, matching their mount priority.
    /// </summary>
    public bool TryGetOwnedAbsolutePath(string relativePath, out string absolutePath)
    {
        foreach (string root in GetAbsoluteAssetRoots())
        {
            string candidate = Normalize(Path.Combine(root, relativePath));
            if (File.Exists(candidate))
            {
                absolutePath = candidate;
                return true;
            }
        }
        absolutePath = string.Empty;
        return false;
    }

    /// <summary>
    /// Resolves an asset-system-relative path to a file under a referenced entry
    /// (directory entries are searched; file entries match by their declared relative path).
    /// </summary>
    public bool TryGetReferencedAbsolutePath(string relativePath, out string absolutePath)
    {
        string normalizedRelative = NormalizeEntry(relativePath);
        foreach (string entry in ReferencedAssets)
        {
            string absoluteEntry = GetAbsolutePath(entry);
            if (Directory.Exists(absoluteEntry))
            {
                string candidate = Normalize(Path.Combine(absoluteEntry, relativePath));
                if (File.Exists(candidate))
                {
                    absolutePath = candidate;
                    return true;
                }
            }
            else if (string.Equals(NormalizeEntry(entry), normalizedRelative, StringComparison.OrdinalIgnoreCase)
                && File.Exists(absoluteEntry))
            {
                absolutePath = absoluteEntry;
                return true;
            }
        }
        absolutePath = string.Empty;
        return false;
    }

    /// <summary>Normalizes an absolute path: full path, '/' separators, no trailing slash.</summary>
    internal static string Normalize(string path) => Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');

    /// <summary>Normalizes an asset-system-relative path for comparison ('/' separators).</summary>
    internal static string NormalizeEntry(string path) => path.Replace('\\', '/').TrimStart('/');

    private static JsonSerializerOptions CreateJsonOptions()
    {
        // Web defaults: camelCase on write, case-insensitive on read (accepts the
        // PascalCase files the early scaffold shipped).
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
        };
    }

    /// <summary>
    /// Accepts either a single JSON string or an array of strings for the path lists.
    /// Always writes an array.
    /// </summary>
    private sealed class JsonConverterStringList : JsonConverter<List<string>>
    {
        public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? value = reader.GetString();
                return string.IsNullOrWhiteSpace(value) ? new List<string>() : new List<string> { value };
            }
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                var list = new List<string>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        return list;
                    }
                    if (reader.TokenType == JsonTokenType.String && reader.GetString() is { } item)
                    {
                        list.Add(item);
                    }
                }
                throw new JsonException("Unterminated string array.");
            }
            throw new JsonException($"Expected a string or an array of strings, got {reader.TokenType}.");
        }

        public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (string item in value)
            {
                writer.WriteStringValue(item);
            }
            writer.WriteEndArray();
        }
    }
}
