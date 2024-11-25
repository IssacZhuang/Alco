using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The instance of the <see cref="GraphicsMaterial"/> which used to override the parameters of the parent material.
/// </summary>
public sealed class GraphicsMaterialInstance : BaseMaterial
{
    private readonly GraphicsMaterial _parent;

    internal GraphicsMaterialInstance(GraphicsMaterial parent, RenderingSystem system, string name) : base(system, parent.Shader, name)
    {
        _parent = parent;
    }

    protected override void SetPipelineResources(MaterialCommandContext context)
    {
        if (StencilReference.HasValue)
        {
            context.SetStencilReference(StencilReference.Value);
        }
        else if (_parent.StencilReference.HasValue)
        {
            context.SetStencilReference(_parent.StencilReference.Value);
        }

        ReadOnlySpan<GPUResourceGroup?> parentResourceGroups = _parent.ResourceGroups;
        ReadOnlySpan<GPUResourceGroup?> resourceGroups = _parameters.ResourceGroups;

        for (uint i = 0; i < resourceGroups.Length; i++)
        {
            GPUResourceGroup? resourceGroup = resourceGroups[(int)i];
            GPUResourceGroup? parentResourceGroup = parentResourceGroups[(int)i];

            if (resourceGroup != null)
            {
                context.SetGraphicsResources(i, resourceGroup);
            }
            else if (parentResourceGroup != null)
            {
                context.SetGraphicsResources(i, parentResourceGroup);
            }
            else
            {
                throw new Exception($"The bind group {i} has no resources. The resources must be setted on the parent material or the material instance.");
            }
        }
    }

    protected override void UpdateSlotResources(ShaderReflectionInfo reflectionInfo)
    {
        // Do nothing.
    }
}