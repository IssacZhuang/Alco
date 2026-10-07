

using System.Text.Json;
using Alco.IO;

namespace Alco.Rendering;


public class JsonConverterTexture2D : BaseJsonConverterAsset<Texture2D>
{
    public JsonConverterTexture2D(AssetSystem assetSystem) : base(assetSystem)
    {
    }

    public override void Write(Utf8JsonWriter writer, Texture2D value, JsonSerializerOptions options)
    {
        // Extensionless alias form: format-independent on re-read (packages may
        // ship a different concrete format, e.g. png -> dds).
        writer.WriteStringValue(_assetSystem.GetAliasPath(value.Name));
    }
}