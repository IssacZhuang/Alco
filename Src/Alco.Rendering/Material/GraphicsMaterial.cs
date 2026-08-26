using Alco.Graphics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

/// <summary>
/// The integration of the GPU pipeline state and shader resources of a graphics shader.
/// Also creates the buffer for uniform buffers and binds the white texture to sampled
/// texture slots by default.
/// </summary>
public class GraphicsMaterial : AutoDisposable
{
    protected readonly RenderingSystem _system;
    protected readonly ShaderParameterSet _parameters;

    protected bool _isPipelineDirty = true;
    protected GraphicsPipelineContext _pipelineContext;

    // Construction-bound (immutable for the material's lifetime): the shader
    // handle. The specialization starts at construction and can be swapped
    // later through SetSpecializations (the same mutation surface the retired
    // defines used to have); the swap rebuilds the pipeline lazily and carries
    // the resource bindings over by name.
    protected readonly Shader _shader;
    private string[] _specializations;

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
    public ShaderReflection ReflectionInfo
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

    /// <summary>
    /// The shader of the material.
    /// </summary>
    public Shader Shader
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _shader;
    }

    /// <summary>The specialization arguments of the material's current variant.</summary>
    public IReadOnlyList<string> Specializations => _specializations;

    /// <summary>
    /// Switches the material to another specialization of its shader module
    /// (the successor of the retired defines-based SetDefines): the new
    /// variant's pipeline compiles lazily on the next use, and every resource
    /// binding carries over by resource name (the modules of one shader share
    /// their binding names). A no-op when the arguments are unchanged.
    /// </summary>
    /// <param name="specializations">The specialization arguments of the new
    /// variant — C# values (<c>false</c>, <c>3</c>) or slang expressions,
    /// normalized internally.</param>
    public void SetSpecializations(params ReadOnlySpan<object> specializations)
    {
        string[] spec = Shader.NormalizeSpecializations(specializations);
        if (spec.AsSpan().SequenceEqual(_specializations))
        {
            return;
        }

        _specializations = spec;
        // The next GetPipelineContext sees the dirty flag, compiles the variant's
        // modules through the shader handle and rebuilds the pipeline; the
        // parameter set re-resolves its slots from the new reflection info,
        // carrying bound values over by name.
        _pipelineContext.Specializations = spec;
        _isPipelineDirty = true;
    }

    /// <summary>
    /// Gets the resource group at the specified index.
    /// </summary>
    /// <param name="index">The index of the resource group.</param>
    /// <returns>The resource group at the specified index.</returns>
    public GPUResourceGroup? this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            _parameters.FlushResourceGroups();
            return _parameters.ResourceGroups[index];
        }
    }

    /// <summary>
    /// The name of the material.
    /// </summary>
    public string Name { get; }



    internal GraphicsMaterial(RenderingSystem system, Shader shader, string name, string[]? specializations = null)
    {
        Name = name;
        _system = system;
        _shader = shader;
        _specializations = specializations ?? [];

        ShaderModulesInfo modulesInfo = shader.GetShaderModules(_specializations);
        if (modulesInfo.IsComputeShader)
        {
            throw new InvalidOperationException("The shader required for material must be a graphics shader");
        }

        ShaderReflection reflectionInfo = modulesInfo.ReflectionInfo;
        _parameters = new ShaderParameterSet(system.GraphicsDevice, system.Samplers, reflectionInfo);
        UpdateSlotResources(reflectionInfo);

        _pipelineContext = GraphicsPipelineContext.Default;
        _pipelineContext.ReflectionInfo = reflectionInfo;
        _pipelineContext.Specializations = _specializations;
    }

    /// <summary>
    /// The shader parameter set of the material.
    /// </summary>
    internal ShaderParameterSet Parameters => _parameters;

    /// <summary>
    /// Get the pipeline context, updating the cached pipeline for the given attachment layout when dirty.
    /// </summary>
    /// <param name="attachmentLayout">The attachment layout.</param>
    /// <returns>The up-to-date pipeline context.</returns>
    public GraphicsPipelineContext GetPipelineContext(GPUAttachmentLayout attachmentLayout)
    {
        if (_shader.TryUpdatePipelineContext(ref _pipelineContext, attachmentLayout, _isPipelineDirty))
        {
            _parameters.SetReflectionInfo(_pipelineContext.ReflectionInfo!);
            UpdateSlotResources(_pipelineContext.ReflectionInfo!);
            _isPipelineDirty = false;
            IncreaseVersion();
        }

        return _pipelineContext;
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

    #region Set sampler

    /// <summary>
    /// Try to bind a custom sampler to the shader's own sampler entry of the given
    /// name (a module-declared <c>SamplerState</c> member that is not a shared
    /// sampler bank member). Shared bank members are immutable engine constants
    /// resolved from the sampler library and cannot be bound or overridden.
    /// </summary>
    /// <param name="name">The shader-side sampler entry name (e.g. <c>_mySampler</c>).</param>
    /// <param name="sampler">The custom sampler to bind.</param>
    /// <returns>Whether any bind group of this shader declares the sampler entry.</returns>
    public bool TrySetSampler(string name, GPUSampler sampler)
    {
        if (_parameters.TrySetSampler(name, sampler))
        {
            IncreaseVersion();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Bind a custom sampler to the shader's own sampler entry of the given name.
    /// </summary>
    /// <param name="name">The shader-side sampler entry name (e.g. <c>_mySampler</c>).</param>
    /// <param name="sampler">The custom sampler to bind.</param>
    /// <exception cref="KeyNotFoundException">No bind group of this shader declares
    /// the sampler entry, or the name is a shared sampler bank member (immutable,
    /// not bindable).</exception>
    public void SetSampler(string name, GPUSampler sampler)
    {
        _parameters.SetSampler(name, sampler);
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

    /// <summary>
    /// Try to set a 3D texture to a sampled texture slot (filtered sampling
    /// through the texture's own sampler; see <see cref="Texture3D"/>).
    /// </summary>
    /// <param name="name">The shader resource name of the texture.</param>
    /// <param name="texture">The 3D texture to set.</param>
    /// <returns>True if the texture is set successfully, otherwise false.</returns>
    public bool TrySetTexture3D(string name, Texture3D texture)
    {
        if (!_parameters.TrySetTexture(name, texture))
        {
            return false;
        }
        IncreaseVersion();
        return true;
    }

    /// <summary>
    /// Set a 3D texture to a sampled texture slot (filtered sampling through
    /// the texture's own sampler; see <see cref="Texture3D"/>).
    /// </summary>
    /// <param name="name">The shader resource name of the texture.</param>
    /// <param name="texture">The 3D texture to set.</param>
    public void SetTexture3D(string name, Texture3D texture)
    {
        _parameters.SetTexture(name, texture);
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

    /// <summary>
    /// Bind the default resources to the slots that have no value yet. Called after
    /// construction and after the shader pipeline was updated. Instances override this
    /// to bind nothing: their slots resolve from the parent chain, and a default bound
    /// into an own slot would shadow the parent value.
    /// </summary>
    /// <param name="reflectionInfo">The reflection info of the shader.</param>
    protected virtual void UpdateSlotResources(ShaderReflection reflectionInfo)
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

    /// <summary>
    /// Set the resources to the command buffer. The resource groups of an instance
    /// already include the values resolved from its parent chain, so pushing works
    /// the same for materials and instances.
    /// </summary>
    /// <param name="renderPass">The render pass to set the resources.</param>
    public void PushResources(GPUCommandBuffer.RenderPass renderPass)
    {
        _parameters.FlushResourceGroups();
        ReadOnlySpan<GPUResourceGroup?> resources = _parameters.ResourceGroups;
        for (uint i = 0; i < resources.Length; i++)
        {
            GPUResourceGroup? resource = resources[(int)i];
            if (resource != null)
            {
                renderPass.SetResources(i, resource);
            }
            else
            {
                throw new InvalidOperationException($"Null resource group at index {i}, {_parameters.ReflectionInfo.BindGroups[(int)i].Bindings[0].Entry.Name} of shader {_shader.Name}");
            }
        }
    }

    /// <summary>
    /// Set the resources to the render bundle. The resource groups of an instance
    /// already include the values resolved from its parent chain, so pushing works
    /// the same for materials and instances.
    /// </summary>
    /// <param name="renderBundle">The render bundle to set the resources.</param>
    public void PushResources(GPURenderBundle renderBundle)
    {
        _parameters.FlushResourceGroups();
        ReadOnlySpan<GPUResourceGroup?> resources = _parameters.ResourceGroups;
        for (uint i = 0; i < resources.Length; i++)
        {
            GPUResourceGroup? resource = resources[(int)i];
            if (resource != null)
            {
                renderBundle.SetGraphicsResources(i, resource);
            }
            else
            {
                throw new InvalidOperationException($"Null resource group at index {i}, {_parameters.ReflectionInfo.BindGroups[(int)i].Bindings[0].Entry.Name} of shader {_shader.Name}");
            }
        }
    }

    /// <summary>
    /// Create a instance of the material. The instance can override part of the parameters of the parent material.
    /// </summary>
    /// <returns>The instance of the material.</returns>
    public GraphicsMaterialInstance CreateInstance()
    {
        return new GraphicsMaterialInstance(_system, this);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The parameter set owns the assembled bind groups; the slot values
            // (textures, buffers) are caller-owned references.
            _parameters.Dispose();
        }
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
