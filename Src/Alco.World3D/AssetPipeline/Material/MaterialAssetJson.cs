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
    /// <summary>The only shading domain M1 understands.</summary>
    internal const string SupportedDomain = "pbr";

    public string? Version { get; set; }
    public string? Name { get; set; }
    public string? Domain { get; set; }
    public float[]? BaseColorFactor { get; set; }
    public float? MetallicFactor { get; set; }
    public float? RoughnessFactor { get; set; }
    public float[]? EmissiveFactor { get; set; }
    public string? AlphaMode { get; set; }
    public float? AlphaCutoff { get; set; }
    public bool? DoubleSided { get; set; }
    public TexturesJson? Textures { get; set; }

    /// <summary>The texture slot references of the material; paths, never loaded by the parser.</summary>
    public sealed class TexturesJson
    {
        public string? Albedo { get; set; }
        public string? Normal { get; set; }
        public string? MetallicRoughness { get; set; }
        public string? Emissive { get; set; }
    }

    /// <summary>
    /// Parse material asset bytes into a <see cref="MaterialAsset"/>.
    /// </summary>
    /// <param name="data">The UTF-8 JSON bytes of the file.</param>
    /// <param name="filename">The file being parsed, used for the default name and error context.</param>
    /// <returns>The parsed material asset.</returns>
    /// <exception cref="InvalidDataException">Thrown when the file is empty, has an
    /// unsupported version or domain, or carries malformed values.</exception>
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

        string domain = json.Domain ?? SupportedDomain;
        if (!string.Equals(domain, SupportedDomain, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Material asset '{filename}' uses domain '{domain}'; only '{SupportedDomain}' is supported.");
        }

        MeshAlphaMode alphaMode = ParseAlphaMode(json.AlphaMode, filename);
        TexturesJson textures = json.Textures ?? new TexturesJson();

        return new MaterialAsset
        {
            Name = string.IsNullOrWhiteSpace(json.Name) ? Path.GetFileNameWithoutExtension(filename) : json.Name.Trim(),
            Domain = SupportedDomain,
            BaseColorFactor = json.BaseColorFactor != null ? ToVector4(json.BaseColorFactor, "baseColorFactor", filename) : Vector4.One,
            MetallicFactor = json.MetallicFactor ?? 0.0f,
            RoughnessFactor = json.RoughnessFactor ?? 1.0f,
            EmissiveFactor = json.EmissiveFactor != null ? ToVector3(json.EmissiveFactor, "emissiveFactor", filename) : Vector3.Zero,
            AlphaMode = alphaMode,
            AlphaCutoff = json.AlphaCutoff ?? 0.5f,
            DoubleSided = json.DoubleSided ?? false,
            AlbedoTexture = AssetJson.NormalizePath(textures.Albedo),
            NormalTexture = AssetJson.NormalizePath(textures.Normal),
            MetallicRoughnessTexture = AssetJson.NormalizePath(textures.MetallicRoughness),
            EmissiveTexture = AssetJson.NormalizePath(textures.Emissive),
        };
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
