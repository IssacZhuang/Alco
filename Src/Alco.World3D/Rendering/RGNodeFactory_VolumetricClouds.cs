using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Factory for the volumetric clouds render plugin node: holds the march,
/// composite and cloud-shadow coverage shaders (resolved at load time), plus the
/// half-resolution march scale. The noise-bake module's base/detail
/// specializations resolve inside the node as generic value specializations, not
/// configuration. Wiring into a composition (post chain, lighting, G-buffer,
/// shadow map, environment) stays with the composing code through
/// <see cref="RGNode_VolumetricClouds.Attach"/>.
/// </summary>
public class RGNodeFactory_VolumetricClouds : RenderNodeFactory
{
    /// <summary>The cloud march shader.</summary>
    public required Shader MarchShader { get; set; }
    /// <summary>The composite shader.</summary>
    public required Shader CompositeShader { get; set; }
    /// <summary>The shadow coverage bake shader.</summary>
    public required Shader ShadowShader { get; set; }

    /// <summary>The ray-march resolution relative to the viewport (0.25 - 1.0).</summary>
    public float MarchResolutionScale { get; set; } = 0.5f;

    /// <inheritdoc />
    public override IRenderNode Create(RenderNodeFactoryContext context)
    {
        return new RGNode_VolumetricClouds(
            context.Rendering, MarchShader, CompositeShader, ShadowShader)
        {
            MarchResolutionScale = MarchResolutionScale,
        };
    }
}
