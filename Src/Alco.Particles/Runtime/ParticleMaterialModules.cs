using System.Threading;
using Alco.Rendering;

namespace Alco.Particles;

/// <summary>
/// The material modules registered with one particle system (see
/// <see cref="IParticleMaterialModule"/>): a list serialized on the owning
/// system's shared gate, so module registration, material publication and the
/// binding refresh are atomic against each other. The gate is reentrant, so a
/// module may safely call back into its system from
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
