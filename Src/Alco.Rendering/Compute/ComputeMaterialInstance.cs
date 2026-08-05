using Alco.Graphics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

/// <summary>
/// The instance of the <see cref="ComputeMaterial"/> which used to override the parameters of the parent compute dispatcher.
/// This allows for creating variations of a compute dispatcher while sharing resources with the parent.
/// </summary>
public sealed class ComputeMaterialInstance : ComputeMaterial
{
    private readonly ComputeMaterial _parent;

    /// <summary>
    /// Gets the resource group at the specified index. Values not bound on this
    /// instance are resolved from the parent chain when the groups are assembled,
    /// so the groups are complete on their own.
    /// </summary>
    /// <param name="index">The index of the resource group.</param>
    /// <returns>The resource group at the specified index.</returns>
    public override GPUResourceGroup? this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            _parameterSet.FlushResourceGroups();
            return _parameterSet.ResourceGroups[index];
        }
    }

    internal ComputeMaterialInstance(RenderingSystem system, ComputeMaterial parent) : base(system, parent.Shader)
    {
        _parent = parent;
        _parameterSet.Fallback = parent.ParameterSet;
        _pipelineContext = new ComputePipelineContext(
            parent.ReflectionInfo,
            parent.Defines
            );
    }
}
