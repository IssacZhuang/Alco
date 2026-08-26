using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A stable reference to a shader library — one composable unit of shader code
/// (a pass template, a material surface, ...; today backed by a Slang module, but
/// the identity is compiler-agnostic and a future precompiled-IR backing changes
/// nothing for holders). Instances are interned by <see cref="ShaderSystem.GetLibrary"/>:
/// one instance per module name, so library references compare by identity.
/// <br/>The reference holds no compiler state of its own — only the
/// <see cref="ShaderSystem"/> that created it. Reflection queries
/// (<see cref="GetReflection"/>) route through that system, which re-resolves
/// the module by name on every use, so references stay valid across shader hot
/// reloads (a module-system session rebuild transparently re-resolves).
/// <br/>This is the composition-domain currency (templates, surfaces); for direct
/// module use take a <see cref="Shader"/> through <see cref="ShaderSystem.GetShader"/>.
/// </summary>
public sealed class ShaderLibrary
{
    private readonly ShaderSystem _owner;

    internal ShaderLibrary(string name, ShaderSystem owner)
    {
        Name = name;
        _owner = owner;
    }

    /// <summary>The module name — the library's durable identity (e.g. <c>pbr_standard</c>).</summary>
    public string Name { get; }

    /// <summary>
    /// The library's reflection — its declared uniform blocks (with attributes
    /// and members), texture slots and sampler slots; the library-domain
    /// counterpart of a <see cref="Shader"/>'s linked reflection. Reads through
    /// the owning shader system's per-module cache and re-resolves the module
    /// by name, so the result reflects the current module state even after a
    /// hot-reload session rebuild.
    /// </summary>
    /// <param name="defines">Optional preprocessor permutation of the module.</param>
    public ShaderLibraryReflection GetReflection(IReadOnlyList<string>? defines = null)
        => _owner.GetLibraryReflection(this, defines);

    /// <inheritdoc />
    public override string ToString() => Name;
}
