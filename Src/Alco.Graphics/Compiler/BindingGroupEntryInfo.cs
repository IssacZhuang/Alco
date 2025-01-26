using System.Text;

namespace Alco.Graphics;

public struct BindGroupEntryInfo
{
    public BindGroupEntry Entry;
    public uint Size;

    public override string ToString()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine($"[Binding Group Entry Info]");
        builder.AppendLine(Entry.ToString());
        builder.AppendLine($"Size: {Size}");

        return builder.ToString();
    }

    public static implicit operator BindGroupEntry(BindGroupEntryInfo info)
    {
        return info.Entry;
    }
}