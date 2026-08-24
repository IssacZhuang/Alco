using System.Numerics;
using System.Text.Json;

namespace Alco.World3D;

/// <summary>
/// JSON schema of a material asset file (<c>.amat</c>) and its mapping onto
/// <see cref="MaterialAsset"/>. The DTO keeps vectors as float arrays so the JSON shape
/// stays explicit (e.g. <c>"baseColorFactor": [1, 1, 1, 1]</c>).
/// </summary>
internal sealed class MaterialAssetJson
{
    public string? Version { get; set; }
    public string? Name { get; set; }

    /// <summary>Asset path of the surface shader; null selects the built-in PbrStandard surface.</summary>
    public string? Shader { get; set; }

    /// <summary>Specialization defines of the surface.</summary>
    public List<string>? Defines { get; set; }

    /// <summary>Texture slots: material slot name → texture path (paths, never loaded by the parser).</summary>
    public Dictionary<string, string>? Textures { get; set; }

    /// <summary>Surface parameter values: member name → number or 1-4 component array.</summary>
    public Dictionary<string, JsonElement>? Parameters { get; set; }

    public float[]? BaseColorFactor { get; set; }
    public float? MetallicFactor { get; set; }
    public float? RoughnessFactor { get; set; }
    public float[]? EmissiveFactor { get; set; }
    public string? AlphaMode { get; set; }
    public float? AlphaCutoff { get; set; }
    public bool? DoubleSided { get; set; }

    /// <summary>
    /// Parse material asset bytes into a <see cref="MaterialAsset"/>.
    /// </summary>
    /// <param name="data">The UTF-8 JSON bytes of the file.</param>
    /// <param name="filename">The file being parsed, used for the default name and error context.</param>
    /// <returns>The parsed material asset.</returns>
    /// <exception cref="InvalidDataException">Thrown when the file is empty, has an
    /// unsupported version, or carries malformed values.</exception>
    public static MaterialAsset Parse(ReadOnlySpan<byte> data, string filename)
    {
        MaterialAssetJson? json;
        try
        {
            json = JsonSerializer.Deserialize<MaterialAssetJson>(data, AssetJson.Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Material asset '{filename}' is not valid JSON: {exception.Message}", exception);
        }

        if (json == null)
        {
            throw new InvalidDataException($"Material asset '{filename}' is empty.");
        }

        AssetJson.ValidateVersion(json.Version, MaterialAsset.FormatVersion, "Material asset", filename);

        MeshAlphaMode alphaMode = ParseAlphaMode(json.AlphaMode, filename);

        Dictionary<string, string> textures = new();
        if (json.Textures != null)
        {
            foreach (KeyValuePair<string, string> pair in json.Textures)
            {
                string slot = pair.Key.Trim();
                string? path = AssetJson.NormalizePath(pair.Value);
                if (slot.Length > 0 && path != null)
                {
                    textures.Add(slot, path);
                }
            }
        }

        return new MaterialAsset
        {
            Name = string.IsNullOrWhiteSpace(json.Name) ? Path.GetFileNameWithoutExtension(filename) : json.Name.Trim(),
            SurfaceShader = AssetJson.NormalizePath(json.Shader),
            Defines = ParseDefines(json.Defines, filename),
            Textures = textures,
            Parameters = ParseParameters(json.Parameters, filename),
            BaseColorFactor = json.BaseColorFactor != null ? ToVector4(json.BaseColorFactor, "baseColorFactor", filename) : Vector4.One,
            MetallicFactor = json.MetallicFactor ?? 0.0f,
            RoughnessFactor = json.RoughnessFactor ?? 1.0f,
            EmissiveFactor = json.EmissiveFactor != null ? ToVector3(json.EmissiveFactor, "emissiveFactor", filename) : Vector3.Zero,
            AlphaMode = alphaMode,
            AlphaCutoff = json.AlphaCutoff ?? 0.5f,
            DoubleSided = json.DoubleSided ?? false,
        };
    }

    /// <summary>
    /// Normalize the define list: trimmed, empty entries dropped, duplicates removed in
    /// first-seen order.
    /// </summary>
    private static IReadOnlyList<string> ParseDefines(List<string>? defines, string filename)
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

    /// <summary>
    /// Normalize the parameter dictionary: each value is a JSON number (one component)
    /// or an array of 1-4 numbers (the components of one member of a
    /// [MaterialParams]-marked parameter block of the surface).
    /// </summary>
    private static IReadOnlyDictionary<string, float[]> ParseParameters(Dictionary<string, JsonElement>? parameters, string filename)
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

    private static MeshAlphaMode ParseAlphaMode(string? alphaMode, string filename)
    {
        if (string.IsNullOrWhiteSpace(alphaMode))
        {
            return MeshAlphaMode.Opaque;
        }

        if (Enum.TryParse<MeshAlphaMode>(alphaMode, ignoreCase: true, out MeshAlphaMode parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new InvalidDataException(
            $"Material asset '{filename}' has unknown alphaMode '{alphaMode}'; expected one of: {string.Join(", ", Enum.GetNames<MeshAlphaMode>())}.");
    }

    private static Vector4 ToVector4(float[] values, string field, string filename)
    {
        if (values.Length != 4)
        {
            throw new InvalidDataException($"Material asset '{filename}' field '{field}' must have 4 components, got {values.Length}.");
        }
        return new Vector4(values[0], values[1], values[2], values[3]);
    }

    private static Vector3 ToVector3(float[] values, string field, string filename)
    {
        if (values.Length != 3)
        {
            throw new InvalidDataException($"Material asset '{filename}' field '{field}' must have 3 components, got {values.Length}.");
        }
        return new Vector3(values[0], values[1], values[2]);
    }
}
