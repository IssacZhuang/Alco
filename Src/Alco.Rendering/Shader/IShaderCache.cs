
using System.Diagnostics.CodeAnalysis;

namespace Alco.Rendering;

public interface IShaderCache
{
    public bool TryGetModules(string name, string shaderText, ReadOnlySpan<string> defines, [NotNullWhen(true)] out ShaderModulesInfo? modulesInfo);
}
