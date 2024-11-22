using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;


/// <summary>
/// The integration of the GPU pipeline state and shader resources.
/// </summary>
public sealed class GraphicsMaterial : BaseMaterial
{
    private readonly Shader _shader;
    private bool _isPipelineDirty = true;
    private GraphicsPipelineContext _pipelineContext;

    /// <summary>
    /// The depth stencil state of the shader pipeline.
    /// </summary>
    public DepthStencilState DepthStencilState
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipelineContext.DepthStencil;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _pipelineContext.DepthStencil = value;
            _isPipelineDirty = true;
        }
    }

    /// <summary>
    /// The blend state of the shader pipeline.
    /// </summary>
    public BlendState BlendState
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipelineContext.BlendState;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _pipelineContext.BlendState = value;
            _isPipelineDirty = true;
        }
    }

    /// <summary>
    /// The rasterizer state of the shader pipeline.
    /// </summary>
    public RasterizerState RasterizerState
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipelineContext.Rasterizer;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _pipelineContext.Rasterizer = value;
            _isPipelineDirty = true;
        }
    }

    /// <summary>
    /// The primitive topology of the shader pipeline.
    /// </summary>
    public PrimitiveTopology PrimitiveTopology
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipelineContext.PrimitiveTopology;
        set
        {
            _pipelineContext.PrimitiveTopology = value;
            _isPipelineDirty = true;
        }
    }

    /// <summary>
    /// The shader of the material.
    /// </summary>
    public Shader Shader
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _shader;
    }


    /// <summary>
    /// The stencil reference value which used in <see cref="GPUCommandBuffer.SetStencilReference"/>.
    /// </summary>
    public uint? StencilReference;


    public ReadOnlySpan<GPUResourceGroup?> ResourceGroups
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _parameters.ResourceGroups;
    }

    internal GraphicsMaterial(RenderingSystem system, Shader shader, string name) : base(system, shader.GetShaderModules().ReflectionInfo, name)
    {
        _shader = shader;
        _pipelineContext = GraphicsPipelineContext.Default;
        _pipelineContext.ReflectionInfo = shader.GetShaderModules().ReflectionInfo;
    }


    /// <summary>
    /// Get the shader pipeline.
    /// </summary>
    /// <param name="renderPass">The render pass.</param>
    /// <returns>The shader pipeline.</returns>
    public override GPUPipeline GetPipeline(GPURenderPass renderPass)
    {
        if (_shader.TryUpdatePipelineContext(ref _pipelineContext, renderPass, _isPipelineDirty))
        {
            UpdateSlotResources(_pipelineContext.ReflectionInfo!);
            _isPipelineDirty = false;
        }

        return _pipelineContext.Pipeline!;
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


}