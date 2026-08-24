#nullable enable

using Alco.Graphics;
using Alco.Rendering;

namespace Alco.World3D.Test;

/// <summary>
/// A configurable <see cref="IMaterialPass{TAsset}"/> test double for the World3D
/// pipeline family: id and template module from the constructor, a minimal
/// materializing factory, overridable routing and specialization.
/// </summary>
internal class StubMaterialPass(string id, string templateModule, RenderingSystem rendering)
    : IMaterialPass<PbrMaterialAsset>
{
    public string Id => id;

    public ShaderLibrary Template => rendering.ShaderSystem.GetLibrary(templateModule);

    public virtual bool Accepts(PbrMaterialAsset asset) => true;

    public virtual IReadOnlyList<string>? GetValueSpecArgs(PbrMaterialAsset asset) => null;

    public GraphicsMaterial CreateMaterial(PbrMaterialAsset asset, Shader shader)
        => rendering.CreateMaterial(shader, $"{asset.Name}_{id}");
}

/// <summary>The forward glass pass double: participates only for blend materials.</summary>
internal sealed class StubGlassPass(RenderingSystem rendering)
    : StubMaterialPass(RGNode_Forward.PassId, "glass", rendering)
{
    public override bool Accepts(PbrMaterialAsset asset) => asset.AlphaMode == MeshAlphaMode.Blend;
}

/// <summary>
/// The shadow pass double: feeds the template's <c>let AlphaTest : bool</c> value
/// specialization from the asset's alpha mode.
/// </summary>
internal sealed class StubShadowPass(RenderingSystem rendering)
    : StubMaterialPass(ShadowRenderer.PassId, "shadow_depth", rendering)
{
    public override IReadOnlyList<string>? GetValueSpecArgs(PbrMaterialAsset asset)
        => [asset.AlphaMode == MeshAlphaMode.Mask ? "true" : "false"];
}
