namespace Alco.Rendering;

/// <summary>
/// A stable reference to a shader library — one composable unit of shader code
/// (a pass template, a material surface, ...; today backed by a Slang module, but
/// the identity is compiler-agnostic and a future precompiled-IR backing changes
/// nothing for holders). Instances are interned by <see cref="ShaderSystem.GetLibrary"/>:
/// one instance per module name, so library references compare by identity.
/// <br/>The reference carries no compiler state of its own — the underlying module is
/// resolved by name wherever it is used, so it stays valid across shader hot reloads
/// (a module-system session rebuild transparently re-resolves on the next use).
/// </summary>
public sealed class ShaderLibrary
{
    internal ShaderLibrary(string name)
    {
        Name = name;
    }

    /// <summary>The module name — the library's durable identity (e.g. <c>pbr_standard</c>).</summary>
    public string Name { get; }

    /// <inheritdoc />
    public override string ToString() => Name;
}
