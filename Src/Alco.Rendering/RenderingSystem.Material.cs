using System.Diagnostics;
using Alco.Graphics;

namespace Alco.Rendering;

// material factory

public partial class RenderingSystem
{
    public GraphicsMaterial CreateMaterial(Shader shader, string name = "unamed")
    {
        Debug.Assert(shader != null);
        GraphicsMaterial material = new GraphicsMaterial(this, shader, name);
        material.TrySetBuffer(ShaderResourceId.GlobalRenderData, _globalRenderData);
        material.TrySetBuffer(ShaderResourceId.Camera, _viewProjectionMatrix);
        return material;
    }
}