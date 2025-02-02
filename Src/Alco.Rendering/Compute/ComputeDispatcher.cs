using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The integration of the GPU compute pipeline state and shader resources.
/// Provides functionality to dispatch compute shaders and manage their resources.
/// </summary>
public class ComputeDispatcher
{
    protected readonly GPUDevice _device;
    protected readonly Shader _shader;
    protected readonly ShaderParameterSet _parameterSet;
    protected ComputePipelineContext _pipelineInfo;

    protected bool _isPipelineDirty = true;

    /// <summary>
    /// Gets the number of resource groups in the compute pipeline.
    /// </summary>
    public int ResourceGroupCount => _pipelineInfo.ReflectionInfo!.BindGroups.Count;

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


    internal ComputeDispatcher(RenderingSystem system, Shader shader)
    {
        _device = system.GraphicsDevice;
        _shader = shader;
        _pipelineInfo = shader.GetComputePipelineInfo();
        _parameterSet = new ShaderParameterSet(_pipelineInfo.ReflectionInfo!);
    }

    internal ComputeDispatcher(RenderingSystem system, Shader shader, ReadOnlySpan<string> defines)
    {
        _device = system.GraphicsDevice;
        _shader = shader;
        _pipelineInfo = shader.GetComputePipelineInfo(defines);
        _parameterSet = new ShaderParameterSet(_pipelineInfo.ReflectionInfo!);
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
    public void Dispatch(GPUCommandBuffer command, uint x, uint y, uint z)
    {
        if (!command.IsRecording)
        {
            throw new InvalidOperationException("The command buffer is not in recording. Try uses GPUCommandBuffer.Begin()");
        }

        if (x == 0 || y == 0 || z == 0)
        {
            throw new ArgumentOutOfRangeException($"The dispatch size must be greater than zero: {x}, {y}, {z}");
        }

        if (_shader.TryUpdateComputePipelineContext(ref _pipelineInfo, _isPipelineDirty))
        {
            _parameterSet.SetReflectionInfo(_pipelineInfo.ReflectionInfo!);
            _isPipelineDirty = false;
        }

        command.SetComputePipeline(_pipelineInfo.Pipeline!);

        int length = ResourceGroupCount;
        for (int i = 0; i < length; i++)
        {
            GPUResourceGroup? resourceGroup = this[i];
            if (resourceGroup != null)
            {
                command.SetComputeResources((uint)i, resourceGroup);
            }
            else
            {
                throw new InvalidOperationException($"The resource group is null at index {i}, {_pipelineInfo.ReflectionInfo!.GetResourceName((uint)i)}");
            }
        }

        command.DispatchCompute(x, y, z);

    }

    /// <summary>
    /// Sets the shader defines to control the variant of the compute shader.
    /// </summary>
    /// <param name="defines">The defines to set.</param>
    /// <exception cref="ArgumentNullException">Thrown when defines is null.</exception>
    public void SetDefines(params string[] defines)
    {
        ArgumentNullException.ThrowIfNull(defines);
        _pipelineInfo.Defines = defines;
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
}