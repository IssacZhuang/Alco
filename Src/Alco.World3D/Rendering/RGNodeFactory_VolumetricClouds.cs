using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Factory for the volumetric clouds render plugin node: holds the node's
/// <see cref="RGNode_VolumetricClouds.Descriptor"/> (shader references resolve
/// through the shared shader system at load time). The noise-bake module's
/// base/detail specializations resolve inside the node as generic value
/// specializations, not configuration. Wiring into a composition (post chain,
/// lighting, G-buffer, shadow map, environment) stays with the composing code
/// through <see cref="RGNode_VolumetricClouds.Attach"/>.
/// </summary>
public class RGNodeFactory_VolumetricClouds : RenderNodeFactory
{
    /// <summary>The node's construction data.</summary>
    public required RGNode_VolumetricClouds.Descriptor Descriptor { get; set; }

    /// <inheritdoc />
    public override IRenderNode Create(RenderNodeFactoryContext context)
    {
        RGNode_VolumetricClouds.Descriptor descriptor = Descriptor;
        return new RGNode_VolumetricClouds(context.Rendering, in descriptor);
    }
}
