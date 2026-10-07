using System.Text;

namespace Alco.Graphics;

/// <summary>
/// One binding slot of a reflected bind group: the binding descriptor it
/// reflects plus the reflected buffer size.
/// </summary>
public struct BindGroupEntryInfo
{
    /// <summary>The binding descriptor this slot reflects (number, stage, type, name).</summary>
    public BindGroupEntry Entry;
    /// <summary>The buffer size in bytes for uniform-buffer bindings; 0 for other entries.</summary>
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