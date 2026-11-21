

using System.Text.Json;
using Alco.IO;

namespace Alco.Rendering;


/// <summary>
/// Json converter that stores a font asset as its extensionless alias path.
/// </summary>
public class JsonConverterFont : BaseJsonConverterAsset<Font>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonConverterFont"/> class.
    /// </summary>
    /// <param name="assetSystem">The asset system used to resolve assets.</param>
    public JsonConverterFont(AssetSystem assetSystem) : base(assetSystem)
    {
    }

    /// <summary>
    /// Writes the font's extensionless asset alias path.
    /// </summary>
    /// <param name="writer">The target JSON writer.</param>
    /// <param name="value">The font to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Font value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(_assetSystem.GetAliasPath(value.Name));
    }
}


