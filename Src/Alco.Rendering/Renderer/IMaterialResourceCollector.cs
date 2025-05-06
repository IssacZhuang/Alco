
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The interface to collect render resources from the material.
/// </summary>
public interface IMaterialResourceCollector
{
    /// <summary>
    /// Set the graphics resources.
    /// </summary>
    /// <param name="slot">The slot index.</param>
    /// <param name="resourceGroup">The resource group.</param>
    public void SetGraphicsResources(uint slot, GPUResourceGroup resourceGroup);

    /// <summary>
    /// Set the stencil reference.
    /// </summary>
    /// <param name="value">The stencil reference value.</param>
    public void SetStencilReference(uint value);


}

