using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

public struct ComputePipelineContext
{
    public GPUPipeline? Pipeline;
    public ShaderReflectionInfo? ReflectionInfo;
    public uint Version;

    /// <summary>
    /// The specialization arguments the context was built for (set by the
    /// Shader.GetComputePipelineInfo call; TryUpdateComputePipelineContext keeps
    /// them) — the variant identity of the cached pipeline.
    /// </summary>
    public string[]? Specializations;

    public static readonly ComputePipelineContext Default = new ComputePipelineContext();

    public ComputePipelineContext()
    {
        Version = 0;
    }

    public ComputePipelineContext(ShaderReflectionInfo reflectionInfo)
    {
        ReflectionInfo = reflectionInfo;
        Version = 0;
    }

    /// <summary>
    /// Tries to get the resource ID associated with the given name.
    /// <br/> <c>thread safe.</c>
    /// </summary>
    /// <param name="name">The name of the resource.</param>

    /// <param name="resourceId">The resource ID if found, otherwise 0.</param>
    /// <returns>True if the resource sID was found, false otherwise.</returns>
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
    /// Gets the resource ID associated with the given name.
    /// <br/> <c>thread safe.</c>
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <throws>KeyNotFoundException if the resource is not found.</throws>
    /// <returns>The resource ID.</returns>
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
