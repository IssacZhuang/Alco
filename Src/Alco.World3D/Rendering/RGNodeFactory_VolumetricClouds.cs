using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Factory for the volumetric clouds render plugin node, holding its <see cref="RGNode_VolumetricClouds.Descriptor"/>; wiring into a composition stays with the composing code through <see cref="RGNode_VolumetricClouds.Attach"/>.
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
