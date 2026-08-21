using Alco.IO;

namespace Alco.World3D;

/// <summary>
/// Loads model asset files (<c>.amdl</c>) — the composition layer — into
/// <see cref="ModelAsset"/>s: parses the JSON, then resolves the referenced
/// <see cref="MeshAsset"/> (<c>.amsh</c>) and <see cref="MaterialAsset"/> (<c>.amat</c>)
/// files through the asset system. All loads are cheap meta loads; no geometry and no
/// textures become resident. Nested loads go through distinct per-file handles, so loading
/// inside a load cannot deadlock. Registered through
/// <see cref="World3DAssetPipeline.RegisterLoaders"/>.
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
