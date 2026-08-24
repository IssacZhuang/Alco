using Alco.Graphics;

namespace Alco.Rendering;

// shader factory — module-backed only (plan §4.4): every shader is a compiled
// slang module program, supplied through the ShaderSystem or a composition
// owner (MaterialCompiler). The retired DXC text-mode entry points are gone.

public partial class RenderingSystem
{
    /// <summary>
    /// Create a shader handle whose modules are compiled through the slang module
    /// system: the compiler is called once per specialization, on demand. Variant
    /// axes are requested through the specialization arguments of the Shader's
    /// accessor methods (where the retired defines used to be).
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
