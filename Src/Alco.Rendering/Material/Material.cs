using Alco.Graphics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

public abstract class Material : AutoDisposable
{
    protected readonly RenderingSystem _system;
    protected readonly ShaderParameterSet _parameters;

    protected bool _isPipelineDirty = true;
    protected GraphicsPipelineContext _pipelineContext;
    protected ShaderPipelineInfo _pipelineInfo;

    protected readonly Shader _shader;

    private uint _version;

    public uint Version => _version;

    public int ResourceGroupCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _parameters.ResourceGroups.Length;
    }

    /// <summary>
    /// The reflection info of the shader.
    /// </summary>
    public ShaderReflectionInfo ReflectionInfo
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipelineContext.ReflectionInfo!;
    }

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

    public string[] Defines
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipelineContext.Defines;
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
    /// Gets the resource group at the specified index.
    /// </summary>
    /// <param name="index">The index of the resource group.</param>
    /// <returns>The resource group at the specified index.</returns>
    public virtual GPUResourceGroup? this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _parameters.ResourceGroups[index];
    }

    /// <summary>
    /// The name of the material.
    /// </summary>
    public string Name { get; }



    protected Material(RenderingSystem system, Shader shader, string name)
    {
        Name = name;
        _system = system;
        _shader = shader;

        if (shader.IsComputeShader)
        {
            throw new InvalidOperationException("The shader required for material must be a graphics shader");
        }

        ShaderReflectionInfo reflectionInfo = shader.GetShaderModules().ReflectionInfo;
        _parameters = new ShaderParameterSet(reflectionInfo);
        UpdateSlotResources(reflectionInfo);

        _pipelineContext = GraphicsPipelineContext.Default;
        _pipelineContext.ReflectionInfo = reflectionInfo;
    }

    /// <summary>
    /// Set the defines of the shader to control the variant of the shader.
    /// </summary>
    /// <param name="defines">The defines to set.</param>
    public void SetDefines(params string[] defines)
    {
        ArgumentNullException.ThrowIfNull(defines);
        _pipelineContext.Defines = defines;
        _isPipelineDirty = true;
    }

    public void ClearDefines()
    {
        _pipelineContext.Defines = [];
        _isPipelineDirty = true;
    }

    /// <summary>
    /// Get the shader pipeline.
    /// </summary>
    /// <param name="attachmentLayout">The attachment layout.</param>
    /// <returns>The shader pipeline.</returns>
    public ShaderPipelineInfo GetPipelineInfo(GPUAttachmentLayout attachmentLayout)
    {
        if (_shader.TryUpdatePipelineContext(ref _pipelineContext, attachmentLayout, _isPipelineDirty))
        {
            UpdateSlotResources(_pipelineContext.ReflectionInfo!);
            _pipelineInfo = new ShaderPipelineInfo
            {
                Pipeline = _pipelineContext.Pipeline!,
                ReflectionInfo = _pipelineContext.ReflectionInfo!,
                PushConstantsStages = _pipelineContext.ReflectionInfo!.PushConstantsStages,
                PushConstantsSize = _pipelineContext.ReflectionInfo!.PushConstantsSize
            };
            _parameters.SetReflectionInfo(_pipelineContext.ReflectionInfo!);
            _isPipelineDirty = false;
            IncreaseVersion();
        }

        return _pipelineInfo;
    }

    #region Set buffer

    public bool TrySetBuffer(string name, GraphicsBuffer buffer)
    {
        if (_parameters.TrySetBuffer(name, buffer))
        {
            IncreaseVersion();
            return true;
        }
        return false;
    }

    public void SetBuffer(string name, GraphicsBuffer buffer)
    {
        _parameters.SetBuffer(name, buffer);
        IncreaseVersion();
    }

    public bool TrySetBuffer(uint id, GraphicsBuffer buffer)
    {
        if (_parameters.TrySetBuffer(id, buffer))
        {
            IncreaseVersion();
            return true;
        }
        return false;
    }

    public void SetBuffer(uint id, GraphicsBuffer buffer)
    {
        _parameters.SetBuffer(id, buffer);
        IncreaseVersion();
    }

    #endregion

    #region Set texture

    /// <summary>
    /// Try to set the texture.
    /// </summary>
    /// <param name="name">The shader resource name of the texture.</param>
    /// <param name="texture">The texture to set.</param>
    /// <returns>True if the texture is set successfully, otherwise false.</returns>
    public bool TrySetTexture(string name, Texture2D texture)
    {
        if (_parameters.TrySetTexture(name, texture))
        {
            IncreaseVersion();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Try to set the texture.
    /// </summary>
    /// <param name="id">The shader resource id of the texture.</param>
    /// <param name="texture">The texture to set.</param>
    /// <returns>True if the texture is set successfully, otherwise false.</returns>
    public bool TrySetTexture(uint id, Texture2D texture)
    {
        if (_parameters.TrySetTexture(id, texture))
        {
            IncreaseVersion();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Set the texture.
    /// </summary>
    /// <param name="name">The shader resource name of the texture.</param>
    /// <param name="texture">The texture to set.</param>
    public void SetTexture(string name, Texture2D texture)
    {
        _parameters.SetTexture(name, texture);
        IncreaseVersion();
    }

    /// <summary>
    /// Set the texture.
    /// </summary>
    /// <param name="id">The shader resource id of the texture.</param>
    /// <param name="texture">The texture to set.</param>
    public void SetTexture(uint id, Texture2D texture)
    {
        _parameters.SetTexture(id, texture);
        IncreaseVersion();
    }


    #endregion

    #region Set render texture

    /// <summary>
    /// Try to set the render texture.
    /// </summary>
    /// <param name="name">The shader resource name of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <param name="renderTextureIndex">The index of the color attachment in the render texture.</param>
    /// <returns>True if the render texture is set successfully, otherwise false.</returns>
    public bool TrySetRenderTexture(string name, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        if (_parameters.TrySetRenderTexture(name, renderTexture, renderTextureIndex))
        {
            IncreaseVersion();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Try to set the render texture.
    /// </summary>
    /// <param name="id">The shader resource id of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <param name="renderTextureIndex">The index of the color attachment in the render texture.</param>
    /// <returns>True if the render texture is set successfully, otherwise false.</returns>
    public bool TrySetRenderTexture(uint id, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        if (_parameters.TrySetRenderTexture(id, renderTexture, renderTextureIndex))
        {
            IncreaseVersion();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Set the render texture.
    /// </summary>
    /// <param name="name">The shader resource name of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <param name="renderTextureIndex">The index of the color attachment in the render texture.</param>
    public void SetRenderTexture(string name, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        _parameters.SetRenderTexture(name, renderTexture, renderTextureIndex);
        IncreaseVersion();
    }

    /// <summary>
    /// Set the render texture.
    /// </summary>
    /// <param name="id">The shader resource id of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <param name="renderTextureIndex">The index of the color attachment in the render texture.</param>
    public void SetRenderTexture(uint id, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        _parameters.SetRenderTexture(id, renderTexture, renderTextureIndex);
        IncreaseVersion();
    }

    /// <summary>
    /// Tries to set the depth attachment of a render texture resource by name.
    /// </summary>
    /// <param name="name">The shader resource name of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <returns>True if the render texture depth was set successfully, otherwise false.</returns>
    public bool TrySetRenderTextureDepth(string name, RenderTexture renderTexture)
    {
        if (_parameters.TrySetRenderTextureDepth(name, renderTexture))
        {
            IncreaseVersion();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Tries to set the depth attachment of a render texture resource by index.
    /// </summary>
    /// <param name="id">The shader resource id of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <returns>True if the render texture depth was set successfully, otherwise false.</returns>
    public bool TrySetRenderTextureDepth(uint id, RenderTexture renderTexture)

    {
        if (_parameters.TrySetRenderTextureDepth(id, renderTexture))
        {
            IncreaseVersion();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Sets the depth attachment of a render texture resource by name.
    /// </summary>
    /// <param name="name">The shader resource name of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    public void SetRenderTextureDepth(string name, RenderTexture renderTexture)
    {
        _parameters.SetRenderTextureDepth(name, renderTexture);
        IncreaseVersion();
    }

    /// <summary>
    /// Sets the depth attachment of a render texture resource by index.
    /// </summary>
    /// <param name="id">The shader resource id of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    public void SetRenderTextureDepth(uint id, RenderTexture renderTexture)
    {
        _parameters.SetRenderTextureDepth(id, renderTexture);
        IncreaseVersion();
    }

    #endregion

    protected abstract void UpdateSlotResources(ShaderReflectionInfo reflectionInfo);


    /// <summary>
    /// Set the resources to the command buffer.
    /// </summary>
    /// <param name="commandBuffer">The command buffer to set the resources.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract void PushResources(GPUCommandBuffer.RenderPass commandBuffer);

    /// <summary>
    /// Set the resources to the render bundle.
    /// </summary>
    /// <param name="renderBundle">The render bundle to set the resources.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract void PushResources(GPURenderBundle renderBundle);

    /// <summary>
    /// Create a instance of the material. The instance can override part of the parameters of the parent material.
    /// </summary>
    /// <returns>The instance of the material.</returns>
    public MaterialInstance CreateInstance()
    {
        return new MaterialInstance(_system, this);
    }

    /// <summary>
    /// Get the resource id of the shader.
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <returns>The resource id of the shader.</returns>
    public uint GetResourceId(string name)
    {
        return _pipelineContext.GetResourceId(name);
    }

    /// <summary>
    /// Try to get the resource id of the shader.
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <param name="id">The resource id of the shader.</param>
    /// <returns>True if the resource id is found, otherwise false.</returns>
    public bool TryGetResourceId(string name, out uint id)
    {
        return _pipelineContext.TryGetResourceId(name, out id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void IncreaseVersion()
    {
        unchecked
        {
            _version++;
        }
    }
}