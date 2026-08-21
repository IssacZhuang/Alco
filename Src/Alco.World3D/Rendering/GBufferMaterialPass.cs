using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// The G-buffer pass's material policy: composes the G-buffer template with the asset's
/// surface, applies the renderer's pass state (reverse-Z depth write, cull mode, camera)
/// and binds the standard-slot fallback textures until streaming delivers real ones.
/// Register where the renderer is created: <c>compiler.RegisterPass(new GBufferMaterialPass(renderer))</c>.
/// </summary>
public sealed class GBufferMaterialPass : IMaterialPass
{
    private readonly GBufferRenderer _renderer;

    /// <summary>Create the pass adapter of a G-buffer renderer.</summary>
    /// <param name="renderer">The G-buffer renderer owning the pass state.</param>
    public GBufferMaterialPass(GBufferRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    /// <inheritdoc />
    public string Id => "gbuffer";

    /// <inheritdoc />
    public GraphicsMaterial Compile(MaterialCompileContext context)
    {
        MaterialAsset asset = context.Asset;
        Shader shader = context.ComposeShader(World3DAssetPaths.Shader_GBuffer);
        GraphicsMaterial material = _renderer.CreateMaterial(shader, asset.DoubleSided, $"{asset.Name}_gbuffer");
        material.SetDefines([.. asset.Defines, "PASS_GBUFFER"]);

        // Standard-slot fallbacks until textures stream in; custom surfaces simply
        // lack these slots and keep the engine-wide white default.
        RenderingSystem rendering = context.Rendering;
        material.TrySetTexture(StandardSurfaceSlotsUtility.ShaderResourceName(StandardSurfaceSlotsUtility.Albedo), rendering.TextureWhite);
        material.TrySetTexture(StandardSurfaceSlotsUtility.ShaderResourceName(StandardSurfaceSlotsUtility.Normal), _renderer.FlatNormalTexture);
        material.TrySetTexture(StandardSurfaceSlotsUtility.ShaderResourceName(StandardSurfaceSlotsUtility.MetallicRoughness), rendering.TextureWhite);
        material.TrySetTexture(StandardSurfaceSlotsUtility.ShaderResourceName(StandardSurfaceSlotsUtility.Emissive), rendering.TextureBlack);
        return material;
    }

    /// <inheritdoc />
    public void RebindTextures(MaterialCompileContext context, GraphicsMaterial material, IReadOnlyDictionary<string, Texture2D?> slots)
    {
        MaterialPassTexturesUtility.Bind(material, slots, context.Rendering, _renderer.FlatNormalTexture);
    }
}
