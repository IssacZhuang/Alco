using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The instance of the <see cref="GraphicsMaterial"/> which used to override the resources of the parent material.
/// </summary>
public class GraphicsMaterialInstance : BaseMaterial
{
    private readonly GraphicsMaterial _parent;
    private readonly Shader _shader;

    internal GraphicsMaterialInstance(GraphicsMaterial parent, RenderingSystem system, string name) : base(system, parent.Shader.GetShaderModules().ReflectionInfo, name)
    {
        _parent = parent;
        _shader = parent.Shader;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override GPUPipeline GetPipeline(GPURenderPass renderPass)
    {
        return _parent.GetPipeline(renderPass);
    }

    protected override void SetPipelineResources(MaterialCommandContext context)
    {
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
}