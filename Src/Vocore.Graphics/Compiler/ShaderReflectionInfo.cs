using System.Text;

namespace Vocore.Graphics;

/// <summary>
/// The reflection information for a shader
/// </summary>
public struct ShaderReflectionInfo
{
    /// <summary>
    /// The vertex input layouts for the shader
    /// </summary>
    public IReadOnlyList<VertexInputLayout> VertexLayouts;
    /// <summary>
    /// The bind groups for the shader
    /// </summary>
    public IReadOnlyList<BindGroupLayout> BindGroups;
    /// <summary>
    /// Push constants ranges
    /// </summary>
    public IReadOnlyList<PushConstantsRange> PushConstantsRanges;
    /// <summary>
    /// Thread group size for compute shader
    /// </summary>
    public ThreadGroupSize Size;

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