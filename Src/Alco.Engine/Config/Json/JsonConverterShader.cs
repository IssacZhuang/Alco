

using System.Text.Json;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;


public class JsonConverterShader : BaseJsonConverterAsset<Shader>
{
    public JsonConverterShader(AssetSystem assetSystem) : base(assetSystem)
    {
    }

    public override void Write(Utf8JsonWriter writer, Shader value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Name);
    }
}