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
        _parameters.FlushResourceGroups();
        ReadOnlySpan<GPUResourceGroup?> resources = _parameters.ResourceGroups;
        for (uint i = 0; i < resources.Length; i++)
        {
            GPUResourceGroup? resource = resources[(int)i];
            if (resource != null)
            {
                renderPass.SetResources(i, resource);
            }else{
                throw new InvalidOperationException($"Null resource group at index {i}, {_parameters.ReflectionInfo.BindGroups[(int)i].Bindings[0].Entry.Name} of shader {_shader.Name}");
            }
        }
    }

    public override void PushResources(GPURenderBundle renderBundle)
    {
        _parameters.FlushResourceGroups();
        ReadOnlySpan<GPUResourceGroup?> resources = _parameters.ResourceGroups;
        for (uint i = 0; i < resources.Length; i++)
        {
            GPUResourceGroup? resource = resources[(int)i];
            if (resource != null)
            {
                renderBundle.SetGraphicsResources(i, resource);
            }else{
                throw new InvalidOperationException($"Null resource group at index {i}, {_parameters.ReflectionInfo.BindGroups[(int)i].Bindings[0].Entry.Name} of shader {_shader.Name}");
            }
        }
    }

    protected override void UpdateSlotResources(ShaderReflectionInfo reflectionInfo)
    {
        for (uint i = 0; i < reflectionInfo.ResourceCount; i++)
        {
            // Sampled texture slots default to the white texture. Depth texture slots
            // (e.g. shadow map with comparison sampler) must be bound via
            // SetRenderTextureDepth; the white texture is not a valid depth binding.
            if (_parameters.NeedsDefaultTexture(i))
            {
                _parameters.SetTexture(i, _system.TextureWhite);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {

    }
}
