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
    private GPUFrameBuffer? _framebuffer;

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
    }

    public void Begin(GPUFrameBuffer target)
    {
        _command.Begin();
        _command.SetFrameBuffer(target);
        _framebuffer = target;
    }

    public void Draw(IMesh mesh, Material material)
    {
        Debug.Assert(_framebuffer != null);
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_framebuffer!.RenderPass);
        _command.SetGraphicsPipeline(pipelineInfo.Pipeline);
        _command.SetVertexBuffer(0, mesh.VertexBuffer);
        _command.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
        material.PushResourceToCommandBuffer(_command);
        _command.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
    }

    public unsafe void DrawWithConstant<T>(IMesh mesh, Material material, T constant) where T : unmanaged
    {
        Debug.Assert(_framebuffer != null);
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_framebuffer!.RenderPass);
        if (pipelineInfo.PushConstantsSize != sizeof(T))
        {
            throw new ArgumentException("The size of the constant does not match the push constants size");
        }
        _command.SetGraphicsPipeline(pipelineInfo.Pipeline);
        _command.SetVertexBuffer(0, mesh.VertexBuffer);
        _command.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
        material.PushResourceToCommandBuffer(_command);
        _command.PushConstants(pipelineInfo.PushConstantsStages, constant);
        _command.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
    }

    public void DrawInstanced(IMesh mesh, Material material, uint instanceCount)
    {
        Debug.Assert(_framebuffer != null);
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_framebuffer!.RenderPass);
        _command.SetGraphicsPipeline(pipelineInfo.Pipeline);
        _command.SetVertexBuffer(0, mesh.VertexBuffer);
        _command.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
        material.PushResourceToCommandBuffer(_command);
        _command.DrawIndexed(mesh.IndexCount, instanceCount, 0, 0, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawInstancedWithConstant<T>(IMesh mesh, Material material, uint instanceCount, T constant) where T : unmanaged
    {
        DrawInstancedWithConstant(mesh, material, instanceCount, 0, constant);
    }

    public void DrawInstancedWithConstant<T>(IMesh mesh, Material material, uint instanceCount, uint instanceStart, T constant) where T : unmanaged
    {
        Debug.Assert(_framebuffer != null);
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_framebuffer!.RenderPass);
        _command.SetGraphicsPipeline(pipelineInfo.Pipeline);
        _command.SetVertexBuffer(0, mesh.VertexBuffer);
        _command.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
        material.PushResourceToCommandBuffer(_command);
        _command.PushConstants(pipelineInfo.PushConstantsStages, constant);
        _command.DrawIndexed(mesh.IndexCount, instanceCount, 0, 0, instanceStart);
    }

    public void End()
    {
        _command.End();
        _renderingSystem.ScheduleCommandBuffer(_command);
    }

    protected override void Dispose(bool disposing)
    {
        _command.Dispose();
    }
}