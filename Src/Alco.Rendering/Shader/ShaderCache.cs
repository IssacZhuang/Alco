
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using System.Text;

namespace Alco.Rendering;

public class ShaderCache : IShaderCache
{
    private class CacheItem
    {

    }

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

    private static ulong GetHash(string shaderText)
    {
        var bytes = Encoding.UTF8.GetBytes(shaderText);
        var hash = XxHash64.HashToUInt64(bytes);
        return hash;
    }
}