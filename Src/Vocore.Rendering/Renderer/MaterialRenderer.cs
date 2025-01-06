using Vocore.Graphics;

namespace Vocore.Rendering;

public class MaterialRenderer : AutoDisposable
{
    private readonly GPUDevice _device;
    private readonly RenderingSystem _renderingSystem;
    private readonly GPUCommandBuffer _command;
    private GPUFrameBuffer? _framebuffer;
    public MaterialRenderer(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
        _device = renderingSystem.GraphicsDevice;
        _command = _device.CreateCommandBuffer(new CommandBufferDescriptor("material_renderer"));
    }

    public void Begin(GPUFrameBuffer target)
    {
        _command.Begin();
        _command.SetFrameBuffer(target);
        _framebuffer = target;
    }



    public void Draw(IMesh mesh, Material material)
    {
        if (_framebuffer == null)
        {
            throw new InvalidOperationException("Begin must be called before Draw");
        }
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_framebuffer.RenderPass);
        _command.SetGraphicsPipeline(pipelineInfo.Pipeline);
        _command.SetVertexBuffer(0, mesh.VertexBuffer);
        _command.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
        material.PushResourceToCommandBuffer(_command);
        _command.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
    }

    public unsafe void DrawWithConstant<T>(IMesh mesh, Material material, T constant) where T : unmanaged
    {
        if (_framebuffer == null)
        {
            throw new InvalidOperationException("Begin must be called before Draw");
        }
        ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(_framebuffer.RenderPass);
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

    public void End()
    {
        _command.End();
        _renderingSystem.ScheduleCommandBuffer(_command);
    }

    protected override void Dispose(bool disposing)
    {

    }
}