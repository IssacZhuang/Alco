using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.IO;

namespace Alco.Rendering;

/// <summary>
/// Converts material texture slots to and from asset path strings, resolved through the
/// asset system; an empty string means no texture.
/// </summary>
public class JsonConverterMaterialTexture : JsonConverter<Texture2D>
{
    private readonly AssetSystem _assetSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonConverterMaterialTexture"/> class.
    /// </summary>
    /// <param name="assetSystem">The asset system used to load textures.</param>
    public JsonConverterMaterialTexture(AssetSystem assetSystem)
    {
        _assetSystem = assetSystem;
    }

    /// <summary>
    /// Reads a texture path and loads the referenced texture, or null for an empty path.
    /// </summary>
    /// <param name="reader">The JSON reader positioned at the texture reference.</param>
    /// <param name="typeToConvert">The target type.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The loaded texture, or null when the path is empty.</returns>
    public override Texture2D? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected a texture asset path string.");
        }
        string? path = AssetJson.NormalizePath(reader.GetString());
        return path == null ? null : _assetSystem.Load<Texture2D>(path);
    }

    /// <summary>
    /// Writes the texture's extensionless asset alias path.
    /// </summary>
    /// <param name="writer">The target JSON writer.</param>
    /// <param name="value">The texture to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Texture2D value, JsonSerializerOptions options)
    {
        // Extensionless alias form: format-independent on re-read (packages may
        // ship a different concrete format, e.g. png -> dds).
        writer.WriteStringValue(_assetSystem.GetAliasPath(value.Name));
    }
}
