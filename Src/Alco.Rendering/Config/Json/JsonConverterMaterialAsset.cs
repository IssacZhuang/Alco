using System.Text.Json;
using Alco.IO;

namespace Alco.Rendering;

/// <summary>
/// JSON converter for <see cref="MaterialAsset"/> references resolved through <see cref="AssetSystem"/>;
/// empty or whitespace paths deserialize as <c>null</c>.
/// </summary>
public class JsonConverterMaterialAsset : BaseJsonConverterAsset<MaterialAsset>
{
    /// <summary>Initializes a new instance of the <see cref="JsonConverterMaterialAsset"/> class.</summary>
    /// <param name="assetSystem">The asset system used to resolve material assets.</param>
    public JsonConverterMaterialAsset(AssetSystem assetSystem) : base(assetSystem)
    {
    }

    /// <inheritdoc/>
    public override MaterialAsset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected a material asset path string.");

        string? path = AssetJson.NormalizePath(reader.GetString());
        return path == null ? null : _assetSystem.Load<MaterialAsset>(path);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, MaterialAsset value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(_assetSystem.GetAliasPath(value.SourceFile ?? value.Name));
    }
}
