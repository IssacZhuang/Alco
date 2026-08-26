using System.Collections.Frozen;
using System.Text;

namespace Alco.Graphics;

/// <summary>
/// The reflection of a linked shader program — its pipeline interface: bind
/// groups (set/binding-keyed), vertex input layouts, push constants, thread
/// group size and fragment output count — plus its uniform blocks in the
/// shared <see cref="ShaderUniformBlock"/> vocabulary (the linked survivors
/// with their post-link member layouts, correlated to bind-group entries by
/// name). Sibling of <see cref="ShaderLibraryReflection"/> (the module's
/// declared-blocks view of the same vocabulary); the two are deliberately
/// unrelated types — a linked program is not a library, and no consumer of
/// one shape can consume the other.
/// </summary>
public sealed class ShaderReflection
{
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
    /// The size of the push constants
    /// </summary>
    public int PushConstantsSize { get; }

    /// <summary>
    /// The number of color attachments the fragment stage writes to, i.e. the highest
    /// fragment output location plus one. Color targets at or beyond this index have no
    /// matching shader output and must be created with a zero write mask.
    /// </summary>
    public int FragmentOutputCount { get; }

    /// <summary>
    /// Thread group size for compute shader
    /// </summary>
    public ThreadGroupSize Size { get; }

    /// <summary>
    /// The uniform blocks that survived linking, in declaration order, with their
    /// post-link member layouts — the same <see cref="ShaderUniformBlock"/> vocabulary
    /// the library view uses. Correlated to <see cref="BindGroups"/> entries by block
    /// name; a pass that never consumes a block may strip its buffer from the layout,
    /// so this list is the linked truth, not the module's declaration set.
    /// </summary>
    public IReadOnlyList<ShaderUniformBlock> UniformBlocks { get; }

    public ShaderReflection(
        IReadOnlyList<VertexInputLayout> vertexLayouts,
        IReadOnlyList<BindGroupLayout> bindGroups,
        IReadOnlyList<PushConstantsRange> pushConstantsRanges,
        ThreadGroupSize size,
        int fragmentOutputCount = 0,
        IReadOnlyList<ShaderUniformBlock>? uniformBlocks = null)
    {
        VertexLayouts = vertexLayouts;
        BindGroups = bindGroups;
        PushConstantsRanges = pushConstantsRanges;
        Size = size;
        FragmentOutputCount = fragmentOutputCount;
        UniformBlocks = uniformBlocks ?? Array.Empty<ShaderUniformBlock>();

        int pushConstantsSize = 0;
        for (int i = 0; i < pushConstantsRanges.Count; i++)
        {
            pushConstantsSize = Math.Max(pushConstantsSize, (int)pushConstantsRanges[i].End);
        }
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
    /// Sampler entries are not counted.
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


    /// <summary>
    /// The members of the named uniform block of the linked program, in
    /// declaration order, at their post-link offsets. Empty when the program
    /// declares no such block; a block whose members do not fit the float
    /// view throws — the material parameter system writes floats only.
    /// </summary>
    public IReadOnlyList<ShaderUniformMember> GetUniformMembers(string blockName)
    {
        for (int i = 0; i < UniformBlocks.Count; i++)
        {
            ShaderUniformBlock block = UniformBlocks[i];
            if (block.Name != blockName)
            {
                continue;
            }
            if (block.UnsupportedMemberReason != null)
            {
                throw new NotSupportedException(block.UnsupportedMemberReason);
            }
            return block.Members;
        }
        return [];
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
    // a buffer or a texture. Samplers are reflected layout entries supplied by
    // the owning texture slot during bind-group assembly.
    private static bool IsSettableResource(BindGroupEntry entry)
    {
        switch (entry.Type)
        {
            case BindingType.UniformBuffer:
            case BindingType.StorageBuffer:
            case BindingType.Texture:
            case BindingType.StorageTexture:
                return true;
            default:
                return false;
        }
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
