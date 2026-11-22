using Alco.Graphics;

namespace Alco.Effects;

/// <summary>
/// Main-thread-only collection of <see cref="IParticleMaterialModule"/> registrations.
/// </summary>
internal sealed class ParticleMaterialModules
{
    private readonly List<IParticleMaterialModule> _modules = [];

    /// <summary>Registers a module and returns a disposable unregistration handle.</summary>
    /// <param name="module">The module to register.</param>
    public IDisposable Add(IParticleMaterialModule module)
    {
        _modules.Add(module);
        return new Registration(this, module);
    }

    /// <summary>Applies all registered modules onto <paramref name="material"/>.</summary>
    /// <param name="material">The target render material.</param>
    public void Apply(Material material)
    {
        for (int i = 0; i < _modules.Count; i++)
            _modules[i].ConfigureMaterial(material);
    }

    private void Remove(IParticleMaterialModule module)
    {
        _modules.Remove(module);
    }

    private sealed class Registration(ParticleMaterialModules owner, IParticleMaterialModule module) : IDisposable
    {
        public void Dispose() => owner.Remove(module);
    }
}
