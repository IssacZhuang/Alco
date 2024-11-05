using System.Diagnostics;
using Vocore.Graphics;

namespace Vocore.Rendering;

public partial class RenderingSystem
{
    public GraphicsMaterial CreateGraphicsMaterial(Shader shader, string name = "unamed")
    {
        Debug.Assert(shader != null);
        GraphicsMaterial material = new GraphicsMaterial(this, shader, name);
        return material;
    }
}