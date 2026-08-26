namespace Alco.Graphics;

/// <summary>
/// The scalar type of a uniform member's elements — the type identity the CPU
/// side marshals by (all 32-bit widths; the engine's uniform vocabulary admits
/// no 8/16/64-bit members).
/// </summary>
public enum ShaderUniformScalarType
{
    /// <summary>A <c>float</c> member (scalar, vector or matrix of floats).</summary>
    Float32 = 0,
    /// <summary>An <c>int</c> member.</summary>
    Int32 = 1,
    /// <summary>A <c>uint</c> member.</summary>
    UInt32 = 2,
    /// <summary>A <c>bool</c> member — 4 bytes on the GPU; the CPU marshals 0/1.</summary>
    Bool32 = 3,
}

/// <summary>
/// One member of a uniform block: name, byte offset, size, element type and
/// element count, in the block's declaration order. The shared parameter
/// vocabulary of both reflection views — <see cref="ShaderReflection"/> (the
/// linked program's blocks) and <see cref="ShaderLibraryReflection"/> (the
/// module's declared blocks) — the compiler layer only fills it in.
/// <br/><see cref="ComponentCount"/> is the component count of one element
/// (1 scalar, 2-4 vector, rows×columns matrix); <see cref="ElementCount"/>
/// is 1 for plain members and N for arrays.
/// </summary>
public readonly record struct ShaderUniformMember(
    string Name,
    uint OffsetBytes,
    uint SizeBytes,
    int ComponentCount,
    ShaderUniformScalarType ScalarType = ShaderUniformScalarType.Float32,
    uint ElementCount = 1);

/// <summary>
/// One uniform/parameter block, as the shared block vocabulary of both
/// reflection views: <see cref="ShaderReflection"/> reports the blocks that
/// survived linking (with their post-link member layouts, correlated to
/// bind-group entries by name), <see cref="ShaderLibraryReflection"/> reports
/// every block the module declares. Name-keyed, no set/binding numbers —
/// those exist only after linking and live on the bind-group side.
/// <see cref="Members"/> lists the members the uniform view can represent;
/// <see cref="UnsupportedMemberReason"/> reports why the rest, if any, do not
/// fit that view.
/// </summary>
public sealed class ShaderUniformBlock
{
    /// <summary>Creates a block view from reflection data.</summary>
    /// <param name="name">The block's declared name.</param>
    /// <param name="attributes">The user-defined attribute names on the block, in declaration order.</param>
    /// <param name="members">The uniform members, in declaration order.</param>
    /// <param name="unsupportedMemberReason">
    /// Why at least one member is absent from <paramref name="members"/> (an unsupported scalar width,
    /// a uniform-carrying struct, a nested array), or null when the listing is complete. Domain layers
    /// rethrow this when their contract requires a fully representable block.
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

    /// <summary>The uniform members (name, offset, size, type), in declaration order.</summary>
    public IReadOnlyList<ShaderUniformMember> Members { get; }

    /// <summary>Non-null when the block declares members the uniform view cannot represent; the reason names the first such member.</summary>
    public string? UnsupportedMemberReason { get; }
}
