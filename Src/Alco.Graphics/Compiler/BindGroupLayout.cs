using System.Text;

namespace Alco.Graphics;


/// <summary>
/// One bind group (set) of a reflected program: its set index and the binding
/// entries it owns.
/// </summary>
public struct BindGroupLayout
{
    /// <summary>The set index this group occupies.</summary>
    public uint Group { get; init; }
    /// <summary>The binding entries of the set.</summary>
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

    /// <summary>
    /// Builds a bind-group descriptor from the reflected entries. The entries
    /// carry layout facts only (binding numbers, stages, types, names) — no real
    /// resources are attached; the caller binds those.
    /// </summary>
    /// <param name="name">The debug name of the descriptor.</param>
    /// <returns>The bind-group descriptor built from the reflected entries.</returns>
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