using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The instance of the <see cref="Material"/> which used to override the parameters of the parent material.
/// </summary>
public sealed class MaterialInstance : Material
{
    private readonly Material _parent;

    public override GPUResourceGroup? this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _parameters.ResourceGroups[index] ?? _parent[index];
    }
    
    internal MaterialInstance(RenderingSystem system, Material parent) : base(system, parent.Shader, $"{parent.Name}_instance")
    {
        _parent = parent;
        _pipelineContext = new GraphicsPipelineContext(
            parent.ReflectionInfo,
            parent.DepthStencilState,
            parent.BlendState,
            parent.RasterizerState,
            parent.PrimitiveTopology,
            parent.Defines
            );
    }

    public override void PushResources(GPUCommandBuffer.RenderPass renderPass)
    {
        int length = ResourceGroupCount;

        for (uint i = 0; i < length; i++)
        {
            GPUResourceGroup? resourceGroup = this[(int)i];//parent resource already included
            if (resourceGroup != null)
            {
                renderPass.SetResources(i, resourceGroup);
                continue;
            }

            throw new InvalidOperationException($"Null resource group at index {i}, {_parameters.ReflectionInfo.GetResourceName(i)} of shader {_shader.Name}");
        }
    }

    public override void PushResources(GPURenderBundle renderBundle)
    {
        int length = ResourceGroupCount;
        for (uint i = 0; i < length; i++)
        {
            GPUResourceGroup? resourceGroup = this[(int)i];//parent resource already included
            if (resourceGroup != null)
            {
                renderBundle.SetGraphicsResources(i, resourceGroup);
                continue;
            }

            throw new InvalidOperationException($"Null resource group at index {i}, {_parameters.ReflectionInfo.GetResourceName(i)} of shader {_shader.Name}");
        }
    }

    protected override void UpdateSlotResources(ShaderReflectionInfo reflectionInfo) { }

    protected override void Dispose(bool disposing)
    {
        //do nothing
    }
}