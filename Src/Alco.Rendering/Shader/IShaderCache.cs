using System.Diagnostics.CodeAnalysis;

namespace Alco.Rendering;

/// <summary>
/// Interface for shader caching functionality to improve shader compilation performance.
/// </summary>
public interface IShaderCache
{
    /// <summary>
    /// Attempts to retrieve compiled shader modules from the cache.
    /// </summary>
    /// <param name="path">The path to the shader file.</param>
    /// <param name="shaderText">The source text of the shader.</param>
    /// <param name="defines">Compilation defines used for the shader.</param>
    /// <param name="modulesInfo">When this method returns, contains the shader modules information if found in the cache; otherwise, null.</param>
    /// <returns>True if the shader modules were found in the cache; otherwise, false.</returns>
    public bool TryGetModules(string path, string shaderText, ReadOnlySpan<string> defines, [NotNullWhen(true)] out ShaderModulesInfo? modulesInfo);

    /// <summary>
    /// Adds or updates the shader modules in the cache.
    /// </summary>
    /// <param name="path">The path to the shader file.</param>
    /// <param name="shaderText">The source text of the shader.</param>
    /// <param name="defines">Compilation defines used for the shader.</param>
    /// <param name="modulesInfo">The shader modules information to cache.</param>
    public void AddOrUpdate(string path, string shaderText, ReadOnlySpan<string> defines, ShaderModulesInfo modulesInfo);
}
