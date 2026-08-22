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

    /// <summary>
    /// Create a shader whose modules are compiled by an external compiler (e.g. World3D's
    /// Slang path): the provider is called once per defines permutation on demand and
    /// returns modules with reflection. The engine's HLSL text pipeline is bypassed.
    /// </summary>
    /// <param name="name">The name of the shader (for debugging and profiling).</param>
    /// <param name="compileModules">Produces the compiled modules for one set of defines.</param>
    public Shader CreateShader(string name, Func<string[], ShaderModulesInfo> compileModules)
    {
        return new Shader(this, name, compileModules);
    }
}