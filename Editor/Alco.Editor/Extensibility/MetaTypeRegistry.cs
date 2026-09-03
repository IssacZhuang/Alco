using Alco.Rendering;

namespace Alco.Editor.Extensibility;

/// <summary>
/// The <see cref="Meta"/> subclasses the meta hot reloader swallows. Built-in and
/// game modules register their meta types here instead of editing the reloader.
/// </summary>
public sealed class MetaTypeRegistry
{
    private readonly List<Type> _types = new();

    /// <summary>The registered meta types, in registration order.</summary>
    public IReadOnlyList<Type> Types => _types;

    /// <summary>Registers a meta type.</summary>
    public void Register<T>() where T : Meta
    {
        _types.Add(typeof(T));
    }
}
