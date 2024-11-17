using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class ComputeDispatcher
{
    private readonly GPUDevice _device;
    private readonly Shader _shader;
    private readonly GraphicsParameterSet _parameterSet;
    private ComputePipelineContext _pipelineInfo;


    internal ComputeDispatcher(RenderingSystem system, Shader shader)
    {
        _device = system.GraphicsDevice;
        _shader = shader;
        _pipelineInfo = shader.GetComputePipelineInfo();
        _parameterSet = new GraphicsParameterSet(_pipelineInfo.ReflectionInfo);
    }

    internal ComputeDispatcher(RenderingSystem system, Shader shader, ReadOnlySpan<string> defines)
    {
        _device = system.GraphicsDevice;
        _shader = shader;
        _pipelineInfo = shader.GetComputePipelineInfo(defines);
        _parameterSet = new GraphicsParameterSet(_pipelineInfo.ReflectionInfo);
    }

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

        command.SetComputePipeline(_pipelineInfo.Pipeline);

        ReadOnlySpan<GPUResourceGroup?> resourceGroups = _parameterSet.ResourceGroups;
        for (int i = 0; i < resourceGroups.Length; i++)
        {
            GPUResourceGroup? resourceGroup = resourceGroups[i];
            if (resourceGroup!= null)
            {
                command.SetComputeResources((uint)i, resourceGroup);
            }
        }

        command.DispatchCompute(x, y, z);

    }

    #region Set Buffer

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetBuffer(string name, GraphicsBuffer buffer)
    {
        return _parameterSet.TrySetBuffer(name, buffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetBuffer(uint id, GraphicsBuffer buffer)
    {
        return _parameterSet.TrySetBuffer(id, buffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetBuffer(string name, GraphicsBuffer buffer)
    {
        _parameterSet.SetBuffer(name, buffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetBuffer(uint id, GraphicsBuffer buffer)
    {
        _parameterSet.SetBuffer(id, buffer);
    }

    #endregion


    #region Set Texture

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetTexture(string name, Texture2D texture)
    {
        return _parameterSet.TrySetTexture(name, texture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetTexture(uint id, Texture2D texture)
    {
        return _parameterSet.TrySetTexture(id, texture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTexture(string name, Texture2D texture)
    {
        _parameterSet.SetTexture(name, texture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTexture(uint id, Texture2D texture)
    {
        _parameterSet.SetTexture(id, texture);
    }

    #endregion

    #region Set RenderTexture

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRenderTexture(string name, RenderTexture renderTexture, int index = 0)
    {
        _parameterSet.SetRenderTexture(name, renderTexture, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRenderTexture(uint id, RenderTexture renderTexture, int index = 0)
    {
        _parameterSet.SetRenderTexture(id, renderTexture, index);
    }



    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetRenderTextureDepth(string name, RenderTexture renderTexture)
    {
        return _parameterSet.TrySetRenderTextureDepth(name, renderTexture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetRenderTextureDepth(uint id, RenderTexture renderTexture)
    {
        return _parameterSet.TrySetRenderTextureDepth(id, renderTexture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRenderTextureDepth(string name, RenderTexture renderTexture)
    {
        _parameterSet.SetRenderTextureDepth(name, renderTexture);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRenderTextureDepth(uint id, RenderTexture renderTexture)
    {
        _parameterSet.SetRenderTextureDepth(id, renderTexture);
    }

    #endregion
}