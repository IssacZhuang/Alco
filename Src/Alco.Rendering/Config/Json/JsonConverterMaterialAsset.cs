using System.Text.Json;
using Alco.IO;

namespace Alco.Rendering;

/// <summary>
/// Json converter for <see cref="MaterialAsset"/> references: an asset references a
/// material (<c>.amat</c>) by asset path string; the material loads through the asset
/// system at deserialization time, so a missing material fails at the referencing
/// asset's load with the file's context.
/// </summary>
public class JsonConverterMaterialAsset : BaseJsonConverterAsset<MaterialAsset>
{
    public JsonConverterMaterialAsset(AssetSystem assetSystem) : base(assetSystem)
    {
    }

    public override void Write(Utf8JsonWriter writer, MaterialAsset value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Name);
    }
}
