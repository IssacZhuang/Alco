using System.Text.Json;

namespace Alco.Rendering;

/// <summary>
/// JSON schema of a material asset file (<c>.amat</c>) — the pipeline-agnostic subset —
/// and its mapping onto <see cref="MaterialAsset"/>. Pipeline families extend the schema
/// by deriving this DTO and registering it under a <c>type</c> discriminator
/// (<see cref="RegisterType{TJson}"/>); a file without <c>type</c> parses as the base
/// schema. The DTO keeps vectors as float arrays so the JSON shape stays explicit
/// (e.g. <c>"tint": [1, 1, 1, 1]</c>).
/// </summary>
public class MaterialAssetJson
{
    /// <summary>The pipeline-family discriminator selecting the derived schema; null parses the base schema.</summary>
    public string? Type { get; set; }

    public string? Version { get; set; }
    public string? Name { get; set; }

    /// <summary>Asset path of the surface shader; null selects the pipeline's default surface.</summary>
    public string? Shader { get; set; }

    /// <summary>Specialization defines of the surface.</summary>
    public List<string>? Defines { get; set; }

    /// <summary>Texture slots: material slot name → texture path (paths, never loaded by the parser).</summary>
    public Dictionary<string, string>? Textures { get; set; }

    /// <summary>Surface parameter values: member name → number or 1-4 component array.</summary>
    public Dictionary<string, JsonElement>? Parameters { get; set; }

    private static readonly Lock _typeLock = new();
    private static readonly Dictionary<string, Type> _types = new(StringComparer.Ordinal);

