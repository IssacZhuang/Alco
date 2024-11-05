using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The high level encapsulation of GPU pipeline
/// </summary>
public class ShaderNew : AutoDisposable
{
    private readonly RenderingSystem _renderingSystem;
    private ShaderReflectionInfo _reflectionInfo;
    private FrozenDictionary<string, uint> _resourceIds = FrozenDictionary<string, uint>.Empty;

    public string Name { get; }

    internal ShaderNew(RenderingSystem renderingSystem, string name, ShaderReflectionInfo reflectionInfo)
    {
        _renderingSystem = renderingSystem;
        Name = name;
        _reflectionInfo = reflectionInfo;
        BuildResourceIndex();
    }

    /// <summary>
    /// Tries to get the resource ID associated with the given name.
    /// <br/> <c>thread safe.</c>
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <param name="resourceId">The resource ID if found, otherwise 0.</param>
    /// <returns>True if the resource sID was found, false otherwise.</returns>
    public bool TryGetResourceId(string name, out uint resourceId)
    {
        return _resourceIds.TryGetValue(name, out resourceId);
    }

    /// <summary>
    /// Gets the resource ID associated with the given name.
    /// <br/> <c>thread safe.</c>
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <throws>KeyNotFoundException if the resource is not found.</throws>
    /// <returns>The resource ID.</returns>
    public uint GetResourceId(string name)
    {
        if (_resourceIds.TryGetValue(name, out uint resourceId))
        {
            return resourceId;
        }
        throw new KeyNotFoundException($"Resource '{name}' not found in shader {Name}");
    }

    private void BuildResourceIndex()
    {
        Dictionary<string, uint> resourceIds = new Dictionary<string, uint>();
        resourceIds.Clear();
        for (uint i = 0; i < _reflectionInfo.BindGroups.Count; i++)
        {
            BindGroupLayout bindGroup = _reflectionInfo.BindGroups[(int)i];
            if (bindGroup.Bindings != null
            && bindGroup.Bindings.Count > 0)
            {
                resourceIds[bindGroup.Bindings[0].Entry.Name] = i;
            }
        }

        _resourceIds = resourceIds.ToFrozenDictionary();
    }

    protected override void Dispose(bool disposing)
    {

    }
}