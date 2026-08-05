using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Alco.Graphics;

/// <summary>
/// The reflection information for a shader
/// </summary>
public sealed class ShaderReflectionInfo
{
    /// <summary>
    /// The suffix that marks a storage buffer entry as the counter companion of the
    /// storage buffer with the same name minus the suffix. Counter companions are
    /// auto-bound from the owner buffer and are not settable resources.
    /// </summary>
    public const string CounterSuffix = "_counter";

    /// <summary>
    /// The prefix DXC gives the implicit counter buffer of a structured buffer in
    /// SPIR-V reflection (e.g. <c>counter.var._lights</c> for <c>_lights</c>).
    /// Counter companions are auto-bound from the owner buffer and are not settable
    /// resources.
    /// </summary>
    public const string CounterPrefix = "counter.var.";

    /// <summary>
    /// The suffix that pairs a sampler entry with the texture entry of the same
    /// name minus the suffix (e.g. `_albedoSampler` for `_albedo`).
    /// </summary>
    public const string SamplerSuffix = "Sampler";

    private FrozenDictionary<string, uint> _resourceIds = FrozenDictionary<string, uint>.Empty;
    private ShaderResourceLocation[] _resourceLocations = Array.Empty<ShaderResourceLocation>();
    private readonly string[] _idToName;

    /// <summary>
    /// The vertex input layouts for the shader
    /// </summary>
    public IReadOnlyList<VertexInputLayout> VertexLayouts { get;}
    /// <summary>
    /// The bind groups for the shader
    /// </summary>
    public IReadOnlyList<BindGroupLayout> BindGroups { get; }
    /// <summary>
    /// Push constants ranges
    /// </summary>
    public IReadOnlyList<PushConstantsRange> PushConstantsRanges { get; }
    /// <summary>
    /// The stage of the push constants
    /// </summary>
    public ShaderStage PushConstantsStages { get; }
    /// <summary>
    /// The size of the push constants
    /// </summary>
    public int PushConstantsSize { get; }

    /// <summary>
    /// Thread group size for compute shader
    /// </summary>
    public ThreadGroupSize Size { get; }

    public ShaderReflectionInfo(
        IReadOnlyList<VertexInputLayout> vertexLayouts,
        IReadOnlyList<BindGroupLayout> bindGroups,
        IReadOnlyList<PushConstantsRange> pushConstantsRanges,
        ThreadGroupSize size)
    {
        VertexLayouts = vertexLayouts;
        BindGroups = bindGroups;
        PushConstantsRanges = pushConstantsRanges;
        Size = size;

        ShaderStage stages = ShaderStage.None;
        int pushConstantsSize = 0;
        for (int i = 0; i < pushConstantsRanges.Count; i++)
        {
            stages |= pushConstantsRanges[i].Stage;
            pushConstantsSize = Math.Max(pushConstantsSize, (int)pushConstantsRanges[i].End);
        }
        PushConstantsStages = stages;
        PushConstantsSize = pushConstantsSize;

        BuildResourceIndex();

        _idToName = new string[_resourceLocations.Length];
        for (int i = 0; i < _resourceLocations.Length; i++)
        {
            _idToName[i] = _resourceLocations[i].Name;
        }
    }

    /// <summary>
    /// The number of settable resources (buffers and textures) of the shader.
    /// Sampler and counter companion entries are not counted.
    /// </summary>
    public int ResourceCount
    {
        get => _resourceLocations.Length;
    }

    /// <summary>
    /// The locations of the settable resources, indexed by resource ID.
    /// </summary>
    public IReadOnlyList<ShaderResourceLocation> ResourceLocations
    {
        get => _resourceLocations;
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
        throw new KeyNotFoundException($"Resource '{name}' not found in shader");
    }

    /// <summary>
    /// Get the resource name associated with the given shader resource ID.
    /// </summary>
    /// <param name="id">The shader resource ID.</param>
    /// <returns>The resource name.</returns>
    public string GetResourceName(uint id)
    {
        if (id < _idToName.Length)
        {
            return _idToName[id];
        }
        return $"Invalid Resource ID: {id}";
    }

    /// <summary>
    /// Tries to get the location of the resource associated with the given name.
    /// <br/> <c>thread safe.</c>
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <param name="location">The resource location if found, otherwise the default value.</param>
    /// <returns>True if the resource was found, false otherwise.</returns>
    public bool TryGetResourceLocation(string name, out ShaderResourceLocation location)
    {
        if (_resourceIds.TryGetValue(name, out uint resourceId))
        {
            location = _resourceLocations[resourceId];
            return true;
        }

        location = default;
        return false;
    }

    /// <summary>
    /// Gets the location of the resource associated with the given resource ID.
    /// <br/> <c>thread safe.</c>
    /// </summary>
    /// <param name="id">The resource ID.</param>
    /// <throws>ArgumentOutOfRangeException if the resource ID is out of range.</throws>
    /// <returns>The resource location.</returns>
    public ShaderResourceLocation GetResourceLocation(uint id)
    {
        if (id < _resourceLocations.Length)
        {
            return _resourceLocations[id];
        }
        throw new ArgumentOutOfRangeException(nameof(id), id, "The resource ID is out of range.");
    }