    /// <summary>
    /// Register a derived material asset schema under its <c>type</c> discriminator.
    /// Called once per pipeline family at startup (e.g. from its asset-pipeline
    /// registration); re-registering the same mapping is a no-op, a conflicting one throws.
    /// </summary>
    /// <typeparam name="TJson">The derived DTO type parsing the family's schema.</typeparam>
    /// <param name="discriminator">The <c>type</c> value selecting the schema (e.g. "pbr").</param>
    public static void RegisterType<TJson>(string discriminator) where TJson : MaterialAssetJson
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discriminator);
        lock (_typeLock)
        {
            if (_types.TryGetValue(discriminator, out Type? existing))
            {
                if (existing != typeof(TJson))
                {
                    throw new ArgumentException(
                        $"Material asset type '{discriminator}' is already registered to {existing.FullName}.");
                }
                return;
            }
            _types.Add(discriminator, typeof(TJson));
        }
    }

    /// <summary>
    /// Parse material asset bytes into a <see cref="MaterialAsset"/> of the family the
    /// file's <c>type</c> discriminator selects (the base <see cref="MaterialAsset"/>
    /// when absent).
    /// </summary>
    /// <param name="data">The UTF-8 JSON bytes of the file.</param>
    /// <param name="filename">The file being parsed, used for the default name and error context.</param>
    /// <returns>The parsed material asset.</returns>
    /// <exception cref="InvalidDataException">Thrown when the file is empty, carries an
    /// unregistered <c>type</c>, has an unsupported version, or carries malformed values.</exception>
    public static MaterialAsset Parse(ReadOnlySpan<byte> data, string filename)
    {
        Type dtoType = ResolveDtoType(ReadDiscriminator(data, filename), filename);

        MaterialAssetJson? json;
        try
        {
            json = (MaterialAssetJson?)JsonSerializer.Deserialize(data, dtoType, AssetJson.Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Material asset '{filename}' is not valid JSON: {exception.Message}", exception);
        }

        if (json == null)
        {
            throw new InvalidDataException($"Material asset '{filename}' is empty.");
        }

        return json.Map(filename);
    }

    /// <summary>
    /// Map the parsed DTO onto the runtime asset. Derived schemas override to fill their
    /// own asset type, reusing the base mapping helpers (<see cref="Validate"/>,
    /// <see cref="MapName"/>, <see cref="MapDefines"/>, <see cref="MapTextures"/>,
    /// <see cref="MapParameters"/>).
    /// </summary>
    protected virtual MaterialAsset Map(string filename)
    {
        Validate(filename);
        return new MaterialAsset
        {
            Name = MapName(filename),
            SurfaceShader = AssetJson.NormalizePath(Shader),
            Defines = MapDefines(Defines, filename),
            Textures = MapTextures(Textures),
            Parameters = MapParameters(Parameters, filename),
        };
    }

    /// <summary>Validate the shared fields of the parsed file (currently: the format version).</summary>
    protected void Validate(string filename)
        => AssetJson.ValidateVersion(Version, MaterialAsset.FormatVersion, "Material asset", filename);

    /// <summary>The asset name: the file's, or the source file name when the file omits it.</summary>
    protected string MapName(string filename)
        => string.IsNullOrWhiteSpace(Name) ? Path.GetFileNameWithoutExtension(filename) : Name.Trim();

    /// <summary>
    /// Normalize the define list: trimmed, empty entries dropped, duplicates removed in
    /// first-seen order.
    /// </summary>
    protected static IReadOnlyList<string> MapDefines(List<string>? defines, string filename)
    {
        if (defines == null || defines.Count == 0)
        {
            return Array.Empty<string>();
        }

        List<string> result = new(defines.Count);
        foreach (string define in defines)
        {
            string trimmed = define.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }
            if (trimmed.Contains(' '))
            {
                throw new InvalidDataException($"Material asset '{filename}' has a define with whitespace: '{trimmed}'.");
            }
            if (!result.Contains(trimmed))
            {
                result.Add(trimmed);
            }
        }
        return result;
    }

    /// <summary>Normalize the texture slot table: trimmed slot names, normalized paths, empty entries dropped.</summary>
    protected static IReadOnlyDictionary<string, string> MapTextures(Dictionary<string, string>? textures)
    {
        Dictionary<string, string> result = new();
        if (textures != null)
        {
            foreach (KeyValuePair<string, string> pair in textures)
            {
                string slot = pair.Key.Trim();
                string? path = AssetJson.NormalizePath(pair.Value);
                if (slot.Length > 0 && path != null)
                {
                    result.Add(slot, path);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Normalize the parameter dictionary: each value is a JSON number (one component)
    /// or an array of 1-4 numbers (the components of one member of a
    /// [MaterialParams]-marked parameter block of the surface).
    /// </summary>
    protected static IReadOnlyDictionary<string, float[]> MapParameters(
        Dictionary<string, JsonElement>? parameters, string filename)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return new Dictionary<string, float[]>();
        }

        Dictionary<string, float[]> result = new(parameters.Count);
        foreach (KeyValuePair<string, JsonElement> pair in parameters)
        {
            string name = pair.Key.Trim();
            if (name.Length == 0)
            {
                throw new InvalidDataException($"Material asset '{filename}' has an empty parameter name.");
            }

            float[] components = pair.Value.ValueKind switch
            {
                JsonValueKind.Number => [pair.Value.GetSingle()],
                JsonValueKind.Array => [.. pair.Value.EnumerateArray().Select(element =>
                    element.ValueKind == JsonValueKind.Number
                        ? element.GetSingle()
                        : throw new InvalidDataException($"Material asset '{filename}' parameter '{name}' has a non-numeric component."))],
                _ => throw new InvalidDataException(
                    $"Material asset '{filename}' parameter '{name}' must be a number or an array of up to 4 numbers."),
            };
            if (components.Length is < 1 or > 4)
            {
                throw new InvalidDataException($"Material asset '{filename}' parameter '{name}' must have 1-4 components, got {components.Length}.");
            }
            result.Add(name, components);
        }
        return result;
    }

    private static string? ReadDiscriminator(ReadOnlySpan<byte> data, string filename)
    {
        try
        {
            var reader = new Utf8JsonReader(data, new JsonReaderOptions
            {
                // The same author-friendly tolerance as AssetJson.Options: comments
                // and trailing commas must not trip the discriminator pre-scan.
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName
                    && reader.CurrentDepth == 1
                    && reader.ValueTextEquals("type"))
                {
                    return reader.Read() && reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                }
            }
            return null;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Material asset '{filename}' is not valid JSON: {exception.Message}", exception);
        }
    }

    private static Type ResolveDtoType(string? discriminator, string filename)
    {
        if (string.IsNullOrWhiteSpace(discriminator))
        {
            return typeof(MaterialAssetJson);
        }
        lock (_typeLock)
        {
            if (_types.TryGetValue(discriminator, out Type? dtoType))
            {
                return dtoType;
            }
        }
        throw new InvalidDataException(
            $"Material asset '{filename}' has unknown type '{discriminator}' " +
            $"(registered: {string.Join(", ", _types.Keys)}); the pipeline family's asset " +
            $"pipeline must be registered before its materials load.");
    }
}
