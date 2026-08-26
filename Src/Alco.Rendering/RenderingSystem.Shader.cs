using Alco.Graphics;

namespace Alco.Rendering;

// shader factory — module-backed only: every shader is a compiled slang module
// program, supplied through the ShaderSystem or a composition owner (MaterialCompiler).

public partial class RenderingSystem
{
    /// <summary>
    /// Create a shader handle whose modules are compiled through the slang module
    /// system: the compiler is called once per specialization, on demand. Variant
    /// axes are requested via specialization arguments of the accessor methods.
    /// </summary>
    /// <param name="name">The name of the shader (for debugging and profiling).</param>
    /// <param name="compileModules">Produces the compiled modules of one specialization.</param>
    /// <param name="customVertexLayouts">Optional vertex layout override (e.g. ImGui's packed vertex color).</param>
    public Shader CreateShader(string name, Func<string[], ShaderModulesInfo> compileModules,
        IReadOnlyList<VertexInputLayout>? customVertexLayouts = null)
    {
        return new Shader(this, name, compileModules, customVertexLayouts);
    }
}
