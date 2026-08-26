namespace Alco.Graphics;

/// <summary>
/// One member of a uniform block: name, byte offset, size and float
/// component count (1 for scalars, up to N for vectors/matrices), in the
/// block's declaration order. The shared parameter vocabulary of both
/// reflection views — <see cref="ShaderReflection"/> (the linked program's
/// blocks) and <see cref="ShaderLibraryReflection"/> (the module's declared
/// blocks) — the compiler layer only fills it in.
/// </summary>
public readonly record struct ShaderUniformMember(
    string Name, uint OffsetBytes, uint SizeBytes, int FloatComponentCount);

/// <summary>
/// One uniform/parameter block, as the shared block vocabulary of both
/// reflection views: <see cref="ShaderReflection"/> reports the blocks that
/// survived linking (with their post-link member layouts, correlated to
/// bind-group entries by name), <see cref="ShaderLibraryReflection"/> reports
/// every block the module declares. Name-keyed, no set/binding numbers —
/// those exist only after linking and live on the bind-group side.
/// <see cref="Members"/> lists the float-shaped uniform members;
/// <see cref="UnsupportedMemberReason"/> reports why the rest, if any, do not
/// fit that view.
/// </summary>
public sealed class ShaderUniformBlock
{
    /// <summary>Creates a block view from reflection data.</summary>
    /// <param name="name">The block's declared name.</param>
    /// <param name="attributes">The user-defined attribute names on the block, in declaration order.</param>
    /// <param name="members">The float-shaped uniform members, in declaration order.</param>
    /// <param name="unsupportedMemberReason">
    /// Why at least one member is absent from <paramref name="members"/> (non-float member or a
    /// uniform-carrying struct), or null when the listing is complete. Domain layers rethrow this
    /// when their contract requires an all-float block.
    /// </param>
    public ShaderUniformBlock(
        string name,
        IReadOnlyList<string> attributes,
        IReadOnlyList<ShaderUniformMember> members,
        string? unsupportedMemberReason = null)
    {
        Name = name;
        Attributes = attributes;
        Members = members;
        UnsupportedMemberReason = unsupportedMemberReason;
    }

    /// <summary>The block's declared name.</summary>
    public string Name { get; }

    /// <summary>The user-defined attribute names on the block (e.g. <c>MaterialParams</c>), in declaration order.</summary>
    public IReadOnlyList<string> Attributes { get; }

    /// <summary>The float-shaped uniform members (name, offset, size, component count), in declaration order.</summary>
    public IReadOnlyList<ShaderUniformMember> Members { get; }

    /// <summary>Non-null when the block declares members the float view cannot represent; the reason names the first such member.</summary>
    public string? UnsupportedMemberReason { get; }
}
