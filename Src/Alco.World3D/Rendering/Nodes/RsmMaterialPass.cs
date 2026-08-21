using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// The reflective-shadow-map pass's material policy — the material face of the voxel
/// GI's sun-bounce feature (see <see cref="RGNode_RsmPass"/>). This class lives with the
/// RSM code and is registered by whatever enables the RSM
/// (<c>compiler.RegisterPass(new RsmMaterialPass(shadowRenderer))</c> right after
/// <c>ShadowRenderer.EnableRsm</c>); the material compiler itself carries no RSM
/// knowledge, so a GI without an RSM simply never registers this pass.
/// </summary>
public sealed class RsmMaterialPass : IMaterialPass
{
    private readonly ShadowRenderer _renderer;

    /// <summary>Create the pass adapter of an RSM-enabled shadow renderer.</summary>
    /// <param name="renderer">The shadow renderer after <c>EnableRsm</c>.</param>
    public RsmMaterialPass(ShadowRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    /// <inheritdoc />
    public string Id => "rsm";

    /// <inheritdoc />
    public GraphicsMaterial Compile(MaterialCompileContext context)
    {
        MaterialAsset asset = context.Asset;
        Shader shader = context.ComposeShader(World3DAssetPaths.Shader_Rsm);
        GraphicsMaterial material = _renderer.CreateRsmMaterial(shader, asset.DoubleSided, $"{asset.Name}_rsm");
        material.SetDefines([.. asset.Defines, "PASS_RSM"]);

        // Albedo fallback until textures stream in; custom surfaces without the
        // standard slots keep the engine-wide white default.
        material.TrySetTexture(StandardSurfaceSlotsUtility.ShaderResourceName(StandardSurfaceSlotsUtility.Albedo), context.Rendering.TextureWhite);
        return material;
    }

    /// <inheritdoc />
    public void RebindTextures(MaterialCompileContext context, GraphicsMaterial material, IReadOnlyDictionary<string, Texture2D?> slots)
    {
        MaterialPassTexturesUtility.Bind(material, slots, context.Rendering);
    }
}
