using System.Diagnostics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The context of the render object. It is a high level encapsulation of the rendering process.
/// All APIs in this class are not thread safe, but you can create multiple instances on different threads.
/// </summary>
public sealed class RenderContext : AutoDisposable
{
    private readonly GPUDevice _device;
    private readonly RenderingSystem _renderingSystem;
    private readonly GPUCommandBuffer _command;
    private readonly List<ICommandListener> _listeners;
    private readonly List<Exception> _exceptionsBegin;
    private readonly List<Exception> _exceptionsEnd;
    private GPUFrameBuffer? _framebuffer;

    private IMesh? _mesh;
    private int _subMeshIndex;

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



    internal RenderContext(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
        _device = renderingSystem.GraphicsDevice;
        _command = _device.CreateCommandBuffer(new CommandBufferDescriptor("render_context"));
        _listeners = new List<ICommandListener>();
        _exceptionsBegin = new List<Exception>();
        _exceptionsEnd = new List<Exception>();
    }

    public void AddListener(ICommandListener listener)
    {
        _listeners.Add(listener);
    }

    public void RemoveListener(ICommandListener listener)
    {
        _listeners.Remove(listener);
    }

    /// <summary>
    /// Begin the render context.
    /// </summary>
    /// <param name="target">The framebuffer to render to.</param>
    /// <returns>The exceptions that occurred during invoking the <see cref="ICommandListener.OnCommandBegin"/> event; otherwise, an empty array.</returns>
    public IReadOnlyList<Exception> Begin(GPUFrameBuffer target)
    {
        _command.Begin();
        _command.SetFrameBuffer(target);
        _framebuffer = target;

        ClearCache();

        return InvokeBegin();
    }

    public void Draw(in IMesh mesh,in Material material)
    {
        Debug.Assert(_framebuffer != null);
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_framebuffer!.RenderPass);
        _command.SetGraphicsPipeline(pipelineInfo.Pipeline);
        SetMesh(mesh, 0);
        material.PushResourceToCommandBuffer(_command);
        _command.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
    }

    public unsafe void DrawWithConstant<T>(in IMesh mesh, in Material material, in T constant) where T : unmanaged
    {
        Debug.Assert(_framebuffer != null);
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_framebuffer!.RenderPass);
        if (pipelineInfo.PushConstantsSize != sizeof(T))
        {
            throw new ArgumentException("The size of the constant does not match the push constants size");
        }
        _command.SetGraphicsPipeline(pipelineInfo.Pipeline);
        SetMesh(mesh, 0);
        material.PushResourceToCommandBuffer(_command);
        _command.PushConstants(pipelineInfo.PushConstantsStages, constant);
        _command.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
    }

    public void DrawInstanced(in IMesh mesh, in Material material, in uint instanceCount)
    {
        Debug.Assert(_framebuffer != null);
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_framebuffer!.RenderPass);
        _command.SetGraphicsPipeline(pipelineInfo.Pipeline);
        SetMesh(mesh, 0);
        material.PushResourceToCommandBuffer(_command);
        _command.DrawIndexed(mesh.IndexCount, instanceCount, 0, 0, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawInstancedWithConstant<T>(in IMesh mesh, in Material material, in uint instanceCount, in T constant) where T : unmanaged
    {
        DrawInstancedWithConstant(mesh, material, instanceCount, 0, constant);
    }

    public void DrawInstancedWithConstant<T>(in IMesh mesh, in Material material, in uint instanceCount, in uint instanceStart, in T constant) where T : unmanaged
    {
        Debug.Assert(_framebuffer != null);
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_framebuffer!.RenderPass);
        _command.SetGraphicsPipeline(pipelineInfo.Pipeline);
        SetMesh(mesh, 0);
        material.PushResourceToCommandBuffer(_command);
        _command.PushConstants(pipelineInfo.PushConstantsStages, constant);
        _command.DrawIndexed(mesh.IndexCount, instanceCount, 0, 0, instanceStart);
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

        return exceptions;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetMesh(in IMesh mesh, in int subMeshIndex)
    {
        if (_mesh == mesh && _subMeshIndex == subMeshIndex)
        {
            return;
        }

        _mesh = mesh;
        _subMeshIndex = subMeshIndex;

        //todo: sub mesh support
        _command.SetVertexBuffer(0, mesh.VertexBuffer);
        _command.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
    }

    private void ClearCache()
    {
        _mesh = null;
        _subMeshIndex = 0;
    }

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