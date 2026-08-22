using Alco.Graphics;

namespace Alco.Rendering;

// shader factory — module-backed only (plan §4.4): every shader is a compiled
// slang module program, supplied through the ShaderSystem or a composition
// owner (MaterialCompiler). The retired DXC text-mode entry points are gone.

public partial class RenderingSystem
{
    /// <summary>
    /// Create a shader whose modules are compiled through the slang module
    /// system: the compiler is called once per compatibility permutation on demand
    /// and returns modules with reflection.
    /// </summary>
    /// <param name="name">The name of the shader (for debugging and profiling).</param>
    /// <param name="compileModules">Produces the compiled modules for one set of defines.</param>
    /// <param name="customVertexLayouts">Optional vertex layout override (e.g. ImGui's packed vertex color).</param>
    /// <param name="permutationSource">Optional module source whose #if blocks drive TestAllDefines.</param>
    public Shader CreateShader(string name, Func<string[], ShaderModulesInfo> compileModules,
        IReadOnlyList<VertexInputLayout>? customVertexLayouts = null, string? permutationSource = null)
    {
        return new Shader(this, name, compileModules, customVertexLayouts, permutationSource);
    }
}
