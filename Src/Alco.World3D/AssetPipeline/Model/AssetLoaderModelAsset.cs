using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Loads model asset files (<c>.amdl</c>) into <see cref="ModelAsset"/>s, resolving the referenced mesh and material assets through the asset system as cheap meta loads.
/// </summary>
public sealed class AssetLoaderModelAsset : BaseAssetLoader<ModelAsset>
{
    /// <inheritdoc />
    public override string Name => "ModelAsset(.amdl)";

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions => [World3DAssetPipeline.ModelExtension];

    /// <inheritdoc />
    public override object CreateAsset(in AssetLoadContext context)
    {
        ModelAssetJson json = ModelAssetJson.Parse(context.GetData(), context.Filename);
        AssetSystem assetSystem = context.AssetSystem;

        MeshAsset mesh = assetSystem.Load<MeshAsset>(AssetJson.NormalizePath(json.Mesh)!);
        MaterialAsset? defaultMaterial = json.DefaultMaterial is null
            ? null
            : assetSystem.Load<MaterialAsset>(AssetJson.NormalizePath(json.DefaultMaterial)!);

        int slotCount = json.Slots?.Count ?? 0;
        ModelMaterialSlot[] slots = new ModelMaterialSlot[slotCount];
        for (int i = 0; i < slotCount; i++)
        {
            ModelAssetJson.SlotJson slot = json.Slots![i];
            slots[i] = new ModelMaterialSlot(
                slot.Name!.Trim(),
                assetSystem.Load<MaterialAsset>(AssetJson.NormalizePath(slot.Material)!));
        }

        return new ModelAsset(Path.GetFileNameWithoutExtension(context.Filename), mesh, slots, defaultMaterial);
    }
}
