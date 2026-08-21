using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// The shadow pass's material policy: composes the shadow depth template with the
/// asset's surface and selects the alpha-tested permutation for
/// <see cref="MeshAlphaMode.Mask"/> assets (the surface's PASS_SHADOW path evaluates
/// alpha only), so cutout meshes cast correctly shaped shadows. Register where the
/// renderer is created.
/// </summary>
public sealed class ShadowMaterialPass : IMaterialPass
{
    private readonly ShadowRenderer _renderer;

    /// <summary>Create the pass adapter of a shadow renderer.</summary>
    /// <param name="renderer">The shadow renderer owning the pass state.</param>
    public ShadowMaterialPass(ShadowRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    /// <inheritdoc />
    public string Id => "shadow";

    /// <inheritdoc />
    public GraphicsMaterial Compile(MaterialCompileContext context)
    {
        MaterialAsset asset = context.Asset;
        bool cutout = asset.AlphaMode == MeshAlphaMode.Mask;

        Shader shader = context.ComposeShader(World3DAssetPaths.Shader_ShadowDepth);
        List<string> defines = [.. asset.Defines, "PASS_SHADOW"];
        if (cutout)
        {
            defines.Add("SHADOW_CUTOUT");
        }
        GraphicsMaterial material = _renderer.CreateShadowMaterial(shader, [.. defines], asset.DoubleSided, $"{asset.Name}_shadow");

        if (cutout)
        {
            material.TrySetTexture(StandardSurfaceSlotsUtility.ShaderResourceName(StandardSurfaceSlotsUtility.Albedo), context.Rendering.TextureWhite);
        }
        return material;
    }

    /// <inheritdoc />
    public void RebindTextures(MaterialCompileContext context, GraphicsMaterial material, IReadOnlyDictionary<string, Texture2D?> slots)
    {
        // Only the cutout permutation evaluates the surface (its albedo alpha); the
        // plain depth material has no texture slots to bind.
        if (context.Asset.AlphaMode == MeshAlphaMode.Mask)
        {
            MaterialPassTexturesUtility.Bind(material, slots, context.Rendering);
        }
    }
}
