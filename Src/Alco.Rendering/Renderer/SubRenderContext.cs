using System.Runtime.CompilerServices;
using Alco.Graphics;
using System.Diagnostics;
using System.Collections.Generic;

namespace Alco.Rendering;

public sealed class SubRenderContext : AutoDisposable, IRenderContext
{
    private readonly GPURenderBundle _renderBundle;
    private readonly List<ICommandListener> _listeners;

    private GPUAttachmentLayout? _attachmentLayout;

    //cached mesh data
    private Mesh? _mesh;
    private int _subMeshIndex;
    private uint _meshVersion;
    private uint _indexCount;

    public bool HasBuffer => _renderBundle.HasBuffer;

    public GPURenderBundle RenderBundle
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _renderBundle;
    }

    public SubRenderContext(RenderingSystem renderingSystem, string name)
    {
        GPUDevice device = renderingSystem.GraphicsDevice;
        _renderBundle = device.CreateRenderBundle(new RenderBundleDescriptor(name));
        _listeners = new List<ICommandListener>();
    }

    /// <summary>
    /// Adds a command listener to the sub render context.
    /// </summary>
    /// <param name="listener">The listener to add.</param>
    public void AddListener(ICommandListener listener)
    {
        _listeners.Add(listener);
    }

    /// <summary>
    /// Removes a command listener from the sub render context.
    /// </summary>
    /// <param name="listener">The listener to remove.</param>
    public void RemoveListener(ICommandListener listener)
    {
        _listeners.Remove(listener);
    }

    /// <summary>
    /// Begin the sub render context.
    /// </summary>
    /// <param name="attachmentLayout">The attachment layout to render to.</param>
    public void Begin(GPUAttachmentLayout attachmentLayout)
    {
        _attachmentLayout = attachmentLayout;
        _renderBundle.Begin(attachmentLayout);

        InvokeBegin();
    }

    /// <summary>
    /// End the sub render context.
    /// </summary>
    public void End()
    {
         InvokeEnd();

        _renderBundle.End();
        ClearCache();
    }

    /// <summary>
    /// Draws a mesh with the specified material.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    public void Draw(in Mesh mesh, in Material material, in int subMeshIndex = 0)
    {
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_attachmentLayout!);
        _renderBundle.SetGraphicsPipeline(pipelineInfo.Pipeline);
        SetMesh(mesh, subMeshIndex);
        material.PushResources(_renderBundle);
        _renderBundle.DrawIndexed(_indexCount, 1, 0, 0, 0);
    }

    /// <summary>
    /// Draws a mesh multiple times with the specified material.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    public void DrawInstanced(in Mesh mesh, in Material material, in uint instanceCount, in int subMeshIndex = 0)
    {
        DrawInstanced(mesh, material, instanceCount, 0, subMeshIndex);
    }

    /// <summary>
    /// Draws a mesh multiple times with the specified material.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="instanceStartIndex">The index of the first instance to draw.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    public void DrawInstanced(in Mesh mesh, in Material material, in uint instanceCount, in uint instanceStartIndex, in int subMeshIndex = 0)
    {
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_attachmentLayout!);
        _renderBundle.SetGraphicsPipeline(pipelineInfo.Pipeline);
        SetMesh(mesh, subMeshIndex);
        material.PushResources(_renderBundle);
        _renderBundle.DrawIndexed(_indexCount, instanceCount, 0, 0, instanceStartIndex);
    }

    /// <summary>
    /// Draws a mesh multiple times with the specified material and push constants.
    /// </summary>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="constant">The constant data to push to the shader.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawInstancedWithConstant<T>(in Mesh mesh, in Material material, in uint instanceCount, in T constant, in int subMeshIndex = 0) where T : unmanaged
    {
        DrawInstancedWithConstant(mesh, material, instanceCount, 0, constant, subMeshIndex);
    }

    /// <summary>
    /// Draws a mesh multiple times with the specified material and push constants, starting from a specific instance.
    /// </summary>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="instanceStart">The index of the first instance to draw.</param>
    /// <param name="constant">The constant data to push to the shader.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    public unsafe void DrawInstancedWithConstant<T>(in Mesh mesh, in Material material, in uint instanceCount, in uint instanceStart, in T constant, in int subMeshIndex = 0) where T : unmanaged
    {
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_attachmentLayout!);
        _renderBundle.SetGraphicsPipeline(pipelineInfo.Pipeline);
        SetMesh(mesh, subMeshIndex);
        material.PushResources(_renderBundle);
        PushConstantSafe(pipelineInfo.PushConstantsStages, constant, pipelineInfo.PushConstantsSize);
        _renderBundle.DrawIndexed(_indexCount, instanceCount, 0, 0, instanceStart);
    }

    /// <summary>
    /// Draws a mesh with the specified material and push constants.
    /// </summary>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="constant">The constant data to push to the shader.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    public unsafe void DrawWithConstant<T>(in Mesh mesh, in Material material, in T constant, in int subMeshIndex = 0) where T : unmanaged
    {
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_attachmentLayout!);
        _renderBundle.SetGraphicsPipeline(pipelineInfo.Pipeline);
        SetMesh(mesh, subMeshIndex);
        material.PushResources(_renderBundle);
        PushConstantSafe(pipelineInfo.PushConstantsStages, constant, pipelineInfo.PushConstantsSize);
        _renderBundle.DrawIndexed(_indexCount, 1, 0, 0, 0);
    }

    protected override void Dispose(bool disposing)
    {
        _renderBundle.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetMesh(in Mesh mesh, in int subMeshIndex)
    {
        if (_mesh == mesh && _subMeshIndex == subMeshIndex && mesh.Version == _meshVersion)
        {
            return;
        }

        _mesh = mesh;
        _subMeshIndex = subMeshIndex;
        _meshVersion = mesh.Version;

        _indexCount = _renderBundle.SetMesh(mesh, subMeshIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void PushConstantSafe<T>(ShaderStage stage, in T data, int pushConstantSize) where T : unmanaged
    {
        if (pushConstantSize != sizeof(T))
        {
            pushConstantSize = Math.Min(pushConstantSize, sizeof(T));
        }

        fixed (T* ptr = &data)
        {
            _renderBundle.PushGraphicsConstants(stage, 0, (byte*)ptr, (uint)pushConstantSize);
        }
    }

    /// <summary>
    /// Clears the cached mesh data.
    /// </summary>
    private void ClearCache()
    {
        _mesh = null;
        _subMeshIndex = 0;
    }

    /// <summary>
    /// Invokes the OnCommandBegin event on all listeners.
    /// </summary>
    /// <returns>A list of exceptions that occurred during the invocation.</returns>
    private void InvokeBegin()
    {
        foreach (var observer in _listeners)
        {
            try
            {
                observer.OnCommandBegin();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }
    }

    /// <summary>
    /// Invokes the OnCommandEnd event on all listeners.
    /// </summary>
    /// <returns>A list of exceptions that occurred during the invocation.</returns>
    private void InvokeEnd()
    {
        foreach (var observer in _listeners)
        {
            try
            {
                observer.OnCommandEnd();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }
    }
}

