using Vocore.Graphics;

namespace Vocore.Rendering;

public class MaterialRenderer : Renderer
{
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _command;
    public MaterialRenderer(GPUDevice device, ICamera camera) : base(camera)
    {
        _device = device;
        _command = _device.CreateCommandBuffer(new CommandBufferDescriptor("material_renderer"));
    }

    public void Begin(GPUFrameBuffer target)
    {
        _command.Begin();
        _command.SetFrameBuffer(target);
    }



    public void Draw(IMesh mesh, Material material)
    {
        _command.SetGraphicsPipeline(material.Pipeline);
        _command.SetVertexBuffer(0, mesh.VertexBuffer);
        _command.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
        material.PushResourceToCommandBuffer(_command);
        _command.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
    }

    public void DrawWithConstant<T>(IMesh mesh, Material material, T constant, ShaderStage pushConstantStage) where T : unmanaged
    {
        _command.SetGraphicsPipeline(material.Pipeline);
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