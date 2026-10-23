using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A stable, interned reference to a shader library, one composable unit of shader code.
/// The module is loaded and reflected eagerly, and hot reload refreshes the reflection in place.
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
    /// The library's reflection: its uniform blocks, texture slots, and sampler slots.
    /// Refreshed in place on hot reload.
    /// </summary>
    public ShaderLibraryReflection Reflection { get; internal set; }

    /// <inheritdoc />
    public override string ToString() => Name;
}
