using Alco.Graphics;

namespace Alco.Rendering;

public partial class RenderingSystem
{
    public Shader CreateShader(string shaderText, string name)
    {
        return new Shader(this, shaderText, name);
    }

    public Shader CreateShader(string shaderText, string name, VertexInputLayout[] customVertexLayouts)
    {
        return new Shader(this, shaderText, name, customVertexLayouts);
    }
}