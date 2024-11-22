using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;
//todo: opt exception message
public sealed class GraphicsMaterial : Material
{

    private readonly RenderingSystem _system;
    private readonly ShaderParameterSet _parameters;
    private readonly Shader _shader;

    private bool _isPipelineDirty = true;

    private GraphicsPipelineContext _pipelineContext;

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

    public Shader Shader
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _shader;
    }

    public string Name { get; } 

    public uint? StencilReference;

    internal GraphicsMaterial(RenderingSystem system, Shader shader, string name)
    {
        _system = system;
        _shader = shader;
        Name = name;

        _pipelineContext = GraphicsPipelineContext.Default;
        _pipelineContext.ReflectionInfo = shader.GetShaderModules().ReflectionInfo;
        _parameters = new ShaderParameterSet(_pipelineContext.ReflectionInfo);
        UpdateSlotResources();
    }

    #region  Set value

    public unsafe bool TrySetValue<T>(string name, T value) where T : unmanaged
    {
        if (_parameters.TryGetBuffer(name, out GraphicsBuffer? buffer))
        {
            if (buffer.Size < sizeof(T))
            {
                return false;
            }

            buffer.UpdateBuffer(value);
            return true;
        }

        return false;
    }

    public unsafe bool TrySetValue<T>(uint id, T value) where T : unmanaged
    {
        if (_parameters.TryGetBuffer(id, out GraphicsBuffer? buffer))
        {
            if (buffer.Size < sizeof(T))
            {
                return false;
            }

            buffer.UpdateBuffer(value);
            return true;
        }

        return false;
    }

    public unsafe void SetValue<T>(string name, T value) where T : unmanaged
    {
        if (!_parameters.TryGetBuffer(name, out GraphicsBuffer? buffer))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        if (buffer.Size < sizeof(T))
        {
            throw new InvalidOperationException($"The buffer size is not enough for the value.");
        }

        buffer.UpdateBuffer(value);
    }

    public unsafe void SetValue<T>(uint id, T value) where T : unmanaged
    {
        if (!_parameters.TryGetBuffer(id, out GraphicsBuffer? buffer))
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        if (buffer.Size < sizeof(T))
        {
            throw new InvalidOperationException($"The buffer size is not enough for the value.");
        }

        buffer.UpdateBuffer(value);
    }

    #endregion

    #region Set texture

    public bool TrySetTexture(string name, Texture2D texture)
    {
        return _parameters.TrySetTexture(name, texture);
    }

    public bool TrySetTexture(uint id, Texture2D texture)
    {
        return _parameters.TrySetTexture(id, texture);
    }

    public void SetTexture(string name, Texture2D texture)
    {
        _parameters.SetTexture(name, texture);
    }

    public void SetTexture(uint id, Texture2D texture)
    {
        _parameters.SetTexture(id, texture);
    }


    #endregion

    #region Set render texture

    public bool TrySetRenderTexture(string name, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        return _parameters.TrySetRenderTexture(name, renderTexture, renderTextureIndex);
    }

    public bool TrySetRenderTexture(uint id, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        return _parameters.TrySetRenderTexture(id, renderTexture, renderTextureIndex);
    }

    public void SetRenderTexture(string name, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        _parameters.SetRenderTexture(name, renderTexture, renderTextureIndex);
    }

    public void SetRenderTexture(uint id, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        _parameters.SetRenderTexture(id, renderTexture, renderTextureIndex);
    }

    public bool TrySetRenderTextureDepth(string name, RenderTexture renderTexture)
    {
        return _parameters.TrySetRenderTextureDepth(name, renderTexture);
    }

    public bool TrySetRenderTextureDepth(uint id, RenderTexture renderTexture)
    {
        return _parameters.TrySetRenderTextureDepth(id, renderTexture);
    }

    public void SetRenderTextureDepth(string name, RenderTexture renderTexture)
    {
        _parameters.SetRenderTextureDepth(name, renderTexture);
    }

    public void SetRenderTextureDepth(uint id, RenderTexture renderTexture)
    {
        _parameters.SetRenderTextureDepth(id, renderTexture);
    }

    #endregion


    public override GPUPipeline GetPipeline(GPURenderPass renderPass)
    {
        if (_shader.TryUpdatePipelineContext(ref _pipelineContext, renderPass, _isPipelineDirty))
        {
            UpdateSlotResources();
            _isPipelineDirty = false;
        }

        return _pipelineContext.Pipeline!;
    }

    protected override void SetPipelineResources(MaterialCommandContext context)
    {
        if (StencilReference.HasValue)
        {
            context.SetStencilReference(StencilReference.Value);
        }

        // for (uint i = 0; i < _slots.Length; i++)
        // {
        //     int index = (int)i;
        //     Slot slot = _slots[index];
        //     if (slot.resourceGroup == null)
        //     {
        //         continue;
        //     }

        //     context.SetGraphicsResources(i, slot.resourceGroup);
        // }
        
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

    private void UpdateSlotResources()
    {
        ShaderReflectionInfo reflectionInfo = _pipelineContext.ReflectionInfo!;

        for (uint i = 0; i < reflectionInfo.BindGroups.Count; i++)
        {

            BindGroupLayout bindGroupLayout = reflectionInfo.BindGroups[(int)i];
            if (UtilsMaterial.IsUniformBufferGroup(bindGroupLayout.Bindings))
            {
                if (!_parameters.TryGetBuffer(i, out GraphicsBuffer? _))
                {
                    BindGroupEntryInfo info = bindGroupLayout.Bindings[0];
                    _parameters.SetBuffer(i, _system.CreateGraphicsBuffer(
                        info.Size,
                        $"Material_{Name}_Buffer_{info.Entry.Name}"
                    ));
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