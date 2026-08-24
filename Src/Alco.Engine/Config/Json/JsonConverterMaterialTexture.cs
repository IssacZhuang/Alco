using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Json converter for material texture slots: a material asset references textures by
/// asset path string; the path normalizes (asset-root separators, trimmed, empty =
/// no texture) and loads through the asset system at deserialization time, so a
/// missing texture fails at asset load with the file's context instead of silently
/// rendering the fallback.
/// </summary>
public class JsonConverterMaterialTexture : JsonConverter<Texture2D>
{
    private readonly AssetSystem _assetSystem;

    public JsonConverterMaterialTexture(AssetSystem assetSystem)
    {
        _assetSystem = assetSystem;
    }

    public override Texture2D? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected a texture asset path string.");
        }
        string? path = AssetJson.NormalizePath(reader.GetString());
        return path == null ? null : _assetSystem.Load<Texture2D>(path);
    }

    public override void Write(Utf8JsonWriter writer, Texture2D value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Name);
    }
}
