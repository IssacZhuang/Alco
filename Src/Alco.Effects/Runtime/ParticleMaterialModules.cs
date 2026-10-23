using System.Threading;
using Alco.Rendering;

namespace Alco.Effects;

/// <summary>
/// The material modules registered with one particle system, serialized on the
/// owning system's shared gate so registration, publication and binding refresh are
/// atomic. The gate is reentrant, so a module may call back into its system from
/// <see cref="IParticleMaterialModule.ConfigureMaterial"/>.
/// </summary>
internal sealed class ParticleMaterialModules
{
    private readonly Lock _gate;
    private readonly List<IParticleMaterialModule> _modules = [];

    /// <summary>Creates the module list sharing the owning system's gate.</summary>
    /// <param name="gate">The owning system's shared, reentrant gate.</param>
    public ParticleMaterialModules(Lock gate)
    {
        _gate = gate;
    }

    /// <summary>Registers the module.</summary>
    /// <param name="module">The module to register.</param>
    /// <returns>The unregistration handle; dispose it to remove the module.</returns>
    public IDisposable Add(IParticleMaterialModule module)
    {
        lock (_gate)
        {
            _modules.Add(module);
        }
        return new Registration(this, module);
    }

    /// <summary>Applies every registered module to one material.</summary>
    /// <param name="material">The material to configure.</param>
    public void Apply(GraphicsMaterial material)
    {
        lock (_gate)
        {
            for (int i = 0; i < _modules.Count; i++)
            {
                _modules[i].ConfigureMaterial(material);
            }
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
        public void Dispose()
        {
            owner.Remove(module);
        }
    }
}
