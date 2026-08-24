using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// One material-slot binding of a <see cref="ModelAsset"/>: a mesh slot name plus the bound
/// material asset.
/// </summary>
public readonly struct ModelMaterialSlot
{
    /// <summary>The mesh slot name this binding targets (see <see cref="MeshAssetSubMesh.Name"/>).</summary>
    public string Name { get; }

    /// <summary>The bound material asset.</summary>
    public MaterialAsset Material { get; }

    /// <summary>Creates a slot binding.</summary>
    /// <param name="name">The mesh slot name.</param>
    /// <param name="material">The bound material asset.</param>
    public ModelMaterialSlot(string name, MaterialAsset material)
    {
        Name = name;
        Material = material;
    }
}

/// <summary>
/// The composition layer of the 3D asset chain — the runtime form of a model asset
/// file (<c>.amdl</c>): one <see cref="MeshAsset"/> plus the material bound to each named
/// mesh slot. The mesh itself never references materials (its submeshes are named slots);
/// this asset is where the binding happens. Pure orchestration: loading resolves the
/// referenced <c>.amsh</c>/<c>.amat</c> files through the asset system as cheap meta loads —
/// no geometry is resident, and material textures load with their material — and this
/// object holds no GPU resources. Geometry streams per LOD via <see cref="Mesh.LoadLodAsync"/>;
/// streamed texture sources (e.g. a glTF scene) bind into compiled materials via
/// <see cref="MaterialCompiler.BindTextures"/>.
/// </summary>
public sealed class ModelAsset
{
    private readonly IReadOnlyList<MaterialAsset> _materials;

    /// <summary>Format version of the model asset files this runtime consumes.</summary>
    public const string FormatVersion = "1.0";

    /// <summary>The model name; the source file name without extension.</summary>
    public string Name { get; }

    /// <summary>The mesh stream of the model (header-only until LODs are loaded).</summary>
    public MeshAsset Mesh { get; }

    /// <summary>The material-slot bindings in file order.</summary>
    public IReadOnlyList<ModelMaterialSlot> Slots { get; }

    /// <summary>
    /// The fallback material for mesh slots without an explicit binding; null means
    /// unbound slots have no material and renderers must apply their own default.
    /// </summary>
    public MaterialAsset? DefaultMaterial { get; }

    /// <summary>Creates a model asset. Called by the loader after the reference graph resolved.</summary>
    /// <param name="name">The model name.</param>
    /// <param name="mesh">The mesh stream.</param>
    /// <param name="slots">The material-slot bindings.</param>
    /// <param name="defaultMaterial">The fallback material, or null.</param>
    internal ModelAsset(string name, MeshAsset mesh, IReadOnlyList<ModelMaterialSlot> slots, MaterialAsset? defaultMaterial)
    {
        Name = name;
        Mesh = mesh;
        Slots = slots;
        DefaultMaterial = defaultMaterial;

        List<MaterialAsset> materials = new(slots.Count + 1);
        for (int i = 0; i < slots.Count; i++)
        {
            if (!materials.Contains(slots[i].Material))
            {
                materials.Add(slots[i].Material);
            }
        }
        if (defaultMaterial != null && !materials.Contains(defaultMaterial))
        {
            materials.Add(defaultMaterial);
        }
        _materials = materials;
    }

    /// <summary>
    /// Resolve the material bound to a mesh slot name. Matching is case-insensitive and
    /// whitespace-trimmed; unbound (or unknown) slot names fall back to
    /// <see cref="DefaultMaterial"/>.
    /// </summary>
    /// <param name="slotName">The mesh slot name.</param>
    /// <param name="material">The bound material, the default material, or null when neither exists.</param>
    /// <returns>True when a material was found (explicit or default); false when the slot
    /// resolves to nothing and the caller must apply its own fallback.</returns>
    public bool TryGetMaterial(string slotName, out MaterialAsset? material)
    {
        if (!string.IsNullOrWhiteSpace(slotName))
        {
            string trimmed = slotName.Trim();
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i].Name.AsSpan().Equals(trimmed.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    material = Slots[i].Material;
                    return true;
                }
            }
        }

        material = DefaultMaterial;
        return material != null;
    }

    /// <summary>
    /// The mesh slot names (of LOD 0 — the canonical slot list) that have no explicit
    /// binding in this model. Such slots render with <see cref="DefaultMaterial"/> (when
    /// present); diagnostics hook for validation tooling and load-time warnings.
    /// </summary>
    public IReadOnlyList<string> GetUnboundSlotNames()
    {
        List<string> unbound = new();
        if (Mesh.LodCount == 0)
        {
            return unbound;
        }

        ReadOnlySpan<MeshAssetSubMesh> meshSlots = Mesh.GetSubMeshes(0);
        for (int i = 0; i < meshSlots.Length; i++)
        {
            bool bound = false;
            for (int s = 0; s < Slots.Count; s++)
            {
                if (Slots[s].Name.AsSpan().Equals(meshSlots[i].Name.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    bound = true;
                    break;
                }
            }
            if (!bound)
            {
                unbound.Add(meshSlots[i].Name);
            }
        }
        return unbound;
    }

    /// <summary>The distinct materials referenced by this model (bindings, then the default), in reference order.</summary>
    public IReadOnlyList<MaterialAsset> EnumerateMaterials() => _materials;
}
