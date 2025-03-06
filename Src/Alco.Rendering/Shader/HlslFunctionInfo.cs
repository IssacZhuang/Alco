using System.Text;
using Alco.Graphics;

namespace Alco.Rendering;

public sealed class HlslFunctionInfo
{
    public const string AttributeVertex = @"[shader(""vertex"")]";
    public const string AttributeFragment = @"[shader(""fragment"")]";
    public const string AttributePixel = @"[shader(""pixel"")]";
    public const string AttributeCompute = @"[shader(""compute"")]";

    public IReadOnlyList<string> Attributes { get; }
    public string ReturnType { get; }
    public string Name { get; }
    public string Parameters { get; }
    public ShaderStage Stage { get; }

    public HlslFunctionInfo(
        string returnType,
        string name,
        string parameters,
        string[] attributes
        )
    {
        ReturnType = returnType;
        Name = name;
        Parameters = parameters;
        Attributes = attributes;

        for (int i = 0; i < attributes.Length; i++)
        {
            if (attributes[i] == AttributeVertex)
            {
                Stage = ShaderStage.Vertex;
                break;
            }

            if (attributes[i] == AttributeFragment ||
                attributes[i] == AttributePixel)
            {
                Stage = ShaderStage.Fragment;
                break;
            }

            if (attributes[i] == AttributeCompute)
            {
                Stage = ShaderStage.Compute;
                break;
            }
        }
    }

    public override string ToString()
    {
        StringBuilder builder = new StringBuilder();
        foreach (string attribute in Attributes)
        {
            builder.Append('[');
            builder.Append(attribute);
            builder.Append("] ");
        }

        builder.Append(ReturnType);
        builder.Append(' ');
        builder.Append(Name);
        builder.Append('(');
        builder.Append(Parameters);
        builder.Append(')');
        return builder.ToString();
    }
}