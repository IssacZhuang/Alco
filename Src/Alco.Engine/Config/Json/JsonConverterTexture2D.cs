

using System.Text.Json;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;


public class JsonConverterTexture2D : BaseJsonConverterAsset<Texture2D>
{
    public JsonConverterTexture2D(AssetSystem assetSystem) : base(assetSystem)
    {
    }

    public override void Write(Utf8JsonWriter writer, Texture2D value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Name);
    }
}