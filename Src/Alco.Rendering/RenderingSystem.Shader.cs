using Alco.Graphics;

namespace Alco.Rendering;

// shader factory

public partial class RenderingSystem
{
    public Shader CreateShader(string shaderText, string name)
    {
        return new Shader(this, shaderText, name);
    }

    public Shader CreateShader(string shaderText, string name, IReadOnlyList<VertexInputLayout>? customVertexLayouts = null, IReadOnlyList<BindGroupLayout>? customBindGroups = null)
    {
        return new Shader(this, shaderText, name, customVertexLayouts, customBindGroups);
    }
}