    /// <summary>
    /// Tries to get the resource name associated with the given shader resource ID.
    /// </summary>
    /// <param name="id">The shader resource ID.</param>
    /// <param name="name">The resource name if found, otherwise an empty string.</param>
    /// <returns>True if the resource name was found, false otherwise.</returns>
    public bool TryGetResourceName(uint id, out string name)

    {
        if (id < _idToName.Length)
        {
            name = _idToName[id];
            return true;
        }

        name = string.Empty;
        return false;
    }


    private void BuildResourceIndex()
    {
        Dictionary<string, uint> resourceIds = new Dictionary<string, uint>();
        List<ShaderResourceLocation> locations = new List<ShaderResourceLocation>();

        for (int groupIndex = 0; groupIndex < BindGroups.Count; groupIndex++)
        {
            IReadOnlyList<BindGroupEntryInfo> bindings = BindGroups[groupIndex].Bindings;
            if (bindings == null)
            {
                continue;
            }

            for (int entryIndex = 0; entryIndex < bindings.Count; entryIndex++)
            {
                BindGroupEntry entry = bindings[entryIndex].Entry;
                if (!IsSettableResource(entry))
                {
                    continue;
                }

                uint id = (uint)locations.Count;
                locations.Add(new ShaderResourceLocation
                {
                    GroupIndex = groupIndex,
                    EntryIndex = entryIndex,
                    Binding = entry.Binding,
                    Type = entry.Type,
                    Name = entry.Name
                });
                resourceIds[entry.Name] = id;
            }
        }

        _resourceIds = resourceIds.ToFrozenDictionary();
        _resourceLocations = locations.ToArray();
    }

    // A settable resource is a shader variable the material API binds by name or id:
    // a buffer or a texture. Samplers are companions of the texture with the same
    // name minus the sampler suffix; counter companions of storage buffers are
    // auto-bound from the owner buffer.
    private static bool IsSettableResource(BindGroupEntry entry)
    {
        switch (entry.Type)
        {
            case BindingType.UniformBuffer:
            case BindingType.Texture:
            case BindingType.StorageTexture:
                return true;
            case BindingType.StorageBuffer:
                return !IsCounterCompanion(entry, out _);
            default:
                return false;
        }
    }

    /// <summary>
    /// Whether the entry is the counter companion of a storage buffer, and if so the
    /// name of the owning buffer. DXC names the implicit counter of a structured
    /// buffer <c>counter.var.&lt;name&gt;</c>; an explicitly declared counter variable
    /// uses the <c>&lt;name&gt;_counter</c> suffix.
    /// </summary>
    /// <param name="entry">The bind group entry to check.</param>
    /// <param name="ownerName">The name of the owning storage buffer if the entry is a counter companion.</param>
    /// <returns>True if the entry is the counter companion of a storage buffer.</returns>
    public static bool IsCounterCompanion(BindGroupEntry entry, [NotNullWhen(true)] out string? ownerName)
    {
        if (entry.Type != BindingType.StorageBuffer)
        {
            ownerName = null;
            return false;
        }

        if (entry.Name.StartsWith(CounterPrefix, StringComparison.Ordinal))
        {
            ownerName = entry.Name.Substring(CounterPrefix.Length);
            return true;
        }

        if (entry.Name.EndsWith(CounterSuffix, StringComparison.Ordinal))
        {
            ownerName = entry.Name.Substring(0, entry.Name.Length - CounterSuffix.Length);
            return true;
        }

        ownerName = null;
        return false;
    }

    public override string ToString()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("[Shader Reflection Info]\n");
        builder.AppendLine("[Vertex]");

        if (VertexLayouts.Count == 0)
        {
            builder.AppendLine("No vertex layouts");
        }
        else
        {
            foreach (var layout in VertexLayouts)
            {
                builder.AppendLine(layout.ToString());
            }
        }


        builder.AppendLine("[Bind Groups]");
        if (BindGroups.Count == 0)
        {
            builder.AppendLine("No bind groups");
        }
        else
        {
            foreach (var bindGroup in BindGroups)
            {
                builder.AppendLine(bindGroup.ToString());
            }
        }
        // foreach (var bindGroup in BindGroups)
        // {
        //     builder.AppendLine(bindGroup.ToString());
        // }

        if (Size != ThreadGroupSize.Default)
        {
            builder.AppendLine(Size.ToString());
        }

        builder.AppendLine("[Push Constants]");

        if (PushConstantsRanges.Count == 0)
        {
            builder.AppendLine("No push constants");
        }
        else
        {
            foreach (var range in PushConstantsRanges)
            {
                builder.AppendLine(range.ToString());
            }
        }

        return builder.ToString();
    }
}