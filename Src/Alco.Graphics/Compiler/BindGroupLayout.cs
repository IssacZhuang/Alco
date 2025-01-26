using System.Text;

namespace Alco.Graphics;


/// <summary>
///  The layout used for the shader reflection
/// </summary>
public struct BindGroupLayout
{
    public uint Group { get; init; }
    public IReadOnlyList<BindGroupEntryInfo> Bindings { get; init; }

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

    public BindGroupDescriptor ToDescriptor(string name = "unnamed_bind_group")
    {
        BindGroupEntry[] entries = new BindGroupEntry[Bindings.Count];
        for (int i = 0; i < Bindings.Count; i++)
        {
            entries[i] = Bindings[i];
        }
        return new BindGroupDescriptor(entries, name);
    }
}