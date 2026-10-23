using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The state cache for a compute pipeline.
/// </summary>
public struct ComputePipelineContext
{
    public GPUPipeline? Pipeline;
    public ShaderReflection? ReflectionInfo;
    public uint Version;

    /// <summary>
    /// The specialization arguments the context was built for (set by the
    /// Shader.GetComputePipelineInfo call; TryUpdateComputePipelineContext keeps
    /// them) — the variant identity of the cached pipeline.
    /// </summary>
    public string[]? Specializations;

    /// <summary>
    /// A default context with no pipeline or reflection info set.
    /// </summary>
    public static readonly ComputePipelineContext Default = new ComputePipelineContext();

    /// <summary>
    /// Creates a context with no pipeline set.
    /// </summary>
    public ComputePipelineContext()
    {
        Version = 0;
    }

    /// <summary>
    /// Creates a context with the given shader reflection info.
    /// </summary>
    /// <param name="reflectionInfo">The shader reflection info.</param>
    public ComputePipelineContext(ShaderReflection reflectionInfo)
    {
        ReflectionInfo = reflectionInfo;
        Version = 0;
    }

    /// <summary>
    /// Tries to resolve the binding id of a shader resource by name.
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <param name="resourceId">The resolved resource id, or 0 when not found.</param>
    /// <returns><c>true</c> when the resource was found; otherwise <c>false</c>.</returns>
    public readonly bool TryGetResourceId(string name, out uint resourceId)
    {
        if (ReflectionInfo == null)
        {
            resourceId = 0;
            return false;
        }
        return ReflectionInfo.TryGetResourceId(name, out resourceId);
    }

    /// <summary>
    /// Resolves the binding id of a shader resource by name.
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <returns>The resolved resource id.</returns>
    /// <exception cref="KeyNotFoundException">No resource with the given name exists in the shader.</exception>
    /// <exception cref="Exception">The context has no reflection info.</exception>
    public readonly uint GetResourceId(string name)
    {
        if (ReflectionInfo == null)
        {
            throw new Exception("ReflectionInfo is null");
        }
        if (ReflectionInfo.TryGetResourceId(name, out uint resourceId))
        {
            return resourceId;
        }
        throw new KeyNotFoundException($"Resource '{name}' not found in shader");
    }
}
