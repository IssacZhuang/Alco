using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The integration of the GPU compute pipeline state and shader resources.
/// Provides functionality to dispatch compute shaders and manage their resources.
/// </summary>
public class ComputeMaterial
{
    protected readonly RenderingSystem _system;
    protected readonly Shader _shader;
    protected readonly ShaderParameterSet _parameterSet;
    protected ComputePipelineContext _pipelineContext;


    protected bool _isPipelineDirty = true;

    /// <summary>
    /// Gets the number of resource groups in the compute pipeline.
    /// </summary>
    public int ResourceGroupCount => _pipelineContext.ReflectionInfo!.BindGroups.Count;

    /// <summary>
    /// Gets the resource group at the specified index.
    /// </summary>
    /// <param name="index">The index of the resource group.</param>
    /// <returns>The resource group at the specified index.</returns>
    public virtual GPUResourceGroup? this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _parameterSet.ResourceGroups[index];
    }

    /// <summary>
    /// Gets the shader used by this compute dispatcher.
    /// </summary>
    public Shader Shader
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _shader;
    }

    public ShaderReflectionInfo ReflectionInfo
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _parameterSet.ReflectionInfo;
    }

    public string[] Defines
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipelineContext.Defines;
    }


    internal ComputeMaterial(RenderingSystem system, Shader shader)
    : this(system, shader, [])
    {

    }

    internal ComputeMaterial(RenderingSystem system, Shader shader, ReadOnlySpan<string> defines)
    {
        ArgumentNullException.ThrowIfNull(shader);
        if (!shader.IsComputeShader)
        {
            throw new InvalidOperationException("The shader required for compute material must be a compute shader");
        }

        _system = system;
        _shader = shader;
        _pipelineContext = shader.GetComputePipelineInfo(defines);
        _parameterSet = new ShaderParameterSet(_pipelineContext.ReflectionInfo!);
    }

    private void SetPipelineResources(GPUCommandBuffer.ComputePass computePass)
    {

        if (_shader.TryUpdateComputePipelineContext(ref _pipelineContext, _isPipelineDirty))
        {
            _parameterSet.SetReflectionInfo(_pipelineContext.ReflectionInfo!);
            _isPipelineDirty = false;
        }

        computePass.SetPipeline(_pipelineContext.Pipeline!);

        int length = ResourceGroupCount;
        for (int i = 0; i < length; i++)
        {
            GPUResourceGroup? resourceGroup = this[i];
            if (resourceGroup != null)
            {
                computePass.SetResources((uint)i, resourceGroup);
            }
            else
            {
                throw new InvalidOperationException($"The resource group is null at index {i}, {_pipelineContext.ReflectionInfo!.GetResourceName((uint)i)} of shader {_shader.Name}");
            }
        }
    }


    /// <summary>
    /// Dispatches the compute shader with the specified thread group counts.
    /// </summary>
    /// <param name="command">The command buffer to record the dispatch command to.</param>
    /// <param name="x">The number of thread groups in the X dimension.</param>
    /// <param name="y">The number of thread groups in the Y dimension.</param>
    /// <param name="z">The number of thread groups in the Z dimension.</param>
    /// <exception cref="InvalidOperationException">Thrown when the command buffer is not in recording state or when a required resource group is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any of the dispatch dimensions is zero.</exception>
    public void DispatchByGroup(GPUCommandBuffer.ComputePass computePass, uint x, uint y, uint z)
    {
        if (x <= 0 || y <= 0 || z <= 0)
        {
            throw new ArgumentOutOfRangeException($"The dispatch size must be greater than zero: {x}, {y}, {z}");
        }
        SetPipelineResources(computePass);
        computePass.DispatchCompute(x, y, z);
    }

    /// <summary>
    /// Dispatches the compute shader with the specified thread group counts and constant.
    /// </summary>
    /// <param name="command">The command buffer to record the dispatch command to.</param>
    /// <param name="x">The number of thread groups in the X dimension.</param>
    /// <param name="y">The number of thread groups in the Y dimension.</param>
    /// <param name="z">The number of thread groups in the Z dimension.</param>
    /// <param name="constant">The constant to push to the compute shader.</param>
    /// <typeparam name="T"></typeparam>
    public void DispatchByGroupWithConstant<T>(GPUCommandBuffer.ComputePass computePass, uint x, uint y, uint z, T constant) where T : unmanaged
    {
        if (x <= 0 || y <= 0 || z <= 0)
        {
            throw new ArgumentOutOfRangeException($"The dispatch size must be greater than zero: {x}, {y}, {z}");
        }
        SetPipelineResources(computePass);
        computePass.PushConstants(constant);
        computePass.DispatchCompute(x, y, z);
    }

    /// <summary>
    /// Dispatches the compute shader with size.
    /// The thread group counts will be calculated by the size automatically.
    /// </summary>
    /// <param name="command">The command buffer to record the dispatch command to.</param>
    /// <param name="x">The x size of the dispatch.</param>
    /// <param name="y">The y size of the dispatch.</param>
    /// <param name="z">The z size of the dispatch.</param>
    /// <exception cref="InvalidOperationException">Thrown when the command buffer is not in recording state or when a required resource group is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any of the dispatch dimensions is zero.</exception>
    public void DispatchBySize(GPUCommandBuffer.ComputePass computePass, uint x, uint y, uint z)
    {
        if (x <= 0 || y <= 0 || z <= 0)
        {
            throw new ArgumentOutOfRangeException($"The dispatch size must be greater than zero: {x}, {y}, {z}");
        }
        SetPipelineResources(computePass);
        ThreadGroupSize threadGroupSize = _parameterSet.ReflectionInfo.Size;
        threadGroupSize.GetDispatchCount(x, y, z, out uint groupX, out uint groupY, out uint groupZ);
        computePass.DispatchCompute(groupX, groupY, groupZ);
    }

    /// <summary>
    /// Dispatches the compute shader with size and constant.
    /// The thread group counts will be calculated by the size automatically.
    /// </summary>
    /// <param name="command">The command buffer to record the dispatch command to.</param>
    /// <param name="x">The x size of the dispatch.</param>
    /// <param name="y">The y size of the dispatch.</param>
    /// <param name="z">The z size of the dispatch.</param>
    /// <param name="constant">The constant to push to the compute shader.</param>
    /// <typeparam name="T"></typeparam>
    public void DispatchBySizeWithConstant<T>(GPUCommandBuffer.ComputePass computePass, uint x, uint y, uint z, T constant) where T : unmanaged
    {
        if (x <= 0 || y <= 0 || z <= 0)
        {
            throw new ArgumentOutOfRangeException($"The dispatch size must be greater than zero: {x}, {y}, {z}");
        }
        SetPipelineResources(computePass);
        computePass.PushConstants(constant);
        ThreadGroupSize threadGroupSize = _parameterSet.ReflectionInfo.Size;
        threadGroupSize.GetDispatchCount(x, y, z, out uint groupX, out uint groupY, out uint groupZ);
        computePass.DispatchCompute(groupX, groupY, groupZ);
    }


    /// <summary>
    /// Sets the shader defines to control the variant of the compute shader.
    /// </summary>
    /// <param name="defines">The defines to set.</param>
    /// <exception cref="ArgumentNullException">Thrown when defines is null.</exception>
    public void SetDefines(params string[] defines)
    {
        ArgumentNullException.ThrowIfNull(defines);
        _pipelineContext.Defines = defines;
        _isPipelineDirty = true;
    }

    #region Set Buffer

    /// <summary>
    /// Tries to set a buffer resource by name.
    /// </summary>
    /// <param name="name">The shader resource name of the buffer.</param>
    /// <param name="buffer">The buffer to set.</param>
    /// <returns>True if the buffer was set successfully, otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetBuffer(string name, GraphicsBuffer buffer)
    {
        return _parameterSet.TrySetBuffer(name, buffer);
    }

    /// <summary>
    /// Tries to set a buffer resource by index.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="buffer"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetBuffer(uint id, GraphicsBuffer buffer)
    {
        return _parameterSet.TrySetBuffer(id, buffer);
    }

    /// <summary>
    /// Sets a buffer resource by name.
    /// </summary>
    /// <param name="name">The shader resource name of the buffer.</param>
    /// <param name="buffer">The buffer to set.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetBuffer(string name, GraphicsBuffer buffer)
    {
        _parameterSet.SetBuffer(name, buffer);
    }

    /// <summary>
    /// Sets a buffer resource by index.
    /// </summary>
    /// <param name="id">The index of the buffer.</param>
    /// <param name="buffer">The buffer to set.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetBuffer(uint id, GraphicsBuffer buffer)
    {
        _parameterSet.SetBuffer(id, buffer);
    }


    #endregion


    #region Set Texture

    /// <summary>
    /// Tries to set a texture resource by name.
    /// </summary>
    /// <param name="name">The shader resource name of the texture.</param>
    /// <param name="texture">The texture to set.</param>
    /// <returns>True if the texture was set successfully, otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetTexture(string name, Texture2D texture)
    {
        return _parameterSet.TrySetTexture(name, texture);
    }

    /// <summary>
    /// Tries to set a texture resource by index.
    /// </summary>
    /// <param name="id">The index of the texture.</param>
    /// <param name="texture">The texture to set.</param>
    /// <returns>True if the texture was set successfully, otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetTexture(uint id, Texture2D texture)

    {
        return _parameterSet.TrySetTexture(id, texture);
    }

    /// <summary>
    /// Sets a texture resource by name.
    /// </summary>
    /// <param name="name">The shader resource name of the texture.</param>
    /// <param name="texture">The texture to set.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTexture(string name, Texture2D texture)
    {
        _parameterSet.SetTexture(name, texture);
    }

    /// <summary>
    /// Sets a texture resource by index.
    /// </summary>
    /// <param name="id">The index of the texture.</param>
    /// <param name="texture">The texture to set.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTexture(uint id, Texture2D texture)
    {
        _parameterSet.SetTexture(id, texture);
    }


    #endregion

    #region Set RenderTexture

    /// <summary>
    /// Tries to set a render texture resource by name.
    /// </summary>
    /// <param name="name">The shader resource name of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <param name="index">The index of the render texture.</param>
    /// <returns>True if the render texture was set successfully, otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetRenderTexture(string name, RenderTexture renderTexture, int index = 0)
    {
        return _parameterSet.TrySetRenderTexture(name, renderTexture, index);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetRenderTexture(uint id, RenderTexture renderTexture, int index = 0)
    {
        return _parameterSet.TrySetRenderTexture(id, renderTexture, index);
    }

    /// <summary>
    /// Sets a render texture resource by name.
    /// </summary>
    /// <param name="name">The shader resource name of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <param name="index">The index of the color attachment in the render texture.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRenderTexture(string name, RenderTexture renderTexture, int index = 0)
    {
        _parameterSet.SetRenderTexture(name, renderTexture, index);
    }

    /// <summary>
    /// Sets a render texture resource by index.
    /// </summary>
    /// <param name="id">The index of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <param name="index">The index of the color attachment in the render texture.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRenderTexture(uint id, RenderTexture renderTexture, int index = 0)
    {
        _parameterSet.SetRenderTexture(id, renderTexture, index);
    }

    /// <summary>
    /// Tries to set the depth attachment of a render texture resource by name.
    /// </summary>
    /// <param name="name">The shader resource name of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <returns>True if the render texture depth was set successfully, otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetRenderTextureDepth(string name, RenderTexture renderTexture)
    {
        return _parameterSet.TrySetRenderTextureDepth(name, renderTexture);
    }

    /// <summary>
    /// Tries to set the depth attachment of a render texture resource by index.
    /// </summary>
    /// <param name="id">The index of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <returns>True if the render texture depth was set successfully, otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    public bool TrySetRenderTextureDepth(uint id, RenderTexture renderTexture)
    {
        return _parameterSet.TrySetRenderTextureDepth(id, renderTexture);
    }

    /// <summary>
    /// Sets the depth attachment of a render texture resource by name.
    /// </summary>
    /// <param name="name">The shader resource name of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRenderTextureDepth(string name, RenderTexture renderTexture)

    {
        _parameterSet.SetRenderTextureDepth(name, renderTexture);
    }

    /// <summary>
    /// Sets the depth attachment of a render texture resource by index.
    /// </summary>
    /// <param name="id">The index of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRenderTextureDepth(uint id, RenderTexture renderTexture)
    {
        _parameterSet.SetRenderTextureDepth(id, renderTexture);
    }

    #endregion

    public ComputeMaterialInstance CreateInstance()
    {
        return new ComputeMaterialInstance(_system, this);
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


}