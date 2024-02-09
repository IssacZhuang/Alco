using System.Text;

namespace Vocore.Graphics;


/// <summary>
///  The layout used for the shader reflection
/// </summary>
public struct BindGroupLayout
{
    public uint Group { get; init; }
    public BindGroupEntry[] Bindings { get; init; }

    public override string ToString()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine($"[Bind Group {Group}]");
        foreach (var bind in Bindings)
        {
            builder.AppendLine(bind.ToString());
        }

        return builder.ToString();
    }
}