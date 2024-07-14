using Vocore.Graphics;

namespace Vocore.Rendering;

public class MaterialRenderer : AutoDisposable
{
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _command;
    private GPUFrameBuffer? _framebuffer;
    public MaterialRenderer(GPUDevice device)
    {
        _device = device;
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
        _command.SetGraphicsPipeline(material.GetPipeline(_framebuffer.RenderPass));
        _command.SetVertexBuffer(0, mesh.VertexBuffer);
        _command.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
        material.PushResourceToCommandBuffer(_command);
        _command.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
    }

    public void DrawWithConstant<T>(IMesh mesh, Material material, T constant, ShaderStage pushConstantStage) where T : unmanaged
    {
        if (_framebuffer == null)
        {
            throw new InvalidOperationException("Begin must be called before Draw");
        }
        _command.SetGraphicsPipeline(material.GetPipeline(_framebuffer.RenderPass));
        _command.SetVertexBuffer(0, mesh.VertexBuffer);
        _command.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
        material.PushResourceToCommandBuffer(_command);
        _command.PushConstants(pushConstantStage, constant);
        _command.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
    }

    public void End()
    {
        _command.End();
        _device.Submit(_command);
    }

    protected override void Dispose(bool disposing)
    {

    }
}