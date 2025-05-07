using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The context of the render object. It is a high level encapsulation of the <see cref="GPUCommandBuffer"/>.
/// All APIs in this class are not thread safe, but you can create multiple instances on different threads.
/// </summary>
public sealed class RenderContext : AutoDisposable, IRenderContext
{
    private readonly GPUDevice _device;
    private readonly RenderingSystem _renderingSystem;
    private readonly GPUCommandBuffer _command;
    private readonly List<ICommandListener> _listeners;
    private readonly List<Exception> _exceptionsBegin;
    private readonly List<Exception> _exceptionsEnd;
    private GPUFrameBuffer? _framebuffer;

    //cached mesh data
    private Mesh? _mesh;
    private int _subMeshIndex;
    private uint _meshVersion;
    private uint _indexCount;




    /// <summary>
    /// The framebuffer that is currently being rendered to.
    /// </summary>
    public GPUFrameBuffer? Framebuffer => _framebuffer;

    /// <summary>
    /// The command buffer that is currently in use.
    /// </summary>
    public GPUCommandBuffer CommandBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _command;
    }



    internal RenderContext(RenderingSystem renderingSystem, string name)
    {
        _renderingSystem = renderingSystem;
        _device = renderingSystem.GraphicsDevice;
        _command = _device.CreateCommandBuffer(new CommandBufferDescriptor(name));
        _listeners = new List<ICommandListener>();
        _exceptionsBegin = new List<Exception>();
        _exceptionsEnd = new List<Exception>();
    }

    /// <summary>
    /// Adds a command listener to the render context.
    /// </summary>
    /// <param name="listener">The listener to add.</param>
    public void AddListener(ICommandListener listener)
    {
        _listeners.Add(listener);
    }

    /// <summary>
    /// Removes a command listener from the render context.
    /// </summary>
    /// <param name="listener">The listener to remove.</param>
    public void RemoveListener(ICommandListener listener)
    {
        _listeners.Remove(listener);
    }

    /// <summary>
    /// Begin the render context.
    /// </summary>
    /// <param name="target">The framebuffer to render to.</param>
    /// <returns>The exceptions that occurred during invoking the <see cref="ICommandListener.OnCommandBegin"/> event; otherwise, an empty array.</returns>
    public IReadOnlyList<Exception> Begin(
        GPUFrameBuffer target, 
        Vector4? clearColor = null,
        float? clearDepth = null,
        uint? clearStencil = null
        )
    {
        _command.Begin();
        _command.SetFrameBuffer(target);
        if (clearColor.HasValue)
        {
            _command.ClearColor(clearColor.Value);
        }

        if (clearDepth.HasValue)
        {
            _command.ClearDepth(clearDepth.Value);
        }

        if (clearStencil.HasValue)
        {
            _command.ClearStencil(clearStencil.Value);
        }

        _framebuffer = target;

        ClearCache();

        return InvokeBegin();
    }

    /// <summary>
    /// Draws a mesh with the specified material.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    public void Draw(in Mesh mesh, in Material material, in int subMeshIndex = 0)
    {
        Debug.Assert(_framebuffer != null);
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_framebuffer!.RenderPass);
        _command.SetGraphicsPipeline(pipelineInfo.Pipeline);
        SetMesh(mesh, subMeshIndex);
        material.PushResourceToCommandBuffer(_command);
        _command.DrawIndexed(_indexCount, 1, 0, 0, 0);
    }


    /// <summary>
    /// Draws a mesh with the specified material and push constants.
    /// </summary>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="constant">The constant data to push to the shader.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    /// <exception cref="ArgumentException">Thrown when the size of the constant does not match the push constants size.</exception>
    public unsafe void DrawWithConstant<T>(in Mesh mesh, in Material material, in T constant, in int subMeshIndex = 0) where T : unmanaged
    {
        Debug.Assert(_framebuffer != null);
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_framebuffer!.RenderPass);
        if (pipelineInfo.PushConstantsSize != sizeof(T))
        {
            throw new ArgumentException("The size of the constant does not match the push constants size");
        }
        _command.SetGraphicsPipeline(pipelineInfo.Pipeline);
        SetMesh(mesh, subMeshIndex);
        material.PushResourceToCommandBuffer(_command);
        _command.PushGraphicsConstants(pipelineInfo.PushConstantsStages, constant);
        _command.DrawIndexed(_indexCount, 1, 0, 0, 0);
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
        Debug.Assert(_framebuffer != null);
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_framebuffer!.RenderPass);
        _command.SetGraphicsPipeline(pipelineInfo.Pipeline);
        SetMesh(mesh, subMeshIndex);
        material.PushResourceToCommandBuffer(_command);
        _command.DrawIndexed(_indexCount, instanceCount, 0, 0, 0);
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
    public void DrawInstancedWithConstant<T>(in Mesh mesh, in Material material, in uint instanceCount, in uint instanceStart, in T constant, in int subMeshIndex = 0) where T : unmanaged
    {
        Debug.Assert(_framebuffer != null);
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_framebuffer!.RenderPass);
        _command.SetGraphicsPipeline(pipelineInfo.Pipeline);
        SetMesh(mesh, subMeshIndex);
        material.PushResourceToCommandBuffer(_command);
        _command.PushGraphicsConstants(pipelineInfo.PushConstantsStages, constant);
        _command.DrawIndexed(_indexCount, instanceCount, 0, 0, instanceStart);
    }


    /// <summary>
    /// Execute the commands recorded in the <see cref="SubRenderContext"/>.
    /// </summary>
    /// <param name="subContext">The sub context to execute.</param>
    public void ExecuteSubContext(SubRenderContext subContext)
    {
        GPURenderBundle renderBundle = subContext.RenderBundle;
        if (!renderBundle.HasBuffer)
        {
            throw new InvalidOperationException("The render bundle of SubRenderContext is not been recorded, try use RenderContext.Begin(GPURenderPass) to record render commands.");
        }

        _command.ExecuteBundle(renderBundle);
    }

    /// <summary>
    /// End the render context.
    /// </summary>
    /// <returns>The exceptions that occurred during invoking the <see cref="ICommandListener.OnCommandEnd"/> event; otherwise, an empty array.</returns>
    public IReadOnlyList<Exception> End()
    {
        var exceptions = InvokeEnd();

        _command.End();
        _renderingSystem.ScheduleCommandBuffer(_command);
        ClearCache();

        _framebuffer = null;
        return exceptions;
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

        _indexCount = _command.SetMesh(mesh, subMeshIndex);
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

    protected override void Dispose(bool disposing)
    {
        _command.Dispose();
    }
}