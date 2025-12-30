

using System.Text.Json;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;


public class JsonConverterFont : BaseJsonConverterAsset<Font>
{
    public JsonConverterFont(AssetSystem assetSystem) : base(assetSystem)
    {
    }

    public override void Write(Utf8JsonWriter writer, Font value, JsonSerializerOptions options)
    {
        // Fonts are loaded by path; serialize the asset name/path
        // Font itself does not expose Name, but AssetSystem tracks by filename.
        // For consistency with other converters, we expect value came from Load<Font>(path)
        // so the writer should output that path via AssetSystem lookup.
        // However, since Font doesn't carry its name, we fall back to a no-op string.
        // Prefer matching Texture2D/Shader pattern by using texture name as a proxy.
        writer.WriteStringValue(value.Texture.Name);
    }
}


