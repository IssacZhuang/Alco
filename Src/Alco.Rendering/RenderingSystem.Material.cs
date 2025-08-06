using System.Diagnostics;
using Alco.Graphics;

namespace Alco.Rendering;

// material factory

public partial class RenderingSystem
{
    /// <summary>
    /// Creates a new graphics material with the specified shader and automatically sets up default properties.
    /// </summary>
    /// <param name="shader">The shader to use for the material. Cannot be null.</param>
    /// <param name="name">The name of the material for debugging purposes. Defaults to "unamed_material".</param>
    /// <returns>A new <see cref="GraphicsMaterial"/> instance with the specified shader and default buffers configured.</returns>
    /// <remarks>
    /// Default properties:
    /// _camera : camera matrix of <see cref="RenderingSystem.MainCamera"/> 
    /// _globalRenderData : global render data of <see cref="RenderingSystem.GlobalRenderDataBuffer"/>
    /// </remarks>
    public GraphicsMaterial CreateMaterial(Shader shader, string name = "unamed_material")
    {
        Debug.Assert(shader != null);
        GraphicsMaterial material = new GraphicsMaterial(this, shader, name);
        material.TrySetBuffer(ShaderResourceId.GlobalRenderData, _globalRenderData);
        material.TrySetBuffer(ShaderResourceId.Camera, _viewProjectionMatrix);
        return material;
    }
}