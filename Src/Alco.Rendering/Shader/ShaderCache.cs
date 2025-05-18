
using System.Diagnostics.CodeAnalysis;

namespace Alco.Rendering;

public class ShaderCache : IShaderCache
{
    public ShaderCache(string directory)
    {

    }

    public void AddOrUpdate(string name, string shaderText, ReadOnlySpan<string> defines, ShaderModulesInfo modulesInfo)
    {
        throw new NotImplementedException();
    }

    public bool TryGetModules(string name, string shaderText, ReadOnlySpan<string> defines, [NotNullWhen(true)] out ShaderModulesInfo? modulesInfo)
    {
        throw new NotImplementedException();
    }
}