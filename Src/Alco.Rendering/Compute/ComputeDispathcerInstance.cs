using Alco.Graphics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

/// <summary>
/// The instance of the <see cref="ComputeDispatcher"/> which used to override the parameters of the parent compute dispatcher.
/// This allows for creating variations of a compute dispatcher while sharing resources with the parent.
/// </summary>
public sealed class ComputeDispatcherInstance : ComputeDispatcher
{
    private readonly ComputeDispatcher _parent;

    /// <summary>
    /// Gets the resource group at the specified index. If the resource group is not set in this instance,
    /// returns the resource group from the parent compute dispatcher.
    /// </summary>
    /// <param name="index">The index of the resource group.</param>
    /// <returns>The resource group at the specified index, or the parent's resource group if not set in this instance.</returns>
    public override GPUResourceGroup? this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _parameterSet.ResourceGroups[index] ?? _parent[index];
    }

    internal ComputeDispatcherInstance(RenderingSystem system, ComputeDispatcher parent) : base(system, parent.Shader)
    {
        _parent = parent;
    }
}

