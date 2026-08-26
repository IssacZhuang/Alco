

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
        // Font carries no asset name; approximate with the underlying texture name.
        writer.WriteStringValue(value.Texture.Name);
    }
}


