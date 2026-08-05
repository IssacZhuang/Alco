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
        get
        {
            _parameters.FlushResourceGroups();
            // The assembly already resolves unbound values from the whole parent
            // chain by name, so the groups are complete on their own; a null group
            // here would be null for the parent as well.
            return _parameters.ResourceGroups[index];
        }
    }
    
    internal MaterialInstance(RenderingSystem system, Material parent) : base(system, parent.Shader, $"{parent.Name}_instance")
    {
        _parent = parent;
        _parameters.Fallback = parent.Parameters;
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
        _parameters.FlushResourceGroups();
        ReadOnlySpan<GPUResourceGroup?> resources = _parameters.ResourceGroups;

        for (uint i = 0; i < resources.Length; i++)
        {
            GPUResourceGroup? resourceGroup = resources[(int)i];//parent resource already included
            if (resourceGroup != null)
            {
                renderPass.SetResources(i, resourceGroup);
                continue;
            }

            throw new InvalidOperationException($"Null resource group at index {i}, {_parameters.ReflectionInfo.BindGroups[(int)i].Bindings[0].Entry.Name} of shader {_shader.Name}");
        }
    }

    public override void PushResources(GPURenderBundle renderBundle)
    {
        _parameters.FlushResourceGroups();
        ReadOnlySpan<GPUResourceGroup?> resources = _parameters.ResourceGroups;
        for (uint i = 0; i < resources.Length; i++)
        {
            GPUResourceGroup? resourceGroup = resources[(int)i];//parent resource already included
            if (resourceGroup != null)
            {
                renderBundle.SetGraphicsResources(i, resourceGroup);
                continue;
            }

            throw new InvalidOperationException($"Null resource group at index {i}, {_parameters.ReflectionInfo.BindGroups[(int)i].Bindings[0].Entry.Name} of shader {_shader.Name}");
        }
    }

    protected override void UpdateSlotResources(ShaderReflectionInfo reflectionInfo) { }

    protected override void Dispose(bool disposing)
    {
        //do nothing
    }
}