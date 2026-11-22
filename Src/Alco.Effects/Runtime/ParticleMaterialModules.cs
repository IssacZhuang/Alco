using System.Threading;
using Alco.Graphics;

namespace Alco.Effects;

/// <summary>
/// Thread-safe collection of <see cref="IParticleMaterialModule"/> registrations sharing the owning
/// particle system's reentrant gate so registration, publication, and binding refresh are atomic.
/// </summary>
internal sealed class ParticleMaterialModules
{
    private readonly Lock _gate;
    private readonly List<IParticleMaterialModule> _modules = [];

    /// <summary>Creates the module list sharing the owning system's gate.</summary>
    /// <param name="gate">The owning system's shared reentrant lock.</param>
    public ParticleMaterialModules(Lock gate)
    {
        _gate = gate;
    }

    /// <summary>Registers a module and returns a disposable unregistration handle.</summary>
    /// <param name="module">The module to register.</param>
    public IDisposable Add(IParticleMaterialModule module)
    {
        lock (_gate)
        {
            _modules.Add(module);
        }
        return new Registration(this, module);
    }

    /// <summary>Applies all registered modules onto <paramref name="material"/>.</summary>
    /// <param name="material">The target render material.</param>
    public void Apply(Material material)
    {
        lock (_gate)
        {
            for (int i = 0; i < _modules.Count; i++)
                _modules[i].ConfigureMaterial(material);
        }
    }

    private void Remove(IParticleMaterialModule module)
    {
        lock (_gate)
        {
            _modules.Remove(module);
        }
    }

    private sealed class Registration(ParticleMaterialModules owner, IParticleMaterialModule module) : IDisposable
    {
        public void Dispose() => owner.Remove(module);
    }
}
