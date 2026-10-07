namespace Alco.Editor.Extensibility;

/// <summary>
/// An interface-keyed service bag shared by editor modules. A later registration
/// replaces an earlier one for the same service type, so modules registered after the
/// built-in module can override its defaults.
/// </summary>
public sealed class EditorServices
{
    private readonly Dictionary<Type, object> _services = new();

    /// <summary>Registers (or replaces) the service for type <typeparamref name="T"/>.</summary>
    /// <param name="instance">The service instance; must not be null.</param>
    public void Register<T>(T instance) where T : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        _services[typeof(T)] = instance;
    }

    /// <summary>
    /// Returns the last service registered for type <typeparamref name="T"/>.
    /// Throws when nothing was registered — the built-in module registers all default
    /// services first, so a throw means a module consumed a service it did not register
    /// or removed.
    /// </summary>
    /// <exception cref="InvalidOperationException">No service of type <typeparamref name="T"/> is registered.</exception>
    public T Get<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out object? service))
        {
            return (T)service;
        }
        throw new InvalidOperationException($"No editor service of type '{typeof(T).FullName}' is registered.");
    }
}
