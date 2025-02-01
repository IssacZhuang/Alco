using System.Collections.Frozen;
using System.Text;

namespace Alco.Graphics;

/// <summary>
/// The reflection information for a shader
/// </summary>
public class ShaderReflectionInfo
{
    private FrozenDictionary<string, uint> _resourceIds = FrozenDictionary<string, uint>.Empty;
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

        _idToName = new string[BindGroups.Count];
        for (int i = 0; i < BindGroups.Count; i++)
        {
            _idToName[i] = BindGroups[i].Bindings[0].Entry.Name;
        }
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
        return _idToName[id];
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
        resourceIds.Clear();
        for (uint i = 0; i < BindGroups.Count; i++)
        {
            BindGroupLayout bindGroup = BindGroups[(int)i];
            if (bindGroup.Bindings != null
            && bindGroup.Bindings.Count > 0)
            {
                resourceIds[bindGroup.Bindings[0].Entry.Name] = i;
            }
        }

        _resourceIds = resourceIds.ToFrozenDictionary();
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