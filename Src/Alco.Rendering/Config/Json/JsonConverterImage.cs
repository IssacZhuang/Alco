using System.Text.Json;
using Alco.IO;

namespace Alco.Rendering;

/// <summary>
/// JSON converter that serializes an <see cref="Image"/> asset as its extensionless alias path
/// and treats empty or whitespace strings as <c>null</c> on read.
/// </summary>
public class JsonConverterImage : BaseJsonConverterAsset<Image>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonConverterImage"/> class.
    /// </summary>
    /// <param name="assetSystem">The asset system used to resolve and load image assets.</param>
    public JsonConverterImage(AssetSystem assetSystem) : base(assetSystem)
    {
    }

    /// <summary>
    /// Reads an image asset path string and loads the referenced <see cref="Image"/>,
    /// returning <c>null</c> when the string is empty or whitespace.
    /// </summary>
    /// <param name="reader">The JSON reader positioned at the image path.</param>
    /// <param name="typeToConvert">The target type.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The loaded <see cref="Image"/>, or <c>null</c> when the path is empty.</returns>
    public override Image? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected an image asset path string.");
        }

        string? path = AssetJson.NormalizePath(reader.GetString());
        return path == null ? null : _assetSystem.Load<Image>(path);
    }

    /// <summary>
    /// Writes the image's extensionless asset alias path.
    /// </summary>
    /// <param name="writer">The target JSON writer.</param>
    /// <param name="value">The image asset to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Image value, JsonSerializerOptions options)
    {
        // Extensionless alias form: format-independent on re-read (packages may
        // ship a different concrete format, e.g. png -> dds).
        writer.WriteStringValue(_assetSystem.GetAliasPath(value.Name));
    }
}
