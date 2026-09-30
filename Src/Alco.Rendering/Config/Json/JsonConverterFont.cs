

using System.Text.Json;
using Alco.IO;

namespace Alco.Rendering;


public class JsonConverterFont : BaseJsonConverterAsset<Font>
{
    public JsonConverterFont(AssetSystem assetSystem) : base(assetSystem)
    {
    }

    public override void Write(Utf8JsonWriter writer, Font value, JsonSerializerOptions options)
    {
        // Font carries no asset name; approximate with the underlying texture name
        // in extensionless alias form so re-reads stay format-independent.
        writer.WriteStringValue(_assetSystem.GetAliasPath(value.Texture.Name));
    }
}


