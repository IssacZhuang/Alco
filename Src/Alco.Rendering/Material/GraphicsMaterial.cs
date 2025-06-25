using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;


/// <summary>
/// The integration of the GPU pipeline state and shader resources. Also create buffer for uniform buffer by default.
/// The texture slot is white texture by default.
/// </summary>
public sealed class GraphicsMaterial : Material
{
    
    internal GraphicsMaterial(RenderingSystem system, Shader shader, string name) : base(system, shader, name)
    {
        
    }

    /// <inheritdoc/>
    public override void PushResources(GPUCommandBuffer.RenderPass renderPass)
    {
        if (StencilReference.HasValue)
        {
            renderPass.SetStencilReference(StencilReference.Value);
        }
        
        ReadOnlySpan<GPUResourceGroup?> resources = _parameters.ResourceGroups;
        for (uint i = 0; i < resources.Length; i++)
        {
            GPUResourceGroup? resource = resources[(int)i];
            if (resource != null)
            {
                renderPass.SetResources(i, resource);
            }else{
                throw new InvalidOperationException($"Null resource group at index {i}, {_parameters.ReflectionInfo.GetResourceName(i)} of shader {_shader.Name}");
            }
        }
    }

    public override void PushResources(GPURenderBundle renderBundle)
    {
        // the stencil value is dynamic state which is not supported in render bundle
        // if (StencilReference.HasValue)
        // {
        //     renderBundle.SetStencilReference(StencilReference.Value);
        // }
        
        ReadOnlySpan<GPUResourceGroup?> resources = _parameters.ResourceGroups;
        for (uint i = 0; i < resources.Length; i++)
        {
            GPUResourceGroup? resource = resources[(int)i];
            if (resource != null)
            {
                renderBundle.SetGraphicsResources(i, resource);
            }else{
                throw new InvalidOperationException($"Null resource group at index {i}, {_parameters.ReflectionInfo.GetResourceName(i)} of shader {_shader.Name}");
            }
        }
    }

    protected override void UpdateSlotResources(ShaderReflectionInfo reflectionInfo)
    {
        for (uint i = 0; i < reflectionInfo.BindGroups.Count; i++)
        {

            BindGroupLayout bindGroupLayout = reflectionInfo.BindGroups[(int)i];
            if (UtilsMaterial.IsTextureSamplerGroup(bindGroupLayout.Bindings))
            {
                if (!_parameters.TryGetTexture(i, out Texture2D? _) &&
                    !_parameters.TryGetRenderTexture(i, out RenderTexture? _))
                {
                    _parameters.SetTexture(i, _system.TextureWhite);
                }
            }
            else
            {
                //do nothing
            }
        }
    }

    protected override void Dispose(bool disposing)
    {

    }
}