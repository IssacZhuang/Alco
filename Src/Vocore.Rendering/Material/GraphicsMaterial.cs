using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;


/// <summary>
/// The integration of the GPU pipeline state and shader resources.
/// </summary>
public sealed class GraphicsMaterial : BaseMaterial
{
    
    public ReadOnlySpan<GPUResourceGroup?> ResourceGroups
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _parameters.ResourceGroups;
    }

    internal GraphicsMaterial(RenderingSystem system, Shader shader, string name) : base(system, shader, name)
    {
        
    }

    /// <summary>
    /// Set resources of the shader pipeline.
    /// </summary>
    /// <param name="context">The material command context.</param>
    protected override void SetPipelineResources(MaterialCommandContext context)
    {
        if (StencilReference.HasValue)
        {
            context.SetStencilReference(StencilReference.Value);
        }
        
        ReadOnlySpan<GPUResourceGroup?> resources = _parameters.ResourceGroups;
        for (uint i = 0; i < resources.Length; i++)
        {
            if (resources[(int)i] != null)
            {
                context.SetGraphicsResources(i, resources[(int)i]!);
            }else{
                throw new InvalidOperationException($"Resource group {i} is null");
            }
        }
    }

    protected override void UpdateSlotResources(ShaderReflectionInfo reflectionInfo)
    {
        for (uint i = 0; i < reflectionInfo.BindGroups.Count; i++)
        {

            BindGroupLayout bindGroupLayout = reflectionInfo.BindGroups[(int)i];
            if (UtilsMaterial.IsUniformBufferGroup(bindGroupLayout.Bindings))
            {
                BindGroupEntryInfo info = bindGroupLayout.Bindings[0];
                if (!_parameters.TryGetBuffer(i, out GraphicsBuffer? buffer))
                {

                    buffer = _system.CreateGraphicsBuffer(
                        info.Size,
                        $"Material_{Name}_Buffer_{info.Entry.Name}"
                    );
                    _parameters.SetBuffer(i, buffer);
                    _managedResources.Add(buffer);
                }
                else if (buffer.Size < info.Size)
                {
                    buffer.Dispose();
                    buffer = _system.CreateGraphicsBuffer(
                        info.Size,
                        $"Material_{Name}_Buffer_{info.Entry.Name}"
                    );
                    _parameters.SetBuffer(i, buffer);
                    _managedResources.Add(buffer);
                }
            }
            else if (UtilsMaterial.IsTextureSamplerGroup(bindGroupLayout.Bindings))
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
}