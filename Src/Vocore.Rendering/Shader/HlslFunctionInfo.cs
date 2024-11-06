using System.Text;

namespace Vocore.Rendering;

public class HlslFunctionInfo
{
    public readonly List<string> Attributes = new List<string>();
    public string ReturnType { get; }
    public string Name { get; }
    public string Parameters { get; }

    public HlslFunctionInfo(string returnType, string name, string parameters)
    {
        ReturnType = returnType;
        Name = name;
        Parameters = parameters;
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