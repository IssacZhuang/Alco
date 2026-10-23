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
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonConverterMaterialAsset"/> class.
    /// </summary>
    /// <param name="assetSystem">The asset system used to resolve assets.</param>
    public JsonConverterMaterialAsset(AssetSystem assetSystem) : base(assetSystem)
    {
    }

    /// <summary>
    /// Writes the material's extensionless asset alias path.
    /// </summary>
    /// <param name="writer">The target JSON writer.</param>
    /// <param name="value">The material asset to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, MaterialAsset value, JsonSerializerOptions options)
    {
        // Write the loadable asset path (stamped by the loader), not the display
        // name: re-reading the reference must resolve through the asset system, and
        // the bare name ("Water") matches no file entry or alias ("Materials/Water").
        // The alias form drops the extension so re-reads stay format-independent.
        writer.WriteStringValue(_assetSystem.GetAliasPath(value.SourceFile ?? value.Name));
    }
}
