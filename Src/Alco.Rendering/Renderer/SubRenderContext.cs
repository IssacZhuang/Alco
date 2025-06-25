using System.Runtime.CompilerServices;
using Alco.Graphics;
using System.Diagnostics;
using System.Collections.Generic;

namespace Alco.Rendering;

public sealed class SubRenderContext : AutoDisposable, IRenderContext
{
    private readonly GPURenderBundle _renderBundle;
    private readonly List<ICommandListener> _listeners;
    private readonly List<Exception> _exceptionsBegin;
    private readonly List<Exception> _exceptionsEnd;

    private GPUAttachmentLayout? _renderPass;

    //cached mesh data
    private Mesh? _mesh;
    private int _subMeshIndex;
    private uint _meshVersion;
    private uint _indexCount;

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
        _exceptionsBegin = new List<Exception>();
        _exceptionsEnd = new List<Exception>();
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
    /// <param name="renderPass">The render pass to render to.</param>
    /// <returns>The exceptions that occurred during invoking the <see cref="ICommandListener.OnCommandBegin"/> event; otherwise, an empty array.</returns>
    public IReadOnlyList<Exception> Begin(GPUAttachmentLayout renderPass)
    {
        _renderPass = renderPass;
        _renderBundle.Begin(renderPass);

        return InvokeBegin();
    }

    /// <summary>
    /// End the sub render context.
    /// </summary>
    /// <returns>The exceptions that occurred during invoking the <see cref="ICommandListener.OnCommandEnd"/> event; otherwise, an empty array.</returns>
    public IReadOnlyList<Exception> End()
    {
        var exceptions = InvokeEnd();

        _renderBundle.End();
        ClearCache();

        return exceptions;
    }

    /// <summary>
    /// Draws a mesh with the specified material.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    public void Draw(in Mesh mesh, in Material material, in int subMeshIndex = 0)
    {
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_renderPass!);
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
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_renderPass!);
        _renderBundle.SetGraphicsPipeline(pipelineInfo.Pipeline);
        SetMesh(mesh, subMeshIndex);
        material.PushResources(_renderBundle);
        _renderBundle.DrawIndexed(_indexCount, instanceCount, 0, 0, 0);
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
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_renderPass!);
        _renderBundle.SetGraphicsPipeline(pipelineInfo.Pipeline);
        SetMesh(mesh, subMeshIndex);
        material.PushResources(_renderBundle);
        _renderBundle.PushGraphicsConstants(pipelineInfo.PushConstantsStages, constant);
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
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_renderPass!);
        _renderBundle.SetGraphicsPipeline(pipelineInfo.Pipeline);
        SetMesh(mesh, subMeshIndex);
        material.PushResources(_renderBundle);
        _renderBundle.PushGraphicsConstants(pipelineInfo.PushConstantsStages, constant);
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
    private IReadOnlyList<Exception> InvokeBegin()
    {
        _exceptionsBegin.Clear();
        foreach (var observer in _listeners)
        {
            try
            {
                observer.OnCommandBegin();
            }
            catch (Exception e)
            {
                _exceptionsBegin.Add(e);
            }
        }
        return _exceptionsBegin;
    }

    /// <summary>
    /// Invokes the OnCommandEnd event on all listeners.
    /// </summary>
    /// <returns>A list of exceptions that occurred during the invocation.</returns>
    private IReadOnlyList<Exception> InvokeEnd()
    {
        _exceptionsEnd.Clear();
        foreach (var observer in _listeners)
        {
            try
            {
                observer.OnCommandEnd();
            }
            catch (Exception e)
            {
                _exceptionsEnd.Add(e);
            }
        }
        return _exceptionsEnd;
    }
}

