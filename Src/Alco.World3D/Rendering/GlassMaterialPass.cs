using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// The forward transparency (glass) pass's material policy: composes the ForwardGlass
/// template with the asset's surface and applies the node's pass state (alpha blending
/// without accumulation, reversed-depth testing, camera/lighting/shadow-map bindings).
/// Register where the node is created; materials are meant for
/// <see cref="MeshAlphaMode.Blend"/> assets.
/// </summary>
public sealed class GlassMaterialPass : IMaterialPass
{
    private readonly RGNode_Forward _forward;

    /// <summary>Create the pass adapter of a forward transparency node.</summary>
    /// <param name="forward">The forward node owning the pass state.</param>
    public GlassMaterialPass(RGNode_Forward forward)
    {
        ArgumentNullException.ThrowIfNull(forward);
        _forward = forward;
    }

    /// <inheritdoc />
    public string Id => "glass";

    /// <inheritdoc />
    public GraphicsMaterial Compile(MaterialCompileContext context)
    {
        MaterialAsset asset = context.Asset;
        Shader shader = context.ComposeShader(World3DAssetPaths.Shader_ForwardGlass);
        GraphicsMaterial material = _forward.CreateGlassMaterial(shader, asset.DoubleSided, $"{asset.Name}_glass");
        material.SetDefines([.. asset.Defines]);

        // Standard-slot fallbacks until textures stream in; custom surfaces simply
        // lack these slots and keep the engine-wide white default.
        RenderingSystem rendering = context.Rendering;
        material.TrySetTexture(StandardSurfaceSlotsUtility.ShaderResourceName(StandardSurfaceSlotsUtility.Albedo), rendering.TextureWhite);
        material.TrySetTexture(StandardSurfaceSlotsUtility.ShaderResourceName(StandardSurfaceSlotsUtility.Normal), _forward.FlatNormalTexture);
        material.TrySetTexture(StandardSurfaceSlotsUtility.ShaderResourceName(StandardSurfaceSlotsUtility.MetallicRoughness), rendering.TextureWhite);
        material.TrySetTexture(StandardSurfaceSlotsUtility.ShaderResourceName(StandardSurfaceSlotsUtility.Emissive), rendering.TextureBlack);
        return material;
    }

    /// <inheritdoc />
    public void RebindTextures(MaterialCompileContext context, GraphicsMaterial material, IReadOnlyDictionary<string, Texture2D?> slots)
    {
        MaterialPassTexturesUtility.Bind(material, slots, context.Rendering, _forward.FlatNormalTexture);
    }
}
