using Alco.Graphics;

namespace Alco.Effects;

/// <summary>
/// Thread-safe collection of <see cref="IParticleMaterialModule"/> registrations synchronized by the owning particle system's material lock.
/// </summary>
internal sealed class ParticleMaterialModules(Lock syncLock)
{
    private readonly Lock _syncLock = syncLock ?? throw new ArgumentNullException(nameof(syncLock));
    private readonly List<IParticleMaterialModule> _modules = [];

    /// <summary>Registers a module and returns a disposable unregistration handle.</summary>
    /// <param name="module">The module to register.</param>
    public IDisposable Add(IParticleMaterialModule module)
    {
        using Lock.Scope _ = _syncLock.EnterScope();
        _modules.Add(module);
        return new Registration(this, module);
    }

    /// <summary>Applies all registered modules onto <paramref name="material"/>.</summary>
    /// <param name="material">The target render material.</param>
    public void Apply(Material material)
    {
        using Lock.Scope _ = _syncLock.EnterScope();
        for (int i = 0; i < _modules.Count; i++)
            _modules[i].ConfigureMaterial(material);
    }

    /// <summary>Clears all registered modules during teardown.</summary>
    public void Clear()
    {
        using Lock.Scope _ = _syncLock.EnterScope();
        _modules.Clear();
    }

    private void Remove(IParticleMaterialModule module)
    {
        using Lock.Scope _ = _syncLock.EnterScope();
        _modules.Remove(module);
    }

    private sealed class Registration(ParticleMaterialModules owner, IParticleMaterialModule module) : IDisposable
    {
        private ParticleMaterialModules? _owner = owner;
        private readonly IParticleMaterialModule _module = module;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Remove(_module);
    }
}
