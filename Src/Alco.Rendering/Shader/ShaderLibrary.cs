using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A stable reference to a shader library — one composable unit of shader code
/// (a pass template, a material surface, ...). Instances are interned by
/// <see cref="ShaderSystem.GetLibrary"/>: one per module name, so library
/// references compare by identity.
/// <br/>Creation acquires the resource: the module is loaded and its reflection
/// materialized eagerly, so failures surface at creation, never at first use.
/// Hot reload refreshes the held reflection in place: identity survives a
/// session rebuild, and a broken edit keeps the last-known-good reflection
/// until the source compiles again.
/// </summary>
public sealed class ShaderLibrary
{
    internal ShaderLibrary(string name, ShaderLibraryReflection reflection)
    {
        Name = name;
        Reflection = reflection;
    }

    /// <summary>The module name — the library's durable identity (e.g. <c>pbr_standard</c>).</summary>
    public string Name { get; }

    /// <summary>
    /// The library's reflection — its declared uniform blocks (with attributes
    /// and members), texture slots and sampler slots; the library-domain
    /// counterpart of a <see cref="Shader"/>'s linked reflection. Materialized
    /// at creation and refreshed in place on hot-reload invalidation; reading it
    /// never compiles and never throws.
    /// </summary>
    public ShaderLibraryReflection Reflection { get; internal set; }

    /// <inheritdoc />
    public override string ToString() => Name;
}
