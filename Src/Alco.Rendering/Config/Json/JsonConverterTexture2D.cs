

using System.Text.Json;
using Alco.IO;

namespace Alco.Rendering;


/// <summary>
/// Json converter that stores a texture asset as its extensionless alias path.
/// </summary>
public class JsonConverterTexture2D : BaseJsonConverterAsset<Texture2D>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonConverterTexture2D"/> class.
    /// </summary>
    /// <param name="assetSystem">The asset system used to resolve assets.</param>
    public JsonConverterTexture2D(AssetSystem assetSystem) : base(assetSystem)
    {
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